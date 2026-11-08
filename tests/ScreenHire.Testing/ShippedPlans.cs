using System.Text.Json;

namespace ScreenHire.Testing;

/// <summary>A plan as <c>packages/plans/plans.json</c> ships it.</summary>
public sealed record ShippedPlan(string Key, string Name, decimal? MonthlyPrice, int? ActiveJobs, bool Unlimited)
{
    /// <summary>The most jobs that may be open at once, or null for no limit.</summary>
    public int? Limit => Unlimited ? null : ActiveJobs;

    /// <summary>The limit, for a test that must reach it. Throws when the plan has none.</summary>
    public int RequiredLimit => Limit ?? throw new InvalidOperationException($"The {Key} plan has no limit in plans.json.");
}

/// <summary>
/// The plans the test stacks run with. The stacks pin nothing, so they sell what the catalogue ships, and a test that needs a
/// price or a limit reads it here: the owner may change a working number without editing a test.
/// </summary>
public static class ShippedPlans
{
    private static readonly Lazy<IReadOnlyDictionary<string, ShippedPlan>> Plans = new(Load);

    public static ShippedPlan Starter => Plans.Value["starter"];

    public static ShippedPlan Growth => Plans.Value["growth"];

    public static ShippedPlan Unlimited => Plans.Value["unlimited"];

    private static Dictionary<string, ShippedPlan> Load()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ScreenHire.slnx")))
        {
            directory = directory.Parent;
        }

        var path = directory is null
            ? throw new InvalidOperationException($"No ScreenHire.slnx above {AppContext.BaseDirectory}.")
            : Path.Combine(directory.FullName, "packages", "plans", "plans.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => new ShippedPlan(
                property.Name,
                property.Value.GetProperty("name").GetString()!,
                Number(property.Value, "monthlyPrice", element => element.GetDecimal()),
                Number(property.Value, "activeJobs", element => element.GetInt32()),
                property.Value.GetProperty("unlimited").GetBoolean()),
            StringComparer.OrdinalIgnoreCase);
    }

    private static T? Number<T>(JsonElement plan, string name, Func<JsonElement, T> read)
        where T : struct =>
        plan.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? read(value) : null;
}
