namespace LeaderboardWebApi.Models;

public class CustomerRanking
{
    public long CustomerId { get; set; }
    public decimal Score { get; set; }
    public int Rank { get; set; }
}