using System.Collections.Concurrent;
using System.Collections.Immutable;
using LeaderboardWebApi.Models;

namespace LeaderboardWebApi.Services;

public interface ILeaderboardService
{
    decimal UpdateScore(long customerId, decimal scoreChange);
    List<CustomerRanking> GetByRank(int start, int end);
    List<CustomerRanking> GetWithNeighbors(long customerId, int high, int low);
    ServiceMetrics GetMetrics();
}