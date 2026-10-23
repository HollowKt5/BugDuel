namespace BeetleArena.Campaign
{
    public static class CampaignInputPolicy
    {
        public static bool CanManage(CampaignPage page,bool hasSession,bool busy,bool modal) =>
            hasSession && !busy && !modal && (page==CampaignPage.Greenhouse || page==CampaignPage.Catching ||
                page==CampaignPage.Market || page==CampaignPage.League || page==CampaignPage.Auction || page==CampaignPage.SpecimenRoom);
    }
}
