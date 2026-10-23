using System.Linq;

namespace BugDuel.Campaign
{
    public sealed partial class CampaignSession
    {
        public const int BeetleNameLimit = 24;

        public static string NameProblem(string name)
        {
            string trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0 || trimmed.Length > BeetleNameLimit) return "名字须为 1–24 个字符";
            return trimmed.Any(c => char.IsControl(c) || c == '<' || c == '>') ? "名字不能包含换行、控制字符或尖括号" : null;
        }

        static string NormalizeName(string name)
        { string problem = NameProblem(name); Require(problem == null, problem); return name.Trim(); }

        public void RenameBeetle(string id, string name)
        {
            var beetle = State.beetles.Concat(State.storedBeetles).FirstOrDefault(b => b.id == id);
            Require(beetle != null, "只能为自己温室或仓库中的甲虫命名");
            beetle.name = NormalizeName(name);
            beetle.customName = true;
            Tell("名字已保存，会随成长、交易和标本保留。");
        }
    }
}
