namespace ScreenHire.Domain.Organizations;

/// <summary>
/// An employer account — the tenant boundary. Every tenant-owned row carries an <c>OrganizationId</c>.
/// </summary>
public sealed class Organization
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>URL-safe identifier used in careers-page hosts and public API routes.</summary>
    public required string Slug { get; set; }

    /// <summary>Data-residency home region (e.g. <c>eu</c>, <c>us</c>); see ADR-0007.</summary>
    public required string HomeRegion { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// A free-trial account. Partner boards (Indeed's Job Sync rules out trial clients) are never offered to one
    /// (docs/architecture/job-distribution.md §3).
    /// </summary>
    public bool IsTrial { get; set; }

    /// <summary>
    /// Cleared for Indeed: the account is a paying customer whose every public job we may send. Set by us after review,
    /// never by the tenant. Nothing reads it for posting yet; there is no Indeed integration in the MVP.
    /// </summary>
    public bool IndeedEligible { get; set; }

    /// <summary>Whether partner boards with Indeed-style gates may receive this organization's jobs.</summary>
    public bool MayUsePartnerBoards => IndeedEligible && !IsTrial;

    /// <summary>
    /// The employer's brand colour (<c>#rrggbb</c>) for its careers pages, or null for ScreenHire's default. Careers
    /// pages use the <see cref="BrandPalette"/> derived from it, never the raw colour.
    /// </summary>
    public string? BrandColor { get; private set; }

    /// <summary>
    /// The second colour (<c>#rrggbb</c>), which fills the header band of the careers pages and of every email to
    /// candidates, or null for no band. Checked like the brand colour before it saves (<see cref="BrandColorCheck"/>).
    /// </summary>
    public string? SecondColor { get; private set; }

    /// <summary>
    /// The careers pages' heading font, by its id (<c>roboto-slab</c>), or null for ScreenHire's, Bricolage Grotesque. One
    /// of the bundled six (<see cref="HeadingFonts.Bundled"/>) or a <see cref="HostedFont"/>, which the branding save
    /// downloads first (docs/architecture/branding.md section 3).
    /// </summary>
    public string? HeadingFontId { get; private set; }

    /// <summary>The heading font's family name (<c>Roboto Slab</c>), kept with its id so History can name it.</summary>
    public string? HeadingFontFamily { get; private set; }

    /// <summary>The heading font, or null for ScreenHire's.</summary>
    public HeadingFont? HeadingFont => HeadingFontId is { } id ? new HeadingFont(id, HeadingFontFamily ?? id) : null;

    /// <summary>
    /// The brand images in use, each a <see cref="BrandImage"/> of the same kind. Not foreign keys: Postgres checks a
    /// foreign key without row-level security, so a key would accept another organization's image. The branding save
    /// looks each one up within the tenant instead (docs/architecture/branding.md).
    /// </summary>
    public Guid? LogoImageId { get; private set; }

    public Guid? FaviconImageId { get; private set; }

    public Guid? SharingImageId { get; private set; }

    /// <summary>
    /// The organization opted in to the cross-employer Signal Network (ADR-0005): contributing is the price of querying.
    /// Off by default, and it does nothing while the network's feature flag is off. Like every column here it is readable
    /// by any tenant under row-level security (organizations are public), so it never goes into a DTO; when the network
    /// is built, consent moves to a tenant-scoped table.
    /// </summary>
    public bool SignalNetworkOptIn { get; set; }

    /// <summary>The page size every new organization starts with.</summary>
    public const int DefaultQuestionsPerPage = 8;

    public const int MinQuestionsPerPage = 1;

    public const int MaxQuestionsPerPage = 50;

    /// <summary>
    /// How many questions a candidate sees on each page of a questionnaire that has no sections (the apply form and
    /// follow-up questionnaires). Careers pages show it anyway, so it is no secret that every tenant can read the row.
    /// </summary>
    public int QuestionsPerPage { get; private set; } = DefaultQuestionsPerPage;

    /// <summary>
    /// The sites allowed to show the careers pages in a frame (<see cref="EmbedOrigins"/>), as origins, in the order they
    /// were added. Empty, the default, lets only the careers pages themselves frame them. The careers pages send the list
    /// in their <c>frame-ancestors</c> header, so it is no secret that every tenant can read the row.
    /// </summary>
    public List<string> EmbedOrigins { get; private set; } = [];

    /// <summary>
    /// Replaces the sites that may embed the careers pages with <paramref name="origins"/>, normalized and without
    /// duplicates. Throws <see cref="ArgumentException"/> for one that is not an allowed origin or for too many.
    /// </summary>
    public void SetEmbedOrigins(IEnumerable<string> origins, bool allowLoopback) =>
        EmbedOrigins = Organizations.EmbedOrigins.NormalizeAll(origins, allowLoopback);

    /// <summary>
    /// The setup steps the organization said it does not need ("Not needed" in the bell), in the order they were skipped.
    /// They leave the checklist for every Owner and Admin on every device; each stays reachable from its settings page.
    /// Nothing secret, so it is fine that every tenant can read the row.
    /// </summary>
    public List<SetupStepId> SkippedSetupSteps { get; private set; } = [];

    /// <summary>Whether someone hid the whole setup checklist for the organization. Settings, General shows it again.</summary>
    public bool SetupHidden { get; private set; }

    /// <summary>Takes <paramref name="step"/> off the setup checklist. Skipping a step twice changes nothing.</summary>
    public void SkipSetupStep(SetupStepId step)
    {
        if (!SkippedSetupSteps.Contains(step))
        {
            // A new list, so EF sees the column change.
            SkippedSetupSteps = [.. SkippedSetupSteps, step];
        }
    }

    /// <summary>Hides the whole setup checklist for the organization.</summary>
    public void HideSetup() => SetupHidden = true;

    /// <summary>Brings the setup checklist back as it started: shown, with every step the organization skipped.</summary>
    public void ShowSetupAgain()
    {
        SetupHidden = false;
        SkippedSetupSteps = [];
    }

    /// <summary>The careers-page palette, or null when the organization has no brand colour.</summary>
    public BrandPalette? GetBrandPalette() => BrandColor is null ? null : BrandPalette.Derive(BrandColor);

    /// <summary>Sets or clears the brand colour; throws <see cref="FormatException"/> unless it is <c>#rrggbb</c>.</summary>
    public void SetBrandColor(string? color) => BrandColor = color is null ? null : BrandPalette.Normalize(color);

    /// <summary>Sets the page size; throws <see cref="ArgumentOutOfRangeException"/> outside 1 to 50.</summary>
    public void SetQuestionsPerPage(int questionsPerPage)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(questionsPerPage, MinQuestionsPerPage);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(questionsPerPage, MaxQuestionsPerPage);
        QuestionsPerPage = questionsPerPage;
    }

    /// <summary>
    /// Replaces the whole branding. Colours must be <c>#rrggbb</c> (throws <see cref="FormatException"/>); the caller has
    /// already checked their contrast and that each image is this organization's, of its kind and servable.
    /// </summary>
    public void SetBranding(
        string? brandColor, string? secondColor, HeadingFont? headingFont, Guid? logoImageId, Guid? faviconImageId, Guid? sharingImageId)
    {
        if (headingFont is not null && (!HeadingFonts.IsValidId(headingFont.Id) || !HeadingFonts.IsValidFamily(headingFont.Family)))
        {
            throw new ArgumentException("A heading font needs a valid id and family name.", nameof(headingFont));
        }

        SetBrandColor(brandColor);
        SecondColor = secondColor is null ? null : BrandPalette.Normalize(secondColor);
        HeadingFontId = headingFont?.Id;
        HeadingFontFamily = headingFont?.Family;
        LogoImageId = logoImageId;
        FaviconImageId = faviconImageId;
        SharingImageId = sharingImageId;
    }

    /// <summary>The image in use for <paramref name="kind"/>, or null.</summary>
    public Guid? ImageId(BrandImageKind kind) => kind switch
    {
        BrandImageKind.Logo => LogoImageId,
        BrandImageKind.Favicon => FaviconImageId,
        _ => SharingImageId,
    };
}
