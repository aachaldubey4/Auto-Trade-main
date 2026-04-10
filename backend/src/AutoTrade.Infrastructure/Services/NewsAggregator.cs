using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using System.Security.Cryptography;
using HtmlAgilityPack;

namespace AutoTrade.Infrastructure.Services;

public class NewsAggregator(HttpClient httpClient, IConfiguration configuration, ILogger<NewsAggregator> logger) : INewsAggregator, IDisposable
{
    private Timer? _timer;
    private Dictionary<string, RSSFeedConfig>? _feedConfigs;
    private Dictionary<string, RSSFeedConfig> FeedConfigs => _feedConfigs ??= LoadFeedConfigurations(configuration);
    private bool _isMonitoring = false;
    private readonly Dictionary<string, FeedHealthStatus> _feedHealth = new();

    public async Task StartMonitoringAsync()
    {
        if (_isMonitoring)
        {
            logger.LogWarning("News monitoring is already running");
            return;
        }

        logger.LogInformation("Starting news monitoring for {Count} RSS feeds", FeedConfigs.Count);
        
        // Initial fetch
        await FetchAllSourcesAsync();
        
        // Set up timer for periodic fetching (every 5 minutes by default)
        var intervalMinutes = configuration.GetValue<int>("NewsFeeds:FetchIntervalMinutes");
        if (intervalMinutes == 0) intervalMinutes = 5;
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        
        _timer = new Timer(async _ => await FetchAllSourcesAsync(), null, interval, interval);
        _isMonitoring = true;
        
        logger.LogInformation("News monitoring started with {Interval} minute intervals", intervalMinutes);
    }

    public async Task StopMonitoringAsync()
    {
        if (!_isMonitoring)
        {
            return;
        }

        _timer?.Dispose();
        _timer = null;
        _isMonitoring = false;
        
        logger.LogInformation("News monitoring stopped");
        await Task.CompletedTask;
    }

    public async Task<List<RawArticle>> FetchFromSourceAsync(string sourceUrl)
    {
        var articles = new List<RawArticle>();
        
        try
        {
            logger.LogDebug("Fetching RSS feed from {Url}", sourceUrl);

            using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5");
            request.Headers.TryAddWithoutValidation("Referer", "https://www.google.com/");

            using var response = await httpClient.SendAsync(request);

            var feedName = FeedConfigs.Values.FirstOrDefault(f => f.Url == sourceUrl)?.Name ?? sourceUrl;
            _feedHealth[sourceUrl] = new FeedHealthStatus(
                Name: feedName,
                Url: sourceUrl,
                IsHealthy: response.IsSuccessStatusCode,
                LastStatusCode: (int)response.StatusCode,
                LastChecked: DateTime.UtcNow,
                LastError: response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}"
            );

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();

            // Handle different encodings
            if (response.Content.Headers.ContentType?.CharSet != null)
            {
                var encoding = Encoding.GetEncoding(response.Content.Headers.ContentType.CharSet);
                var bytes = await response.Content.ReadAsByteArrayAsync();
                content = encoding.GetString(bytes);
            }

            using var stringReader = new StringReader(content);
            using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            });

            var feed = SyndicationFeed.Load(xmlReader);
            var feedTitle = feed.Title?.Text ?? ExtractDomainFromUrl(sourceUrl);

            foreach (var item in feed.Items)
            {
                try
                {
                    var article = CreateArticleFromSyndicationItem(item, feedTitle);
                    if (article != null)
                    {
                        articles.Add(article);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error processing RSS item from {Url}", sourceUrl);
                }
            }

            logger.LogInformation("Fetched {Count} articles from {Url}", articles.Count, sourceUrl);
        }
        catch (Exception ex)
        {
            var feedName = FeedConfigs.Values.FirstOrDefault(f => f.Url == sourceUrl)?.Name ?? sourceUrl;
            _feedHealth[sourceUrl] = new FeedHealthStatus(
                Name: feedName,
                Url: sourceUrl,
                IsHealthy: false,
                LastStatusCode: 0,
                LastChecked: DateTime.UtcNow,
                LastError: ex.Message.Length > 120 ? ex.Message[..120] : ex.Message
            );
            logger.LogError(ex, "Failed to fetch RSS feed from {Url}", sourceUrl);
        }

        return articles;
    }

    public async Task<bool> ValidateFeedAsync(string feedUrl)
    {
        try
        {
            using var response = await httpClient.GetAsync(feedUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("RSS feed validation failed for {Url}: {StatusCode}", feedUrl, response.StatusCode);
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            
            // Try to parse as RSS/Atom
            using var stringReader = new StringReader(content);
            using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            });

            var feed = SyndicationFeed.Load(xmlReader);
            
            logger.LogInformation("RSS feed validation successful for {Url}: {Title}", feedUrl, feed.Title?.Text);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RSS feed validation failed for {Url}", feedUrl);
            return false;
        }
    }

    public async Task<List<RawArticle>> FetchAllSourcesAsync()
    {
        var allArticles = new List<RawArticle>();
        var fetchTasks = new List<Task<List<RawArticle>>>();

        foreach (var feedConfig in FeedConfigs.Values.Where(f => f.Enabled))
        {
            fetchTasks.Add(FetchFromSourceAsync(feedConfig.Url));
        }

        try
        {
            var results = await Task.WhenAll(fetchTasks);
            
            foreach (var articles in results)
            {
                allArticles.AddRange(articles);
            }

            // Remove duplicates based on content hash
            var uniqueArticles = allArticles
                .GroupBy(a => a.ContentHash)
                .Select(g => g.First())
                .OrderByDescending(a => a.PublishedAt)
                .ToList();

            logger.LogInformation("Fetched {Total} articles ({Unique} unique) from {Sources} sources", 
                allArticles.Count, uniqueArticles.Count, FeedConfigs.Count);

            return uniqueArticles;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching from all RSS sources");
            return allArticles;
        }
    }

    private static Dictionary<string, RSSFeedConfig> LoadFeedConfigurations(IConfiguration configuration)
    {
        var configs = new Dictionary<string, RSSFeedConfig>();

        // Load from configuration
        var feedSection = configuration.GetSection("NewsFeeds");
        
        // Default feeds
        var defaultFeeds = new[]
        {
            new RSSFeedConfig
            {
                Name = "Moneycontrol",
                Url = feedSection["MoneycontrolRss"] ?? "https://www.moneycontrol.com/rss/business.xml",
                Enabled = true,
                FetchInterval = 5,
                Priority = 8
            },
            new RSSFeedConfig
            {
                Name = "Economic Times",
                Url = feedSection["EconomicTimesRss"] ?? "https://economictimes.indiatimes.com/markets/rssfeeds/1977021501.cms",
                Enabled = true,
                FetchInterval = 5,
                Priority = 9
            },
            new RSSFeedConfig
            {
                Name = "LiveMint",
                Url = feedSection["LiveMintRss"] ?? "https://www.livemint.com/rss/markets",
                Enabled = true,
                FetchInterval = 5,
                Priority = 7
            },
            new RSSFeedConfig
            {
                Name = "Business Standard",
                Url = "https://www.business-standard.com/rss/markets-106081.rss",
                Enabled = true,
                FetchInterval = 5,
                Priority = 5
            },
            new RSSFeedConfig
            {
                Name = "NDTV Profit",
                Url = "https://www.ndtv.com/business/rss",
                Enabled = true,
                FetchInterval = 5,
                Priority = 5
            }
        };

        foreach (var feed in defaultFeeds)
        {
            configs[feed.Name] = feed;
        }

        return configs;
    }

    private RawArticle? CreateArticleFromSyndicationItem(SyndicationItem item, string source)
    {
        try
        {
            var title = item.Title?.Text?.Trim();
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }

            var content = ExtractContent(item);
            var url = item.Links?.FirstOrDefault()?.Uri?.ToString() ?? string.Empty;
            var publishedAt = item.PublishDate.DateTime;
            
            // If no publish date, use current time
            if (publishedAt == default)
            {
                publishedAt = DateTime.UtcNow;
            }

            var contentForHash = $"{title}|{content}|{url}";
            var contentHash = ComputeHash(contentForHash);

            return new RawArticle
            {
                Title = title,
                Content = content,
                Url = url,
                PublishedAt = publishedAt,
                Source = source,
                ContentHash = contentHash
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error creating article from syndication item");
            return null;
        }
    }

    private string ExtractContent(SyndicationItem item)
    {
        // Try different content sources in order of preference
        string content = string.Empty;

        // 1. Try summary
        if (item.Summary?.Text != null)
        {
            content = item.Summary.Text;
        }
        // 2. Try content
        else if (item.Content is TextSyndicationContent textContent)
        {
            content = textContent.Text;
        }
        // 3. Try first element extension
        else if (item.ElementExtensions.Any())
        {
            try
            {
                var extension = item.ElementExtensions.First();
                content = extension.GetObject<string>() ?? string.Empty;
            }
            catch
            {
                // Ignore extension parsing errors
            }
        }

        // Clean HTML if present
        if (!string.IsNullOrEmpty(content) && content.Contains('<'))
        {
            content = CleanHtmlContent(content);
        }

        return content?.Trim() ?? string.Empty;
    }

    private string CleanHtmlContent(string htmlContent)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);
            
            // Remove script and style elements
            doc.DocumentNode.Descendants()
                .Where(n => n.Name == "script" || n.Name == "style")
                .ToList()
                .ForEach(n => n.Remove());
            
            return doc.DocumentNode.InnerText?.Trim() ?? string.Empty;
        }
        catch
        {
            // Fallback: simple HTML tag removal
            return System.Text.RegularExpressions.Regex.Replace(htmlContent, "<.*?>", string.Empty).Trim();
        }
    }

    private string ComputeHash(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private string ExtractDomainFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch
        {
            return "Unknown Source";
        }
    }

    public IReadOnlyList<FeedHealthStatus> GetFeedHealth()
    {
        return FeedConfigs.Values.Select(f =>
            _feedHealth.TryGetValue(f.Url, out var status)
                ? status
                : new FeedHealthStatus(f.Name, f.Url, false, 0, null, "Not yet fetched")
        ).ToList();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        httpClient?.Dispose();
    }
}