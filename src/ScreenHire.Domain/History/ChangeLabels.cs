using System.Globalization;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Domain.History;

/// <summary>
/// The words the dashboard uses for each kind of thing, so a history line names it as the editor does. They mirror
/// the label maps in <c>apps/dashboard/src/lib/workflow.ts</c> and <c>format.ts</c>; <c>ChangeLabelsTests</c> fails
/// when the two drift apart, so change both together.
/// </summary>
public static class ChangeLabels
{
    public static string Question(QuestionType type) => type switch
    {
        QuestionType.ShortText => "Short answer",
        QuestionType.LongText => "Long answer",
        QuestionType.YesNo => "Yes or no",
        QuestionType.SingleChoice => "One choice",
        QuestionType.MultiChoice => "Several choices",
        QuestionType.Number => "Number",
        QuestionType.Url => "Link",
        QuestionType.File => "File upload",
        _ => type.ToString(),
    };

    public static string Stage(StageKind kind) => kind.ToString();

    public static string Trigger(TriggerType type) => type switch
    {
        TriggerType.ApplicationSubmitted => "An application arrives",
        TriggerType.StageEntered => "A candidate enters a stage",
        TriggerType.StageExited => "A candidate leaves a stage",
        TriggerType.TimeElapsed => "Time passes in a stage",
        TriggerType.QuestionnaireCompleted => "A follow-up questionnaire is answered",
        TriggerType.IntegrityAssessed => "The integrity checks finish",
        TriggerType.ReviewerDecision => "A reviewer records a decision",
        TriggerType.SlaBreached => "A stage's time limit passes",
        TriggerType.Manual => "Someone runs it by hand",
        TriggerType.VerificationCompleted => "A verification finishes",
        TriggerType.CandidateReplied => "The candidate replies",
        TriggerType.InterviewScheduled => "An interview is booked",
        _ => type.ToString(),
    };

    public static string Action(ActionType type) => type switch
    {
        ActionType.SendEmail => "Send an email",
        ActionType.SendQuestionnaire => "Send a questionnaire",
        ActionType.MoveToStage => "Move to a stage",
        ActionType.AddTag => "Add a tag",
        ActionType.AssignReviewer => "Assign a reviewer",
        ActionType.NotifyTeam => "Notify the team",
        ActionType.CreateTask => "Create a task",
        ActionType.CallWebhook => "Call a webhook",
        ActionType.QueueForRejectionReview => "Queue for rejection review",
        ActionType.Reject => "Reject",
        ActionType.RequestVerification => "Request a verification",
        ActionType.ScheduleInterview => "Book an interview",
        _ => type.ToString(),
    };

    /// <summary>The policy's name: the part of the dashboard's label before its explanation.</summary>
    public static string AiUse(AiUsePolicy policy) => policy switch
    {
        AiUsePolicy.Allowed => "Allowed",
        AiUsePolicy.AllowedWithDisclosure => "Allowed with disclosure",
        AiUsePolicy.NotAllowed => "Not allowed",
        _ => policy.ToString(),
    };

    public static string Employment(EmploymentType type) => type switch
    {
        EmploymentType.FullTime => "Full-time",
        EmploymentType.PartTime => "Part-time",
        _ => type.ToString(),
    };

    public static string Workplace(WorkplaceType type) => type == WorkplaceType.Onsite ? "On-site" : type.ToString();

    public static string Period(SalaryPeriod period) => period switch
    {
        SalaryPeriod.Hour => "an hour",
        SalaryPeriod.Day => "a day",
        SalaryPeriod.Week => "a week",
        SalaryPeriod.Month => "a month",
        _ => "a year",
    };

    public static string OnOff(bool value) => value ? "on" : "off";

    public static string Days(int days) => days == 1 ? "1 day" : $"{days.ToString(CultureInfo.InvariantCulture)} days";

    public static string Number(decimal value) => value.ToString("#,0.##", CultureInfo.InvariantCulture);

    /// <summary>A pay range in words: "70,000 to 85,000 GBP a year", "from 30,000 GBP a year".</summary>
    public static string? Salary(JobSalary? salary)
    {
        if (salary is null)
        {
            return null;
        }

        var period = Period(salary.Period);
        return (salary.Min, salary.Max) switch
        {
            ({ } min, { } max) => $"{Number(min)} to {Number(max)} {salary.Currency} {period}",
            ({ } min, null) => $"from {Number(min)} {salary.Currency} {period}",
            (null, { } max) => $"up to {Number(max)} {salary.Currency} {period}",
            _ => $"{salary.Currency} {period}",
        };
    }
}
