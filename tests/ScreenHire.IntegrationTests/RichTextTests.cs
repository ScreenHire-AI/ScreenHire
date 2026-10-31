using System.Text.Json.Nodes;
using Npgsql;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Content;
using ScreenHire.Infrastructure.Persistence.Migrations;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Rich text through the running API (docs/architecture/rich-text.md): job descriptions and email bodies are stored as
/// sanitized HTML kept to the editor's elements, plain text still reads as paragraphs and lists, a save that changes
/// nothing records nothing, and the migration's conversion of plain text gives the same canonical HTML.
/// </summary>
public sealed class RichTextTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_api_clients_plain_text_description_becomes_paragraphs_and_a_list_and_old_tags_become_the_editors()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("ATS sync", ["Read", "Write"])).Text("token"));
        var job = Samples.RemoteJob("Support Engineer");
        job["description"] = "We help small teams.\n\n- Remote\n- Flexible hours\n\nApply & say hi.";

        var created = (await key.PostAsync("/v1/jobs", job)).Expect(HttpStatusCode.Created);

        created.Text("description").Should().Be(
            "<p>We help small teams.</p><ul><li>Remote</li><li>Flexible hours</li></ul><p>Apply &amp; say hi.</p>");

        job["description"] = "<h1>Role</h1><p><b>Bold</b> <i>italic</i> <u>plain</u></p><p><a href=\"javascript:alert(1)\">x</a></p>";
        var updated = (await key.PutAsync($"/v1/jobs/{created.Id()}", job)).Expect(HttpStatusCode.OK);
        updated.Text("description").Should().Be("<h2>Role</h2><p><strong>Bold</strong> <em>italic</em> plain</p><p>x</p>");
    }

    [Fact]
    public async Task Saving_the_same_description_in_other_markup_keeps_it_and_records_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = Samples.RemoteJob("Platform Engineer");
        job["description"] = "<h2>About</h2><p>We build tools.</p><ul><li>Remote</li></ul>";
        var created = await owner.CreateJobAsync(job);
        await owner.PublishJobAsync(created.Id());
        var historyBefore = (await owner.GetAsync($"/v1/jobs/{created.Id()}/history")).Expect(HttpStatusCode.OK).Count("entries");

        // As the editor writes it: list items in paragraphs, whitespace between blocks, an empty last paragraph.
        job["description"] = "<h2>About</h2>\n<p>We build   tools.</p>\n<ul><li><p>Remote</p></li></ul><p></p>";
        var saved = (await owner.PutAsync($"/v1/jobs/{created.Id()}", job)).Expect(HttpStatusCode.OK);

        saved.Text("description").Should().Be("<h2>About</h2><p>We build tools.</p><ul><li>Remote</li></ul>");
        saved.At("hasUnpublishedChanges").GetBoolean().Should().BeFalse("only the markup differs");
        (await owner.GetAsync($"/v1/jobs/{created.Id()}/history")).Expect(HttpStatusCode.OK).Count("entries").Should().Be(historyBefore);
    }

    [Fact]
    public async Task An_email_template_stores_its_body_sanitized_and_refuses_an_empty_or_oversized_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var created = (await owner.PostAsync("/v1/email-templates", new JsonObject
        {
            ["name"] = "Interview",
            ["subject"] = "Interview for {{ jobTitle }}",
            ["body"] = "<h2>Hi {{ firstName }}</h2><p onclick=\"x()\">See you <b>soon</b>.</p><script>steal()</script>",
        })).Expect(HttpStatusCode.Created);
        created.Text("body").Should().Be("<h2>Hi {{ firstName }}</h2><p>See you <strong>soon</strong>.</p>");

        var plain = (await owner.PostAsync("/v1/email-templates", new JsonObject
        {
            ["name"] = "Plain",
            ["subject"] = "Hello",
            ["body"] = "Hi {{ firstName }},\n\nThanks.",
        })).Expect(HttpStatusCode.Created);
        plain.Text("body").Should().Be("<p>Hi {{ firstName }},</p><p>Thanks.</p>");

        (await owner.PostAsync("/v1/email-templates", new JsonObject { ["name"] = "Empty", ["subject"] = "Hi", ["body"] = "<p> </p><h2></h2>" }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("body");
        var tooLong = string.Concat(Enumerable.Repeat("<p>Hello there</p>", EmailTemplate.MaxBodyLength / 17 + 1));
        (await owner.PostAsync("/v1/email-templates", new JsonObject { ["name"] = "Long", ["subject"] = "Hi", ["body"] = tooLong }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("body");
    }

    [Fact]
    public async Task Saving_a_built_in_email_in_other_markup_adds_no_row_of_changes()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var original = (await owner.GetAsync($"/v1/email-templates/{EmailTemplateIds.StageChange}")).Expect(HttpStatusCode.OK);

        var saved = (await owner.PutAsync($"/v1/email-templates/{EmailTemplateIds.StageChange}", new JsonObject
        {
            ["subject"] = original.Text("subject"),
            ["body"] = original.Text("body").Replace("</p><p>", "</p>\n<p>", StringComparison.Ordinal),
        })).Expect(HttpStatusCode.OK);

        saved.Text("body").Should().Be(original.Text("body"));
        (await owner.GetAsync($"/v1/email-templates/{EmailTemplateIds.StageChange}/history")).Expect(HttpStatusCode.OK)
            .Count("entries").Should().Be(0);
    }

    public static TheoryData<string, bool, string> PlainText() => new()
    {
        { "Hi {{ firstName }},\n\nThank you.", true, "<p>Hi {{ firstName }},</p><p>Thank you.</p>" },
        { "Line one\r\nline two\n\n\n\nNext", true, "<p>Line one<br>line two</p><p>Next</p>" },
        { "Perks:\n\n- Remote\n- Pension\n\n- Books\n\nThanks", true, "<p>Perks:</p><ul><li>Remote</li><li>Pension</li><li>Books</li></ul><p>Thanks</p>" },
        { "Tom & Jerry <3 {% if stageName == \"Offer\" %}yes{% endif %}", true, "<p>Tom &amp; Jerry &lt;3 {% if stageName == \"Offer\" %}yes{% endif %}</p>" },
        { "  Spaced    out  \n\n- item", true, "<p>Spaced out</p><ul><li>item</li></ul>" },
        { "AT&amp;T\n\nSecond", false, "<p>AT&amp;T</p><p>Second</p>" },
        // A comparison inside Liquid is entity-encoded like any text, as the sanitizer writes it; the renderer decodes it.
        { "{% if stageName > \"A\" %}Next{% endif %}", true, "<p>{% if stageName &gt; \"A\" %}Next{% endif %}</p>" },
        { "", true, "" },
    };

    [Theory]
    [MemberData(nameof(PlainText))]
    public async Task The_migrations_conversion_gives_the_canonical_html(string text, bool escape, string html)
    {
        await using var connection = new NpgsqlConnection(await fixture.GetAppConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using (var create = new NpgsqlCommand(ConvertPlainTextToRichText.PlainTextToHtml, connection))
        {
            await create.ExecuteNonQueryAsync(Ct);
        }

        await using var convert = new NpgsqlCommand("SELECT pg_temp.plain_text_to_html(@text, @escape)", connection);
        convert.Parameters.AddWithValue("text", text);
        convert.Parameters.AddWithValue("escape", escape);
        var converted = (string)(await convert.ExecuteScalarAsync(Ct))!;

        converted.Should().Be(html);
        RichText.Sanitize(converted).Should().Be(converted, "a converted body is already canonical, so its first save records nothing");
    }
}
