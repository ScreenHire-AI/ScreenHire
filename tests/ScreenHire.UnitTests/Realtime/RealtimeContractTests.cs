using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using ScreenHire.Api.Realtime;
using ScreenHire.Application.Messaging;

namespace ScreenHire.UnitTests.Realtime;

/// <summary>
/// The C# half of the real-time contract. It serializes a sample of every <see cref="ITenantEvent"/> exactly as the hub
/// sends it and compares the result with <c>packages/realtime/contract/events.json</c>, which the TypeScript half
/// (<c>events.test.ts</c>) parses with the Zod schemas. Changing an event fails here first. Rewrite the file with
/// <c>UPDATE_REALTIME_CONTRACT=1 dotnet test --project tests/ScreenHire.UnitTests --filter-class "*RealtimeContractTests"</c>,
/// then update the schema until the Vitest suite passes.
/// </summary>
public sealed class RealtimeContractTests
{
    private const string UpdateVariable = "UPDATE_REALTIME_CONTRACT";

    [Fact]
    public void Every_tenant_event_is_in_the_contract_file_as_the_hub_serializes_it()
    {
        var path = ContractFilePath();
        var expected = SerializeSamples();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, expected);
        }

        File.Exists(path).Should().BeTrue($"{path} holds the contract; create it with {UpdateVariable}=1");
        File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(
            expected,
            $"packages/realtime/contract/events.json must match the C# events. Rerun this test with {UpdateVariable}=1 to " +
            "rewrite it, then update packages/realtime/src/events.ts until its Vitest suite passes");
    }

    [Fact]
    public void The_known_events_are_tenant_events_with_unique_names()
    {
        ScreenHireMessaging.TenantEventTypes.Keys.Should().Contain(
        ["JobPublished", "JobClosed", "ApplicationSubmitted", "ApplicationMoved", "HiringTemplateVersionPublished", "LiveUpdatesChecked"]);
        ScreenHireMessaging.TenantEventTypes.Should().AllSatisfy(entry => entry.Key.Should().Be(entry.Value.Name));
    }

    private static string SerializeSamples()
    {
        // SignalR's own defaults (camelCase), plus the API's additions: what the hub really writes.
        var options = new JsonSerializerOptions(new JsonHubProtocolOptions().PayloadSerializerOptions) { WriteIndented = true };
        RealtimeSetup.ConfigureJson(options);

        var samples = ScreenHireMessaging.TenantEventTypes
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new RealtimeEvent(entry.Key, Sample(entry.Value)))
            .ToList();
        return JsonSerializer.Serialize(samples, options).ReplaceLineEndings("\n") + "\n";
    }

    // A record built from its primary constructor, with a fixed, valid value per parameter.
    private static object Sample(Type eventType)
    {
        var constructor = eventType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var arguments = constructor.GetParameters().Select((parameter, index) => SampleValue(eventType, parameter.ParameterType, index + 1));
        return constructor.Invoke([.. arguments]);
    }

    private static object SampleValue(Type eventType, Type type, int position)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(Guid))
        {
            return Guid.Parse($"0199f0c4-0000-7000-8000-{position:D12}"); // a version 7 id, like Guid.CreateVersion7()
        }

        if (underlying == typeof(DateTimeOffset))
        {
            return new DateTimeOffset(2026, 10, 20, 9, position, 0, TimeSpan.Zero);
        }

        if (underlying == typeof(string))
        {
            return $"sample-{position}";
        }

        if (underlying == typeof(int))
        {
            return position;
        }

        if (underlying == typeof(bool))
        {
            return true;
        }

        if (underlying.IsEnum)
        {
            return Enum.GetValues(underlying).GetValue(0)!;
        }

        throw new NotSupportedException(
            $"{eventType.Name} has a {type.Name} property; teach {nameof(RealtimeContractTests)}.{nameof(SampleValue)} a sample value for it.");
    }

    private static string ContractFilePath() => RepositoryFiles.PathTo("packages", "realtime", "contract", "events.json");
}
