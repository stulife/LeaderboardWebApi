using LeaderboardWebApi.Models;
using LeaderboardWebApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeaderboardWebApi.Controllers;

[ApiController]
[Route("[controller]")]
public class MonitoringController : ControllerBase
{
    private readonly ILeaderboardService _leaderboardService;

    public MonitoringController(ILeaderboardService leaderboardService)
    {
        _leaderboardService = leaderboardService;
    }

    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        return Ok("Healthy");
    }

    [HttpGet("metrics")]
    public IActionResult GetMetrics()
    {
        var metrics = _leaderboardService.GetMetrics();
        return Ok(metrics);
    }
}