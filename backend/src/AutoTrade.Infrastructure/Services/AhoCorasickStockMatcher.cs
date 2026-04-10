using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Text.Json;

namespace AutoTrade.Infrastructure.Services;

/// <summary>
/// Aho-Corasick multi-pattern stock name matcher.
/// Scans article text once (O(n)) to find all mentioned NSE companies.
/// Thread-safe: readers use an immutable trie snapshot; rebuilds swap atomically.
/// </summary>
public class AhoCorasickStockMatcher(
    MongoDbContext dbContext,
    ILogger<AhoCorasickStockMatcher> logger) : IStockMatcher
{
    // Volatile so reads see the latest reference without a lock
    private volatile AhoCorasickTrie? _trie;

    public List<string> FindMentionedStocks(string text)
    {
        var trie = _trie;
        if (trie is null || string.IsNullOrWhiteSpace(text))
            return [];

        return trie.Search(text.ToLowerInvariant());
    }

    public async Task RebuildIndexAsync()
    {
        try
        {
            logger.LogInformation("Rebuilding Aho-Corasick stock index...");

            var stocks = await dbContext.Stocks
                .Find(Builders<StockDocument>.Filter.Eq(s => s.IsActive, true))
                .ToListAsync();

            if (stocks.Count == 0)
            {
                logger.LogWarning("No active stocks in DB — Aho-Corasick index not built");
                return;
            }

            var patterns = BuildPatterns(stocks);
            var newTrie = new AhoCorasickTrie();
            newTrie.Build(patterns);

            // Atomic swap — no lock needed for readers
            _trie = newTrie;

            logger.LogInformation("Aho-Corasick index rebuilt: {PatternCount} patterns from {StockCount} stocks",
                patterns.Count, stocks.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rebuild Aho-Corasick index");
        }
    }

    /// <summary>
    /// Build lowercased pattern → symbol mappings from all stock names, symbols, and aliases.
    /// </summary>
    private static Dictionary<string, string> BuildPatterns(List<StockDocument> stocks)
    {
        var patterns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var stock in stocks)
        {
            // Symbol itself (e.g. "RELIANCE")
            AddPattern(patterns, stock.Symbol, stock.Symbol);

            // Company name (e.g. "Reliance Industries Limited")
            AddPattern(patterns, stock.CompanyName, stock.Symbol);

            // Common short form: first two words of company name (e.g. "Reliance Industries")
            var nameParts = stock.CompanyName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (nameParts.Length >= 2)
                AddPattern(patterns, string.Join(" ", nameParts.Take(2)), stock.Symbol);

            // All aliases
            foreach (var alias in stock.Aliases)
                AddPattern(patterns, alias, stock.Symbol);

            // Individual meaningful words from company name (length > 3, skip generic words)
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "limited", "ltd", "private", "pvt", "india", "indian", "industries", "industry",
                "the", "and", "of", "for", "bank", "corporation", "corp", "company", "co"
            };

            foreach (var part in nameParts.Where(p => p.Length > 3 && !stopWords.Contains(p)))
                AddPattern(patterns, part, stock.Symbol);
        }

        return patterns;
    }

    private static void AddPattern(Dictionary<string, string> patterns, string key, string symbol)
    {
        if (!string.IsNullOrWhiteSpace(key) && key.Length >= 2)
            patterns.TryAdd(key.ToLowerInvariant(), symbol);
    }
}

/// <summary>
/// Immutable Aho-Corasick trie. Built once, then read-only (thread-safe).
/// </summary>
internal sealed class AhoCorasickTrie
{
    private sealed class TrieNode
    {
        public readonly Dictionary<char, TrieNode> Children = new();
        public TrieNode? FailureLink;
        public readonly List<string> Output = new();   // symbols matched at this node
    }

    private TrieNode? _root;

    public void Build(Dictionary<string, string> patterns)
    {
        _root = new TrieNode();

        // Phase 1: Insert all patterns into the trie
        foreach (var (pattern, symbol) in patterns)
        {
            var node = _root;
            foreach (var ch in pattern)
            {
                if (!node.Children.TryGetValue(ch, out var child))
                {
                    child = new TrieNode();
                    node.Children[ch] = child;
                }
                node = child;
            }
            if (!node.Output.Contains(symbol))
                node.Output.Add(symbol);
        }

        // Phase 2: BFS to compute failure links (like KMP failure function, generalized)
        var queue = new Queue<TrieNode>();
        foreach (var child in _root.Children.Values)
        {
            child.FailureLink = _root;
            queue.Enqueue(child);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var (ch, child) in current.Children)
            {
                // Find the longest proper suffix of current+ch that is a prefix in the trie
                var failure = current.FailureLink!;
                while (failure != _root && !failure.Children.ContainsKey(ch))
                    failure = failure.FailureLink!;

                child.FailureLink = failure.Children.TryGetValue(ch, out var fl) && fl != child
                    ? fl
                    : _root;

                // Dictionary links: inherit outputs from failure chain
                child.Output.AddRange(child.FailureLink.Output.Where(s => !child.Output.Contains(s)));

                queue.Enqueue(child);
            }
        }
    }

    /// <summary>
    /// Scan lowercased text, return deduplicated matched symbols.
    /// Only counts a match if the matched text is surrounded by word boundaries.
    /// </summary>
    public List<string> Search(string lowerText)
    {
        if (_root is null) return [];

        var matched = new HashSet<string>();
        var node = _root;
        int i = 0;

        while (i < lowerText.Length)
        {
            var ch = lowerText[i];

            while (node != _root && !node.Children.ContainsKey(ch))
                node = node.FailureLink!;

            if (node.Children.TryGetValue(ch, out var next))
                node = next;

            if (node.Output.Count > 0)
            {
                // Verify word boundary at the end of the match
                if (i + 1 >= lowerText.Length || !char.IsLetterOrDigit(lowerText[i + 1]))
                {
                    foreach (var symbol in node.Output)
                        matched.Add(symbol);
                }
            }

            i++;
        }

        return [.. matched];
    }
}
