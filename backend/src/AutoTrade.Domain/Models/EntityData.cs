namespace AutoTrade.Domain.Models;

public class EntityData
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "PERSON", "ORGANIZATION", "LOCATION", etc.
    public double Confidence { get; set; }
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
}