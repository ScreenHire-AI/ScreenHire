using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Privacy;
using ScreenHire.Domain.Privacy;

namespace ScreenHire.UnitTests.Privacy;

/// <summary>
/// The two signed links of the data request flow, the email that carries the first, and the request validators
/// (docs/architecture/data-requests.md). Data Protection checks a token's limit against the system clock, so a token
/// that has expired is one issued by a provider whose clock was hours in the past.
/// </summary>
public sealed class DataRequestLinksTests
{
    private static readonly CareersSiteOptions Careers = new() { Scheme = "https", CareersDomain = "careers.example.com", CareersHostPattern = "{tenant}.{CareersDomain}" };

    private readonly EphemeralDataProtectionProvider keyRing = new();

    private DataRequestLinks Links(TimeProvider? time = null, CareersSiteOptions? careers = null, DataRequestOptions? options = null) =>
        new(keyRing, Options.Create(careers ?? Careers), Options.Create(options ?? new DataRequestOptions()), time ?? TimeProvider.System);

    [Fact]
    public void A_link_token_round_trips_to_the_organization_and_the_candidate()
    {
        var organization = Guid.NewGuid();
        var candidate = Guid.NewGuid();
        var links = Links();

        links.ReadLink(links.IssueLink(organization, candidate)).Should().Be(new DataRequestLink(organization, candidate));
    }

    [Fact]
    public void A_download_token_round_trips_to_the_organization_and_the_request()
    {
        var organization = Guid.NewGuid();
        var request = Guid.NewGuid();
        var links = Links();

        links.ReadDownload(links.IssueDownload(organization, request)).Should().Be(new DataRequestDownload(organization, request));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CfDJ8forged-token-that-reads-as-nothing")]
    public void Forged_or_empty_tokens_read_as_nothing(string? token)
    {
        var links = Links();

        links.ReadLink(token).Should().BeNull();
        links.ReadDownload(token).Should().BeNull();
    }

    [Fact]
    public void A_token_from_another_key_ring_reads_as_nothing()
    {
        var token = Links().IssueLink(Guid.NewGuid(), Guid.NewGuid());
        var otherInstallation = new DataRequestLinks(
            new EphemeralDataProtectionProvider(), Options.Create(Careers), Options.Create(new DataRequestOptions()), TimeProvider.System);

        otherInstallation.ReadLink(token).Should().BeNull();
    }

    [Fact]
    public void A_token_is_good_for_its_own_purpose_only()
    {
        var links = Links();
        var link = links.IssueLink(Guid.NewGuid(), Guid.NewGuid());
        var download = links.IssueDownload(Guid.NewGuid(), Guid.NewGuid());

        links.ReadDownload(link).Should().BeNull("a link that opens the choices cannot fetch a copy");
        links.ReadLink(download).Should().BeNull("a download link cannot open the choices");
    }

    [Fact]
    public void An_oversized_token_is_refused_without_being_read()
    {
        var links = Links();

        links.ReadLink(new string('A', 1001)).Should().BeNull();
        links.ReadDownload(new string('A', 1001)).Should().BeNull();
    }

    [Fact]
    public void An_expired_token_reads_as_nothing_like_a_forged_one()
    {
        var longAgo = new FixedTime(DateTimeOffset.UtcNow.AddDays(-30));
        var past = Links(longAgo);
        var link = past.IssueLink(Guid.NewGuid(), Guid.NewGuid());
        var download = past.IssueDownload(Guid.NewGuid(), Guid.NewGuid());

        Links().ReadLink(link).Should().BeNull("the link lasted an hour, thirty days ago");
        Links().ReadDownload(download).Should().BeNull("the copy was good for a week, thirty days ago");
    }

    [Fact]
    public void A_token_just_inside_its_lifetime_still_reads()
    {
        var options = new DataRequestOptions { LinkLifetime = TimeSpan.FromHours(1) };
        var recent = Links(new FixedTime(DateTimeOffset.UtcNow.AddMinutes(-50)), options: options);
        var organization = Guid.NewGuid();
        var candidate = Guid.NewGuid();

        Links(options: options).ReadLink(recent.IssueLink(organization, candidate)).Should().Be(new DataRequestLink(organization, candidate));
    }

    [Fact]
    public void Addresses_are_on_the_organizations_careers_board()
    {
        var links = Links();

        links.LinkUrl("acme", "tok").Should().Be("https://acme.careers.example.com/data-request/tok");
        links.DownloadUrl("acme", "tok").Should().Be("https://acme.careers.example.com/data-request/download/tok");
    }

    [Fact]
    public void Without_a_careers_host_pattern_the_links_are_not_configured()
    {
        Links().IsConfigured.Should().BeTrue();
        Links(careers: new CareersSiteOptions { CareersHostPattern = "" }).IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void The_email_names_the_organization_the_link_and_how_long_it_works()
    {
        var message = DataRequestEmails.LinkEmail("sam@example.com", "Acme & Sons", "https://acme.careers.example.com/data-request/tok", TimeSpan.FromHours(1));

        message.To.Should().Be("sam@example.com");
        message.Subject.Should().Be("Your link to ask Acme & Sons about your data");
        message.TextBody.Should().Contain("https://acme.careers.example.com/data-request/tok").And.Contain("It works for 1 hour.")
            .And.Contain("If you didn't ask for this you can ignore this email.");
        message.HtmlBody.Should().Contain("href=\"https://acme.careers.example.com/data-request/tok\"").And.Contain("Acme &amp; Sons");
        message.TextBody.Should().NotMatchRegex("[\u2013\u2014]");
    }

    [Theory]
    [InlineData(60, "1 hour")]
    [InlineData(120, "2 hours")]
    [InlineData(90, "90 minutes")]
    [InlineData(1, "1 minute")]
    [InlineData(0.2, "1 minute")]
    public void The_lifetime_is_said_in_hours_or_minutes(double minutes, string expected) =>
        DataRequestEmails.Describe(TimeSpan.FromMinutes(minutes)).Should().Be(expected);

    [Theory]
    [InlineData("sam@example.com", true)]
    [InlineData("", false)]
    [InlineData("not an address", false)]
    public async Task The_link_request_needs_an_email_address(string email, bool valid)
    {
        var result = await new PublicDataRequestLinkRequestValidator().ValidateAsync(new PublicDataRequestLinkRequest(email), TestContext.Current.CancellationToken);

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task The_link_request_refuses_an_oversized_bot_check_token()
    {
        var result = await new PublicDataRequestLinkRequestValidator()
            .ValidateAsync(new PublicDataRequestLinkRequest("sam@example.com", new string('x', 4097)), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(DataRequestKind.Copy, true)]
    [InlineData(DataRequestKind.Erasure, true)]
    [InlineData((DataRequestKind)7, false)]
    public async Task The_choice_is_a_known_kind(DataRequestKind kind, bool valid)
    {
        var result = await new PublicDataRequestChoiceValidator().ValidateAsync(new PublicDataRequestChoice(kind), TestContext.Current.CancellationToken);

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void The_claim_key_is_stable_for_one_claim_and_differs_between_claims()
    {
        var candidate = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

        SendDataRequestLink.KeyFor(candidate, at).Should().Be(SendDataRequestLink.KeyFor(candidate, at));
        SendDataRequestLink.KeyFor(candidate, at).Should().NotBe(SendDataRequestLink.KeyFor(candidate, at.AddMinutes(6)));
        SendDataRequestLink.KeyFor(candidate, at).Should().NotBe(SendDataRequestLink.KeyFor(Guid.NewGuid(), at));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
