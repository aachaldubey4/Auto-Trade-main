using AutoTrade.Domain.Models;
using AutoTrade.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AutoTrade.WebAPI.Controllers;

/// <summary>
/// API endpoints for trading signals
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SignalsController(
    ISignalStorage signalStorage,
    ISignalGenerator signalGenerator,
    IMarketDataProvider marketDataProvider,
    TradingSignalsConfig config,
    ILogger<SignalsController> logger)
    : ControllerBase
{
    /// <summary>
    /// Get all active trading signals
    /// </summary>
    [HttpGet("active")]
    [ProducesResponseType(typeof(SignalsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SignalsResponse>> GetActiveSignals()
    {
        try
        {
            var signals = await signalStorage.GetActiveSignalsAsync();

            return Ok(new SignalsResponse
            {
                Success = true,
                Signals = signals,
                Count = signals.Count,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get active signals");
            return StatusCode(500, new SignalsResponse
            {
                Success = false,
                Error = "Failed to retrieve active signals"
            });
        }
    }

    /// <summary>
    /// Get overnight signals (generated at 8 PM for next day)
    /// </summary>
    [HttpGet("overnight")]
    [ProducesResponseType(typeof(SignalsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SignalsResponse>> GetOvernightSignals()
    {
        try
        {
            var signals = await signalStorage.GetOvernightSignalsAsync();

            return Ok(new SignalsResponse
            {
                Success = true,
                Signals = signals,
                Count = signals.Count,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get overnight signals");
            return StatusCode(500, new SignalsResponse
            {
                Success = false,
                Error = "Failed to retrieve overnight signals"
            });
        }
    }

    /// <summary>
    /// Get intraday signals (generated during market hours)
    /// </summary>
    [HttpGet("intraday")]
    [ProducesResponseType(typeof(SignalsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SignalsResponse>> GetIntradaySignals()
    {
        try
        {
            var signals = await signalStorage.GetIntradaySignalsAsync();

            return Ok(new SignalsResponse
            {
                Success = true,
                Signals = signals,
                Count = signals.Count,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get intraday signals");
            return StatusCode(500, new SignalsResponse
            {
                Success = false,
                Error = "Failed to retrieve intraday signals"
            });
        }
    }

    /// <summary>
    /// Get signals for a specific stock symbol
    /// </summary>
    [HttpGet("stock/{symbol}")]
    [ProducesResponseType(typeof(SignalsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SignalsResponse>> GetSignalsByStock(string symbol)
    {
        try
        {
            var signals = await signalStorage.GetSignalsBySymbolAsync(symbol.ToUpper());

            if (!signals.Any())
            {
                return NotFound(new SignalsResponse
                {
                    Success = false,
                    Error = $"No signals found for symbol {symbol}"
                });
            }

            return Ok(new SignalsResponse
            {
                Success = true,
                Signals = signals,
                Count = signals.Count,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get signals for {Symbol}", symbol);
            return StatusCode(500, new SignalsResponse
            {
                Success = false,
                Error = $"Failed to retrieve signals for {symbol}"
            });
        }
    }

    /// <summary>
    /// Get historical signals with optional filtering
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(SignalsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SignalsResponse>> GetHistoricalSignals(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? symbol,
        [FromQuery] string? action,
        [FromQuery] string? status)
    {
        try
        {
            var fromDate = from ?? DateTime.UtcNow.AddDays(-7);
            var toDate = to ?? DateTime.UtcNow;

            var signals = await signalStorage.GetHistoricalSignalsAsync(fromDate, toDate);

            // Apply additional filters
            if (!string.IsNullOrEmpty(symbol))
            {
                signals = signals.Where(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(action))
            {
                signals = signals.Where(s => s.Action.ToString().Equals(action, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(status))
            {
                signals = signals.Where(s => s.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return Ok(new SignalsResponse
            {
                Success = true,
                Signals = signals,
                Count = signals.Count,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get historical signals");
            return StatusCode(500, new SignalsResponse
            {
                Success = false,
                Error = "Failed to retrieve historical signals"
            });
        }
    }

    /// <summary>
    /// Manually trigger signal generation
    /// </summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(SignalsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SignalsResponse>> GenerateSignals([FromBody] GenerateSignalsRequest request)
    {
        try
        {
            if (request == null)
            {
                return BadRequest(new SignalsResponse
                {
                    Success = false,
                    Error = "Request body is required"
                });
            }

            var requestedType = request.Type;
            var effectiveType = requestedType;
            string? effectiveTypeReason = null;

            if (requestedType == SignalType.Intraday && !await marketDataProvider.IsMarketOpenAsync())
            {
                effectiveType = SignalType.Overnight;
                effectiveTypeReason = "market_closed_auto_switched_to_overnight";
            }

            logger.LogInformation("Manual signal generation triggered for requested type: {RequestedType}, effective type: {EffectiveType}",
                requestedType, effectiveType);

            var generationResult = await signalGenerator.GenerateSignalsWithDiagnosticsAsync(effectiveType);
            var signals = generationResult.Signals;
            var diagnostics = request.IncludeDiagnostics ? generationResult.Diagnostics : new List<StockSignalDiagnostic>();
            var diagnosticsSummary = request.IncludeDiagnostics
                ? BuildDiagnosticsSummary(generationResult.Diagnostics)
                : new Dictionary<string, int>();

            return Ok(new SignalsResponse
            {
                Success = true,
                Signals = signals,
                Count = signals.Count,
                Message = $"Generated {signals.Count} {effectiveType} signals",
                RequestedType = requestedType.ToString(),
                EffectiveType = effectiveType.ToString(),
                EffectiveTypeReason = effectiveTypeReason,
                DiagnosticsSummary = diagnosticsSummary,
                StockDiagnostics = diagnostics,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate signals");
            return StatusCode(500, new SignalsResponse
            {
                Success = false,
                Error = "Failed to generate signals"
            });
        }
    }

    private static Dictionary<string, int> BuildDiagnosticsSummary(List<StockSignalDiagnostic> diagnostics)
    {
        var summary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var diagnostic in diagnostics)
        {
            foreach (var reason in diagnostic.RejectionReasons)
            {
                summary[reason] = summary.TryGetValue(reason, out var count) ? count + 1 : 1;
            }
        }

        return summary;
    }

    /// <summary>
    /// Update signal status (e.g., mark as executed)
    /// </summary>
    [HttpPatch("{signalId}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> UpdateSignalStatus(string signalId, [FromBody] UpdateStatusRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request?.Status))
            {
                return BadRequest(new { error = "Status is required" });
            }

            await signalStorage.UpdateSignalStatusAsync(signalId, request.Status);

            return Ok(new { success = true, message = $"Signal {signalId} status updated to {request.Status}" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update signal status");
            return StatusCode(500, new { error = "Failed to update signal status" });
        }
    }

    /// <summary>
    /// Mark a signal as executed with optional price overrides
    /// </summary>
    [HttpPost("{signalId}/execute")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ExecuteSignal(string signalId, [FromBody] ExecuteSignalRequest request)
    {
        try
        {
            var existing = await signalStorage.GetSignalByIdAsync(signalId);
            if (existing == null)
            {
                return NotFound(new { error = "Signal not found" });
            }

            var entry = request.EntryPrice ?? existing.EntryPrice;
            var target = request.TargetPrice ?? existing.TargetPrice;
            var stop = request.StopLoss ?? existing.StopLoss;

            if (entry <= 0 || target <= 0 || stop <= 0)
            {
                return BadRequest(new { error = "Entry, target and stop-loss must be > 0" });
            }

            var stopPercent = Math.Abs((entry - stop) / entry) * 100;
            var targetPercent = Math.Abs((target - entry) / entry) * 100;

            if (stopPercent < (decimal)config.RiskManagement.StopLossMinPercent ||
                stopPercent > (decimal)config.RiskManagement.StopLossMaxPercent)
            {
                return BadRequest(new
                {
                    error = $"Stop-loss must be between {config.RiskManagement.StopLossMinPercent}% and {config.RiskManagement.StopLossMaxPercent}% from entry"
                });
            }

            if (targetPercent < (decimal)config.RiskManagement.TargetMinPercent ||
                targetPercent > (decimal)config.RiskManagement.TargetMaxPercent)
            {
                return BadRequest(new
                {
                    error = $"Target must be between {config.RiskManagement.TargetMinPercent}% and {config.RiskManagement.TargetMaxPercent}% from entry"
                });
            }

            var riskReward = stopPercent == 0 ? 0 : targetPercent / stopPercent;
            if (riskReward < (decimal)config.RiskManagement.MinRiskRewardRatio)
            {
                return BadRequest(new
                {
                    error = $"Risk-reward ratio must be at least {config.RiskManagement.MinRiskRewardRatio}"
                });
            }

            await signalStorage.ExecuteSignalAsync(signalId, request.EntryPrice, request.TargetPrice, request.StopLoss);

            return Ok(new { success = true, message = $"Signal {signalId} executed" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute signal");
            return StatusCode(500, new { error = "Failed to execute signal" });
        }
    }
}

public class SignalsResponse
{
    public bool Success { get; set; }
    public List<TradingSignal> Signals { get; set; } = new();
    public int Count { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public string? RequestedType { get; set; }
    public string? EffectiveType { get; set; }
    public string? EffectiveTypeReason { get; set; }
    public Dictionary<string, int> DiagnosticsSummary { get; set; } = new();
    public List<StockSignalDiagnostic> StockDiagnostics { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class GenerateSignalsRequest
{
    public SignalType Type { get; set; }
    public List<string>? Symbols { get; set; }
    public bool IncludeDiagnostics { get; set; } = true;
}

public class UpdateStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

public class ExecuteSignalRequest
{
    public decimal? EntryPrice { get; set; }
    public decimal? TargetPrice { get; set; }
    public decimal? StopLoss { get; set; }
}
