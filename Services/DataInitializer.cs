using LeaderboardWebApi.Models;

namespace LeaderboardWebApi.Services;

public class DataInitializer
{
    private readonly ILeaderboardService _leaderboardService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataInitializer> _logger;

    public DataInitializer(
        ILeaderboardService leaderboardService,
        IConfiguration configuration,
        ILogger<DataInitializer> logger)
    {
        _leaderboardService = leaderboardService;
        _configuration = configuration;
        _logger = logger;
    }

    public void Initialize()
    {
        try
        {
            var seedOnStartup = _configuration.GetValue<bool>("SeedData:Enable", false);
            if (!seedOnStartup) return;

            _logger.LogInformation("Initializing leaderboard with seed data...");
            _leaderboardService.InitializeFromSeed(SeedData.Customers);
            
            var metrics = _leaderboardService.GetMetrics();
            _logger.LogInformation("Leaderboard initialized with {Total} customers, {Leaderboard} in leaderboard",
                metrics.TotalCustomers, metrics.LeaderboardCustomers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing seed data");
        }
    }
}