using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ScreenHire.Application.Privacy;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Privacy;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;
using static ScreenHire.IntegrationTests.CandidateTestSupport;
using static ScreenHire.IntegrationTests.DataRequests.DataRequestSupport;

namespace ScreenHire.IntegrationTests.DataRequests;

/// <summary>
/// The team's side of data requests (docs/architecture/data-requests.md): who may use the routes, the list, sending a copy
/// (the file, the emailed link, the sweep that deletes it), declining with a reason, and erasing through a request.
/// Each test makes its own organization and people no other test uses, so a scan of the whole database finds only its own rows.
/// </summary>
public sealed partial class DataRequestAdminTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Signed_out_and_organization_API_keys_get_401_on_every_route()
    {
        using var world = await fixture.NewWorldAsync();
        var requestId = await fixture.OpenRequestAsync(world.OrganizationId, world.CandidateId, DataRequestKind.Copy);
        using var anonymous = fixture.CreateHttpClient("api");
        using var key = new KeyClient(fixture, (await world.Owner.CreateKeyAsync("Everything", ["Read", "Write"])).Text("token"));

        (await anonymous.GetAsync("/v1/data-requests", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"/v1/data-requests/{requestId}/decline", new { reason = "No." }, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await key.GetAsync("/v1/data-requests")).Status.Should().Be(HttpStatusCode.Unauthorized, "only people use these routes");
        (await key.PostAsync($"/v1/data-requests/{requestId}/send-copy", new JsonObject())).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await world.Owner.ListedAsync(requestId))!.Value.GetProperty("status").GetString().Should().Be("Open");
    }

    [Fact]
    public async Task Recruiters_and_viewers_get_403_and_a_member_on_only_their_jobs_and_another_organization_get_404()
    {
        using var world = await fixture.NewWorldAsync();
        using var stranger = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(world.Owner, "Recruiter");
        using var viewer = await fixture.InviteAndJoinAsync(world.Owner, "Viewer");
        var copy = await fixture.OpenRequestAsync(world.OrganizationId, world.CandidateId, DataRequestKind.Copy);
        var erasure = await fixture.OpenRequestAsync(world.OrganizationId, world.CandidateId, DataRequestKind.Erasure);

        foreach (var member in new[] { recruiter, viewer })
        {
            (await member.GetAsync("/v1/data-requests")).Expect(HttpStatusCode.Forbidden);
            (await member.PostAsync($"/v1/data-requests/{copy}/send-copy")).Expect(HttpStatusCode.Forbidden);
            (await member.PostAsync($"/v1/data-requests/{erasure}/erase", new JsonObject { ["confirmation"] = world.Person.FullName })).Expect(HttpStatusCode.Forbidden);
            (await member.PostAsync($"/v1/data-requests/{copy}/decline", new JsonObject { ["reason"] = "No." })).Expect(HttpStatusCode.Forbidden);
        }

        // Another organization's request is nothing to them, whatever role they hold.
        (await stranger.PostAsync($"/v1/data-requests/{copy}/send-copy")).Expect(HttpStatusCode.NotFound);
        (await stranger.PostAsync($"/v1/data-requests/{erasure}/erase", new JsonObject { ["confirmation"] = world.Person.FullName })).Expect(HttpStatusCode.NotFound);
        (await stranger.PostAsync($"/v1/data-requests/{copy}/decline", new JsonObject { ["reason"] = "No." })).Expect(HttpStatusCode.NotFound);
        (await stranger.GetAsync("/v1/data-requests")).Expect(HttpStatusCode.OK).At("items").GetArrayLength().Should().Be(0);
        (await world.Owner.PostAsync($"/v1/data-requests/{Guid.NewGuid()}/send-copy")).Expect(HttpStatusCode.NotFound);

        // A custom role may hold Erase candidates, but a copy and an erasure reach every application: on Only jobs they're on, it is a 404.
        var roleId = (await world.Owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Compliance", ["copyFrom"] = "Viewer" })).Expect(HttpStatusCode.Created).Id();
        (await world.Owner.PutAsync($"/v1/organization/roles/{roleId}", new JsonObject
        {
            ["name"] = "Compliance",
            ["permissions"] = new JsonArray("ViewHiring", "EraseCandidates"),
        })).Expect(HttpStatusCode.OK);
        using var narrow = await fixture.InviteAndJoinAsync(world.Owner, "Viewer", jobAccess: "HiringTeamOnly");
        await world.Owner.GiveRoleAsync(narrow, roleId);
        (await narrow.GetAsync("/v1/data-requests")).Expect(HttpStatusCode.NotFound);
        (await narrow.PostAsync($"/v1/data-requests/{copy}/send-copy")).Expect(HttpStatusCode.NotFound);
        (await narrow.PostAsync($"/v1/data-requests/{erasure}/erase", new JsonObject { ["confirmation"] = world.Person.FullName })).Expect(HttpStatusCode.NotFound);
        (await narrow.PostAsync($"/v1/data-requests/{copy}/decline", new JsonObject { ["reason"] = "No." })).Expect(HttpStatusCode.NotFound);

        // An Admin sees every job and may answer.
        using var admin = await fixture.InviteAndJoinAsync(world.Owner, "Admin");
        (await admin.GetAsync("/v1/data-requests")).Expect(HttpStatusCode.OK).At("items").GetArrayLength().Should().Be(2);
        (await world.Owner.ListedAsync(copy))!.Value.GetProperty("status").GetString().Should().Be("Open", "no refused call changed anything");
    }

    [Fact]
    public async Task The_list_has_the_requests_earliest_due_date_first_with_the_candidate_while_they_exist()
    {
        using var world = await fixture.NewWorldAsync();
        var other = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var otherApplication = (await world.Owner.CreateApplicationAsync(world.JobId, other.Email, other.FullName)).Id();
        var otherCandidate = await CandidateOfAsync(world.Owner, otherApplication);
        var late = await fixture.OpenRequestAsync(world.OrganizationId, world.CandidateId, DataRequestKind.Copy, TimeSpan.FromDays(30));
        var overdue = await fixture.OpenRequestAsync(world.OrganizationId, otherCandidate, DataRequestKind.Erasure, TimeSpan.FromDays(30));
        var answered = await fixture.OpenRequestAsync(world.OrganizationId, otherCandidate, DataRequestKind.Copy, TimeSpan.FromDays(30));
        await fixture.OwnerSqlAsync(
            "UPDATE data_requests SET received_at = now() - interval '40 days', due_at = now() - interval '10 days' WHERE id = @id", ("id", overdue));
        await fixture.OwnerSqlAsync(
            "UPDATE data_requests SET status = 'Completed', resolved_at = now(), due_at = now() + interval '5 days' WHERE id = @id", ("id", answered));

        var list = (await world.Owner.GetAsync("/v1/data-requests")).Expect(HttpStatusCode.OK);

        list.At("openCount").GetInt32().Should().Be(2);
        var items = list.At("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("id").GetGuid()).Should().Equal([overdue, answered, late], "earliest due date first");
        var first = items[0];
        first.GetProperty("kind").GetString().Should().Be("Erasure");
        first.GetProperty("status").GetString().Should().Be("Open");
        first.GetProperty("overdue").GetBoolean().Should().BeTrue();
        first.GetProperty("candidate").GetProperty("id").GetGuid().Should().Be(otherCandidate);
        first.GetProperty("candidate").GetProperty("name").GetString().Should().Be(other.FullName);
        first.GetProperty("candidate").GetProperty("email").GetString().Should().Be(other.Email);
        items[1].GetProperty("overdue").GetBoolean().Should().BeFalse("an answered request is not overdue");
        items[1].GetProperty("resolvedAt").ValueKind.Should().Be(JsonValueKind.String);
        items[2].GetProperty("overdue").GetBoolean().Should().BeFalse();
        items[2].GetProperty("dueAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Sending_a_copy_builds_a_file_with_the_candidates_data_and_not_the_verification_output_and_the_link_leads_to_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await OpenJobWithQuestionAsync(owner);
        var person = SyntheticApplicants.ApplicationFarm[0].AsNewPerson();
        var motivation = $"I like payments work {Guid.NewGuid():N}";
        var applicationId = await VerificationSupport.ApplyAsync(
            fixture, slug, jobId, person, answers: [.. person.Answers, new ApplicationAnswer("motivation", motivation)]);
        var candidateId = await CandidateOfAsync(owner, applicationId);
        var note = $"Warm call, strong on queues {Guid.NewGuid():N}";
        (await owner.PostAsync($"/v1/applications/{applicationId}/notes", new JsonObject { ["note"] = note })).Expect(HttpStatusCode.Created);
        var emailSubject = $"Interview times {Guid.NewGuid():N}";
        var replyBody = $"Tuesday works for me {Guid.NewGuid():N}";
        await using (var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId))
        {
            db.ApplicationEmails.Add(new ApplicationEmail
            {
                OrganizationId = owner.OrganizationId, ApplicationId = applicationId, To = person.Email, Subject = emailSubject,
                TextBody = "Can you do Tuesday?", HtmlBody = "<p>Can you do Tuesday?</p>", CreatedAt = DateTimeOffset.UtcNow,
            });
            db.InboundEmails.Add(new InboundEmail
            {
                OrganizationId = owner.OrganizationId, ApplicationId = applicationId, SendId = Guid.NewGuid(), FromAddress = person.Email,
                Subject = "Re: " + emailSubject, TextBody = replyBody, HtmlBody = $"<p>{replyBody}</p>", ReceivedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        // Let verification settle so the file can be shown to hold none of it.
        await VerificationSupport.SettledAsync(fixture, owner, applicationId);
        var requestId = await fixture.OpenRequestAsync(owner.OrganizationId, candidateId, DataRequestKind.Copy);

        (await owner.PostAsync($"/v1/data-requests/{requestId}/send-copy")).Expect(HttpStatusCode.Accepted);

        var mail = await WaitForMailAsync(person.Email, "Your copy of your data from ");
        var download = DownloadLink().Match(mail.Text);
        download.Success.Should().BeTrue($"the email carries the download link: {mail.Text}");
        mail.Text.Should().Contain("The link works for 7 days");
        // The email goes just before the handler's transaction commits, and the link works from the commit.
        await WolverineStore.WaitUntilAsync(
            async () => (await owner.ListedAsync(requestId))?.GetProperty("status").GetString() == "Completed", "the sent copy to commit", Ct);
        using var client = fixture.CreateHttpClient("api");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}");
        var ready = await client.GetFromJsonAsync<JsonElement>(
            $"/v1/public/organizations/{slug}/data-requests/downloads/{download.Groups["token"].Value}", Ct);
        using var storage = new HttpClient();
        var readLink = ready.GetProperty("url").GetString()!;
        var json = await storage.GetStringAsync(readLink, Ct);

        json.Should().Contain(person.FullName).And.Contain(person.Email).And.Contain("Platform Engineer")
            .And.Contain("Why do you want this job?").And.Contain(motivation).And.Contain(note).And.Contain(emailSubject).And.Contain(replyBody)
            .And.Contain("Team member");
        json.Should().NotContainEquivalentOf("verification").And.NotContainEquivalentOf("scorecard").And.NotContain(owner.UserId.ToString());
        JsonDocument.Parse(json).RootElement.GetProperty("applications").GetArrayLength().Should().Be(1);

        var listed = (await owner.ListedAsync(requestId))!.Value;
        listed.GetProperty("status").GetString().Should().Be("Completed");
        listed.GetProperty("copySentAt").ValueKind.Should().Be(JsonValueKind.String);
        var log = (await owner.GetAsync("/v1/organization/membership-log")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "DataRequestCopySent");
        log.GetProperty("dataRequestId").GetGuid().Should().Be(requestId);
        log.GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null);
        (await TablesHoldingAsync(fixture, person.FullName, person.Email)).Should().NotContain(t => t.StartsWith("membership_events", StringComparison.Ordinal));

        // Answering twice is refused, and the file stays until the sweep.
        (await owner.PostAsync($"/v1/data-requests/{requestId}/send-copy")).Expect(HttpStatusCode.Conflict);
        (await owner.PostAsync($"/v1/data-requests/{requestId}/decline", new JsonObject { ["reason"] = "Too late." })).Expect(HttpStatusCode.Conflict);

        // The daily sweep deletes a copy past its window and forgets where it was.
        await fixture.OwnerSqlAsync("UPDATE data_requests SET export_expires_at = now() - interval '1 minute' WHERE id = @id", ("id", requestId));
        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        await sender.Services.GetRequiredService<IMessageBus>().PublishAsync(new PurgeOrganizationDataExports(owner.OrganizationId, DateTimeOffset.UtcNow));
        await WolverineStore.WaitUntilAsync(
            async () => await fixture.OwnerScalarAsync<string>("SELECT export_blob_name FROM data_requests WHERE id = @id", ("id", requestId)) is null,
            "the sweep to forget the expired copy", Ct);
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                using var gone = await storage.GetAsync(readLink, Ct);
                return gone.StatusCode == HttpStatusCode.NotFound;
            },
            "the sweep to delete the expired copy's blob", Ct);
        (await client.GetAsync($"/v1/public/organizations/{slug}/data-requests/downloads/{download.Groups["token"].Value}", Ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sending_a_copy_for_a_request_that_is_not_an_open_copy_is_refused_and_a_candidate_who_vanished_sends_nothing()
    {
        using var world = await fixture.NewWorldAsync();
        var erasure = await fixture.OpenRequestAsync(world.OrganizationId, world.CandidateId, DataRequestKind.Erasure);

        (await world.Owner.PostAsync($"/v1/data-requests/{erasure}/send-copy")).Expect(HttpStatusCode.Conflict);

        // The handler returns quietly when the request is gone, or is not an open copy: no dead letter, no email.
        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        var bus = sender.Services.GetRequiredService<IMessageBus>();
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        var missing = Guid.NewGuid();
        await bus.PublishAsync(new BuildDataCopy(world.OrganizationId, missing));
        await bus.PublishAsync(new BuildDataCopy(world.OrganizationId, erasure));
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(missing.ToString(), Ct) == 1 && await store.CountHandledAsync(erasure.ToString(), Ct) >= 1,
            "both messages to be handled", Ct);
        (await store.GetDeadLettersAsync(missing.ToString(), Ct)).Should().BeEmpty();
        (await store.GetDeadLettersAsync(erasure.ToString(), Ct)).Should().BeEmpty();
        (await fixture.Mailpit.MessagesAsync(world.Person.Email, Ct)).Should().NotContain(m => m.Subject.StartsWith("Your copy", StringComparison.Ordinal));
        (await fixture.OwnerScalarAsync<string>("SELECT status FROM data_requests WHERE id = @id", ("id", erasure))).Should().Be("Open");
    }

    [Fact]
    public async Task A_copy_waits_for_an_erasure_under_way_and_then_writes_nothing_and_emails_nothing()
    {
        using var world = await fixture.NewWorldAsync();
        var requestId = await fixture.OpenRequestAsync(world.OrganizationId, world.CandidateId, DataRequestKind.Copy);

        await BuildCopyWhileAnotherTransactionHoldsTheCandidateAsync(world, requestId, "DELETE FROM candidates WHERE id = @candidate");

        (await fixture.OwnerScalarAsync<string>("SELECT export_blob_name FROM data_requests WHERE id = @id", ("id", requestId))).Should().BeNull();
        (await fixture.OwnerScalarAsync<DateTime?>("SELECT copy_sent_at FROM data_requests WHERE id = @id", ("id", requestId))).Should().BeNull();
        (await fixture.Mailpit.MessagesAsync(world.Person.Email, Ct)).Should().NotContain(m => m.Subject.StartsWith("Your copy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_copy_waits_for_a_decline_under_way_and_then_leaves_the_request_declined()
    {
        using var world = await fixture.NewWorldAsync();
        var requestId = await fixture.OpenRequestAsync(world.OrganizationId, world.CandidateId, DataRequestKind.Copy);

        await BuildCopyWhileAnotherTransactionHoldsTheCandidateAsync(
            world, requestId, "UPDATE data_requests SET status = 'Declined', decline_reason = 'No.', resolved_at = now() WHERE candidate_id = @candidate");

        (await fixture.OwnerScalarAsync<string>("SELECT status FROM data_requests WHERE id = @id", ("id", requestId))).Should().Be("Declined");
        (await fixture.OwnerScalarAsync<string>("SELECT export_blob_name FROM data_requests WHERE id = @id", ("id", requestId))).Should().BeNull();
        (await fixture.Mailpit.MessagesAsync(world.Person.Email, Ct)).Should().NotContain(m => m.Subject.StartsWith("Your copy", StringComparison.Ordinal));
    }

    // Holds the candidate's row, as an erasure does, until the worker's copy build is seen waiting behind it, then runs the
    // other side of the race and commits. The wait is the proof: a handler that takes no lock never appears in it.
    private async Task BuildCopyWhileAnotherTransactionHoldsTheCandidateAsync(World world, Guid requestId, string otherSide)
    {
        await using (var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct)))
        {
            await connection.OpenAsync(Ct);
            await using var transaction = await connection.BeginTransactionAsync(Ct);
            await using (var hold = new NpgsqlCommand("SELECT 1 FROM candidates WHERE id = @candidate FOR UPDATE", connection, transaction))
            {
                hold.Parameters.AddWithValue("candidate", world.CandidateId);
                await hold.ExecuteNonQueryAsync(Ct);
            }

            (await world.Owner.PostAsync($"/v1/data-requests/{requestId}/send-copy")).Expect(HttpStatusCode.Accepted);
            await WolverineStore.WaitUntilAsync(
                async () => await fixture.OwnerScalarAsync<long>(
                    "SELECT count(*) FROM pg_stat_activity WHERE @holder = ANY(pg_blocking_pids(pid))", ("holder", connection.ProcessID)) > 0,
                "the copy build to wait for the candidate's row", Ct);

            await using (var other = new NpgsqlCommand(otherSide, connection, transaction))
            {
                other.Parameters.AddWithValue("candidate", world.CandidateId);
                await other.ExecuteNonQueryAsync(Ct);
            }

            await transaction.CommitAsync(Ct);
        }

        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(requestId.ToString(), Ct, "BuildDataCopy") > 0, "the copy build to finish", Ct);
        (await store.GetDeadLettersAsync(requestId.ToString(), Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Declining_needs_a_reason_emails_it_to_the_candidate_and_leaves_the_reason_out_of_the_team_log()
    {
        using var world = await fixture.NewWorldAsync();
        var requestId = await fixture.OpenRequestAsync(world.OrganizationId, world.CandidateId, DataRequestKind.Erasure);
        var reason = $"We must keep this record for a pending audit {Guid.NewGuid():N}";
        var path = $"/v1/data-requests/{requestId}/decline";

        (await world.Owner.PostAsync(path, new JsonObject { ["reason"] = "" })).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("reason");
        (await world.Owner.PostAsync(path, new JsonObject { ["reason"] = "   " })).Expect(HttpStatusCode.BadRequest);
        (await world.Owner.PostAsync(path, new JsonObject { ["reason"] = new string('x', 501) })).Expect(HttpStatusCode.BadRequest);
        (await world.Owner.ListedAsync(requestId))!.Value.GetProperty("status").GetString().Should().Be("Open", "a refused decline changes nothing");

        (await world.Owner.PostAsync(path, new JsonObject { ["reason"] = $"  {reason}  " })).Expect(HttpStatusCode.NoContent);

        var mail = await WaitForMailAsync(world.Person.Email, "About your request to ");
        mail.Text.Should().Contain(reason).And.Contain("erasure of your data");
        var listed = (await world.Owner.ListedAsync(requestId))!.Value;
        listed.GetProperty("status").GetString().Should().Be("Declined");
        listed.GetProperty("declineReason").GetString().Should().Be(reason);
        listed.GetProperty("resolvedAt").ValueKind.Should().Be(JsonValueKind.String);
        var log = (await world.Owner.GetAsync("/v1/organization/membership-log")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "DataRequestDeclined");
        log.GetProperty("actorUserId").GetGuid().Should().Be(world.Owner.UserId);
        log.GetProperty("dataRequestId").GetGuid().Should().Be(requestId);
        (await TablesHoldingAsync(fixture, reason)).Should().Equal([$"data_requests holds {reason}"], "only the request holds the reason, not the team log");

        // Once declined it is closed to every answer.
        (await world.Owner.PostAsync(path, new JsonObject { ["reason"] = "Again." })).Expect(HttpStatusCode.Conflict);
        (await world.Owner.PostAsync($"/v1/data-requests/{requestId}/erase", new JsonObject { ["confirmation"] = world.Person.FullName })).Expect(HttpStatusCode.Conflict);

        // One mail only, however the message was delivered.
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        (await fixture.Mailpit.MessagesAsync(world.Person.Email, Ct)).Count(m => m.Subject.StartsWith("About your request to ", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task Two_people_declining_one_request_at_once_get_one_204_and_one_409_and_the_candidate_one_email_and_the_log_one_entry()
    {
        using var world = await fixture.NewWorldAsync();
        var requestId = await fixture.OpenRequestAsync(world.OrganizationId, world.CandidateId, DataRequestKind.Copy);
        var path = $"/v1/data-requests/{requestId}/decline";
        var reason = $"Both of us said no {Guid.NewGuid():N}";

        // Hold the request's row so both calls are in flight at the same moment: the test releases it once two database
        // sessions are waiting on a lock, instead of hoping two requests overlap.
        Task<ApiResult> first, second;
        await using (var gate = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct)))
        {
            await gate.OpenAsync(Ct);
            await using var transaction = await gate.BeginTransactionAsync(Ct);
            await using (var hold = new NpgsqlCommand("SELECT 1 FROM data_requests WHERE id = @id FOR UPDATE", gate, transaction))
            {
                hold.Parameters.AddWithValue("id", requestId);
                await hold.ExecuteScalarAsync(Ct);
            }

            first = world.Owner.PostAsync(path, new JsonObject { ["reason"] = reason });
            second = world.Owner.PostAsync(path, new JsonObject { ["reason"] = reason });
            await WolverineStore.WaitUntilAsync(
                async () => await fixture.OwnerScalarAsync<long>(
                    "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND pid <> pg_backend_pid() AND query ILIKE '%data_requests%'") >= 2,
                "both decline calls to be waiting on the request's row", Ct);
            await transaction.RollbackAsync(Ct);
        }

        var answers = await Task.WhenAll(first, second);

        answers.Select(a => a.Status).Should().BeEquivalentTo([HttpStatusCode.NoContent, HttpStatusCode.Conflict], "the second to arrive sees it already declined");
        (await fixture.OwnerScalarAsync<long>(
            "SELECT count(*) FROM membership_events WHERE kind = 'DataRequestDeclined' AND data_request_id = @id", ("id", requestId)))
            .Should().Be(1, "the team log records one decline");

        await WaitForMailAsync(world.Person.Email, "About your request to ");
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(requestId.ToString(), Ct, ".SendDataRequestDeclined") >= 1,
            "the worker to handle the decline email's message", Ct);
        (await store.CountHandledAsync(requestId.ToString(), Ct, ".SendDataRequestDeclined")).Should().Be(1);
        (await store.GetDeadLettersAsync(requestId.ToString(), Ct)).Should().BeEmpty();
        (await fixture.Mailpit.MessagesAsync(world.Person.Email, Ct)).Count(m => m.Subject.StartsWith("About your request to ", StringComparison.Ordinal))
            .Should().Be(1, "one decline, one email");
        (await world.Owner.ListedAsync(requestId))!.Value.GetProperty("status").GetString().Should().Be("Declined");
    }

    [Fact]
    public async Task Erasing_through_a_request_needs_the_typed_name_and_leaves_no_trace_of_the_person_anywhere()
    {
        using var world = await fixture.NewWorldAsync();
        var erasure = await fixture.OpenRequestAsync(world.OrganizationId, world.CandidateId, DataRequestKind.Erasure);
        var declinedReason = $"Cannot confirm the address {Guid.NewGuid():N}";
        var declined = await fixture.OpenRequestAsync(world.OrganizationId, world.CandidateId, DataRequestKind.Copy);
        await fixture.OwnerSqlAsync(
            "UPDATE data_requests SET status = 'Declined', decline_reason = @reason, resolved_at = now() WHERE id = @id", ("id", declined), ("reason", declinedReason));
        var otherOpen = await fixture.OpenRequestAsync(world.OrganizationId, world.CandidateId, DataRequestKind.Copy);
        var path = $"/v1/data-requests/{erasure}/erase";
        var values = new[] { world.Person.Email, world.Person.FullName, world.Person.FirstName, world.Person.LastName, world.CandidateId.ToString(), world.ApplicationId.ToString(), declinedReason };

        (await TablesHoldingAsync(fixture, [.. values])).Should().Contain($"candidates holds {world.Person.Email}");

        (await world.Owner.PostAsync(path, new JsonObject { ["confirmation"] = "Someone Else" })).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("confirmation");
        (await world.Owner.PostAsync(path, new JsonObject { ["confirmation"] = "" })).Expect(HttpStatusCode.BadRequest);
        (await world.Owner.GetAsync($"/v1/candidates/{world.CandidateId}")).Expect(HttpStatusCode.OK);
        (await world.Owner.PostAsync($"/v1/data-requests/{otherOpen}/erase", new JsonObject { ["confirmation"] = world.Person.FullName })).Expect(HttpStatusCode.Conflict);

        (await world.Owner.PostAsync(path, new JsonObject { ["confirmation"] = $"  {world.Person.FullName.ToUpperInvariant()} " })).Expect(HttpStatusCode.NoContent);

        (await world.Owner.GetAsync($"/v1/candidates/{world.CandidateId}")).Expect(HttpStatusCode.NotFound);
        var done = (await world.Owner.ListedAsync(erasure))!.Value;
        done.GetProperty("status").GetString().Should().Be("Completed");
        done.GetProperty("candidate").ValueKind.Should().Be(JsonValueKind.Null, "the request outlives the person with no one attached");
        var closed = (await world.Owner.ListedAsync(otherOpen))!.Value;
        closed.GetProperty("status").GetString().Should().Be("Completed", "erasing answers the candidate's other open requests");
        (await world.Owner.ListedAsync(declined))!.Value.GetProperty("declineReason").ValueKind.Should().Be(JsonValueKind.Null, "a reason may name the person");

        var tombstone = (await world.Owner.GetAsync("/v1/organization/membership-log")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "CandidateErased");
        tombstone.GetProperty("actorUserId").GetGuid().Should().Be(world.Owner.UserId);
        tombstone.GetProperty("dataRequestId").GetGuid().Should().Be(erasure);
        await WolverineStore.WaitUntilAsync(
            async () => (await TablesHoldingAsync(fixture, [.. values])).Count == 0,
            "no table to hold the erased person's name, address, ids or a reason that may name them", Ct);
        (await world.Owner.PostAsync(path, new JsonObject { ["confirmation"] = world.Person.FullName })).Expect(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_data_request_arriving_emails_the_members_who_can_answer_it_with_no_word_about_the_candidate()
    {
        using var world = await fixture.NewWorldAsync();
        var adminEmail = ApiSession.NewEmail("admin");
        var recruiterEmail = ApiSession.NewEmail("recruiter");
        using var admin = await fixture.InviteAndJoinAsync(world.Owner, "Admin", adminEmail);
        using var recruiter = await fixture.InviteAndJoinAsync(world.Owner, "Recruiter", recruiterEmail);

        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        await sender.Services.GetRequiredService<IMessageBus>()
            .PublishAsync(new DataRequestReceived(world.OrganizationId, Guid.NewGuid(), DataRequestKind.Copy, DateTimeOffset.UtcNow));

        var mail = await WaitForMailAsync(adminEmail, "A candidate asked for a copy of their data");
        mail.Text.Should().Contain("Settings > Data requests").And.Contain("/settings/data-requests");
        mail.Text.Should().NotContain(world.Person.FullName).And.NotContain(world.Person.Email);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        (await fixture.Mailpit.MessagesAsync(recruiterEmail, Ct)).Should().NotContain(m => m.Subject.StartsWith("A candidate asked", StringComparison.Ordinal),
            "a Recruiter cannot open the page");
    }

    // A published job whose questions are the Software engineer ones plus a free-text motivation.
    private async Task<(string Slug, Guid JobId)> OpenJobWithQuestionAsync(Tenant tenant)
    {
        var definition = await Samples.DefinitionOfAsync(tenant, SystemTemplates.SoftwareEngineer, 2);
        definition["workflow"]!["questionnaire"]!["questions"]!.AsArray()
            .Add(new JsonObject { ["id"] = "motivation", ["type"] = "LongText", ["label"] = "Why do you want this job?", ["required"] = false });
        var (_, versionId) = await tenant.CreatePublishedTemplateAsync($"Data copy {Guid.NewGuid():N}", definition);
        return await VerificationSupport.OpenJobAsync(fixture, tenant, versionId);
    }

    private async Task<MailpitMessage> WaitForMailAsync(string address, string subjectStart)
    {
        MailpitMessage? found = null;
        await WolverineStore.WaitUntilAsync(
            async () => (found = (await fixture.Mailpit.MessagesAsync(address, Ct)).FirstOrDefault(m => m.Subject.StartsWith(subjectStart, StringComparison.Ordinal))) is not null,
            $"an email to the address starting \"{subjectStart}\"", Ct);
        return found!;
    }

    [GeneratedRegex(@"https?://\S+/data-request/download/(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex DownloadLink();
}
