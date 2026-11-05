using System.Text.Json;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Privacy;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Privacy;

namespace ScreenHire.UnitTests.Privacy;

/// <summary>
/// The copy of a candidate's data (docs/architecture/data-requests.md, "The copy"): what the file holds, what it leaves out,
/// the emails of the data request answers, and the validators of the team's routes.
/// </summary>
public sealed class CandidateDataExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Colleague = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static Question[] Questions =>
    [
        new("why", QuestionType.LongText, "Why this job?"),
        new("level", QuestionType.SingleChoice, "Experience level", Options: [new QuestionOption("senior", "Senior"), new QuestionOption("junior", "Junior")]),
        new("tools", QuestionType.MultiChoice, "Tools you use", Options: [new QuestionOption("git", "Git"), new QuestionOption("sql", "SQL")]),
        new("resume", QuestionType.File, "Resume or CV"),
    ];

    private static DataExportSource Source(IReadOnlyList<ApplicationAnswer>? answers = null, string? savedNote = "Strong on systems") =>
        new(
            "Rowan Ellis",
            "rowan@example.com",
            "+1 555 0100",
            Now.AddMonths(-3),
            [
                new DataExportApplication(
                    "Backend Engineer",
                    Now.AddMonths(-2),
                    "Interview",
                    Questions,
                    answers ??
                    [
                        new("why", "I like the product"),
                        new("level", "senior"),
                        new("tools", Values: ["git", "sql"]),
                        new("resume", "0199f0c4-0000-7000-8000-000000000009"),
                        new("removed-question", "kept with its id"),
                    ],
                    "2026-10",
                    AskedToBeKeptForFutureRoles: true,
                    [new DataExportEmail("rowan@example.com", "Thanks for applying", "We got your application.", Now.AddMonths(-2))],
                    [new DataExportReply("rowan@example.com", "Re: Thanks for applying", "Happy to talk.", Now.AddMonths(-2).AddDays(1), ["portfolio.pdf"])],
                    [new DataExportNote($"Ask about @[{Colleague}] later", Now.AddMonths(-1))],
                    [new DataExportFile("Resume or CV", "rowan-resume.pdf")]),
            ],
            savedNote);

    private static JsonElement Parse(DataExportSource source) => JsonDocument.Parse(CandidateDataExport.Build(source, Now)).RootElement;

    [Fact]
    public void The_file_holds_the_candidates_details_and_each_application_in_camel_case()
    {
        var file = Parse(Source());

        file.GetProperty("generatedAt").GetDateTimeOffset().Should().Be(Now);
        var candidate = file.GetProperty("candidate");
        candidate.GetProperty("name").GetString().Should().Be("Rowan Ellis");
        candidate.GetProperty("email").GetString().Should().Be("rowan@example.com");
        candidate.GetProperty("phone").GetString().Should().Be("+1 555 0100");
        var application = file.GetProperty("applications").EnumerateArray().Single();
        application.GetProperty("jobTitle").GetString().Should().Be("Backend Engineer");
        application.GetProperty("stage").GetString().Should().Be("Interview");
        application.GetProperty("privacyNoticeVersion").GetString().Should().Be("2026-10");
        application.GetProperty("askedToBeKeptForFutureRoles").GetBoolean().Should().BeTrue();
        file.GetProperty("savedCandidateNote").GetString().Should().Be("Strong on systems");
    }

    [Fact]
    public void Answers_carry_their_question_labels_and_choices_their_option_labels()
    {
        var answers = Parse(Source()).GetProperty("applications")[0].GetProperty("answers").EnumerateArray().ToList();

        answers.Should().HaveCount(4, "the resume answer is an internal upload id and is listed as a file instead");
        answers[0].GetProperty("question").GetString().Should().Be("Why this job?");
        answers[0].GetProperty("answer").GetString().Should().Be("I like the product");
        answers[1].GetProperty("answer").GetString().Should().Be("Senior");
        answers[2].GetProperty("answers").EnumerateArray().Select(a => a.GetString()).Should().Equal("Git", "SQL");
        answers[3].GetProperty("question").GetString().Should().Be("removed-question", "a question the form no longer has is shown by its id");
    }

    [Fact]
    public void Emails_replies_files_and_notes_are_listed_and_the_author_is_only_a_team_member()
    {
        var application = Parse(Source()).GetProperty("applications")[0];

        application.GetProperty("emailsSent")[0].GetProperty("subject").GetString().Should().Be("Thanks for applying");
        application.GetProperty("emailsSent")[0].GetProperty("body").GetString().Should().Be("We got your application.");
        var reply = application.GetProperty("repliesReceived")[0];
        reply.GetProperty("body").GetString().Should().Be("Happy to talk.");
        reply.GetProperty("attachments")[0].GetString().Should().Be("portfolio.pdf");
        application.GetProperty("uploadedFiles")[0].GetProperty("fileName").GetString().Should().Be("rowan-resume.pdf");
        var note = application.GetProperty("notes")[0];
        note.GetProperty("author").GetString().Should().Be("Team member");
        note.GetProperty("text").GetString().Should().Be("Ask about @Team member later", "a mention is a colleague's id and the copy names no colleague");
    }

    [Fact]
    public void The_file_has_no_verification_output_scorecards_or_internal_ids()
    {
        var json = System.Text.Encoding.UTF8.GetString(CandidateDataExport.Build(Source(), Now));

        json.Should().NotContainEquivalentOf("verification").And.NotContainEquivalentOf("scorecard").And.NotContainEquivalentOf("signal")
            .And.NotContainEquivalentOf("assessment").And.NotContain(Colleague.ToString())
            .And.NotContain("0199f0c4-0000-7000-8000-000000000009", "the upload id behind a file answer is internal");
        JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["generatedAt", "candidate", "applications", "savedCandidateNote"]);
    }

    [Fact]
    public void A_candidate_with_nothing_saved_has_no_saved_note_and_the_file_is_indented()
    {
        var bytes = CandidateDataExport.Build(Source(savedNote: null), Now);

        JsonDocument.Parse(bytes).RootElement.TryGetProperty("savedCandidateNote", out _).Should().BeFalse();
        System.Text.Encoding.UTF8.GetString(bytes).Should().Contain("\n  \"candidate\"");
    }

    [Fact]
    public void The_blob_name_is_stable_for_a_request()
    {
        var organization = Guid.Parse("0199f0c4-0000-7000-8000-000000000001");
        var request = Guid.Parse("0199f0c4-0000-7000-8000-000000000002");

        CandidateDataExport.BlobNameFor(organization, request).Should().Be($"{organization:N}/{request:N}.json");
        CandidateDataExport.BlobNameFor(organization, request).Should().Be(CandidateDataExport.BlobNameFor(organization, request));
    }

    [Fact]
    public void The_copy_email_says_what_it_is_and_when_it_expires()
    {
        var mail = DataRequestAnswerEmails.CopyEmail("rowan@example.com", "Brightwater <Studio>", "https://careers.example.com/data-request/download/abc", TimeSpan.FromDays(7));

        mail.To.Should().Be("rowan@example.com");
        mail.Subject.Should().Be("Your copy of your data from Brightwater <Studio>");
        mail.TextBody.Should().Contain("https://careers.example.com/data-request/download/abc").And.Contain("The link works for 7 days");
        mail.HtmlBody.Should().Contain("Brightwater &lt;Studio&gt;").And.NotContain("<Studio>");
    }

    [Theory]
    [InlineData(1, "1 day")]
    [InlineData(7, "7 days")]
    public void Whole_days_are_said_as_days(int days, string said) =>
        DataRequestAnswerEmails.Describe(TimeSpan.FromDays(days)).Should().Be(said);

    [Fact]
    public void Part_of_a_day_falls_back_to_hours_and_minutes() =>
        DataRequestAnswerEmails.Describe(TimeSpan.FromHours(6)).Should().Be("6 hours");

    [Theory]
    [InlineData(DataRequestKind.Copy, "a copy of your data")]
    [InlineData(DataRequestKind.Erasure, "erasure of your data")]
    public void The_decline_email_carries_the_teams_reason_in_screenhires_words(DataRequestKind kind, string what)
    {
        var mail = DataRequestAnswerEmails.DeclinedEmail("rowan@example.com", "Brightwater", kind, "We could not confirm it is you.\nPlease write to us <here>.");

        mail.TextBody.Should().Contain(what).And.Contain("We could not confirm it is you.\nPlease write to us <here>.").And.Contain("contact Brightwater directly");
        mail.HtmlBody.Should().Contain("Please write to us &lt;here&gt;").And.Contain("<br>").And.NotContain("<here>");
        mail.TextBody.Should().NotContainEquivalentOf("legal").And.NotContainEquivalentOf("law");
    }

    [Theory]
    [InlineData(DataRequestKind.Copy, "A candidate asked Brightwater for a copy of their data.")]
    [InlineData(DataRequestKind.Erasure, "A candidate asked Brightwater to erase their data.")]
    public void The_arrival_email_names_the_kind_and_the_page_and_nothing_about_the_candidate(DataRequestKind kind, string line)
    {
        var mail = DataRequestNotificationEmail.Compose("owner@example.com", "Brightwater", kind, new Uri("https://app.example.com/settings/data-requests"));

        mail.TextBody.Should().Contain(line).And.Contain("Settings > Data requests").And.Contain("https://app.example.com/settings/data-requests");
        mail.HtmlBody.Should().Contain(line);
    }

    [Theory]
    [InlineData("We cannot verify who you are.", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void A_decline_needs_a_reason(string reason, bool valid) =>
        new DeclineDataRequestRequestValidator().Validate(new DeclineDataRequestRequest(reason)).IsValid.Should().Be(valid);

    [Fact]
    public void A_decline_reason_is_at_most_500_characters_once_trimmed()
    {
        var validator = new DeclineDataRequestRequestValidator();

        validator.Validate(new DeclineDataRequestRequest(new string('a', 500) + "   ")).IsValid.Should().BeTrue();
        validator.Validate(new DeclineDataRequestRequest(new string('a', 501))).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(120, true)]
    [InlineData(0, false)]
    [InlineData(121, false)]
    [InlineData(-5, false)]
    public void Retention_is_one_to_120_months_or_off(int? months, bool valid) =>
        new UpdateRetentionRequestValidator().Validate(new UpdateRetentionRequest(months)).IsValid.Should().Be(valid);
}
