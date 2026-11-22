using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>
/// The three ways a candidate picks a time and changing between them (docs/architecture/interview-scheduling.md, "Three ways
/// to pick a time"): what each option needs, the candidate's suggestions, asking again, and moving a booked interview.
/// </summary>
public sealed class InterviewOptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowStart = Now;
    private static readonly DateTimeOffset WindowEnd = Now.AddDays(14);

    private static DateTimeOffset At(int day, int hour) => new(2026, 11, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_request_made_without_an_option_picks_from_our_times_in_its_working_hours()
    {
        var request = Requested();

        request.Option.Should().Be(InterviewOption.OurTimes);
        request.UseBlocks.Should().BeFalse();
        request.EffectiveHours.Should().BeEquivalentTo(WeeklyHours.Weekdays(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        request.NamedTimes().Should().BeEmpty();
    }

    [Fact]
    public void Each_option_takes_only_what_it_needs()
    {
        Problem(new InterviewPick(InterviewOption.OurSuggestion)).Should().Be("Pick one or 2 times for the candidate.");
        Problem(new InterviewPick(InterviewOption.OurSuggestion, ProposedTimes: [At(3, 10), At(4, 10), At(5, 10)])).Should().Be("Pick one or 2 times for the candidate.");
        Problem(new InterviewPick(InterviewOption.TheirTimes, ProposedTimes: [At(3, 10)])).Should().Be("Only We'll suggest a time has picked times.");
        Problem(new InterviewPick(InterviewOption.TheirTimes, ExtraTimes: [At(3, 10)])).Should().Be("Only Pick from our times has one-off times.");
        Problem(new InterviewPick(InterviewOption.TheirTimes, UseBlocks: true)).Should().Be("Interview blocks need their days and hours.");
        Problem(new InterviewPick(InterviewOption.OurTimes, UseBlocks: true)).Should().Be("Interview blocks need their days and hours.");
        Problem(new InterviewPick(InterviewOption.OurTimes, ExtraTimes: [At(1, 10)])).Should().Be("Each time must be in the future.");
        Problem(new InterviewPick(InterviewOption.OurTimes, ExtraTimes: [At(20, 10)])).Should().Be("Each time must be inside the days the candidate picks from.");
        Problem(new InterviewPick(InterviewOption.OurSuggestion, ProposedTimes: [At(3, 10), At(3, 10)])).Should().Be("Each time can be picked once.");
        Problem(new InterviewPick(InterviewOption.OurSuggestion, ProposedTimes: [At(3, 10).AddSeconds(30)])).Should().Be("Times are in whole minutes.");
        Problem(new InterviewPick(InterviewOption.OurSuggestion, ProposedTimes: [At(3, 10), At(4, 15)])).Should().BeNull();
    }

    [Fact]
    public void The_candidate_suggests_three_to_five_allowed_times_and_the_next_step_is_the_team()
    {
        var request = Requested(new InterviewPick(InterviewOption.TheirTimes));
        var allowed = Enumerable.Range(3, 6).Select(day => At(day, 10)).ToList();

        InterviewRequest.SuggestionProblem([At(3, 10), At(4, 10)], allowed).Should().Be(SuggestionFault.TooFew);
        InterviewRequest.SuggestionProblem([.. allowed], allowed).Should().Be(SuggestionFault.TooMany);
        InterviewRequest.SuggestionProblem([At(3, 10), At(4, 10), At(3, 11)], allowed).Should().Be(SuggestionFault.NotOffered);
        InterviewRequest.SuggestionProblem([At(3, 10)], [At(3, 10)]).Should().BeNull("with one allowed time, one is all there is");

        request.WaitsForTeam(Now).Should().BeFalse();
        request.SuggestTimes([At(5, 10), At(3, 10), At(4, 10)], allowed, Now);

        request.CandidateTimes.Should().Equal(At(3, 10), At(4, 10), At(5, 10));
        request.NamedTimes().Should().Equal(request.CandidateTimes);
        request.WaitsForTeam(Now).Should().BeTrue();
    }

    [Fact]
    public void Asking_for_more_times_clears_the_candidates_and_keeps_the_teams_message()
    {
        var request = Requested(new InterviewPick(InterviewOption.TheirTimes));
        var allowed = new[] { At(3, 10), At(4, 10), At(5, 10) };
        request.SuggestTimes(allowed, allowed, Now);

        request.AskForMoreTimes("  Could you do an afternoon?  ", Now);

        request.CandidateTimes.Should().BeEmpty();
        request.TeamNote.Should().Be("Could you do an afternoon?");
        request.WaitsForTeam(Now).Should().BeFalse();
        var act = () => request.AskForMoreTimes(null, Now);
        act.Should().Throw<InvalidOperationException>("there are no times to ask about");
    }

    [Fact]
    public void The_candidate_asks_for_another_time_once_and_the_teams_times_stop_being_offered()
    {
        var request = Requested(new InterviewPick(InterviewOption.OurSuggestion, ProposedTimes: [At(3, 10)]));
        request.NamedTimes().Should().Equal(At(3, 10));

        request.AskForAnotherTime("Mornings are hard for me", Now);

        request.WaitsForTeam(Now).Should().BeTrue();
        request.CandidateNote.Should().Be("Mornings are hard for me");
        request.NamedTimes().Should().BeEmpty();
        var again = () => request.AskForAnotherTime(null, Now);
        again.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Changing_the_option_starts_it_afresh_and_keeps_the_lookup_span()
    {
        var request = Requested(new InterviewPick(InterviewOption.OurSuggestion, ProposedTimes: [At(3, 10)]));
        request.AskForAnotherTime("Mornings are hard", Now);
        var later = Now.AddHours(1);

        request.ChangeOption(new InterviewPick(InterviewOption.TheirTimes), Now.AddDays(1), Now.AddDays(30), later);

        request.Option.Should().Be(InterviewOption.TheirTimes);
        request.ProposedTimes.Should().BeEmpty();
        request.CandidateNote.Should().BeNull();
        request.AnotherTimeAskedAt.Should().BeNull();
        request.OptionChangedAt.Should().Be(later);
        request.WindowStart.Should().Be(Now.AddDays(1));
        request.LookupStart.Should().Be(WindowStart, "the lookup span only widens");
        request.LookupEnd.Should().Be(Now.AddDays(30));
        request.NewTimesOfferedAt.Should().BeNull("an open interview has nothing booked to move");
    }

    [Fact]
    public void Moving_a_booked_interview_with_another_option_keeps_the_booked_time_until_a_new_one()
    {
        var request = Requested();
        request.Book(At(3, 10), Now);
        var revision = request.Revision;

        request.ChangeOption(new InterviewPick(InterviewOption.OurSuggestion, ProposedTimes: [At(10, 14)]), Now, At(11, 0), Now);

        request.Status.Should().Be(InterviewStatus.Booked);
        request.StartsAt.Should().Be(At(3, 10));
        request.Revision.Should().Be(revision);
        request.HasPendingNewTimes(Now).Should().BeTrue();
        request.IsPicking(Now).Should().BeTrue();

        request.Reschedule(At(10, 14), Now);
        request.HasPendingNewTimes(Now).Should().BeFalse();
        request.IsPicking(Now).Should().BeFalse();
    }

    [Fact]
    public void A_cancelled_interview_cannot_change_how_the_candidate_picks()
    {
        var request = Requested();
        request.Cancel(Now, byCandidate: false);

        var act = () => request.ChangeOption(new InterviewPick(InterviewOption.TheirTimes), WindowStart, WindowEnd, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    private static string? Problem(InterviewPick pick) => InterviewRequest.PickProblem(pick, 30, WindowStart, WindowEnd, Now);

    private static InterviewRequest Requested(InterviewPick? pick = null)
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Support engineer", Slug = "support-engineer" };
        var candidate = Candidate.Create(organization, "Grace", "Adeyemi", "grace@example.com", null, Now);
        var (application, _) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Now, null,
            new ApplicationIntake(ApplicationSource.CareersSite, []));
        return InterviewRequest.Create(application, 30, WindowStart, WindowEnd, "UTC", null, Now, pick: pick);
    }
}
