namespace AutoTrade.Application.Interfaces;

/// <summary>
/// Efficient multi-pattern stock name matcher using Aho-Corasick algorithm.
/// Scans article text once (O(n)) to find all mentioned NSE companies.
/// </summary>
public interface IStockMatcher
{
    /// <summary>
    /// Scan text for mentions of any NSE company name, symbol, or alias.
    /// Returns deduplicated list of matching NSE symbols.
    /// </summary>
    List<string> FindMentionedStocks(string text);

    /// <summary>
    /// Rebuild the Aho-Corasick trie from current MongoDB stocks collection.
    /// Called after NseStockRefreshService updates the stock list.
    /// Thread-safe — swaps the trie atomically.
    /// </summary>
    Task RebuildIndexAsync();
}
