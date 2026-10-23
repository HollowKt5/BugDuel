namespace BugDuel.Campaign
{
    public sealed partial class CampaignSession
    {
        public bool NeedsPlayerName => string.IsNullOrEmpty(State.playerName);
        public string PlayerSignature => NeedsPlayerName ? "尚未命名" : CampaignDisplayText.Literal(State.playerName);

        void UpgradePlayerIdentity()
        {
            if (State.playerIdentityVersion != 0) return;
            // Old auction saves used a display string as the ownership flag. Names never determine ownership now.
            foreach (var lot in State.marketAuctions)
                lot.highBidderIsPlayer = lot.playerBid > 0 || lot.won || lot.highBidder == "你";
            State.playerIdentityVersion = 1;
        }

        public void RenamePlayer(string name)
        {
            State.playerName = NormalizeName(name);
            foreach (var lot in State.marketAuctions)
                if (lot.highBidderIsPlayer) lot.highBidder = State.playerName;
            Tell("玩家名字已保存。邮件与市场署名会使用这个名字。");
        }

        public string MailBody(CampaignMail mail) => PlayerSignature + "，你好：\n\n" + mail.body;
        public string AuctionBidderName(MarketAuctionLot lot) => lot.highBidderIsPlayer ? PlayerSignature : lot.highBidder;

        void ValidatePlayerIdentity()
        {
            RequireData(State.playerIdentityVersion == 1, "玩家身份数据版本无效");
            RequireData(NeedsPlayerName || State.playerName == State.playerName.Trim() && NameProblem(State.playerName) == null, "玩家名字数据无效");
        }
    }
}
