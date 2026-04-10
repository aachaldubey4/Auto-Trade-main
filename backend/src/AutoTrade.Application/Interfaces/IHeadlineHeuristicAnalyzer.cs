namespace AutoTrade.Application.Interfaces;

/// <summary>
/// Scores a news headline using regex patterns for common Indian financial headline structures.
/// Returns a sentiment adjustment in the range [-1.0, +1.0].
/// </summary>
public interface IHeadlineHeuristicAnalyzer
{
    /// <summary>
    /// Score the headline using pattern matching.
    /// Returns a sentiment adjustment: positive = bullish, negative = bearish, 0 = neutral.
    /// </summary>
    double ScoreHeadline(string headline);
}
