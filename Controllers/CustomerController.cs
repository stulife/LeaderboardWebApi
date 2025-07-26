using LeaderboardWebApi.Models;
using LeaderboardWebApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeaderboardWebApi.Controllers;

[ApiController]
[Route("[controller]")]
public class CustomerController : ControllerBase
{
    private readonly ILeaderboardService _leaderboardService;
    private readonly ILogger<CustomerController> _logger;

    public CustomerController(
        ILeaderboardService leaderboardService,
        ILogger<CustomerController> logger)
    {
        _leaderboardService = leaderboardService;
        _logger = logger;
    }

    [HttpPost("{customerId}/score/{score}")]
    public IActionResult UpdateScore(long customerId, decimal score)
    {
        try
        {
            var newScore = _leaderboardService.UpdateScore(customerId, score);
            _logger.LogInformation(
                "Updated score for customer {CustomerId}: {Score} change, new score: {NewScore}",
                customerId, score, newScore);
                
            return Ok(newScore);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, "Invalid score update for customer {CustomerId}", customerId);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update score for customer {CustomerId}", customerId);
            return StatusCode(500, "Internal server error");
        }
    }
}