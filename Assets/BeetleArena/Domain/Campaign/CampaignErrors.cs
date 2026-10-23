using System;

namespace BugDuel.Campaign
{
    // Only explicit, player-actionable rule refusals belong in this category.
    public sealed class CampaignRuleException : InvalidOperationException
    {
        public CampaignRuleException(string message) : base(message) { }
    }

    // Expected at an external data boundary; inside a transaction this is an invariant failure.
    public sealed class CampaignDataException : InvalidOperationException
    {
        public CampaignDataException(string message) : base(message) { }
        public CampaignDataException(string message, Exception innerException) : base(message, innerException) { }
    }
}
