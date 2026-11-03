namespace ScreenHire.Domain.Organizations;

/// <summary>
/// A member's choices about the sidebar's views (docs/architecture/saved-views.md). One per membership, so each
/// organization keeps its own and the choice follows the person across devices.
/// </summary>
public sealed record ViewPreferences
{
    /// <summary>Nothing hidden, the preferences of every member who has not hidden a view.</summary>
    public static ViewPreferences Default { get; } = new();

    /// <summary>The ids of the built-in views (<c>BuiltInViews</c>) the member hid, in the order they hid them.</summary>
    public IReadOnlyList<string> HiddenBuiltIns { get; init; } = [];

    public bool Equals(ViewPreferences? other) => other is not null && HiddenBuiltIns.SequenceEqual(other.HiddenBuiltIns);

    public override int GetHashCode() => HashCode.Combine(HiddenBuiltIns.Count, HiddenBuiltIns.Count > 0 ? HiddenBuiltIns[0] : null);
}
