using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BugDuel.Campaign
{
    // Display messages mark player text explicitly. Stored names remain plain strings.
    public static class CampaignDisplayText
    {
        static readonly Regex literals=new Regex("\uE000([A-Za-z0-9+/=]*)\uE001",RegexOptions.CultureInvariant);
        static readonly Regex placeholders=new Regex("\uE002([0-9]+)\uE003",RegexOptions.CultureInvariant);
        public static string Literal(string value) => "\uE000"+Convert.ToBase64String(Encoding.UTF8.GetBytes(value))+"\uE001";
        public static string Name(CampaignBeetle beetle) => beetle.customName?Literal(beetle.name):beetle.name;
        public static string Name(HallRecord record) => record.customName?Literal(record.name):record.name;
        public static string Name(CampaignAttachment attachment) => attachment.specimens.Count>0?SpecimenName(attachment.specimens[0]):attachment.literalName?Literal(attachment.name):attachment.name;
        public static string SpecimenName(CampaignSpecimen specimen) => specimen.customLabelName?Literal(specimen.labelName):specimen.labelName;
        public static string SpecimenSpecies(CampaignSpecimen specimen) => specimen.customSpeciesLabel?Literal(specimen.speciesLabel):specimen.speciesLabel;
        public static string SpecimenDate(CampaignSpecimen specimen) => specimen.customDateLabel?Literal(specimen.dateLabel):specimen.dateLabel;

        public static string Localize(string source,Func<string,string> translate)
        {
            var values=new List<string>();
            string protectedText=literals.Replace(source,match=>
            {
                int index=values.Count;values.Add(Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups[1].Value)));
                return "\uE002"+index+"\uE003";
            });
            string translated=translate(protectedText);
            return values.Count==0?translated:placeholders.Replace(translated,match=>values[int.Parse(match.Groups[1].Value)]);
        }
    }
}
