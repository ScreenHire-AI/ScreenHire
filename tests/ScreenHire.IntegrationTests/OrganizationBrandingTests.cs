using System.Buffers.Binary;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ScreenHire.Application.Branding;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// <c>/v1/organization/branding</c> (docs/architecture/branding.md): colours checked before they save, the heading
/// font, and brand images uploaded straight to storage, checked, scanned, saved and served to the careers pages.
/// </summary>
public sealed class OrganizationBrandingTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_owner_sets_both_colours_and_a_font_and_the_careers_pages_get_them()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var saved = (await owner.PutAsync("/v1/organization/branding", new JsonObject
        {
            ["brandColor"] = "#4338CA",
            ["secondColor"] = "#E9DCC4",
            ["headingFont"] = "Fraunces",
        })).Expect(HttpStatusCode.OK);

        saved.Text("brandColor").Should().Be("#4338ca");
        saved.Text("secondColor").Should().Be("#e9dcc4");
        saved.Text("headingFont").Should().Be("Fraunces");
        (await owner.GetAsync("/v1/organization/branding")).Expect(HttpStatusCode.OK).Text("brandColor").Should().Be("#4338ca");

        var branding = (await PublicOrganizationAsync(owner)).GetProperty("branding");
        branding.At("colors.brand").GetString().Should().Be("#4338ca");
        branding.At("colors.onBrand").GetString().Should().Be("#ffffff");
        branding.At("header.background").GetString().Should().Be("#e9dcc4");
        branding.At("header.foreground").GetString().Should().Be("#231f1a");
        branding.At("headingFont").GetString().Should().Be("Fraunces");
        branding.At("logo").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_colour_that_fails_is_refused_with_the_pair_and_the_nearest_shade_that_passes()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var check = (await owner.GetAsync("/v1/organization/branding/contrast?brandColor=%23ffd400&secondColor=%23808080")).Expect(HttpStatusCode.OK);
        check.At("brandColor.passes").GetBoolean().Should().BeFalse();
        check.At("secondColor.passes").GetBoolean().Should().BeFalse();
        var shade = check.Text("brandColor.suggestion");

        var refused = (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["brandColor"] = "#ffd400", ["secondColor"] = "#808080" }))
            .Expect(HttpStatusCode.BadRequest);
        refused.ProblemKeys().Should().BeEquivalentTo("brandColor", "secondColor");
        refused.Text("errors.brandColor.0").Should().StartWith("Links in #ffd400 are hard to read on the page")
            .And.EndWith($"{shade} is the nearest shade that passes.");
        (await owner.GetAsync("/v1/organization/branding")).Expect(HttpStatusCode.OK).At("brandColor").ValueKind
            .Should().Be(JsonValueKind.Null, "nothing saved");

        (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["brandColor"] = shade })).Expect(HttpStatusCode.OK)
            .Text("brandColor").Should().Be(shade);
    }

    [Fact]
    public async Task A_malformed_colour_is_refused_and_null_clears_the_branding()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["brandColor"] = "#123456", ["headingFont"] = "Inter" })).Expect(HttpStatusCode.OK);

        var refused = (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["brandColor"] = "red; background:url(x)" }))
            .Expect(HttpStatusCode.BadRequest);
        var cleared = (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["brandColor"] = null })).Expect(HttpStatusCode.OK);

        refused.ProblemKeys().Should().Equal("brandColor");
        cleared.At("brandColor").ValueKind.Should().Be(JsonValueKind.Null);
        cleared.At("headingFont").ValueKind.Should().Be(JsonValueKind.Null, "a save replaces the whole branding");
        (await PublicOrganizationAsync(owner)).At("branding.colors").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_logo_goes_to_storage_is_scanned_and_reaches_the_careers_pages_on_save()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var png = Png(400, 100);

        var image = await UploadAsync(owner, "Logo", "logo.png", png);
        image.Text("scanStatus").Should().Be("Pending");
        image.Text("fileName").Should().Be("logo.png", "the tile on Settings names the file");
        image.At("url").ValueKind.Should().Be(JsonValueKind.Null, "an image is not served before its scan");
        var id = image.Text("id");
        var scanned = await ScannedAsync(owner, id);
        scanned.Text("scanStatus").Should().Be("NotScanned", "no scanner is configured locally");

        var saved = (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["logoId"] = id })).Expect(HttpStatusCode.OK);
        saved.Text("logo.id").Should().Be(id);

        var organization = await PublicOrganizationAsync(owner);
        var slug = organization.GetProperty("slug").GetString();
        var logo = organization.At("branding.logo");
        logo.GetProperty("url").GetString().Should().EndWith($"/brand/images/{id}.png").And.Contain(slug);
        logo.GetProperty("width").GetInt32().Should().Be(400);
        logo.GetProperty("height").GetInt32().Should().Be(100);

        using var anonymous = fixture.CreateHttpClient("api");
        using var served = await anonymous.GetAsync($"/v1/public/organizations/{slug}/branding/images/{id}", Ct);
        served.StatusCode.Should().Be(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        served.Headers.CacheControl!.ToString().Should().Contain("immutable");
        served.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        (await served.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(png);

        var history = (await owner.GetAsync("/v1/organization/branding/history")).Expect(HttpStatusCode.OK);
        history.Text("entries.0.lines.0.text").Should().Be("added a logo");
    }

    // An image that goes into the branding is marked saved for good; one nobody saves is removed a day after its upload,
    // blob and row, along with blobs nobody ever verified. The sweep runs one organization at a time.
    [Fact]
    public async Task The_daily_sweep_removes_uploads_nobody_saved_and_keeps_the_rest()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var neighbour = await fixture.SignedInWithOrganizationAsync();
        var saved = (await UploadAsync(owner, "Logo", "logo.png", Png(400, 100))).Text("id");
        var abandoned = (await UploadAsync(owner, "SharingImage", "share.png", Png(1200, 630))).Text("id");
        var fresh = (await UploadAsync(owner, "Favicon", "icon.png", Png(64, 64))).Text("id");
        var unverified = await TicketAndPutAsync(owner, "Logo", "stray.png", Png(400, 100));
        var neighbours = (await UploadAsync(neighbour, "Logo", "logo.png", Png(400, 100))).Text("id");
        await ScannedAsync(owner, saved);
        await ScannedAsync(neighbour, neighbours);
        (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["logoId"] = saved })).Expect(HttpStatusCode.OK);

        await using (var db = await fixture.CreateDbContextAsync(TenantOf(owner.OrganizationId), Ct))
        {
            (await db.BrandImages.AsNoTracking().Where(i => i.Id == Guid.Parse(saved)).Select(i => i.SavedAt).SingleAsync(Ct))
                .Should().NotBeNull("saving the branding marks the image saved");
            (await db.BrandImages.AsNoTracking().Where(i => i.Id == Guid.Parse(abandoned)).Select(i => i.SavedAt).SingleAsync(Ct))
                .Should().BeNull("an upload nobody saved stays unsaved");
        }

        // Two days old, the saved logo among them: saving once keeps an image for good, even after a newer logo replaces it.
        await using (var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct)))
        {
            await connection.OpenAsync(Ct);
            await using var age = new NpgsqlCommand("UPDATE brand_images SET uploaded_at = now() - interval '2 days' WHERE id = ANY(@ids)", connection);
            age.Parameters.AddWithValue("ids", new[] { Guid.Parse(saved), Guid.Parse(abandoned), Guid.Parse(neighbours) });
            await age.ExecuteNonQueryAsync(Ct);
        }

        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        var bus = sender.Services.GetRequiredService<IMessageBus>();

        // What the sweep sends for this organization, a day after the upload.
        await bus.PublishAsync(new PurgeOrganizationBrandImageUploads(owner.OrganizationId, DateTimeOffset.UtcNow.AddDays(-1)));
        await WolverineStore.WaitUntilAsync(
            async () => (await owner.GetAsync($"/v1/organization/branding/images/{abandoned}")).Status == HttpStatusCode.NotFound,
            "the sweep to remove the abandoned image", Ct);
        // Verifying an upload whose blob is still there would keep it again, so a refusal means the blob went too.
        (await owner.PostAsync($"/v1/organization/branding/images/{abandoned}/verify")).Expect(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/v1/organization/branding/images/{saved}")).Expect(HttpStatusCode.OK);
        (await owner.GetAsync($"/v1/organization/branding/images/{fresh}")).Expect(HttpStatusCode.OK);
        (await neighbour.GetAsync($"/v1/organization/branding/images/{neighbours}")).Expect(HttpStatusCode.OK);
        await ExpectServedAsync(neighbour, neighbours);

        // Later, with the unverified upload and the fresh one past the day: both go, the saved logo stays.
        await bus.PublishAsync(new PurgeOrganizationBrandImageUploads(owner.OrganizationId, DateTimeOffset.UtcNow.AddHours(1)));
        await WolverineStore.WaitUntilAsync(
            async () => (await owner.GetAsync($"/v1/organization/branding/images/{fresh}")).Status == HttpStatusCode.NotFound,
            "the sweep to remove the image nobody saved", Ct);
        (await owner.PostAsync($"/v1/organization/branding/images/{unverified}/verify")).Expect(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/v1/organization/branding/images/{saved}")).Expect(HttpStatusCode.OK);
        (await owner.GetAsync("/v1/organization/branding")).Expect(HttpStatusCode.OK).Text("logo.id").Should().Be(saved);
        (await neighbour.GetAsync($"/v1/organization/branding/images/{neighbours}")).Expect(HttpStatusCode.OK);
        await ExpectServedAsync(neighbour, neighbours);
        await ExpectServedAsync(owner, saved);

        // The schedule's own message goes to every organization, so the neighbour's upload, two days old and never saved,
        // goes too.
        await bus.PublishAsync(new PurgeExpiredBrandImageUploads());
        await WolverineStore.WaitUntilAsync(
            async () => (await neighbour.GetAsync($"/v1/organization/branding/images/{neighbours}")).Status == HttpStatusCode.NotFound,
            "the daily sweep to reach the other organization", Ct);
        await ExpectServedAsync(owner, saved);
    }

    // The row is only half of an image: the public route streams the blob, so this fails if the blob went.
    private async Task ExpectServedAsync(Tenant tenant, string imageId)
    {
        var slug = (await tenant.Session.CurrentUserAsync(Ct))!.Value.At("organization.slug").GetString();
        using var anonymous = fixture.CreateHttpClient("api");
        using var served = await anonymous.GetAsync($"/v1/public/organizations/{slug}/branding/images/{imageId}", Ct);
        served.StatusCode.Should().Be(HttpStatusCode.OK, "the image's blob is still in storage");
    }

    private static TenantContext TenantOf(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return tenant;
    }

    [Fact]
    public async Task Verifying_an_upload_that_was_already_kept_returns_it_as_it_is()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var id = await TicketAndPutAsync(owner, "Logo", "logo.png", Png(200, 50));

        var first = (await owner.PostAsync($"/v1/organization/branding/images/{id}/verify")).Expect(HttpStatusCode.OK);
        var again = (await owner.PostAsync($"/v1/organization/branding/images/{id}/verify")).Expect(HttpStatusCode.OK);

        again.Text("id").Should().Be(first.Text("id"));
        again.At("width").GetInt32().Should().Be(200);
        again.At("height").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task Files_that_are_not_a_fitting_png_or_jpeg_are_refused_and_removed()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        (await owner.PostAsync("/v1/organization/branding/images", new JsonObject { ["kind"] = "Logo", ["fileName"] = "logo.svg", ["sizeBytes"] = 100 }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("fileName");
        (await owner.PostAsync("/v1/organization/branding/images", new JsonObject { ["kind"] = "Favicon", ["fileName"] = "icon.png", ["sizeBytes"] = 300_000 }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("sizeBytes");

        var gif = await TicketAndPutAsync(owner, "Logo", "logo.png", Encoding.ASCII.GetBytes("GIF89a not really a png"));
        var notPng = (await owner.PostAsync($"/v1/organization/branding/images/{gif}/verify")).Expect(HttpStatusCode.BadRequest);
        notPng.Text("errors.file.0").Should().Be("This file is not a PNG or JPEG image. Upload a PNG or JPEG.");
        (await owner.PostAsync($"/v1/organization/branding/images/{gif}/verify")).Expect(HttpStatusCode.NotFound); // the refused file was deleted

        var oblong = await TicketAndPutAsync(owner, "Favicon", "icon.png", Png(64, 32));
        (await owner.PostAsync($"/v1/organization/branding/images/{oblong}/verify")).Expect(HttpStatusCode.BadRequest)
            .Text("errors.file.0").Should().Be("A favicon is square, and this image is 64 by 32 pixels.");
    }

    [Fact]
    public async Task An_image_still_scanning_blocked_or_another_organizations_is_never_saved_or_served()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var pending = await AddImageAsync(owner.OrganizationId, MalwareScanStatus.Pending);
        var infected = await AddImageAsync(owner.OrganizationId, MalwareScanStatus.Infected);
        var theirs = await AddImageAsync(other.OrganizationId, MalwareScanStatus.Clean);

        (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["logoId"] = pending.ToString() })).Expect(HttpStatusCode.BadRequest)
            .Text("errors.logoId.0").Should().Be("The logo is still being scanned for malware. Save again in a moment.");
        (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["logoId"] = infected.ToString() })).Expect(HttpStatusCode.BadRequest)
            .Text("errors.logoId.0").Should().Be("The logo did not pass the malware scan. Upload another file.");
        (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["logoId"] = theirs.ToString() })).Expect(HttpStatusCode.BadRequest)
            .Text("errors.logoId.0").Should().Be("This logo is not one of your uploads. Upload it again.");
        (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["faviconId"] = (await AddImageAsync(owner.OrganizationId, MalwareScanStatus.Clean)).ToString() }))
            .Expect(HttpStatusCode.BadRequest).Text("errors.faviconId.0").Should().Be("This image was uploaded as another kind. Upload it as the favicon.");
        (await owner.GetAsync($"/v1/organization/branding/images/{theirs}")).Expect(HttpStatusCode.NotFound);

        var slug = (await PublicOrganizationAsync(owner)).GetProperty("slug").GetString();
        using var anonymous = fixture.CreateHttpClient("api");
        foreach (var id in new[] { pending, infected, theirs })
        {
            using var response = await anonymous.GetAsync($"/v1/public/organizations/{slug}/branding/images/{id}", Ct);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, id.ToString());
        }
    }

    [Fact]
    public async Task An_email_to_a_candidate_starts_with_the_logo_at_its_one_public_address()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var id = (await UploadAsync(owner, "Logo", "logo.png", Png(400, 100))).Text("id");
        await ScannedAsync(owner, id);
        (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["logoId"] = id, ["secondColor"] = "#1f4e5a" })).Expect(HttpStatusCode.OK);

        var preview = (await owner.PostAsync("/v1/email-templates/preview", new JsonObject { ["subject"] = "Hello", ["body"] = "Hi {{ candidateName }}" }))
            .Expect(HttpStatusCode.OK);
        var me = (await owner.Session.CurrentUserAsync(Ct))!.Value.GetProperty("email").GetString()!;
        var marker = Guid.NewGuid().ToString("N")[..8];
        (await owner.PostAsync("/v1/email-templates/test", new JsonObject { ["subject"] = $"Logo {marker}", ["body"] = "Hi {{ candidateName }}" }))
            .Expect(HttpStatusCode.OK);

        var src = $"{(await PublicOrganizationAsync(owner)).At("branding.logo.url").GetString()}";
        src.Should().NotContain("?");
        preview.Text("html").Should().Contain($"<img src=\"{src}\"").And.Contain("background: #1f4e5a;");
        var received = await WaitForSubjectAsync(me, marker);
        received.Html.Should().Contain($"<img src=\"{src}\"");
        received.Text.Should().NotContain("brand/images");
    }

    private async Task<JsonElement> PublicOrganizationAsync(Tenant owner)
    {
        using var anonymous = fixture.CreateHttpClient("api");
        var slug = (await owner.Session.CurrentUserAsync(Ct))!.Value.At("organization.slug").GetString();
        return await anonymous.GetFromJsonAsync<JsonElement>($"/v1/public/organizations/{slug}", Ct);
    }

    // As the dashboard does it: a link from the API, the bytes straight to storage, then the API's check.
    private static async Task<ApiResult> UploadAsync(Tenant owner, string kind, string fileName, byte[] content)
    {
        var id = await TicketAndPutAsync(owner, kind, fileName, content);
        return (await owner.PostAsync($"/v1/organization/branding/images/{id}/verify")).Expect(HttpStatusCode.OK);
    }

    private static async Task<string> TicketAndPutAsync(Tenant owner, string kind, string fileName, byte[] content)
    {
        var ticket = (await owner.PostAsync("/v1/organization/branding/images", new JsonObject
        {
            ["kind"] = kind,
            ["fileName"] = fileName,
            ["sizeBytes"] = content.Length,
        })).Expect(HttpStatusCode.OK);
        await CareersUploads.PutAsync(ticket.Body, content);
        return ticket.Text("uploadId");
    }

    private static async Task<ApiResult> ScannedAsync(Tenant owner, string id)
    {
        ApiResult? image = null;
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                image = (await owner.GetAsync($"/v1/organization/branding/images/{id}")).Expect(HttpStatusCode.OK);
                return image.Text("scanStatus") != "Pending";
            },
            "the worker to scan the brand image",
            Ct);
        return image!;
    }

    private async Task<Guid> AddImageAsync(Guid organizationId, MalwareScanStatus status)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        var image = new BrandImage
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Kind = BrandImageKind.Logo,
            BlobName = $"{organizationId}/{Guid.NewGuid()}/Logo/logo.png",
            ContentType = "image/png",
            SizeBytes = 33,
            Width = 400,
            Height = 100,
            UploadedAt = DateTimeOffset.UtcNow,
        };
        if (status != MalwareScanStatus.Pending)
        {
            image.RecordScan(status, DateTimeOffset.UtcNow);
        }

        db.BrandImages.Add(image);
        await db.SaveChangesAsync(Ct);
        return image.Id;
    }

    private async Task<MailpitMessage> WaitForSubjectAsync(string address, string marker)
    {
        MailpitMessage? match = null;
        await WolverineStore.WaitUntilAsync(
            async () => (match = (await fixture.Mailpit.MessagesAsync(address, Ct)).FirstOrDefault(m => m.Subject.Contains(marker, StringComparison.Ordinal))) is not null,
            "the test email to arrive",
            Ct);
        return match!;
    }

    // The smallest bytes the API reads as a PNG of this size: the signature and the IHDR chunk.
    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }
}
