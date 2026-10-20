using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ScreenHire.Infrastructure.Persistence;

/// <summary>
/// JSONB columns for value objects such as hiring definitions, job snapshots and timeline data. They use the same JSON
/// shape as the API (camelCase names, enums as strings, strict numbers), so a stored definition, the wire format and the
/// JSON Schema the API publishes are one contract (docs/architecture/workflow-engine.md §6).
/// </summary>
/// <remarks>
/// A value converter rather than EF's <c>ToJson()</c> mapping: <c>ToJson()</c> names JSON keys after CLR properties
/// and cannot map the immutable records with list members that these value objects are. Nothing queries inside these
/// columns; anything that needs filtering gets its own column.
/// </remarks>
public static class JsonColumns
{
    /// <summary>The serializer options for every JSONB column, and for the JSON Schema the API publishes.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    /// <summary>Stores the property as <c>jsonb</c>, compared by its JSON so change tracking sees edits inside lists.</summary>
    public static PropertyBuilder<T> HasJsonColumn<T>(this PropertyBuilder<T> property)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(property);
        var converter = new ValueConverter<T, string>(value => Serialize(value), json => Deserialize<T>(json));
        var comparer = new ValueComparer<T>(
            (left, right) => Serialize(left) == Serialize(right),
            value => Serialize(value).GetHashCode(StringComparison.Ordinal),
            value => Deserialize<T>(Serialize(value)));
        return property.HasConversion(converter, comparer).HasColumnType("jsonb");
    }

    public static string Serialize<T>(T? value) => JsonSerializer.Serialize(value, SerializerOptions);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? throw new JsonException($"A {typeof(T).Name} column held JSON null.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
            // Optional members stay out of the stored JSON; absent and null read back the same.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }
}
