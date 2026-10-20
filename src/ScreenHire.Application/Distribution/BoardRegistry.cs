using ScreenHire.Domain.Organizations;

namespace ScreenHire.Application.Distribution;

/// <summary>
/// Every configured board adapter, by id. An adapter that is not configured is not here at all, so a hidden board can
/// never be listed, offered or posted to.
/// </summary>
public sealed class BoardRegistry
{
    private readonly Dictionary<string, IJobBoardAdapter> adapters;

    public BoardRegistry(IEnumerable<IJobBoardAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        this.adapters = adapters.Where(a => a.IsConfigured).ToDictionary(a => a.BoardId, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<IJobBoardAdapter> All => adapters.Values;

    /// <summary>The boards this organization may post to: partner-gated boards only for cleared, paying customers.</summary>
    public IReadOnlyList<IJobBoardAdapter> AvailableTo(Organization organization)
    {
        ArgumentNullException.ThrowIfNull(organization);
        return [.. adapters.Values.Where(a => !a.Capabilities.PartnerGated || organization.MayUsePartnerBoards).OrderBy(a => Order(a))];
    }

    public IJobBoardAdapter? Find(string boardId) => adapters.GetValueOrDefault(boardId);

    /// <summary>The adapter for a board that has postings. A board whose configuration was removed since fails loudly.</summary>
    public IJobBoardAdapter Get(string boardId) =>
        Find(boardId) ?? throw new InvalidOperationException($"Board '{boardId}' is not configured.");

    // Our own channels first, then the rest by name.
    private static (int, string) Order(IJobBoardAdapter adapter) => (adapter.Capabilities.Mode switch
    {
        BoardMode.Hosted => 0,
        BoardMode.Notify => 1,
        BoardMode.PullFromAts => 2,
        _ => 3,
    }, adapter.Name);
}
