using System.Text;
using System.Text.Json;
using ScreenHire.Api.Endpoints;
using ScreenHire.Domain.Candidates;

namespace ScreenHire.UnitTests.Email;

/// <summary>
/// The pure parts of the email provider's webhooks (docs/architecture/email.md): the inbound credentials, the envelope the
/// recipient is read from, the headers that mark an automatic answer, and which events flag a candidate's address.
/// </summary>
public sealed class EmailWebhookEndpointsTests
{
    private const string Secret = "unit-test-inbound-secret-0123456789";

    private static readonly Guid Organization = Guid.Parse("0197a3c4-5b6d-7e8f-9a0b-1c2d3e4f5a6b");
    private static readonly Guid Send = Guid.Parse("0197a3c4-aaaa-7bbb-8ccc-ddddeeeeffff");

    private static string Basic(string credentials) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));

    [Fact]
    public void The_user_sendgrid_with_the_inbound_secret_as_password_is_accepted()
    {
        EmailWebhookEndpoints.BasicCredentialMatches(Basic($"sendgrid:{Secret}"), Secret).Should().BeTrue();
        EmailWebhookEndpoints.BasicCredentialMatches(Basic("sendgrid:se:cret"), "se:cret").Should().BeTrue("the password may hold a colon");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Basic")]
    [InlineData("Basic !!!not-base64!!!")]
    [InlineData("Bearer c2VuZGdyaWQ6dW5pdC10ZXN0LWluYm91bmQtc2VjcmV0LTAxMjM0NTY3ODk=")]
    public void A_missing_or_malformed_authorization_header_is_refused(string authorization)
    {
        EmailWebhookEndpoints.BasicCredentialMatches(authorization, Secret).Should().BeFalse();
    }

    [Fact]
    public void Another_user_or_another_password_is_refused()
    {
        EmailWebhookEndpoints.BasicCredentialMatches(Basic($"someone:{Secret}"), Secret).Should().BeFalse();
        EmailWebhookEndpoints.BasicCredentialMatches(Basic($"sendgrid:{Secret}x"), Secret).Should().BeFalse();
        EmailWebhookEndpoints.BasicCredentialMatches(Basic("sendgrid:"), Secret).Should().BeFalse();
        EmailWebhookEndpoints.BasicCredentialMatches(Basic($"sendgrid {Secret}"), Secret).Should().BeFalse("no colon");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void With_no_secret_configured_nothing_is_accepted_not_even_an_empty_password(string? secret)
    {
        EmailWebhookEndpoints.BasicCredentialMatches(Basic("sendgrid:"), secret).Should().BeFalse();
    }

    [Fact]
    public void The_envelope_names_the_addresses_that_reached_the_provider()
    {
        EmailWebhookEndpoints.EnvelopeRecipients("""{"to":["a@reply.example.com","b@reply.example.com"],"from":"c@example.org"}""")
            .Should().Equal("a@reply.example.com", "b@reply.example.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"to":"a@example.com"}""")]
    [InlineData("""{"to":[]}""")]
    [InlineData("[1,2]")]
    public void An_envelope_without_a_list_of_recipients_has_none(string? envelope)
    {
        EmailWebhookEndpoints.EnvelopeRecipients(envelope).Should().BeEmpty();
    }

    [Fact]
    public void Only_text_recipients_count_and_at_most_twenty_are_read()
    {
        EmailWebhookEndpoints.EnvelopeRecipients("""{"to":[1,null,"a@example.com"]}""").Should().Equal("a@example.com");

        var many = "{\"to\":[" + string.Join(',', Enumerable.Range(0, 30).Select(i => $"\"r{i}@example.com\"")) + "]}";
        EmailWebhookEndpoints.EnvelopeRecipients(many).Should().HaveCount(20);
    }

    [Fact]
    public void The_message_id_is_read_from_the_headers_whatever_their_case()
    {
        EmailWebhookEndpoints.MessageIdOf("Received: from x\r\nMessage-ID: <abc.123@mail.example.org>\r\nSubject: Re: hi")
            .Should().Be("<abc.123@mail.example.org>");
        EmailWebhookEndpoints.MessageIdOf("message-id:   <lower@mail.example.org>").Should().Be("<lower@mail.example.org>");
        EmailWebhookEndpoints.MessageIdOf("Subject: no id here").Should().BeNull();
        EmailWebhookEndpoints.MessageIdOf("In-Reply-To: <other@mail.example.org>").Should().BeNull("only the message's own id counts");
    }

    [Theory]
    [InlineData("Auto-Submitted: auto-replied", "Re: Next steps", true)]
    [InlineData("Auto-Submitted: auto-generated", "Re: Next steps", true)]
    [InlineData("Auto-Submitted: no", "Re: Next steps", false)]
    [InlineData("Precedence: bulk", "Re: Next steps", true)]
    [InlineData("Precedence: auto_reply", "Re: Next steps", true)]
    [InlineData("Precedence: list", "Re: Next steps", false)]
    [InlineData("X-Autoreply: yes", "Re: Next steps", true)]
    [InlineData("Subject: Re: x", "Automatic reply: Next steps", true)]
    [InlineData("Subject: Re: x", "Out of office until Monday", true)]
    [InlineData("Subject: Re: x", "Re: Next steps", false)]
    [InlineData("", "", false)]
    public void An_out_of_office_or_other_automatic_answer_is_recognised_by_its_headers_or_subject(string headers, string subject, bool automatic)
    {
        EmailWebhookEndpoints.IsAutomatic(headers, subject).Should().Be(automatic);
    }

    private static List<EmailWebhookEndpoints.DeliveryProblem> Parse(string json) =>
        EmailWebhookEndpoints.DeliveryProblem.Parse(Encoding.UTF8.GetBytes(json));

    private static string Event(string kind, string? type = null, string? reason = null, string email = "casey.morgan@example.com", bool withArguments = true)
    {
        var fields = new List<string> { $"\"event\":\"{kind}\"", $"\"email\":\"{email}\"" };
        if (type is not null)
        {
            fields.Add($"\"type\":\"{type}\"");
        }

        if (reason is not null)
        {
            fields.Add($"\"reason\":{JsonSerializer.Serialize(reason)}");
        }

        if (withArguments)
        {
            fields.Add($"\"organizationId\":\"{Organization:D}\"");
            fields.Add($"\"emailId\":\"{Send:D}\"");
        }

        return "{" + string.Join(',', fields) + "}";
    }

    [Fact]
    public void A_hard_bounce_flags_the_address_with_the_providers_reason()
    {
        var problems = Parse($"[{Event("bounce", "bounce", "550 5.1.1 mailbox not found")}]");

        problems.Should().ContainSingle().Which.Should().Be(new EmailWebhookEndpoints.DeliveryProblem(
            Organization, Send, "casey.morgan@example.com", CandidateEmailIssue.Bounced, "550 5.1.1 mailbox not found"));
    }

    [Fact]
    public void A_bounce_of_type_blocked_is_a_soft_failure_and_is_ignored()
    {
        Parse($"[{Event("bounce", "blocked", "blocked by recipient server")}]").Should().BeEmpty();
        Parse($"[{Event("bounce", "BLOCKED", "blocked")}]").Should().BeEmpty();
    }

    [Fact]
    public void A_bounce_with_no_type_still_counts_as_a_bounce()
    {
        Parse($"[{Event("bounce", reason: "550")}]").Should().ContainSingle().Which.Issue.Should().Be(CandidateEmailIssue.Bounced);
    }

    [Fact]
    public void A_drop_counts_only_when_the_reason_is_a_known_bounced_address()
    {
        Parse($"[{Event("dropped", reason: "Bounced Address")}]").Should().ContainSingle().Which.Issue.Should().Be(CandidateEmailIssue.Bounced);
        Parse($"[{Event("dropped", reason: "bounced address")}]").Should().ContainSingle();
        Parse($"[{Event("dropped", reason: "Spam Content (if from spam submission)")}]").Should().BeEmpty();
        Parse($"[{Event("dropped", reason: "Invalid")}]").Should().BeEmpty();
        Parse($"[{Event("dropped")}]").Should().BeEmpty();
    }

    [Fact]
    public void A_spam_report_flags_a_complaint()
    {
        Parse($"[{Event("spamreport")}]").Should().ContainSingle().Which.Issue.Should().Be(CandidateEmailIssue.SpamComplaint);
    }

    [Theory]
    [InlineData("delivered")]
    [InlineData("processed")]
    [InlineData("deferred")]
    [InlineData("open")]
    [InlineData("click")]
    [InlineData("unsubscribe")]
    public void Every_other_event_is_not_a_problem(string kind)
    {
        Parse($"[{Event(kind)}]").Should().BeEmpty();
    }

    [Fact]
    public void An_event_without_our_custom_arguments_or_an_address_is_ignored()
    {
        Parse($"[{Event("bounce", "bounce", withArguments: false)}]").Should().BeEmpty("it is not about a candidate email");
        Parse($"[{{\"event\":\"bounce\",\"organizationId\":\"{Organization:D}\",\"emailId\":\"{Send:D}\"}}]").Should().BeEmpty();
        Parse($"[{{\"event\":\"bounce\",\"email\":\"a@example.com\",\"organizationId\":\"nope\",\"emailId\":\"{Send:D}\"}}]").Should().BeEmpty();
    }

    [Fact]
    public void A_batch_yields_a_problem_for_each_flagging_event_and_skips_the_rest()
    {
        var problems = Parse($"[{Event("delivered")},{Event("bounce", "bounce")},{Event("spamreport")},{Event("bounce", "blocked")}]");

        problems.Select(p => p.Issue).Should().Equal(CandidateEmailIssue.Bounced, CandidateEmailIssue.SpamComplaint);
    }

    [Fact]
    public void A_long_reason_is_cut_to_300_characters()
    {
        Parse($"[{Event("bounce", "bounce", new string('x', 500))}]").Single().Reason!.Length.Should().Be(300);
    }

    [Fact]
    public void A_body_that_is_not_a_list_has_no_problems_and_a_body_that_is_not_json_throws_for_the_endpoint_to_answer_400()
    {
        Parse("{}").Should().BeEmpty();
        Parse("[]").Should().BeEmpty();
        var parse = () => Parse("not json");
        parse.Should().Throw<JsonException>();
    }
}
