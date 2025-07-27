using System.Collections.Concurrent;
using System.Collections.Immutable;
using LeaderboardWebApi.Models;

namespace LeaderboardWebApi.Services;


public class LeaderboardService : ILeaderboardService
{
    private readonly ConcurrentDictionary<long, decimal> _scores = new();

    private ImmutableSortedSet<(decimal Score, long CustomerId)> _leaderboard =
    ImmutableSortedSet.Create<(decimal Score, long CustomerId)>(
        Comparer<(decimal Score, long CustomerId)>.Create((x, y) =>
            x.Score != y.Score ? y.Score.CompareTo(x.Score) :
            x.CustomerId.CompareTo(y.CustomerId)));

    private readonly ILogger<LeaderboardService> _logger;

    public LeaderboardService(ILogger<LeaderboardService> logger)
    {
        _logger = logger;
    }
    public void InitializeFromSeed(IEnumerable<(long CustomerId, decimal Score)> seedData)
    {
        foreach (var (customerId, score) in seedData)
        {
            _scores[customerId] = score;
            if (score > 0)
            {
                _leaderboard = _leaderboard.Add((score, customerId));
            }
        }
        _logger.LogInformation("Initialized leaderboard with {Count} customers", seedData.Count());

    }
    public decimal UpdateScore(long customerId, decimal scoreChange)
    {
        if (scoreChange < -1000 || scoreChange > 1000)
            throw new ArgumentOutOfRangeException(nameof(scoreChange),
                "Score change must be between -1000 and 1000");

        _scores.TryGetValue(customerId, out decimal currentScore);
        decimal newScore = currentScore + scoreChange;

        _scores[customerId] = newScore;

        if (currentScore > 0)
            _leaderboard = _leaderboard.Remove((currentScore, customerId));

        if (newScore > 0)
            _leaderboard = _leaderboard.Add((newScore, customerId));

        return newScore;

    }

    public List<CustomerRanking> GetByRank(int start, int end)
    {
        if (start < 1) throw new ArgumentOutOfRangeException(nameof(start), "Start rank must be at least 1");
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end), "End rank cannot be less than start");

        var results = new List<CustomerRanking>();
        int currentRank = 1;

        foreach (var entry in _leaderboard)
        {
            if (currentRank > end) break;
            if (currentRank >= start)
                results.Add(new CustomerRanking
                {
                    CustomerId = entry.CustomerId,
                    Score = entry.Score,
                    Rank = currentRank
                });

            currentRank++;
        }

        return results;
    }

    public List<CustomerRanking> GetWithNeighbors(long customerId, int high, int low)
    {
        if (high < 0) throw new ArgumentOutOfRangeException(nameof(high), "High neighbor count cannot be negative");
        if (low < 0) throw new ArgumentOutOfRangeException(nameof(low), "Low neighbor count cannot be negative");

        if (!_scores.TryGetValue(customerId, out decimal score) || score <= 0)
            return new List<CustomerRanking>();

        int targetIndex = -1;
        int currentIndex = 0;
        foreach (var entry in _leaderboard)
        {
            if (entry.Score == score && entry.CustomerId == customerId)
            {
                targetIndex = currentIndex;
                break;
            }
            currentIndex++;
        }

        if (targetIndex == -1)
            return new List<CustomerRanking>();

        int startIndex = Math.Max(0, targetIndex - high);
        int endIndex = Math.Min(_leaderboard.Count - 1, targetIndex + low);

        var results = new List<CustomerRanking>();
        int currentRank = 1;
        int currentPos = 0;

        foreach (var entry in _leaderboard)
        {
            if (currentPos > endIndex) break;
            if (currentPos >= startIndex)
                results.Add(new CustomerRanking
                {
                    CustomerId = entry.CustomerId,
                    Score = entry.Score,
                    Rank = currentRank
                });

            currentRank++;
            currentPos++;
        }

        return results;
    }

    public ServiceMetrics GetMetrics()
    {
        return new ServiceMetrics
        {
            TotalCustomers = _scores.Count,
            LeaderboardCustomers = _leaderboard.Count,
            TopScore = _leaderboard.Count > 0 ? _leaderboard.Max.Score : 0
        };

    }
}