using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace AutoTrade.Infrastructure.Services;

/// <summary>
/// Blends Loughran-McDonald financial lexicon (with Indian phrases) and headline heuristics
/// to produce a sentiment score for each article.
///
/// Blending weights (configurable in appsettings.json under SentimentAnalysis):
///   - When article has content  (>= MinContentLength): LexiconWeight + HeadlineWeight = 1.0
///   - When only title available: LexiconWeightHeadlineOnly + HeadlineWeightHeadlineOnly = 1.0
/// </summary>
public class SentimentAnalyzer(
    ILoughranMcDonaldAnalyzer lmAnalyzer,
    IHeadlineHeuristicAnalyzer headlineAnalyzer,
    MongoDbContext dbContext,
    IConfiguration configuration,
    ILogger<SentimentAnalyzer> logger)
    : ISentimentAnalyzer
{
    private SentimentAnalysisSettings Settings => new(
        LexiconWeightWithContent:  configuration.GetValue("SentimentAnalysis:LexiconWeightWithContent",  0.7),
        HeadlineWeightWithContent: configuration.GetValue("SentimentAnalysis:HeadlineWeightWithContent", 0.3),
        LexiconWeightHeadlineOnly: configuration.GetValue("SentimentAnalysis:LexiconWeightHeadlineOnly", 0.4),
        HeadlineWeightHeadlineOnly:configuration.GetValue("SentimentAnalysis:HeadlineWeightHeadlineOnly",0.6),
        MinContentLength:          configuration.GetValue("SentimentAnalysis:MinContentLengthForFullAnalysis", 50)
    );

    public async Task<ProcessedArticle> AnalyzeArticleAsync(RawArticle article)
    {
        try
        {
            logger.LogDebug("Analyzing article: {Title}", article.Title);

            var sentiment = await ComputeSentimentAsync(article);
            var marketCategory = DetermineMarketCategory(article.Title + " " + article.Content);
            var keywords = ExtractKeywords(article.Title + " " + article.Content);

            var processed = new ProcessedArticle
            {
                Title = article.Title,
                Content = article.Content,
                Url = article.Url,
                PublishedAt = article.PublishedAt,
                Source = article.Source,
                ContentHash = article.ContentHash,
                Sentiment = sentiment,
                Keywords = keywords,
                Entities = [],
                MarketCategory = marketCategory,
                MarketRelevance = ComputeMarketRelevance(sentiment, keywords.Count),
                ProcessedAt = DateTime.UtcNow
            };

            logger.LogDebug("Analyzed '{Title}' → {Sentiment} (confidence {Confidence:F2})",
                article.Title, sentiment.Overall, sentiment.Confidence);

            return processed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error analyzing article: {Title}", article.Title);
            return CreateFallbackProcessedArticle(article);
        }
    }

    public async Task<List<ProcessedArticle>> BatchAnalyzeAsync(List<RawArticle> articles)
    {
        logger.LogInformation("Batch analyzing {Count} articles", articles.Count);

        var tasks = articles.Select(AnalyzeArticleAsync);
        var results = await Task.WhenAll(tasks);
        return [.. results];
    }

    public async Task<MarketCategory> CategorizeByMarketAsync(ProcessedArticle article)
    {
        return DetermineMarketCategory(article.Title + " " + article.Content);
    }

    public async Task<SentimentScore> GetNewsSentimentAsync(string symbol)
    {
        try
        {
            var filter = Builders<ArticleDocument>.Filter.And(
                Builders<ArticleDocument>.Filter.AnyEq(x => x.StockSymbols, symbol),
                Builders<ArticleDocument>.Filter.Gte(x => x.PublishedAt, DateTime.UtcNow.AddHours(-24))
            );

            var articles = await dbContext.Articles
                .Find(filter)
                .SortByDescending(x => x.PublishedAt)
                .Limit(50)
                .ToListAsync();

            if (articles.Count == 0)
                return NeutralScore(0.0);

            var totalWeight = 0.0;
            var wPos = 0.0;
            var wNeg = 0.0;
            var wNeu = 0.0;

            foreach (var a in articles)
            {
                var hoursOld = (DateTime.UtcNow - a.PublishedAt).TotalHours;
                var weight = Math.Max(0.1, 1.0 - hoursOld / 24.0) * a.MarketRelevance;

                totalWeight += weight;
                wPos += a.Sentiment.Positive * weight;
                wNeg += a.Sentiment.Negative * weight;
                wNeu += a.Sentiment.Neutral  * weight;
            }

            if (totalWeight == 0) return NeutralScore(0.0);

            var p = wPos / totalWeight;
            var n = wNeg / totalWeight;
            var u = wNeu / totalWeight;
            var overall = p > n && p > u ? "positive" : n > p && n > u ? "negative" : "neutral";

            return new SentimentScore
            {
                Positive = p, Negative = n, Neutral = u,
                Overall = overall,
                Confidence = Math.Max(p, Math.Max(n, u))
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting news sentiment for {Symbol}", symbol);
            return NeutralScore(0.0);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────

    private async Task<SentimentScore> ComputeSentimentAsync(RawArticle article)
    {
        var s = Settings;
        var hasContent = !string.IsNullOrWhiteSpace(article.Content)
                         && article.Content.Length >= s.MinContentLength;

        // 1. Loughran-McDonald score (uses content > title internally)
        var lmResult = await lmAnalyzer.AnalyzeAsync(article);

        // Convert L-M result to a single [-1, +1] value
        var adjustedNeg = lmResult.Negative + lmResult.Uncertainty * 0.5 + lmResult.Litigious * 0.7;
        var lmScore = lmResult.Positive - adjustedNeg;   // positive = bullish

        // 2. Headline heuristic score [-1, +1]
        var headlineScore = headlineAnalyzer.ScoreHeadline(article.Title);

        // 3. Blend
        double lexiconW, headlineW;
        if (hasContent)
        {
            lexiconW  = s.LexiconWeightWithContent;
            headlineW = s.HeadlineWeightWithContent;
        }
        else
        {
            lexiconW  = s.LexiconWeightHeadlineOnly;
            headlineW = s.HeadlineWeightHeadlineOnly;
        }

        var blended = lexiconW * lmScore + headlineW * headlineScore;

        // Map blended [-1, +1] → positive/negative/neutral scores summing to 1.0
        var positive = Math.Clamp(0.5 + blended * 0.5, 0.0, 1.0);
        var negative  = Math.Clamp(0.5 - blended * 0.5, 0.0, 1.0);
        var neutral   = Math.Max(0.0, 1.0 - positive - negative);

        var overall = positive > negative + 0.05 ? "positive"
                    : negative > positive + 0.05 ? "negative"
                    : "neutral";

        // Confidence: higher when both signals agree, lower when they conflict
        var signalAlignment = 1.0 - Math.Abs(Math.Sign(lmScore) - Math.Sign(headlineScore)) * 0.3;
        var baseConfidence = Math.Max(Math.Abs(lmScore), Math.Abs(headlineScore));
        var confidence = Math.Clamp(baseConfidence * signalAlignment, 0.0, 1.0);

        return new SentimentScore
        {
            Positive   = Math.Round(positive,  3),
            Negative   = Math.Round(negative,  3),
            Neutral    = Math.Round(neutral,   3),
            Overall    = overall,
            Confidence = Math.Round(confidence, 3)
        };
    }

    private static MarketCategory DetermineMarketCategory(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return MarketCategory.GeneralMarket;

        var lower = text.ToLowerInvariant();
        var checks = new[]
        {
            (MarketCategory.Banking,       new[] { "bank", "rbi", "npa", "loan", "credit", "nbfc", "insurance" }),
            (MarketCategory.IT,            new[] { "software", "it sector", "tech", "cloud", "ai ", "digital" }),
            (MarketCategory.Pharma,        new[] { "pharma", "drug", "medicine", "healthcare", "vaccine", "biocon" }),
            (MarketCategory.Auto,          new[] { "auto", "vehicle", "car", "ev ", "electric vehicle", "motorcycle" }),
            (MarketCategory.Energy,        new[] { "oil", "gas", "petroleum", "power", "renewable", "solar", "wind" }),
            (MarketCategory.FMCG,          new[] { "fmcg", "consumer goods", "retail", "packaged food" }),
            (MarketCategory.Metals,        new[] { "steel", "copper", "aluminium", "iron ore", "mining" }),
            (MarketCategory.Realty,        new[] { "real estate", "property", "housing", "construction" }),
            (MarketCategory.Telecom,       new[] { "telecom", "mobile network", "5g", "spectrum" }),
            (MarketCategory.Infrastructure,new[] { "infrastructure", "roads", "ports", "airports", "railway" }),
        };

        foreach (var (category, keywords) in checks)
            if (keywords.Any(k => lower.Contains(k)))
                return category;

        return MarketCategory.GeneralMarket;
    }

    private static List<string> ExtractKeywords(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by",
            "is", "are", "was", "were", "be", "been", "have", "has", "had", "will", "would",
            "could", "should", "may", "might", "can", "a", "an", "this", "that", "its"
        };

        return text.ToLowerInvariant()
            .Split([' ', '.', ',', ';', ':', '!', '?', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3 && !stopWords.Contains(w))
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .Take(15)
            .Select(g => g.Key)
            .ToList();
    }

    private static double ComputeMarketRelevance(SentimentScore sentiment, int keywordCount)
    {
        // Higher confidence and more financial keywords = more relevant to markets
        var keywordRelevance = Math.Min(1.0, keywordCount / 10.0);
        return Math.Round((sentiment.Confidence * 0.6 + keywordRelevance * 0.4), 2);
    }

    private static SentimentScore NeutralScore(double confidence) => new()
    {
        Positive = 0.33, Negative = 0.33, Neutral = 0.34,
        Overall = "neutral", Confidence = confidence
    };

    private static ProcessedArticle CreateFallbackProcessedArticle(RawArticle article) => new()
    {
        Title = article.Title,
        Content = article.Content,
        Url = article.Url,
        PublishedAt = article.PublishedAt,
        Source = article.Source,
        ContentHash = article.ContentHash,
        Sentiment = NeutralScore(0.1),
        Keywords = [],
        Entities = [],
        MarketCategory = MarketCategory.GeneralMarket,
        MarketRelevance = 0.5,
        ProcessedAt = DateTime.UtcNow
    };

    private record SentimentAnalysisSettings(
        double LexiconWeightWithContent,
        double HeadlineWeightWithContent,
        double LexiconWeightHeadlineOnly,
        double HeadlineWeightHeadlineOnly,
        int MinContentLength);
}
