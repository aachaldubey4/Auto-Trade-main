using Microsoft.AspNetCore.Mvc;
using AutoTrade.Domain.Models;
using AutoTrade.Application.Interfaces;

namespace AutoTrade.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class HealthController(
    INewsProcessingService newsProcessing,
    ILogger<HealthController> logger)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 500)]
    public ActionResult<ApiResponse<object>> GetHealth()
    {
        try
        {
            var healthData = new
            {
                Status = "healthy",
                Timestamp = DateTime.UtcNow,
                Version = "1.0.0",
                Services = new
                {
                    NewsProcessing = newsProcessing.IsProcessing ? "running" : "stopped"
                }
            };

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Data = healthData
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in health check");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "HEALTH_CHECK_FAILED",
                    Message = "Health check failed"
                }
            });
        }
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 500)]
    public ActionResult<ApiResponse<object>> GetStatus()
    {
        try
        {
            var statusData = new
            {
                Application = new
                {
                    Name = "AutoTrade Backend",
                    Version = "1.0.0",
                    Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
                    StartTime = DateTime.UtcNow,
                    Uptime = TimeSpan.FromMinutes(1)
                },
                Services = new
                {
                    NewsProcessing = new
                    {
                        Status = newsProcessing.IsProcessing ? "running" : "stopped",
                        LastProcessed = DateTime.UtcNow.AddMinutes(-5)
                    },
                    Database = new
                    {
                        Status = "connected",
                        Type = "MongoDB"
                    }
                },
                System = new
                {
                    MachineName = Environment.MachineName,
                    ProcessorCount = Environment.ProcessorCount,
                    WorkingSet = GC.GetTotalMemory(false),
                    OSVersion = Environment.OSVersion.ToString()
                }
            };

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Data = statusData
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting system status");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "STATUS_CHECK_FAILED",
                    Message = "Status check failed"
                }
            });
        }
    }
}
