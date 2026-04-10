using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Text.Json;

namespace AutoTrade.Infrastructure.Services;

/// <summary>
/// Seeds MongoDB stocks from static JSON fallback and refreshes daily from NSE's public CSV.
/// NSE publishes a complete equity list at a stable public URL (no authentication required).
/// </summary>
public class NseStockRefreshService(
    MongoDbContext dbContext,
    IStockMatcher stockMatcher,
    IConfiguration configuration,
    ILogger<NseStockRefreshService> logger) : IHostedService, INseStockRefreshService, IDisposable
{
    // NSE publishes the full equity list as a CSV at this public URL
    private const string NseEquityListUrl =
        "https://archives.nseindia.com/content/equities/EQUITY_L.csv";

    private const int MinStocksThreshold = 100;
    private const string StaticJsonPath = "backend/data/nse-stocks.json";

    private static readonly TimeOnly RefreshTime = new(6, 0, 0);   // 06:00 IST

    private readonly HttpClient _http = CreateHttpClient();
    private Timer? _dailyTimer;

    public Task StartAsync(CancellationToken cancellationToken) => InitializeAsync();

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _dailyTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var count = await dbContext.Stocks.CountDocumentsAsync(
                Builders<StockDocument>.Filter.Empty);

            if (count < MinStocksThreshold)
            {
                logger.LogInformation(
                    "Stocks collection has {Count} entries (below threshold {Threshold}) — seeding from static JSON",
                    count, MinStocksThreshold);
                await SeedFromStaticJsonAsync();
            }
            else
            {
                logger.LogInformation("Stocks collection has {Count} entries — skipping seed", count);
            }

            // Best-effort refresh from NSE on startup
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));  // Let app finish startup first
                await RefreshAsync();
            });

            ScheduleDailyRefresh();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during NseStockRefreshService initialization");
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            logger.LogInformation("Refreshing NSE stock list from {Url}", NseEquityListUrl);
            var csv = await _http.GetStringAsync(NseEquityListUrl);
            var stocks = ParseNseCsv(csv);

            if (stocks.Count < MinStocksThreshold)
            {
                logger.LogWarning("NSE CSV returned only {Count} stocks — skipping upsert", stocks.Count);
                return;
            }

            await UpsertStocksAsync(stocks);
            await stockMatcher.RebuildIndexAsync();

            logger.LogInformation("NSE stock refresh complete — {Count} stocks upserted", stocks.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NSE stock refresh failed — using existing data");
        }
    }

    private async Task SeedFromStaticJsonAsync()
    {
        try
        {
            // Locate the static JSON relative to the executable
            var baseDirs = new[]
            {
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "backend", "data"),
            };

            string? jsonPath = null;
            foreach (var baseDir in baseDirs)
            {
                var candidate = Path.GetFullPath(Path.Combine(baseDir, "nse-stocks.json"));
                if (File.Exists(candidate)) { jsonPath = candidate; break; }

                candidate = Path.GetFullPath(Path.Combine(baseDir, StaticJsonPath));
                if (File.Exists(candidate)) { jsonPath = candidate; break; }
            }

            if (jsonPath is null)
            {
                logger.LogWarning("Static nse-stocks.json not found — stocks DB will be empty until NSE refresh");
                return;
            }

            var json = await File.ReadAllTextAsync(jsonPath);
            var entries = JsonSerializer.Deserialize<List<NseStockEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (entries is null || entries.Count == 0)
            {
                logger.LogWarning("Static nse-stocks.json is empty");
                return;
            }

            var stocks = entries.Select(MapToStockDocument).ToList();
            await UpsertStocksAsync(stocks);

            // Rebuild Aho-Corasick after seeding
            await stockMatcher.RebuildIndexAsync();

            logger.LogInformation("Seeded {Count} stocks from static JSON: {Path}", stocks.Count, jsonPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed from static JSON");
        }
    }

    private async Task UpsertStocksAsync(List<StockDocument> stocks)
    {
        var now = DateTime.UtcNow;
        var writes = stocks.Select(stock =>
        {
            stock.LastUpdated = now;
            var filter = Builders<StockDocument>.Filter.Eq(s => s.Symbol, stock.Symbol);
            var update = Builders<StockDocument>.Update
                .Set(s => s.CompanyName, stock.CompanyName)
                .Set(s => s.ISIN, stock.ISIN)
                .Set(s => s.Series, stock.Series)
                .Set(s => s.Sector, stock.Sector)
                .Set(s => s.IsActive, true)
                .Set(s => s.LastUpdated, now)
                .SetOnInsert(s => s.Aliases, stock.Aliases)
                .SetOnInsert(s => s.SearchTerms, stock.SearchTerms);

            return new UpdateOneModel<StockDocument>(filter, update) { IsUpsert = true };
        }).ToList();

        if (writes.Count > 0)
            await dbContext.Stocks.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false });
    }

    /// <summary>
    /// Parse NSE's public EQUITY_L.csv.
    /// Format: SYMBOL,NAME OF COMPANY,SERIES,DATE OF LISTING,PAID UP VALUE,MARKET LOT,ISIN NUMBER,FACE VALUE
    /// </summary>
    private List<StockDocument> ParseNseCsv(string csv)
    {
        var stocks = new List<StockDocument>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Skip(1))   // skip header
        {
            var cols = ParseCsvLine(line);
            if (cols.Length < 7) continue;

            var symbol = cols[0].Trim();
            var name = cols[1].Trim().Trim('"');
            var series = cols[2].Trim();
            var isin = cols[6].Trim();

            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(name))
                continue;

            var doc = new StockDocument
            {
                Symbol = symbol,
                CompanyName = name,
                ISIN = isin,
                Series = series,
                IsActive = series == "EQ",   // only include equity series by default
                Sector = MarketCategory.GeneralMarket,
                SearchTerms = BuildSearchTerms(symbol, name, [])
            };
            stocks.Add(doc);
        }

        return stocks;
    }

    private static string[] ParseCsvLine(string line)
    {
        // Simple CSV parser that handles quoted fields
        var fields = new List<string>();
        var inQuote = false;
        var current = new System.Text.StringBuilder();

        foreach (var ch in line)
        {
            if (ch == '"') { inQuote = !inQuote; }
            else if (ch == ',' && !inQuote) { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(ch);
        }
        fields.Add(current.ToString());
        return [.. fields];
    }

    private static StockDocument MapToStockDocument(NseStockEntry entry) => new()
    {
        Symbol = entry.Symbol,
        CompanyName = entry.CompanyName,
        ISIN = entry.ISIN ?? string.Empty,
        Series = entry.Series ?? "EQ",
        IsActive = true,
        Sector = MapSector(entry.Sector),
        SearchTerms = BuildSearchTerms(entry.Symbol, entry.CompanyName, [])
    };

    private static List<string> BuildSearchTerms(string symbol, string companyName, List<string> aliases)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        terms.Add(symbol.ToLowerInvariant());
        terms.Add(companyName.ToLowerInvariant());

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "limited", "ltd", "private", "pvt", "india", "industries", "the", "and", "of",
            "corporation", "corp", "company", "co", "inc", "group"
        };

        foreach (var word in companyName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (word.Length > 3 && !stopWords.Contains(word))
                terms.Add(word.ToLowerInvariant());

        foreach (var alias in aliases)
            terms.Add(alias.ToLowerInvariant());

        return [.. terms];
    }

    private static MarketCategory MapSector(string? sector) => sector?.ToUpperInvariant() switch
    {
        "BANKING" or "BANK" or "FINANCE" => MarketCategory.Banking,
        "IT" or "TECHNOLOGY" or "SOFTWARE" => MarketCategory.IT,
        "PHARMA" or "HEALTHCARE" or "PHARMACEUTICAL" => MarketCategory.Pharma,
        "AUTO" or "AUTOMOBILE" or "AUTOMOTIVE" => MarketCategory.Auto,
        "ENERGY" or "OIL" or "GAS" or "POWER" => MarketCategory.Energy,
        "FMCG" or "CONSUMER" => MarketCategory.FMCG,
        "METALS" or "METAL" or "MINING" => MarketCategory.Metals,
        "REALTY" or "REAL ESTATE" or "CONSTRUCTION" => MarketCategory.Realty,
        "TELECOM" or "TELECOMMUNICATIONS" => MarketCategory.Telecom,
        "INFRASTRUCTURE" or "INFRA" => MarketCategory.Infrastructure,
        _ => MarketCategory.GeneralMarket
    };

    private void ScheduleDailyRefresh()
    {
        // Calculate time until 06:00 IST tomorrow
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist);
        var todayRefresh = DateTime.Today.Add(RefreshTime.ToTimeSpan());
        var nextRefresh = nowIst.TimeOfDay >= RefreshTime.ToTimeSpan()
            ? todayRefresh.AddDays(1)
            : todayRefresh;

        var delay = nextRefresh - nowIst;
        _dailyTimer = new Timer(
            _ => Task.Run(RefreshAsync),
            null,
            delay,
            TimeSpan.FromHours(24));

        logger.LogInformation("Next NSE stock refresh scheduled at {NextRefresh} IST", nextRefresh);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // NSE requires a proper browser User-Agent
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,*/*");
        return client;
    }

    public void Dispose()
    {
        _dailyTimer?.Dispose();
        _http.Dispose();
    }

    private sealed record NseStockEntry(
        string Symbol,
        string CompanyName,
        string? ISIN,
        string? Series,
        string? Sector);
}
