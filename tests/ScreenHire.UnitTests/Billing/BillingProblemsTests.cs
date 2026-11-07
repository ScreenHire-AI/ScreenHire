using Microsoft.AspNetCore.Http;
using ScreenHire.Api.Endpoints;
using ScreenHire.Domain.Billing;

namespace ScreenHire.UnitTests.Billing;

/// <summary>
/// The 402 a refused publish returns: a <c>type</c> the dashboard recognises and a <c>detail</c> in plain words that says
/// how many jobs the plan allows, how many are open, and that open jobs stay open.
/// </summary>
public sealed class BillingProblemsTests
{
    [Fact]
    public void A_full_plan_is_a_402_plan_limit_that_names_the_numbers()
    {
        var problem = BillingProblems.Refused(new PublishDecision(PublishDecisionKind.LimitReached, Limit: 3, OpenJobs: 3));

        problem.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
        problem.ProblemDetails.Type.Should().Be("urn:screenhire:problem:plan-limit");
        problem.ProblemDetails.Detail.Should().Contain("allows 3 open jobs").And.Contain("3 are open").And.Contain("stay open");
    }

    [Fact]
    public void A_plan_of_one_job_reads_in_the_singular()
    {
        var problem = BillingProblems.Refused(new PublishDecision(PublishDecisionKind.LimitReached, Limit: 1, OpenJobs: 1));

        problem.ProblemDetails.Detail.Should().Contain("allows 1 open job and 1 is open");
    }

    [Fact]
    public void An_ended_subscription_is_a_402_subscription_ended_that_says_open_jobs_stay_open()
    {
        var problem = BillingProblems.Refused(new PublishDecision(PublishDecisionKind.SubscriptionEnded, Limit: 50, OpenJobs: 2));

        problem.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
        problem.ProblemDetails.Type.Should().Be("urn:screenhire:problem:subscription-ended");
        problem.ProblemDetails.Detail.Should().Contain("subscription has ended").And.Contain("open jobs stay open");
    }

    [Fact]
    public void An_allowed_decision_has_no_refusal()
    {
        var act = () => BillingProblems.Refused(new PublishDecision(PublishDecisionKind.Allowed));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
