using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests.Automations;

/// <summary>Automations as a client sends them, and helpers to put them on a job and wait for their runs.</summary>
internal static class AutomationSamples
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>When an application enters <paramref name="stageId"/>, send <paramref name="template"/>.</summary>
    public static JsonObject EmailOnStage(string stageId, string template) => new()
    {
        ["id"] = "email-on-stage",
        ["name"] = "Email on stage",
        ["trigger"] = new JsonObject { ["type"] = "StageEntered", ["stageId"] = stageId },
        ["actions"] = new JsonArray(new JsonObject { ["type"] = "SendEmail", ["emailTemplate"] = template }),
    };

    /// <summary>
    /// A job whose workflow has these automations: a published template carrying them, and a job created from it. Jobs
    /// take their workflow from a template version, so that is the only way to give one automations.
    /// </summary>
    public static async Task<Guid> JobWithAutomationsAsync(Tenant recruiter, string title, params JsonObject[] automations)
    {
        var (_, versionId) = await recruiter.CreatePublishedTemplateAsync($"{title} template", Samples.DefinitionWith(automations));
        var body = Samples.RemoteJob(title);
        body["templateVersionId"] = versionId;
        return (await recruiter.CreateJobAsync(body)).Id();
    }

    /// <summary>Waits until the application has a run of <paramref name="automationId"/> with <paramref name="status"/>, and returns it.</summary>
    public static async Task<JsonElement> WaitForRunAsync(Tenant recruiter, Guid applicationId, string automationId, string status)
    {
        JsonElement found = default;
        await WolverineStore.WaitUntilAsync(async () =>
        {
            var runs = (await recruiter.GetAsync($"/v1/applications/{applicationId}/automation-runs")).Expect(HttpStatusCode.OK).Body;
            foreach (var run in runs.EnumerateArray())
            {
                if (run.GetProperty("automationId").GetString() == automationId && run.GetProperty("status").GetString() == status)
                {
                    found = run.Clone();
                    return true;
                }
            }

            return false;
        }, $"automation {automationId} to reach {status}", Ct);
        return found;
    }
}
