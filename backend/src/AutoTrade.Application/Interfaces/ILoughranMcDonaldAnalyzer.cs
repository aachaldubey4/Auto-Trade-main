using AutoTrade.Domain.Models;

namespace AutoTrade.Application.Interfaces;

/// <summary>
/// Interface for Loughran-McDonald financial sentiment analysis
/// </summary>
public interface ILoughranMcDonaldAnalyzer
{
    /// <summary>
    /// Analyze text using Loughran-McDonald financial dictionary
    /// </summary>
    /// <param name="article">Raw article to analyze</param>
    /// <returns>Financial sentiment analysis with uncertainty and litigation metrics</returns>
    Task<LoughranMcDonaldResult> AnalyzeAsync(RawArticle article);

    /// <summary>
    /// Initialize the analyzer with dictionary data
    /// </summary>
    /// <returns>True if initialization successful</returns>
    Task<bool> InitializeAsync();

    /// <summary>
    /// Check if analyzer is ready to use
    /// </summary>
    bool IsInitialized { get; }
}