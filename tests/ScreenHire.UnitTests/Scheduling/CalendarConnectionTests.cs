using ScreenHire.Domain.Scheduling;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>A member's calendar connection: connecting, connecting again, refreshing tokens and recording that it stopped working.</summary>
public sealed class CalendarConnectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly Guid organization = Guid.CreateVersion7();
    private readonly Guid user = Guid.CreateVersion7();

    private CalendarConnection Connected() =>
        CalendarConnection.Connect(organization, user, CalendarProvider.Google, "avery@example.com", "access-1", "refresh-1", Now.AddHours(1), Now);

    [Fact]
    public void Connecting_records_the_member_provider_account_and_tokens()
    {
        var connection = Connected();

        connection.OrganizationId.Should().Be(organization);
        connection.UserId.Should().Be(user);
        connection.Provider.Should().Be(CalendarProvider.Google);
        connection.AccountEmail.Should().Be("avery@example.com");
        connection.ProtectedAccessToken.Should().Be("access-1");
        connection.ProtectedRefreshToken.Should().Be("refresh-1");
        connection.AccessTokenExpiresAt.Should().Be(Now.AddHours(1));
        connection.ConnectedAt.Should().Be(Now);
        connection.Problem.Should().BeNull();
        connection.ProblemAt.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_account_address_is_no_address(string? email)
    {
        CalendarConnection.Connect(organization, user, CalendarProvider.Sandbox, email, "a", "r", Now.AddHours(1), Now)
            .AccountEmail.Should().BeNull();
    }

    [Fact]
    public void An_account_address_is_trimmed_and_kept_within_its_column()
    {
        var tooLong = new string('a', 400) + "@example.com";

        CalendarConnection.Connect(organization, user, CalendarProvider.Sandbox, "  avery@example.com ", "a", "r", Now.AddHours(1), Now)
            .AccountEmail.Should().Be("avery@example.com");
        CalendarConnection.Connect(organization, user, CalendarProvider.Sandbox, tooLong, "a", "r", Now.AddHours(1), Now)
            .AccountEmail.Should().HaveLength(CalendarConnection.MaxAccountEmailLength);
    }

    [Fact]
    public void A_problem_is_recorded_once_with_the_time_it_first_happened()
    {
        var connection = Connected();

        connection.RecordProblem(Now.AddMinutes(5));
        connection.RecordProblem(Now.AddMinutes(10));

        connection.Problem.Should().Be(CalendarProblem.NeedsReconnect);
        connection.ProblemAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void Connecting_again_replaces_the_account_and_tokens_and_clears_the_problem()
    {
        var connection = Connected();
        connection.RecordProblem(Now.AddMinutes(5));

        connection.Reconnect(CalendarProvider.Microsoft, null, "access-2", "refresh-2", Now.AddHours(3), Now.AddDays(1));

        connection.Provider.Should().Be(CalendarProvider.Microsoft);
        connection.AccountEmail.Should().BeNull();
        connection.ProtectedAccessToken.Should().Be("access-2");
        connection.ProtectedRefreshToken.Should().Be("refresh-2");
        connection.AccessTokenExpiresAt.Should().Be(Now.AddHours(3));
        connection.ConnectedAt.Should().Be(Now.AddDays(1));
        connection.Problem.Should().BeNull();
        connection.ProblemAt.Should().BeNull();
    }

    [Fact]
    public void A_refresh_keeps_the_refresh_token_unless_the_provider_rotated_it()
    {
        var connection = Connected();

        connection.Refreshed("access-2", null, Now.AddHours(2));
        connection.ProtectedAccessToken.Should().Be("access-2");
        connection.ProtectedRefreshToken.Should().Be("refresh-1");
        connection.AccessTokenExpiresAt.Should().Be(Now.AddHours(2));

        connection.Refreshed("access-3", "refresh-2", Now.AddHours(3));
        connection.ProtectedRefreshToken.Should().Be("refresh-2");
    }
}
