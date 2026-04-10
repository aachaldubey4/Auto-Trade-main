using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoDB.Bson;
using System.Text.RegularExpressions;

namespace AutoTrade.Infrastructure.Services;

public class StockMapper(
    MongoDbContext dbContext,
    IStockMatcher stockMatcher,
    ILogger<StockMapper> logger) : IStockMapper
{
    public Task<List<string>> MapArticleToStocksAsync(ProcessedArticle article)
    {
        try
        {
            // Single O(n) pass using Aho-Corasick over all 2000+ NSE company names
            var text = string.IsNullOrWhiteSpace(article.Content)
                ? article.Title
                : $"{article.Title} {article.Content}";

            var symbols = stockMatcher.FindMentionedStocks(text);

            if (symbols.Count > 0)
                logger.LogDebug("Mapped article '{Title}' to stocks: {Stocks}",
                    article.Title, string.Join(", ", symbols));

            return Task.FromResult(symbols);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error mapping article to stocks: {Title}", article.Title);
            return Task.FromResult(new List<string>());
        }
    }

    public Task UpdateStockDatabaseAsync()
    {
        // Stock database is now managed by NseStockRefreshService.
        // This method is a no-op kept for interface compatibility.
        logger.LogDebug("UpdateStockDatabaseAsync: delegated to NseStockRefreshService");
        return Task.CompletedTask;
    }

    public async Task<StockSymbol?> FindStockByNameAsync(string companyName)
    {
        try
        {
            var searchTerm = companyName.ToLowerInvariant();
            
            // First try exact match
            var filter = Builders<StockDocument>.Filter.Or(
                Builders<StockDocument>.Filter.Regex(x => x.CompanyName, new MongoDB.Bson.BsonRegularExpression(searchTerm, "i")),
                Builders<StockDocument>.Filter.AnyEq(x => x.Aliases, companyName),
                Builders<StockDocument>.Filter.AnyIn(x => x.SearchTerms, new[] { searchTerm })
            );

            var stockDoc = await dbContext.Stocks.Find(filter).FirstOrDefaultAsync();
            
            if (stockDoc != null)
            {
                return new StockSymbol
                {
                    Symbol = stockDoc.Symbol,
                    CompanyName = stockDoc.CompanyName,
                    Aliases = stockDoc.Aliases,
                    Sector = stockDoc.Sector,
                    MarketCap = stockDoc.MarketCap,
                    IsActive = stockDoc.IsActive
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error finding stock by name: {Name}", companyName);
            return null;
        }
    }

    public async Task<List<StockSymbol>> GetStocksByCategoryAsync(MarketCategory category)
    {
        try
        {
            var filter = Builders<StockDocument>.Filter.And(
                Builders<StockDocument>.Filter.Eq(x => x.Sector, category),
                Builders<StockDocument>.Filter.Eq(x => x.IsActive, true)
            );

            var stockDocs = await dbContext.Stocks.Find(filter).ToListAsync();
            
            return stockDocs.Select(doc => new StockSymbol
            {
                Symbol = doc.Symbol,
                CompanyName = doc.CompanyName,
                Aliases = doc.Aliases,
                Sector = doc.Sector,
                MarketCap = doc.MarketCap,
                IsActive = doc.IsActive
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting stocks by category: {Category}", category);
            return new List<StockSymbol>();
        }
    }

    public async Task InitializeStockDatabaseAsync()
    {
        try
        {
            var count = await dbContext.Stocks.CountDocumentsAsync(FilterDefinition<StockDocument>.Empty);
            
            if (count == 0)
            {
                logger.LogInformation("Initializing stock database with default data");
                await UpdateStockDatabaseAsync();
            }
            else
            {
                logger.LogInformation("Stock database already initialized with {Count} stocks", count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing stock database");
            throw;
        }
    }

    private async Task<List<string>> FindStocksByFuzzyMatchAsync(string content)
    {
        try
        {
            var matches = new List<string>();
            
            // Get all active stocks for fuzzy matching
            var filter = Builders<StockDocument>.Filter.Eq(x => x.IsActive, true);
            var stocks = await dbContext.Stocks.Find(filter).ToListAsync();
            
            foreach (var stock in stocks)
            {
                // Check if any search terms match
                foreach (var searchTerm in stock.SearchTerms)
                {
                    if (content.Contains(searchTerm.ToLowerInvariant()))
                    {
                        matches.Add(stock.Symbol);
                        break;
                    }
                }
            }
            
            return matches.Distinct().ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in fuzzy stock matching");
            return new List<string>();
        }
    }

    private List<string> ExtractPotentialStockSymbols(string content)
    {
        var symbols = new List<string>();
        
        // Look for uppercase words that might be stock symbols (3-10 characters)
        var regex = new Regex(@"\b[A-Z]{3,10}\b");
        var matches = regex.Matches(content.ToUpperInvariant());
        
        foreach (Match match in matches)
        {
            var symbol = match.Value;
            
            // Filter out common words that aren't stock symbols
            var excludeWords = new HashSet<string> { "THE", "AND", "FOR", "ARE", "BUT", "NOT", "YOU", "ALL", "CAN", "HER", "WAS", "ONE", "OUR", "OUT", "DAY", "GET", "HAS", "HIM", "HIS", "HOW", "ITS", "NEW", "NOW", "OLD", "SEE", "TWO", "WHO", "BOY", "DID", "ITS", "LET", "PUT", "SAY", "SHE", "TOO", "USE" };
            
            if (!excludeWords.Contains(symbol) && symbol.Length >= 3 && symbol.Length <= 10)
            {
                symbols.Add(symbol);
            }
        }
        
        return symbols.Distinct().ToList();
    }

    private async Task<bool> IsValidStockSymbolAsync(string symbol)
    {
        try
        {
            var filter = Builders<StockDocument>.Filter.And(
                Builders<StockDocument>.Filter.Eq(x => x.Symbol, symbol),
                Builders<StockDocument>.Filter.Eq(x => x.IsActive, true)
            );
            
            var count = await dbContext.Stocks.CountDocumentsAsync(filter);
            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    private Dictionary<string, string> InitializeStockMappings()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Banking
            { "Reliance", "RELIANCE" },
            { "Reliance Industries", "RELIANCE" },
            { "RIL", "RELIANCE" },
            { "State Bank of India", "SBIN" },
            { "SBI", "SBIN" },
            { "HDFC Bank", "HDFCBANK" },
            { "ICICI Bank", "ICICIBANK" },
            { "Axis Bank", "AXISBANK" },
            { "Kotak Mahindra Bank", "KOTAKBANK" },
            { "IndusInd Bank", "INDUSINDBK" },
            
            // IT
            { "TCS", "TCS" },
            { "Tata Consultancy Services", "TCS" },
            { "Infosys", "INFY" },
            { "Wipro", "WIPRO" },
            { "HCL Technologies", "HCLTECH" },
            { "Tech Mahindra", "TECHM" },
            { "LTI Mindtree", "LTIM" },
            
            // Auto
            { "Tata Motors", "TATAMOTORS" },
            { "Maruti Suzuki", "MARUTI" },
            { "Mahindra & Mahindra", "M&M" },
            { "Bajaj Auto", "BAJAJ-AUTO" },
            { "Hero MotoCorp", "HEROMOTOCO" },
            
            // Pharma
            { "Sun Pharma", "SUNPHARMA" },
            { "Dr. Reddy's", "DRREDDY" },
            { "Cipla", "CIPLA" },
            { "Divi's Laboratories", "DIVISLAB" },
            { "Lupin", "LUPIN" },
            
            // FMCG
            { "Hindustan Unilever", "HINDUNILVR" },
            { "HUL", "HINDUNILVR" },
            { "ITC", "ITC" },
            { "Nestle India", "NESTLEIND" },
            { "Britannia", "BRITANNIA" },
            
            // Energy
            { "ONGC", "ONGC" },
            { "Oil and Natural Gas Corporation", "ONGC" },
            { "Indian Oil Corporation", "IOC" },
            { "IOC", "IOC" },
            { "BPCL", "BPCL" },
            { "Bharat Petroleum", "BPCL" },
            
            // Metals
            { "Tata Steel", "TATASTEEL" },
            { "JSW Steel", "JSWSTEEL" },
            { "Hindalco", "HINDALCO" },
            { "Vedanta", "VEDL" },
            { "Coal India", "COALINDIA" },
            
            // Telecom
            { "Bharti Airtel", "BHARTIARTL" },
            { "Airtel", "BHARTIARTL" },
            { "Reliance Jio", "RELIANCE" }, // Jio is part of RIL
            { "Jio", "RELIANCE" }
        };
    }

    private Dictionary<string, List<string>> InitializeCompanyAliases()
    {
        return new Dictionary<string, List<string>>
        {
            { "RELIANCE", new List<string> { "reliance industries", "ril", "reliance jio", "jio" } },
            { "TCS", new List<string> { "tata consultancy services", "tata consultancy" } },
            { "INFY", new List<string> { "infosys limited", "infosys technologies" } },
            { "HDFCBANK", new List<string> { "hdfc bank limited", "housing development finance corporation bank" } },
            { "SBIN", new List<string> { "state bank of india", "sbi" } },
            { "ICICIBANK", new List<string> { "icici bank limited", "industrial credit and investment corporation of india bank" } },
            { "HINDUNILVR", new List<string> { "hindustan unilever limited", "hul", "unilever india" } },
            { "ITC", new List<string> { "itc limited", "indian tobacco company" } },
            { "BHARTIARTL", new List<string> { "bharti airtel limited", "airtel india" } },
            { "KOTAKBANK", new List<string> { "kotak mahindra bank limited", "kotak bank" } }
        };
    }

    private MarketCategory DetermineMarketCategory(string symbol)
    {
        var bankingSymbols = new[] { "SBIN", "HDFCBANK", "ICICIBANK", "AXISBANK", "KOTAKBANK", "INDUSINDBK" };
        var itSymbols = new[] { "TCS", "INFY", "WIPRO", "HCLTECH", "TECHM", "LTIM" };
        var autoSymbols = new[] { "TATAMOTORS", "MARUTI", "M&M", "BAJAJ-AUTO", "HEROMOTOCO" };
        var pharmaSymbols = new[] { "SUNPHARMA", "DRREDDY", "CIPLA", "DIVISLAB", "LUPIN" };
        var fmcgSymbols = new[] { "HINDUNILVR", "ITC", "NESTLEIND", "BRITANNIA" };
        var energySymbols = new[] { "RELIANCE", "ONGC", "IOC", "BPCL" };
        var metalSymbols = new[] { "TATASTEEL", "JSWSTEEL", "HINDALCO", "VEDL", "COALINDIA" };
        var telecomSymbols = new[] { "BHARTIARTL" };

        if (bankingSymbols.Contains(symbol)) return MarketCategory.Banking;
        if (itSymbols.Contains(symbol)) return MarketCategory.IT;
        if (autoSymbols.Contains(symbol)) return MarketCategory.Auto;
        if (pharmaSymbols.Contains(symbol)) return MarketCategory.Pharma;
        if (fmcgSymbols.Contains(symbol)) return MarketCategory.FMCG;
        if (energySymbols.Contains(symbol)) return MarketCategory.Energy;
        if (metalSymbols.Contains(symbol)) return MarketCategory.Metals;
        if (telecomSymbols.Contains(symbol)) return MarketCategory.Telecom;

        return MarketCategory.GeneralMarket;
    }

    private long GetEstimatedMarketCap(string symbol)
    {
        // Rough estimates in crores (for demo purposes)
        var marketCaps = new Dictionary<string, long>
        {
            { "RELIANCE", 1500000 },
            { "TCS", 1200000 },
            { "HDFCBANK", 800000 },
            { "INFY", 600000 },
            { "ICICIBANK", 500000 },
            { "SBIN", 400000 },
            { "HINDUNILVR", 500000 },
            { "ITC", 300000 },
            { "BHARTIARTL", 400000 },
            { "KOTAKBANK", 300000 }
        };

        return marketCaps.ContainsKey(symbol) ? marketCaps[symbol] : 50000; // Default 50,000 crores
    }

    private List<string> CreateSearchTerms(string companyName, List<string> aliases)
    {
        var searchTerms = new List<string> { companyName.ToLowerInvariant() };
        
        searchTerms.AddRange(aliases.Select(a => a.ToLowerInvariant()));
        
        // Add individual words from company name
        var words = companyName.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        searchTerms.AddRange(words.Where(w => w.Length > 2));
        
        return searchTerms.Distinct().ToList();
    }
}