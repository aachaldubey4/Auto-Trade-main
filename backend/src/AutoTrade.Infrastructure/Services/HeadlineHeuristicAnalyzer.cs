using AutoTrade.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AutoTrade.Infrastructure.Services;

/// <summary>
/// Scores news headlines using regex patterns common in Indian financial journalism.
/// Returns a sentiment adjustment in [-1.0, +1.0].
/// Positive = bullish signal, Negative = bearish signal, 0 = neutral/no pattern matched.
/// </summary>
public class HeadlineHeuristicAnalyzer(ILogger<HeadlineHeuristicAnalyzer> logger)
    : IHeadlineHeuristicAnalyzer
{
    private static readonly (Regex Pattern, double Adjustment)[] Rules =
    [
        // Strong price moves (with percentage)
        (new Regex(@"\b(surges?|soars?|rallies|jumps?|skyrockets?)\s+\d", RegexOptions.IgnoreCase | RegexOptions.Compiled), +0.35),
        (new Regex(@"\b(falls?|drops?|slumps?|crashes?|plunges?|tumbles?)\s+\d", RegexOptions.IgnoreCase | RegexOptions.Compiled), -0.35),

        // Price moves without percentage
        (new Regex(@"\b(surges?|soars?|rallies|spikes?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), +0.20),
        (new Regex(@"\b(falls?|drops?|slumps?|crashes?|plunges?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), -0.20),

        // 52-week / all-time extremes
        (new Regex(@"\b(hits?|touches?|reaches?)\s+(52-?week|all.?time)\s+high\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), +0.30),
        (new Regex(@"\b(hits?|touches?|reaches?)\s+(52-?week|all.?time)\s+low\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), -0.30),

        // Earnings beats / misses
        (new Regex(@"\b(beats?|exceeds?)\s+(estimates?|expectations?|forecast)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), +0.30),
        (new Regex(@"\b(misses?|below)\s+(estimates?|expectations?|forecast)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), -0.30),

        // Order wins / business expansion
        (new Regex(@"\b(bags?|wins?|secures?|clinches?)\s+(order|contract|deal|project)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), +0.25),
        (new Regex(@"\b(expands?|enters?|launches?|partners?|acquires?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), +0.15),

        // Analyst rating changes
        (new Regex(@"\b(upgrades?|upgrad(ed|ing))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), +0.20),
        (new Regex(@"\b(downgrades?|downgrad(ed|ing))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), -0.20),

        // Profit / loss headlines
        (new Regex(@"\b(profit|revenue|earnings)\s+(rises?|jumps?|surges?|grows?|up)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), +0.25),
        (new Regex(@"\b(net\s+)?loss\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), -0.20),
        (new Regex(@"\b(profit|revenue)\s+(falls?|drops?|declines?|shrinks?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), -0.25),

        // Regulatory / legal negatives
        (new Regex(@"\b(sebi|rbi|cci)\s+(action|penalty|notice|ban|investigation|probe)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), -0.25),
        (new Regex(@"\b(fraud|scam|default|bankrupt|insolvency|npa)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), -0.30),

        // India-specific positive triggers
        (new Regex(@"\b(rbi\s+rate\s+cut|rate\s+cut)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), +0.20),
        (new Regex(@"\b(dividend|buyback|bonus\s+shares?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), +0.15),
        (new Regex(@"\b(promoter\s+pledge|pledging)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), -0.20),
        (new Regex(@"\b(record\s+(high|revenue|profit|earnings))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), +0.25),
    ];

    public double ScoreHeadline(string headline)
    {
        if (string.IsNullOrWhiteSpace(headline))
            return 0.0;

        var total = 0.0;
        var matchCount = 0;

        foreach (var (pattern, adjustment) in Rules)
        {
            if (pattern.IsMatch(headline))
            {
                total += adjustment;
                matchCount++;
            }
        }

        if (matchCount == 0)
            return 0.0;

        // Clamp to [-1.0, +1.0]
        var score = Math.Clamp(total, -1.0, 1.0);

        logger.LogDebug("Headline heuristic: '{Headline}' → {Score:+0.00;-0.00} ({Count} patterns matched)",
            headline.Length > 80 ? headline[..80] + "…" : headline, score, matchCount);

        return score;
    }
}
