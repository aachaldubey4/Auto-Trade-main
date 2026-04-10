namespace AutoTrade.Application.Interfaces;

/// <summary>
/// Manages NSE stock list — seeds from static JSON fallback and refreshes daily from NSE.
/// </summary>
public interface INseStockRefreshService
{
    /// <summary>
    /// Seeds MongoDB stocks collection from static JSON if empty, then attempts NSE API refresh.
    /// Called on application startup.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Fetches the current NSE equity list and upserts into MongoDB.
    /// Triggers Aho-Corasick trie rebuild after successful refresh.
    /// </summary>
    Task RefreshAsync();
}
