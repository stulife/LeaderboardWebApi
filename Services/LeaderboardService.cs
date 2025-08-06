using System.Collections.Concurrent;

using LeaderboardWebApi.Models;
using LeaderboardWebApi.Utilities;

namespace LeaderboardWebApi.Services;


public class LeaderboardService : ILeaderboardService
{
    private readonly SkipList<CustomerScore> _leaderboard = new();
    private readonly ConcurrentDictionary<long, decimal> _scores = new();
    private readonly Dictionary<long, int> _rankCache = new();
    private readonly List<CustomerScore> _topRankCache = new();
    private long _cacheVersion;
    private long _topCacheVersion;
    private const int TopRankCacheSize = 10;
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly ILogger<LeaderboardService> _logger;

    public LeaderboardService(ILogger<LeaderboardService> logger)
    {
        _logger = logger;
    }
    public void InitializeFromSeed(IEnumerable<(long CustomerId, decimal Score)> seedData)
    {
        _lock.EnterWriteLock();
        try
        {
            // Clear existing data
            _scores.Clear();
            _rankCache.Clear();
            _topRankCache.Clear();
            Interlocked.Exchange(ref _cacheVersion, 0);
            Interlocked.Exchange(ref _topCacheVersion, 0);

            // Bulk load scores
            var scoreDict = new Dictionary<long, decimal>();
            var leaderboardItems = new List<CustomerScore>();

            foreach (var (customerId, score) in seedData)
            {
                scoreDict[customerId] = score;
                if (score > 0)
                {
                    leaderboardItems.Add(new CustomerScore(customerId, score));
                }
            }

            // Sort for efficient bulk insertion (descending score, ascending customer ID)
            leaderboardItems.Sort((a, b) =>
                a.Score != b.Score ? b.Score.CompareTo(a.Score) :
                a.CustomerId.CompareTo(b.CustomerId));

            // Add items to skip list and capture ranks
            foreach (var item in leaderboardItems)
            {
                var (success, rank) = _leaderboard.Add(item);
                if (success)
                {
                    _rankCache[item.CustomerId] = rank;
                }
            }

            // Update scores dictionary
            foreach (var kvp in scoreDict)
            {
                _scores[kvp.Key] = kvp.Value;
            }

            // Prebuild top rank cache
            _topRankCache.AddRange(_leaderboard.GetByRankRange(1, Math.Min(TopRankCacheSize, leaderboardItems.Count)));

            // Set cache versions
            Interlocked.Exchange(ref _cacheVersion, 1);
            Interlocked.Exchange(ref _topCacheVersion, 1);
            _logger.LogInformation("Initialized leaderboard with {Count} customers", seedData.Count());
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }


    public decimal UpdateScore(long customerId, decimal scoreChange)
    {
        if (scoreChange < -1000 || scoreChange > 1000)
            throw new ArgumentOutOfRangeException(nameof(scoreChange),
                "Score must be between -1000 and 1000");

        _lock.EnterWriteLock();
        try
        {
            // Get current score
            _scores.TryGetValue(customerId, out decimal currentScore);
            decimal newScore = currentScore + scoreChange;
            _scores[customerId] = newScore;

            // Flags for leaderboard operations
            bool shouldRemove = currentScore > 0;  // Only remove if previously valid
            bool shouldAdd = newScore > 0;         // Only add if new score is valid
            int? oldRank = null;
            int? newRank = null;

            // Update skip list only if required
            if (shouldRemove || shouldAdd)
            {
                // Remove previous score from leaderboard if applicable
                if (shouldRemove)
                {
                    var oldScoreObj = new CustomerScore(customerId, currentScore);
                    var (removed, rank) = _leaderboard.Remove(oldScoreObj);
                    if (removed) oldRank = rank;
                }

                // Add new score to leaderboard if applicable
                if (shouldAdd)
                {
                    var newScoreObj = new CustomerScore(customerId, newScore);
                    var (added, rank) = _leaderboard.Add(newScoreObj);
                    if (added) newRank = rank;
                }

                // Consolidated cache invalidation
                InvalidateCaches(oldRank, newRank, customerId);
            }

            return newScore;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void InvalidateCaches(int? oldRank, int? newRank, long customerId)
    {
        _rankCache.Remove(customerId);
        if (oldRank.HasValue ||
            (newRank.HasValue && newRank.Value <= TopRankCacheSize))
        {
            _topRankCache.RemoveAll(x => x.CustomerId == customerId);
        }
        Interlocked.Increment(ref _cacheVersion);
        Interlocked.Increment(ref _topCacheVersion);
    }
    public List<CustomerRanking> GetByRank(int start, int end)
    {
        if (start < 1 || end < start)
            throw new ArgumentException("Invalid rank range");

        // Use top cache if possible
        if (start == 1 && end <= TopRankCacheSize)
            return GetTopRanksFromCache(end);

        _lock.EnterReadLock();
        try
        {
            var results = new List<CustomerRanking>();
            int index = 0;

            // Directly iterate through required ranks
            foreach (var score in _leaderboard.GetByRankRange(start, end))
            {
                results.Add(new CustomerRanking
                {
                    CustomerId = score.CustomerId,
                    Score = score.Score,
                    Rank = start + index++
                });
            }
            return results;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private List<CustomerRanking> GetTopRanksFromCache(int count)
    {
        long currentVersion = Interlocked.Read(ref _topCacheVersion);

        // Use cache if valid
        if (_topRankCache.Count >= count && currentVersion == _topCacheVersion)
            return BuildRankings(_topRankCache, count);
        // Rebuild cache
        _lock.EnterWriteLock();
        try
        {
            long newVersion = Interlocked.Read(ref _topCacheVersion);
            if (_topRankCache.Count >= count && newVersion == currentVersion)
                return BuildRankings(_topRankCache, count);

            _topRankCache.Clear();
            var topItems = _leaderboard.GetByRankRange(1, TopRankCacheSize);
            _topRankCache.AddRange(topItems);
            return BuildRankings(_topRankCache, count);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private List<CustomerRanking> BuildRankings(List<CustomerScore> scores, int count)
    {
        var results = new List<CustomerRanking>();
        for (int i = 0; i < Math.Min(count, scores.Count); i++)
        {
            results.Add(new CustomerRanking
            {
                CustomerId = scores[i].CustomerId,
                Score = scores[i].Score,
                Rank = i + 1
            });
        }
        return results;
    }

    public List<CustomerRanking> GetWithNeighbors(long customerId, int high, int low)
    {
        if (high < 0 || low < 0)
            throw new ArgumentException("Neighbor counts must be non-negative");

        int? rank = GetCachedRank(customerId);
        if (rank == null) return new List<CustomerRanking>();

        int start = Math.Max(1, rank.Value - high);
        int end = Math.Min(_leaderboard.Count, rank.Value + low);

        return GetByRank(start, end);
    }

    private int? GetCachedRank(long customerId)
    {
        long currentVersion = Interlocked.Read(ref _cacheVersion);

        // Check cache first 
        if (_rankCache.TryGetValue(customerId, out int rank))
        {
            // Validate cache entry is still valid
            _lock.EnterReadLock();
            try
            {
                // Check if cache version has changed since we read it
                if (currentVersion != Interlocked.Read(ref _cacheVersion))
                {
                    // Cache version has been updated, re-validate
                    if (_rankCache.TryGetValue(customerId, out rank))
                    {
                        // Double-check that the customer's score is still valid
                        if (_scores.TryGetValue(customerId, out decimal scoreValue) && scoreValue > 0)
                        {
                            return rank;
                        }
                        else
                        {
                            // Score is no longer valid, remove from cache
                            _rankCache.Remove(customerId);
                            return null;
                        }
                    }
                    else
                    {
                        // Entry no longer exists in cache
                        return null;
                    }
                }

                // Cache version hasn't changed, validate score directly
                if (!_scores.TryGetValue(customerId, out decimal score) || score <= 0)
                {
                    // Score is invalid, remove from cache
                    _rankCache.Remove(customerId);
                    return null;
                }

                return rank;
            }
            }

            // Rank not in cache, need to skipList look it up
            // Double-check cache version hasn't changed
            long newVersion = Interlocked.Read(ref _cacheVersion);
            if (newVersion != currentVersion)
            {
                // Version has changed, check if rank was added in the meantime
                if (_rankCache.TryGetValue(customerId, out rank))
                {
                    // Verify score is still valid
                    if (_scores.TryGetValue(customerId, out decimal scoreValue) && scoreValue > 0)
                    {
                        return rank;
                    }
                    else
                    {
                        _rankCache.Remove(customerId);
                        return null;
                    }
                }
            }

            // Get customer's score from dictionary
            if (!_scores.TryGetValue(customerId, out decimal score) || score <= 0)
                return null;

            // Get rank from leaderboard
            var customerScore = new CustomerScore(customerId, score);
            int? result = _leaderboard.GetRank(customerScore);

            if (result.HasValue)
            {
                // Only cache the result if cache version hasn't changed
                if (Interlocked.Read(ref _cacheVersion) == currentVersion)
                {
                    _rankCache[customerId] = result.Value;
                }
                return result.Value;
            }
            return null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
        {
            // Double-check cache version hasn't changed
            long newVersion = Interlocked.Read(ref _cacheVersion);
            if (newVersion != currentVersion)
            {
                // Version has changed, check if rank was added in the meantime
                if (_rankCache.TryGetValue(customerId, out rank))
                {
                    // Verify score is still valid
                    if (_scores.TryGetValue(customerId, out decimal scoreValue) && scoreValue > 0)
                    {
                        return rank;
                    }
                    else
                    {
                        _rankCache.Remove(customerId);
                        return null;
                    }
                }
            }

            // Get customer's score from dictionary
            if (!_scores.TryGetValue(customerId, out decimal score) || score <= 0)
                return null;

            // Get rank from leaderboard
            var customerScore = new CustomerScore(customerId, score);
            int? result = _leaderboard.GetRank(customerScore);

            if (result.HasValue)
            {
                // Only cache the result if cache version hasn't changed
                if (Interlocked.Read(ref _cacheVersion) == currentVersion)
                {
                    _rankCache[customerId] = result.Value;
                }
                return result.Value;
            }
            return null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public ServiceMetrics GetMetrics()
    {
        _lock.EnterReadLock();
        try
        {
            return new ServiceMetrics
            {
                TotalCustomers = _scores.Count,
                LeaderboardCustomers = _leaderboard.Count,
                TopScore = _leaderboard.Count > 0 ?
                    _leaderboard.GetByRankRange(1, 1).First().Score : 0
            };
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
}