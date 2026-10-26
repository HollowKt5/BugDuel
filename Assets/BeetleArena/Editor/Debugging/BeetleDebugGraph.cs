using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace BeetleArena.EditorTools
{
    /// <summary>
    /// Editor-only, type-whitelisted scene graph serializer. Preserves shared references, readonly instance fields, and RNG internal state.
    /// Does not use TypeNameHandling, avoids invoking constructors, and disallows arbitrary CLR types from file.
    /// Unsupported fields fail explicitly to avoid masking missed state behind " load success\. Compatibility is bounded by snapshot fingerprints.
    /// </summary>
    internal sealed class BeetleDebugGraph
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
        sealed class Identity : IEqualityComparer<object>
        {
            public new bool Equals(object a,object b)=>ReferenceEquals(a,b);
            public int GetHashCode(object v)=>RuntimeHelpers.GetHashCode(v);
        }
        readonly Dictionary<object,int> written=new Dictionary<object,int>(new Identity());
        readonly Dictionary<int,object> read=new Dictionary<int,object>();
        readonly Dictionary<Object,string> objectKeys=new Dictionary<Object,string>();
        readonly Dictionary<string,Object> objects=new Dictionary<string,Object>();
        readonly Dictionary<string,Type> types=new Dictionary<string,Type>();
        readonly JArray nodes=new JArray();
        public JArray Nodes=>nodes;
        public BeetleDebugGraph(BeetleMatchController match)
        {
            Register(typeof(int));Register(typeof(float));Register(typeof(double));Register(typeof(bool));Register(typeof(string));Register(typeof(long));Register(typeof(uint));
            foreach(var type in new[]{typeof(Vector2),typeof(Vector3),typeof(Vector4),typeof(Quaternion),typeof(Color),typeof(Bounds),typeof(Transform),typeof(GameObject),typeof(Collider),typeof(Renderer),typeof(Material),typeof(Camera)})Register(type);
            Register(typeof(System.Random));
            foreach(var type in typeof(BeetleSimulation).Assembly.GetTypes().Where(t=>t.Namespace=="BeetleArena" && !t.IsGenericTypeDefinition))Register(type);
            AddObject(match.settings,"settings");
            foreach(var root in match.gameObject.scene.GetRootGameObjects())
                foreach(var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string key=TransformKey(t);AddObject(t.gameObject,key+"/gameObject");
                    var components=t.GetComponents<Component>();
                    for(int i=0;i<components.Length;i++)if(components[i])AddObject(components[i],key+"/component/"+i+"/"+components[i].GetType().FullName);
                }
        }
        void Register(Type type){if(type.FullName!=null)types[type.FullName]=type;}
        void AddObject(Object obj,string key){if(objects.ContainsKey(key))throw new InvalidOperationException("Duplicate scene key: "+key);objects[key]=obj;objectKeys[obj]=key;}
        internal static string TransformKey(Transform t)
        {
            var parts=new List<string>();for(var p=t;p;p=p.parent)parts.Add(Uri.EscapeDataString(p.name)+"["+(p.parent?p.GetSiblingIndex():0)+"]");
            parts.Reverse();return "scene/"+string.Join("/",parts);
        }
        public string Key(Object obj)
        {
            if(!obj)return null;
            if(objectKeys.TryGetValue(obj,out var key))return key;
            if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj,out string guid,out long local) && !string.IsNullOrEmpty(guid))
                return "asset/"+guid+"/"+local+"/"+obj.GetType().FullName;
            throw new InvalidOperationException("Unmapped transient Unity object: "+obj.name+" ("+obj.GetType().Name+")");
        }
        public Object Resolve(string key)
        {
            if(key==null)return null;if(objects.TryGetValue(key,out var obj))return obj;
            if(key.StartsWith("asset/",StringComparison.Ordinal))
            {
                var parts=key.Split('/');string path=AssetDatabase.GUIDToAssetPath(parts[1]);
                foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset,out string guid,out long local) && local.ToString()==parts[2] && asset.GetType().FullName==parts[3])return asset;
            }
            throw new InvalidOperationException("Snapshot reference no longer exists: "+key);
        }
        internal static FieldInfo[] Fields(Type type)
        {
            var fields=new List<FieldInfo>();
            for(var t=type;t!=null && t!=typeof(object) && t!=typeof(MonoBehaviour) && t!=typeof(ScriptableObject);t=t.BaseType)
                fields.AddRange(t.GetFields(Flags).Where(f=>!f.IsStatic && !typeof(Delegate).IsAssignableFrom(f.FieldType)));
            return fields.OrderBy(f=>f.DeclaringType.FullName+"/"+f.Name,StringComparer.Ordinal).ToArray();
        }
        static string FieldKey(FieldInfo f)=>f.DeclaringType.FullName+"/"+f.Name;
        public JObject WriteFields(object value,Func<FieldInfo,bool> include=null)
        {
            var result=new JObject();foreach(var f in Fields(value.GetType()))if(include==null || include(f))result[FieldKey(f)]=Write(f.GetValue(value));return result;
        }
        public void ApplyFields(object value,JObject data,Func<FieldInfo,bool> include=null)
        {
            var fields=Fields(value.GetType()).Where(f=>include==null || include(f)).ToArray();
            if(fields.Length!=data.Count)throw new InvalidOperationException("Snapshot field count changed: "+value.GetType().Name);
            foreach(var f in fields)
            {
                if(!data.TryGetValue(FieldKey(f),out var token))throw new InvalidOperationException("Missing snapshot field "+f.Name);
                f.SetValue(value,Read(token));
            }
        }
        JToken TypeToken(Type type)
        {
            if(type.IsArray)return new JObject{{"array",TypeToken(type.GetElementType())}};
            if(type.IsGenericType)
            {
                var def=type.GetGenericTypeDefinition();
                if(def!=typeof(List<>) && def!=typeof(Dictionary<,>))throw new InvalidOperationException("Unsupported snapshot collection "+type);
                return new JObject{{"generic",def==typeof(List<>)?"list":"dictionary"},{"args",new JArray(type.GetGenericArguments().Select(TypeToken))}};
            }
            if(!types.ContainsKey(type.FullName))throw new InvalidOperationException("Unsupported snapshot type "+type);
            return new JValue(type.FullName);
        }
        Type ResolveType(JToken token)
        {
            if(token.Type==JTokenType.String && types.TryGetValue((string)token,out var t))return t;
            if(token is JObject o)
            {
                if(o["array"]!=null)return ResolveType(o["array"]).MakeArrayType();
                var args=((JArray)o["args"]).Select(ResolveType).ToArray();
                return ((string)o["generic"]=="list"?typeof(List<>):(string)o["generic"]=="dictionary"?typeof(Dictionary<,>):throw new InvalidOperationException("Invalid collection")).MakeGenericType(args);
            }
            throw new InvalidOperationException("Unregistered snapshot type: "+token);
        }
        public JToken Write(object value)
        {
            if(value==null)return JValue.CreateNull();
            if(value is Object obj)return !obj?JValue.CreateNull():new JObject{{"unity",Key(obj)}};
            var type=value.GetType();
            if(type.IsPrimitive || type==typeof(string) || type.IsEnum)return new JObject{{"scalar",TypeToken(type)},{"value",JToken.FromObject(value)}};
            if(type.IsValueType)return new JObject{{"struct",TypeToken(type)},{"fields",WriteFields(value)}};
            if(written.TryGetValue(value,out int known))return new JObject{{"ref",known}};
            int id=nodes.Count;written.Add(value,id);var node=new JObject{{"type",TypeToken(type)}};nodes.Add(node);
            if(value is IDictionary dictionary)
            {
                var entries=new JArray();foreach(DictionaryEntry entry in dictionary)entries.Add(new JArray(Write(entry.Key),Write(entry.Value)));node["entries"]=entries;
            }
            else if(value is IList list){var items=new JArray();foreach(var item in list)items.Add(Write(item));node["items"]=items;}
            else node["fields"]=WriteFields(value);
            return new JObject{{"ref",id}};
        }
        public void Load(JArray input)
        {
            if(input.Count>50000)throw new InvalidOperationException("Snapshot node limit exceeded.");
            // Three-pass: Allocate -> Object fields/arrays -> Dictionaries. All references exist and dictionary keys are fully restored first.
            for(int i=0;i<input.Count;i++)
            {
                var n=(JObject)input[i];var type=ResolveType(n["type"]);object value;
                if(type.IsArray)value=Array.CreateInstance(type.GetElementType(),((JArray)n["items"]).Count);
                else if(type.IsGenericType)value=Activator.CreateInstance(type);
                else
                {
                    if(typeof(Object).IsAssignableFrom(type) || type.IsAbstract || type.IsValueType)throw new InvalidOperationException("Invalid object node: "+type);
                    value=FormatterServices.GetUninitializedObject(type);
                }
                read.Add(i,value);
            }
            for(int i=0;i<input.Count;i++)
            {
                var n=(JObject)input[i];var value=read[i];
                if(value is IDictionary)continue;
                if(value is IList list){int j=0;foreach(var item in (JArray)n["items"]){if(value is Array)list[j++]=Read(item);else list.Add(Read(item));}}
                else ApplyFields(value,(JObject)n["fields"]);
            }
            for(int i=0;i<input.Count;i++)if(read[i] is IDictionary dict)
                foreach(var entry in (JArray)input[i]["entries"])dict.Add(Read(entry[0]),Read(entry[1]));
        }
        public object Read(JToken token)
        {
            if(token==null || token.Type==JTokenType.Null)return null;
            if(token["unity"]!=null)return Resolve((string)token["unity"]);
            if(token["ref"]!=null)return read[(int)token["ref"]];
            if(token["scalar"]!=null)return token["value"].ToObject(ResolveType(token["scalar"]));
            if(token["struct"]!=null){object v=Activator.CreateInstance(ResolveType(token["struct"]));ApplyFields(v,(JObject)token["fields"]);return v;}
            throw new InvalidOperationException("Invalid snapshot token.");
        }
    }
}
