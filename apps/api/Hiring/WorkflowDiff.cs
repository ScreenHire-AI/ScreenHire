using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// Compares two workflows member by member, in their stored JSON shape. Lists whose elements all have an <c>id</c>
/// (stages, questions, options, automations) are matched by id, so a renamed stage is one change rather than a removal
/// and an addition. Paths name elements by id: <c>stages[screening].slaDays</c>.
/// </summary>
internal static class WorkflowDiff
{
    public static IReadOnlyList<WorkflowChange> Compare(HiringWorkflow before, HiringWorkflow after)
    {
        var changes = new List<WorkflowChange>();
        Walk(ToNode(before), ToNode(after), "", changes);
        return changes;
    }

    // A form's link to its library form (which form, which version) is bookkeeping, not what the candidate is asked: its
    // questions are compared, its link is not, so a job that copied a form before forms had links shows no change for it.
    private static JsonNode? ToNode(HiringWorkflow workflow)
    {
        var node = JsonSerializer.SerializeToNode(workflow, JsonColumns.SerializerOptions);
        if (node?["followUpQuestionnaires"] is JsonArray forms)
        {
            foreach (var form in forms.OfType<JsonObject>())
            {
                form.Remove("formTemplateId");
                form.Remove("formVersion");
            }
        }

        return node;
    }

    private static void Walk(JsonNode? before, JsonNode? after, string path, List<WorkflowChange> changes)
    {
        switch (before, after)
        {
            case (null, null):
                return;
            case (null, _):
                changes.Add(new(path, WorkflowChangeKind.Added, null, Text(after)));
                return;
            case (_, null):
                changes.Add(new(path, WorkflowChangeKind.Removed, Text(before), null));
                return;
            case (JsonObject left, JsonObject right):
                foreach (var key in left.Select(p => p.Key).Union(right.Select(p => p.Key)))
                {
                    Walk(left[key], right[key], Member(path, key), changes);
                }

                return;
            case (JsonArray left, JsonArray right) when HasIds(left) && HasIds(right):
                WalkById(left, right, path, changes);
                return;
            default:
                if (!JsonNode.DeepEquals(before, after))
                {
                    changes.Add(new(path, WorkflowChangeKind.Changed, Text(before), Text(after)));
                }

                return;
        }
    }

    private static void WalkById(JsonArray before, JsonArray after, string path, List<WorkflowChange> changes)
    {
        var left = before.ToDictionary(Id);
        var right = after.ToDictionary(Id);
        foreach (var id in left.Keys.Union(right.Keys))
        {
            Walk(left.GetValueOrDefault(id), right.GetValueOrDefault(id), $"{path}[{id}]", changes);
        }

        // Order matters for stages (the pipeline) and questions (the form), so a reordering is a change of its own.
        var leftOrder = left.Keys.Where(right.ContainsKey).ToList();
        var rightOrder = right.Keys.Where(left.ContainsKey).ToList();
        if (!leftOrder.SequenceEqual(rightOrder))
        {
            changes.Add(new($"{path}.order", WorkflowChangeKind.Changed, string.Join(", ", leftOrder), string.Join(", ", rightOrder)));
        }
    }

    // An empty list counts, so the first automation added to an empty list is matched by id like any other.
    private static bool HasIds(JsonArray array) =>
        array.All(e => e is JsonObject o && o["id"] is JsonValue v && v.TryGetValue<string>(out _))
        && array.Select(Id).Distinct(StringComparer.Ordinal).Count() == array.Count;

    private static string Id(JsonNode? node) => node!["id"]!.GetValue<string>();

    private static string Member(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    private static string? Text(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString(JsonColumns.SerializerOptions),
    };
}
