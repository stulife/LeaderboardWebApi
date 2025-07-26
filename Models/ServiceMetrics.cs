namespace LeaderboardWebApi.Models;

public class ServiceMetrics
{
    public int TotalCustomers { get; set; }
    public int LeaderboardCustomers { get; set; }
    public decimal TopScore { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}