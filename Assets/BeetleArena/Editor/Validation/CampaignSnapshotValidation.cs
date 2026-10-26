using System;
using System.Collections;
using System.Reflection;
using BugDuel.Campaign;
using UnityEditor;
using UnityEngine;

public static class CampaignSnapshotValidation
{
    [MenuItem("Tools/甲壳虫斗/验证/存档与数据/战役快照深拷贝", false, 663)]
    public static void Run()
    {
        // Populate every public DTO field, including normally mutually exclusive
        // activities. Future fields must also survive a clone without mutable aliases.
        var source = (CampaignState)Populate(typeof(CampaignState));
        CheckGraph(source, CampaignSnapshots.Copy(source), "state");
        var empty = new CampaignState();
        CheckGraph(empty, CampaignSnapshots.Copy(empty), "empty");
        var session = CampaignSession.New(CampaignCatalog.Defaults(), 7351);
        CheckGraph(session.State, CampaignSnapshots.Copy(session.State), "journey");
        Debug.Log("Campaign snapshot graph PASS: all fields preserved, all mutable objects detached.");
    }

    static object Populate(Type type)
    {
        if (type == typeof(string)) return "snapshot-value";
        if (type == typeof(bool)) return true;
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
        if (type.IsPrimitive) return Convert.ChangeType(3, type);
        if (typeof(IList).IsAssignableFrom(type))
        {
            var list = (IList)Activator.CreateInstance(type);
            list.Add(Populate(type.GetGenericArguments()[0]));
            return list;
        }
        object value = type == typeof(Gene) ? new Gene("snapshot-gene", 3) : Activator.CreateInstance(type);
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            field.SetValue(value, Populate(field.FieldType));
        return value;
    }

    static void CheckGraph(object source, object copy, string path)
    {
        if (source == null || source is string || source.GetType().IsValueType)
        {
            if (!Equals(source, copy)) throw new Exception("Snapshot changed " + path);
            return;
        }
        if (copy == null || copy.GetType() != source.GetType() || ReferenceEquals(source, copy))
            throw new Exception("Snapshot missing or sharing " + path);
        if (source is IList list)
        {
            var cloned = (IList)copy;
            if (list.Count != cloned.Count) throw new Exception("Snapshot list size changed " + path);
            for (int i = 0; i < list.Count; i++) CheckGraph(list[i], cloned[i], path + "[" + i + "]");
            return;
        }
        foreach (var field in source.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            CheckGraph(field.GetValue(source), field.GetValue(copy), path + "." + field.Name);
    }
}
