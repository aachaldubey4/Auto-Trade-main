namespace AutoTrade.Domain.Models;

public class SentimentScore
{
    public double Positive { get; set; }    // 0-1 confidence
    public double Negative { get; set; }    // 0-1 confidence  
    public double Neutral { get; set; }     // 0-1 confidence
    public string Overall { get; set; } = "neutral";     // "positive", "negative", "neutral"
    public double Confidence { get; set; }  // 0-1 overall confidence
}