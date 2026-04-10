namespace AutoTrade.Domain.Models;

/// <summary>
/// Result from Loughran-McDonald financial sentiment analysis
/// </summary>
public class LoughranMcDonaldResult
{
    /// <summary>
    /// Positive sentiment score (0.0 to 1.0)
    /// </summary>
    public double Positive { get; set; }

    /// <summary>
    /// Negative sentiment score (0.0 to 1.0)
    /// </summary>
    public double Negative { get; set; }

    /// <summary>
    /// Neutral sentiment score (0.0 to 1.0)
    /// </summary>
    public double Neutral { get; set; }

    /// <summary>
    /// Uncertainty score - unique to L-M financial analysis (0.0 to 1.0)
    /// </summary>
    public double Uncertainty { get; set; }

    /// <summary>
    /// Litigious score - regulatory/legal risk indicator (0.0 to 1.0)
    /// </summary>
    public double Litigious { get; set; }

    /// <summary>
    /// Constraining score - indicates limitations or restrictions (0.0 to 1.0)
    /// </summary>
    public double Constraining { get; set; }

    /// <summary>
    /// Overall sentiment classification
    /// </summary>
    public string Overall { get; set; } = "neutral";

    /// <summary>
    /// Confidence in the analysis (0.0 to 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Total words analyzed
    /// </summary>
    public int TotalWords { get; set; }

    /// <summary>
    /// Financial words found (L-M dictionary matches)
    /// </summary>
    public int FinancialWords { get; set; }

    /// <summary>
    /// Breakdown of word counts by category
    /// </summary>
    public Dictionary<string, int> WordCounts { get; set; } = new();
}