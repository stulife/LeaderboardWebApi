using LeaderboardWebApi.Models;
using LeaderboardWebApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeaderboardWebApi.Controllers;

[ApiController]
[Route("[controller]")]
public class LeaderboardController : ControllerBase
{
    private readonly ILeaderboardService _leaderboardService;
    private readonly ILogger<LeaderboardController> _logger;

    public LeaderboardController(
        ILeaderboardService leaderboardService,
        ILogger<LeaderboardController> logger)
    {
        _leaderboardService = leaderboardService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetByRank([FromQuery] int start, [FromQuery] int end)
    {
        try
        {
            var rankings = _leaderboardService.GetByRank(start, end);
            _logger.LogInformation("Retrieved {Count} rankings from {Start} to {End}", 
                rankings.Count, start, end);
                
            return Ok(rankings);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, "Invalid rank range request: start={Start}, end={End}", start, end);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get rankings from {Start} to {End}", start, end);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{customerId}")]
    public IActionResult GetWithNeighbors(
        long customerId, 
        [FromQuery] int? high, 
        [FromQuery] int? low)
    {
        try
        {
            var rankings = _leaderboardService.GetWithNeighbors(
                customerId, high ?? 0, low ?? 0);
                
            if (rankings.Count == 0)
            {
                _logger.LogWarning(
                    "No rankings found for customer {CustomerId} with neighbors high={High}, low={Low}", 
                    customerId, high, low);
                    
                return NotFound();
            }
            
            _logger.LogInformation(
                "Retrieved {Count} rankings for customer {CustomerId} and neighbors", 
                rankings.Count, customerId);
                
            return Ok(rankings);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, 
                "Invalid neighbor parameters for customer {CustomerId}: high={High}, low={Low}", 
                customerId, high, low);
                
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to get rankings for customer {CustomerId} with neighbors", customerId);
                
            return StatusCode(500, "Internal server error");
        }
    }
}