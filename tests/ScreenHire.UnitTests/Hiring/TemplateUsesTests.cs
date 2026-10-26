using ScreenHire.Domain.Hiring;

namespace ScreenHire.UnitTests.Hiring;

/// <summary>
/// What a workflow uses from the template library, and the "Not used in 6 months" rule the Templates page filters by
/// (design system section 21).
/// </summary>
public sealed class TemplateUsesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Each_automation_counts_once_for_each_email_it_sends_and_reject_and_questionnaire_actions_count_for_their_built_ins()
    {
        var own = Guid.CreateVersion7().ToString("D");
        var workflow = HiringWorkflow.Default with
        {
            Automations =
            [
                new("welcome", "Welcome", new(TriggerType.ApplicationSubmitted), null,
                    [new(ActionType.SendEmail, EmailTemplate: EmailTemplateIds.ApplicationReceived), new(ActionType.SendEmail, EmailTemplate: EmailTemplateIds.ApplicationReceived)]),
                new("invite", "Invite", new(TriggerType.StageEntered, StageId: "interview"), null,
                    [new(ActionType.SendEmail, EmailTemplate: own), new(ActionType.AddTag, Tag: "invited")]),
                new("reject", "Reject without right to work", new(TriggerType.ApplicationSubmitted), null, [new(ActionType.Reject, Reason: "Right to work")]),
                new("follow-up", "Ask for availability", new(TriggerType.ApplicationSubmitted), null, [new(ActionType.SendQuestionnaire, QuestionnaireId: "availability")]),
                new("notify", "Tell the team", new(TriggerType.ApplicationSubmitted), null, [new(ActionType.NotifyTeam)]),
            ],
        };

        TemplateUses.EmailUses(workflow).Should().Equal(
            new EmailUse(EmailTemplateIds.ApplicationReceived, "welcome", "Welcome"),
            new EmailUse(own, "invite", "Invite"),
            new EmailUse(EmailTemplateIds.ApplicationRejected, "reject", "Reject without right to work"),
            new EmailUse(EmailTemplateIds.QuestionnaireRequest, "follow-up", "Ask for availability"));
    }

    [Theory]
    [InlineData(null, 400, true)]
    [InlineData(181, 400, true)]
    [InlineData(180, 400, true)]
    [InlineData(179, 400, false)]
    [InlineData(null, 179, false)]
    [InlineData(null, 180, true)]
    public void Not_used_recently_means_nothing_used_it_in_180_days_and_the_team_has_had_it_longer(int? usedDaysAgo, int hadDays, bool expected) =>
        TemplateUses.NotUsedRecently(usedDaysAgo is { } days ? Now.AddDays(-days) : null, Now.AddDays(-hadDays), Now).Should().Be(expected);
}
