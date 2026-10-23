using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BugDuel.Campaign;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace BeetleArena.Campaign
{
    /// <summary>Presentation boundary for legacy domain messages; locale ownership stays with Unity Localization.</summary>
    public static class CampaignLocalization
    {
        public const string TableName="GameText",FontTable="GameFonts";
        [Serializable] public sealed class Entry { public string key,source,zh,en; public string[] sites; }
        [Serializable] public sealed class Catalog { public Entry[] entries; }
        sealed class Pattern { public Entry entry; public Regex regex; public int[] arguments; }
        static Dictionary<string,Entry> entries;
        static Pattern[] patterns;
        static Regex fragments;
        static StringTable table;
        static TMP_FontAsset currentFont;
        static readonly Dictionary<string,string> cache=new Dictionary<string,string>();
        public static event Action Changed;
        public static bool English => LocalizationSettings.HasSettings && LocalizationSettings.SelectedLocale && LocalizationSettings.SelectedLocale.Identifier.Code=="en";
        public static TMP_FontAsset Font => currentFont ? currentFont : Resources.Load<TMP_FontAsset>("RetroUiFont");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            LocalizationSettings.SelectedLocaleChanged-=LocaleChanged;
            entries=null;patterns=null;fragments=null;table=null;currentFont=null;cache.Clear();Changed=null;
        }
        public static void Initialize(int savedLanguage)
        {
            if(!LocalizationSettings.HasSettings)return;
            LocalizationSettings.InitializationOperation.WaitForCompletion();
            LocalizationSettings.SelectedLocaleChanged-=LocaleChanged;
            LocalizationSettings.SelectedLocaleChanged+=LocaleChanged;
            var locale=LocalizationSettings.AvailableLocales.GetLocale(savedLanguage==1?"en":"zh-Hans");
            if(locale)LocalizationSettings.SelectedLocale=locale;
            LocaleChanged(LocalizationSettings.SelectedLocale);
        }
        public static void Select(int language)
        {
            var locale=LocalizationSettings.AvailableLocales.GetLocale(language==1?"en":"zh-Hans");
            if(locale)LocalizationSettings.SelectedLocale=locale;
        }
        static void LocaleChanged(Locale locale)
        {
            if(!locale)return;
            table=LocalizationSettings.StringDatabase.GetTable(TableName,locale);
            currentFont=LocalizationSettings.AssetDatabase.GetLocalizedAsset<TMP_FontAsset>(FontTable,"ui",locale);
            cache.Clear();Changed?.Invoke();
        }
        static void EnsurePatterns()
        {
            if(entries!=null)return;
            var source=Resources.Load<TextAsset>("CampaignLanguagePatterns");
            var all=source?JsonUtility.FromJson<Catalog>(source.text).entries:Array.Empty<Entry>();
            entries=all.ToDictionary(e=>string.IsNullOrEmpty(e.source)?e.zh:e.source,StringComparer.Ordinal);
            // New UI may already use the clean Chinese wording, while old saves and authored
            // scenes retain their original source. Both resolve to the same table entry.
            foreach(var entry in all)if(!entries.ContainsKey(entry.zh))entries.Add(entry.zh,entry);
            var placeholder=new Regex(@"\{(\d+)\}");
            patterns=entries.Where(e=>placeholder.IsMatch(e.Key)).OrderByDescending(e=>placeholder.Replace(e.Key,"").Length)
                .Select(e=>
                {
                    var arguments=new List<int>();int offset=0;string regex="\\A";
                    foreach(Match match in placeholder.Matches(e.Key))
                    {
                        regex+=Regex.Escape(e.Key.Substring(offset,match.Index-offset))+"(.*?)";
                        arguments.Add(int.Parse(match.Groups[1].Value));offset=match.Index+match.Length;
                    }
                    regex+=Regex.Escape(e.Key.Substring(offset))+"\\z";
                    return new Pattern{entry=e.Value,arguments=arguments.ToArray(),regex=new Regex(regex,RegexOptions.Singleline|RegexOptions.CultureInvariant)};
                }).ToArray();
            // Legacy messages are assembled in the domain. Translate only their known display fragments,
            // longest first; neither saved data nor identifiers are changed. Unknown hot-update text is retained.
            fragments=new Regex(string.Join("|",entries.Keys.Where(e=>!placeholder.IsMatch(e)).OrderByDescending(e=>e.Length).Select(e=>
                (char.IsLetter(e[0])&&e[0]<128?"(?<![A-Za-z_@./])":"")+Regex.Escape(e)+(char.IsLetter(e[e.Length-1])&&e[e.Length-1]<128?"(?![A-Za-z_@./])":""))),RegexOptions.CultureInvariant);
        }
        public static string Text(string source)
        {
            if(string.IsNullOrEmpty(source))return source;
            if(cache.TryGetValue(source,out var translated))return translated;
            bool localize=table;
            if(localize)EnsurePatterns();
            translated=CampaignDisplayText.Localize(source,text=>localize?Translate(text,0):text);
            if(cache.Count>=1024)cache.Clear();cache[source]=translated;return translated;
        }
        public static string Literal(string value)=>CampaignDisplayText.Literal(value);
        public static string Name(CampaignBeetle beetle)=>CampaignDisplayText.Name(beetle);
        public static string Name(HallRecord record)=>CampaignDisplayText.Name(record);
        public static string Name(CampaignAttachment attachment)=>CampaignDisplayText.Name(attachment);
        static string Value(Entry entry)=>table.GetEntry(entry.key)?.Value??entry.zh;
        static string Translate(string source,int depth)
        {
            if(entries.TryGetValue(source,out var direct))return Value(direct);
            if(depth<5)
                foreach(var pattern in patterns)
                {
                    var match=pattern.regex.Match(source);if(!match.Success)continue;
                    var args=new object[pattern.arguments.Max()+1];
                    for(int i=0;i<pattern.arguments.Length;i++)args[pattern.arguments[i]]=Translate(match.Groups[i+1].Value,depth+1);
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture,Value(pattern.entry),args);
                }
            return fragments.Replace(source,m=>Value(entries[m.Value]));
        }
        public static void Bind(TMP_Text label,string source)
        {
            var binding=label.GetComponent<CampaignLocalizedLabel>()??label.gameObject.AddComponent<CampaignLocalizedLabel>();
            binding.SetSource(source);
        }
        public static void BindHierarchy(Transform root)
        {
            if(!currentFont&&LocalizationSettings.HasSettings)
            {
                int language=CampaignApp.Instance?CampaignApp.Instance.Preferences.language:0;
                if(!CampaignApp.Instance)
                {
                    string folder=CampaignApp.StorageOverride??System.IO.Path.Combine(Application.persistentDataPath,"Campaign");
                    string path=System.IO.Path.Combine(folder,"settings.json");
                    try { if(System.IO.File.Exists(path))language=CampaignJson.Read<CampaignPreferences>(path).language; }
                    catch(Exception error) when(CampaignApp.IsExternalDataFailure(error))
                    { Debug.LogWarning("语言设置读取失败，使用默认语言：\n"+error); }
                    catch(Exception error) { Debug.LogException(error); }
                }
                Initialize(language);
            }
            foreach(var label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var input=label.GetComponentInParent<TMP_InputField>(true);
                if(input&&(input.textComponent==label||input.placeholder==label))continue;
                // A second theme pass must not replace the original source with its translation.
                var binding=label.GetComponent<CampaignLocalizedLabel>();
                if(binding)binding.Refresh();else Bind(label,label.text);
            }
        }
    }
}
