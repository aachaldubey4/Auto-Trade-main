namespace AutoTrade.Domain.Models;

public record FeedHealthStatus(
    string Name,
    string Url,
    bool IsHealthy,
    int LastStatusCode,
    DateTime? LastChecked,
    string? LastError
);
