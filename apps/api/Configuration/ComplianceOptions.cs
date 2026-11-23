namespace ScreenHire.Api.Configuration;

/// <summary>
/// <c>ScreenHire:Compliance</c>: features that help an organization meet the rules on automated decisions in hiring
/// (docs/research/compliance-gap-map.md). Each is off until the owner turns it on, after counsel's review.
/// </summary>
public sealed class ComplianceOptions
{
    public const string Section = "ScreenHire:Compliance";

    public DecisionRecordOptions DecisionRecord { get; set; } = new();

    public ReviewRequestOptions ReviewRequests { get; set; } = new();

    /// <summary>
    /// <c>Enabled</c> (default false) serves <c>GET /v1/applications/{id}/decision-record</c>, the team's copy of one
    /// application's record for answering a candidate's access, correction or review request
    /// (docs/architecture/data-requests.md, Decision record). Off, the route is a 404.
    /// </summary>
    public sealed class DecisionRecordOptions
    {
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// <c>Enabled</c> (default false) lets a candidate ask, from their own page, for a person to review the decision on an
    /// application that is not moving forward (docs/architecture/candidate-portal.md, Review requests). Off, the page offers
    /// nothing and the route is a 404. The page's wording is the owner's call with counsel.
    /// </summary>
    public sealed class ReviewRequestOptions
    {
        public bool Enabled { get; set; }
    }
}
