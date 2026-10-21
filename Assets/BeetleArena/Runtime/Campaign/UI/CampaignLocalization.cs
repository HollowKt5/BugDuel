using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        [Serializable] public sealed class Entry { public string key,zh,en; public string[] sites; }
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
            entries=all.ToDictionary(e=>e.zh);
            var placeholder=new Regex(@"\{(\d+)\}");
            patterns=all.Where(e=>placeholder.IsMatch(e.zh)).OrderByDescending(e=>placeholder.Replace(e.zh,"").Length)
                .Select(e=>
                {
                    var arguments=new List<int>();int offset=0;string regex="\\A";
                    foreach(Match match in placeholder.Matches(e.zh))
                    {
                        regex+=Regex.Escape(e.zh.Substring(offset,match.Index-offset))+"(.*?)";
                        arguments.Add(int.Parse(match.Groups[1].Value));offset=match.Index+match.Length;
                    }
                    regex+=Regex.Escape(e.zh.Substring(offset))+"\\z";
                    return new Pattern{entry=e,arguments=arguments.ToArray(),regex=new Regex(regex,RegexOptions.Singleline|RegexOptions.CultureInvariant)};
                }).ToArray();
            // Legacy messages are assembled in the domain. Translate only their known display fragments,
            // longest first; neither saved data nor identifiers are changed. Unknown hot-update text is retained.
            fragments=new Regex(string.Join("|",all.Where(e=>!placeholder.IsMatch(e.zh)).OrderByDescending(e=>e.zh.Length).Select(e=>Regex.Escape(e.zh))),RegexOptions.CultureInvariant);
        }
        public static string Text(string source)
        {
            if(string.IsNullOrEmpty(source)||!English||!table)return source;
            EnsurePatterns();
            if(cache.TryGetValue(source,out var translated))return translated;
            translated=Translate(source,0);
            if(cache.Count>=1024)cache.Clear();cache[source]=translated;return translated;
        }
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
                    try{if(System.IO.File.Exists(path))language=JsonUtility.FromJson<CampaignPreferences>(System.IO.File.ReadAllText(path)).language;}
                    catch(System.IO.IOException){ }
                }
                Initialize(language);
            }
            foreach(var label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                // A second theme pass must not replace the original source with its translation.
                var binding=label.GetComponent<CampaignLocalizedLabel>();
                if(binding)binding.Refresh();else Bind(label,label.text);
            }
        }
    }
}
