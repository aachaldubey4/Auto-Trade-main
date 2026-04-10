using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

namespace AutoTrade.Infrastructure.Services;

/// <summary>
/// Loughran-McDonald financial sentiment analyzer
/// Uses the Loughran-McDonald Master Dictionary for financial text analysis
/// </summary>
public class LoughranMcDonaldAnalyzer(ILogger<LoughranMcDonaldAnalyzer> logger) : ILoughranMcDonaldAnalyzer
{
    private readonly Dictionary<string, string> _wordCategories = new();
    private bool _isInitialized = false;

    // Word lists by category
    private HashSet<string> _positiveWords = new();
    private HashSet<string> _negativeWords = new();
    private HashSet<string> _uncertaintyWords = new();
    private HashSet<string> _litigiousWords = new();
    private HashSet<string> _constrainingWords = new();

    public bool IsInitialized => _isInitialized;

    public async Task<bool> InitializeAsync()
    {
        try
        {
            logger.LogInformation("Initializing Loughran-McDonald financial dictionary");

            // Initialize with built-in financial word lists
            // In production, you would load from CSV files downloaded from:
            // https://sraf.nd.edu/loughranmcdonald-master-dictionary/
            InitializeBuiltInDictionary();

            _isInitialized = true;
            logger.LogInformation("Loughran-McDonald analyzer initialized with {WordCount} financial terms", 
                _wordCategories.Count);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize Loughran-McDonald analyzer");
            return false;
        }
    }

    // Indian financial phrases — checked via string containment, weighted by IndianPhraseMultiplier
    private static readonly Dictionary<string, string> IndianFinancialPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Positive phrases
        { "beats estimates", "positive" },
        { "beat estimates", "positive" },
        { "above expectations", "positive" },
        { "bags order", "positive" },
        { "bagged order", "positive" },
        { "wins contract", "positive" },
        { "record profit", "positive" },
        { "record revenue", "positive" },
        { "rbi rate cut", "positive" },
        { "strong quarterly results", "positive" },
        { "fii inflow", "positive" },
        { "upgrade rating", "positive" },
        { "rating upgrade", "positive" },
        { "market outperform", "positive" },
        { "order win", "positive" },
        { "revenue growth", "positive" },
        { "margin expansion", "positive" },
        { "dividend declared", "positive" },
        { "buyback approved", "positive" },
        { "expansion plan", "positive" },
        { "strong demand", "positive" },
        { "capacity expansion", "positive" },
        { "new product launch", "positive" },
        { "export growth", "positive" },
        { "profit rises", "positive" },
        { "profit jumps", "positive" },
        // Negative phrases
        { "misses estimates", "negative" },
        { "missed estimates", "negative" },
        { "below expectations", "negative" },
        { "promoter pledge", "negative" },
        { "sebi penalty", "negative" },
        { "sebi action", "negative" },
        { "npa rises", "negative" },
        { "npa increase", "negative" },
        { "downgrade rating", "negative" },
        { "rating downgrade", "negative" },
        { "fii outflow", "negative" },
        { "debt default", "negative" },
        { "rights issue", "negative" },
        { "fraud allegation", "negative" },
        { "revenue decline", "negative" },
        { "margin pressure", "negative" },
        { "stake sale by promoter", "negative" },
        { "regulatory action", "negative" },
        { "import duty hike", "negative" },
        { "profit decline", "negative" },
        { "profit falls", "negative" },
        { "loan default", "negative" },
        { "insolvency proceedings", "negative" },
        // Uncertainty phrases
        { "sebi review", "uncertainty" },
        { "rbi policy review", "uncertainty" },
        { "merger speculation", "uncertainty" },
        { "management change", "uncertainty" },
        { "earnings guidance", "uncertainty" },
    };

    public async Task<LoughranMcDonaldResult> AnalyzeAsync(RawArticle article)
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        try
        {
            // Use article content (description/summary) if meaningful; fall back to title
            var analysisText = !string.IsNullOrWhiteSpace(article.Content) && article.Content.Length >= 50
                ? $"{article.Title} {article.Content}"
                : article.Title;

            var text = analysisText;
            var words = ExtractWords(text);
            
            var result = new LoughranMcDonaldResult
            {
                TotalWords = words.Count
            };

            // Count words by category
            var positiveCount = 0;
            var negativeCount = 0;
            var uncertaintyCount = 0;
            var litigiousCount = 0;
            var constrainingCount = 0;
            var financialWordCount = 0;

            foreach (var word in words)
            {
                var lowerWord = word.ToLowerInvariant();
                
                if (_positiveWords.Contains(lowerWord))
                {
                    positiveCount++;
                    financialWordCount++;
                }
                else if (_negativeWords.Contains(lowerWord))
                {
                    negativeCount++;
                    financialWordCount++;
                }
                else if (_uncertaintyWords.Contains(lowerWord))
                {
                    uncertaintyCount++;
                    financialWordCount++;
                }
                else if (_litigiousWords.Contains(lowerWord))
                {
                    litigiousCount++;
                    financialWordCount++;
                }
                else if (_constrainingWords.Contains(lowerWord))
                {
                    constrainingCount++;
                    financialWordCount++;
                }
            }

            // Score Indian financial phrases (weighted 2x single words)
            var lowerText = text.ToLowerInvariant();
            const double indianPhraseMultiplier = 2.0;
            foreach (var (phrase, category) in IndianFinancialPhrases)
            {
                if (!lowerText.Contains(phrase, StringComparison.OrdinalIgnoreCase)) continue;
                financialWordCount += (int)indianPhraseMultiplier;
                switch (category)
                {
                    case "positive":    positiveCount    += (int)indianPhraseMultiplier; break;
                    case "negative":    negativeCount    += (int)indianPhraseMultiplier; break;
                    case "uncertainty": uncertaintyCount += (int)indianPhraseMultiplier; break;
                }
            }

            result.FinancialWords = financialWordCount;
            result.WordCounts = new Dictionary<string, int>
            {
                ["positive"] = positiveCount,
                ["negative"] = negativeCount,
                ["uncertainty"] = uncertaintyCount,
                ["litigious"] = litigiousCount,
                ["constraining"] = constrainingCount
            };

            // Calculate normalized scores
            if (financialWordCount > 0)
            {
                result.Positive = (double)positiveCount / financialWordCount;
                result.Negative = (double)negativeCount / financialWordCount;
                result.Uncertainty = (double)uncertaintyCount / financialWordCount;
                result.Litigious = (double)litigiousCount / financialWordCount;
                result.Constraining = (double)constrainingCount / financialWordCount;
            }

            // Calculate neutral as remainder
            result.Neutral = Math.Max(0, 1.0 - result.Positive - result.Negative);

            // Determine overall sentiment
            result.Overall = DetermineOverallSentiment(result);

            // Calculate confidence based on financial word density
            result.Confidence = Math.Min(1.0, (double)financialWordCount / Math.Max(1, words.Count / 10));

            logger.LogDebug("L-M Analysis: {Title} -> P:{Positive:F2} N:{Negative:F2} U:{Uncertainty:F2} ({FinancialWords}/{TotalWords} words)",
                article.Title, result.Positive, result.Negative, result.Uncertainty, 
                result.FinancialWords, result.TotalWords);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in Loughran-McDonald analysis for article: {Title}", article.Title);
            
            // Return neutral result on error
            return new LoughranMcDonaldResult
            {
                Positive = 0.33,
                Negative = 0.33,
                Neutral = 0.34,
                Overall = "neutral",
                Confidence = 0.1
            };
        }
    }

    private List<string> ExtractWords(string text)
    {
        // Remove HTML tags, special characters, keep only alphanumeric
        var cleanText = Regex.Replace(text, @"<[^>]+>", " ");
        cleanText = Regex.Replace(cleanText, @"[^\w\s]", " ");
        
        // Split into words, filter out short words and numbers
        return cleanText
            .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !Regex.IsMatch(w, @"^\d+$"))
            .ToList();
    }

    private string DetermineOverallSentiment(LoughranMcDonaldResult result)
    {
        // Consider uncertainty and litigation as negative factors for trading
        var adjustedNegative = result.Negative + (result.Uncertainty * 0.5) + (result.Litigious * 0.7);
        
        if (result.Positive > adjustedNegative + 0.1)
            return "positive";
        else if (adjustedNegative > result.Positive + 0.1)
            return "negative";
        else
            return "neutral";
    }

    private void InitializeBuiltInDictionary()
    {
        // Built-in financial sentiment words based on Loughran-McDonald research
        // In production, load from official CSV files
        
        // Positive financial words
        var positiveWords = new[]
        {
            "achieve", "achieved", "achievement", "achievements", "achieving", "advance", "advanced", "advancement",
            "advances", "advancing", "benefit", "benefited", "benefiting", "benefits", "better", "boom", "booming",
            "boost", "boosted", "boosting", "breakthrough", "breakthroughs", "brilliant", "bullish", "confident",
            "confidence", "deliver", "delivered", "delivering", "delivers", "earnings", "efficient", "enhance",
            "enhanced", "enhancement", "enhances", "enhancing", "excellent", "exceptional", "expand", "expanded",
            "expanding", "expansion", "expansions", "expands", "gain", "gained", "gaining", "gains", "good",
            "great", "grow", "growing", "grown", "grows", "growth", "high", "improve", "improved", "improvement",
            "improvements", "improving", "improves", "increase", "increased", "increases", "increasing", "innovation",
            "innovative", "leader", "leading", "opportunity", "opportunities", "optimistic", "outperform",
            "outperformed", "outperforming", "outperforms", "positive", "profit", "profitable", "profits",
            "progress", "record", "revenue", "revenues", "rise", "rising", "strong", "stronger", "success",
            "successful", "successfully", "superior", "surge", "surged", "surges", "surging", "upbeat", "upturn"
        };

        // Negative financial words
        var negativeWords = new[]
        {
            "abandon", "abandoned", "abandoning", "abandonment", "adverse", "adversely", "allegation", "allegations",
            "bankrupt", "bankruptcy", "breach", "breached", "breaches", "breaching", "challenge", "challenged",
            "challenges", "challenging", "concern", "concerned", "concerning", "concerns", "crisis", "critical",
            "decline", "declined", "declines", "declining", "decrease", "decreased", "decreases", "decreasing",
            "deficit", "deficits", "deteriorate", "deteriorated", "deteriorates", "deteriorating", "deterioration",
            "difficult", "difficulties", "difficulty", "disappoint", "disappointed", "disappointing", "disappointment",
            "disappoints", "downturn", "drop", "dropped", "dropping", "drops", "fail", "failed", "failing",
            "fails", "failure", "failures", "fall", "fallen", "falling", "falls", "fell", "harm", "harmed",
            "harmful", "harming", "harms", "hurdle", "hurdles", "impair", "impaired", "impairing", "impairment",
            "impairs", "impossible", "inadequate", "loss", "losses", "lost", "negative", "negatively", "obstacle",
            "obstacles", "poor", "problem", "problems", "recession", "reduce", "reduced", "reduces", "reducing",
            "reduction", "reductions", "restructure", "restructured", "restructures", "restructuring", "risk",
            "risks", "risky", "setback", "setbacks", "shortfall", "shortfalls", "slow", "slowed", "slower",
            "slowing", "slows", "struggle", "struggled", "struggles", "struggling", "unfavorable", "unfavourable",
            "weak", "weaken", "weakened", "weakening", "weakens", "weakness", "weaknesses", "worse", "worsen",
            "worsened", "worsening", "worsens", "worst"
        };

        // Uncertainty words
        var uncertaintyWords = new[]
        {
            "ambiguous", "ambiguity", "anticipate", "anticipated", "anticipates", "anticipating", "anticipation",
            "appear", "appeared", "appearing", "appears", "approximate", "approximately", "assume", "assumed",
            "assumes", "assuming", "assumption", "assumptions", "believe", "believed", "believes", "believing",
            "cautious", "cautiously", "conditional", "could", "depend", "depended", "depending", "depends",
            "estimate", "estimated", "estimates", "estimating", "estimation", "expect", "expected", "expecting",
            "expects", "forecast", "forecasted", "forecasting", "forecasts", "guidance", "hope", "hoped",
            "hopes", "hoping", "indicate", "indicated", "indicates", "indicating", "indication", "indications",
            "may", "maybe", "might", "outlook", "pending", "perhaps", "possible", "possibly", "potential",
            "potentially", "predict", "predicted", "predicting", "prediction", "predictions", "predicts",
            "preliminary", "probably", "project", "projected", "projecting", "projection", "projections",
            "projects", "risk", "risks", "risky", "should", "subject", "suggest", "suggested", "suggesting",
            "suggests", "uncertain", "uncertainties", "uncertainty", "unclear", "unknown", "unpredictable",
            "variable", "variability", "variables", "vary", "varying", "volatile", "volatility", "would"
        };

        // Litigious words
        var litigiousWords = new[]
        {
            "action", "actions", "allegation", "allegations", "allege", "alleged", "alleges", "alleging",
            "attorney", "attorneys", "claim", "claimed", "claiming", "claims", "complaint", "complaints",
            "court", "courts", "defendant", "defendants", "enforce", "enforced", "enforcement", "enforces",
            "enforcing", "investigation", "investigations", "lawsuit", "lawsuits", "legal", "legally",
            "legislation", "legislative", "liable", "liability", "liabilities", "litigate", "litigation",
            "litigations", "penalty", "penalties", "plaintiff", "plaintiffs", "prosecute", "prosecuted",
            "prosecutes", "prosecuting", "prosecution", "regulatory", "regulations", "settle", "settled",
            "settlement", "settlements", "settles", "settling", "sue", "sued", "sues", "suing", "suit",
            "suits", "trial", "trials", "violation", "violations", "violate", "violated", "violates",
            "violating"
        };

        // Constraining words
        var constrainingWords = new[]
        {
            "constrain", "constrained", "constraining", "constrains", "constraint", "constraints", "limit",
            "limited", "limiting", "limits", "limitation", "limitations", "prevent", "prevented", "preventing",
            "prevents", "prevention", "prohibit", "prohibited", "prohibiting", "prohibition", "prohibitions",
            "prohibits", "restrict", "restricted", "restricting", "restriction", "restrictions", "restricts"
        };

        // Populate hash sets for fast lookup
        foreach (var word in positiveWords)
            _positiveWords.Add(word.ToLowerInvariant());

        foreach (var word in negativeWords)
            _negativeWords.Add(word.ToLowerInvariant());

        foreach (var word in uncertaintyWords)
            _uncertaintyWords.Add(word.ToLowerInvariant());

        foreach (var word in litigiousWords)
            _litigiousWords.Add(word.ToLowerInvariant());

        foreach (var word in constrainingWords)
            _constrainingWords.Add(word.ToLowerInvariant());

        logger.LogInformation("Loaded L-M dictionary: {Positive} positive, {Negative} negative, {Uncertainty} uncertainty, {Litigious} litigious, {Constraining} constraining words",
            _positiveWords.Count, _negativeWords.Count, _uncertaintyWords.Count, _litigiousWords.Count, _constrainingWords.Count);
    }
}