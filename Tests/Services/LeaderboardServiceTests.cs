using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using LeaderboardWebApi.Models;
using LeaderboardWebApi.Services;

namespace LeaderboardWebApi.Tests.Services;

public class LeaderboardServiceTests
{
    private readonly Mock<ILogger<LeaderboardService>> _loggerMock;
    private readonly LeaderboardService _leaderboardService;

    public LeaderboardServiceTests()
    {
        _loggerMock = new Mock<ILogger<LeaderboardService>>();
        _leaderboardService = new LeaderboardService(_loggerMock.Object);
    }

    #region 

    [Fact]
    public void InitializeFromSeed_Test()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        const int recordCount = 100000; // 100,000 test records
        var seedData = CreateTestData(recordCount);

        _leaderboardService.InitializeFromSeed(seedData);
        stopwatch.Stop();
        var metrics = _leaderboardService.GetMetrics();

        Assert.Equal(recordCount, metrics.TotalCustomers);
        Assert.Equal(recordCount, metrics.LeaderboardCustomers);
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, "Large data initialization took too long"); 
    }

    [Fact]
    public void UpdateScore_Test()
    {
        const int recordCount = 100000;
        var seedData = CreateTestData(recordCount);
        _leaderboardService.InitializeFromSeed(seedData);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var random = new Random(42);
        var testCustomerId = random.Next(1, recordCount);
        const decimal scoreChange = 100;

        var newScore = _leaderboardService.UpdateScore(testCustomerId, scoreChange);
        stopwatch.Stop();
        var rankings = _leaderboardService.GetByRank(1, 100000);
        var updatedCustomer = rankings.FirstOrDefault(r => r.CustomerId == testCustomerId);

        Assert.NotNull(updatedCustomer);
        Assert.Equal(newScore, updatedCustomer.Score);
        Assert.True(stopwatch.ElapsedMilliseconds < 100, "Score update operation took too long"); // Complete within 100ms
    }

    [Fact]
    public void GetByRank_LargeRangeQuery_ReturnCorrectResults()
    {
        const int recordCount = 100000;
        var seedData = CreateTestData(recordCount);
        _leaderboardService.InitializeFromSeed(seedData);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        const int startRank = 1000;
        const int endRank = 2000;

        var results = _leaderboardService.GetByRank(startRank, endRank);
        stopwatch.Stop();

        Assert.Equal(endRank - startRank + 1, results.Count);
        Assert.Equal(startRank, results.First().Rank);
        Assert.Equal(endRank, results.Last().Rank);
        Assert.True(results.Zip(results.Skip(1), (a, b) => a.Score >= b.Score).All(x => x), "Ranking scores not in descending order");
        Assert.True(stopwatch.ElapsedMilliseconds < 200, "Large range query took too long"); 
    }

    [Fact]
    public async Task HighConcurrencyUpdates_MaintainDataConsistency()
    {

        const int recordCount = 100000;
        const int concurrentUsers = 500; 
        var seedData = CreateTestData(recordCount);
        _leaderboardService.InitializeFromSeed(seedData);
        
        var random = new Random(42); 
        var updateTasks = new List<Task<decimal>>();
        var customerIds = new HashSet<long>();
        
        // Generate unique customer IDs to avoid conflicts
        while (customerIds.Count < concurrentUsers)
        {
            customerIds.Add(random.Next(1, recordCount));
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Create high concurrency update tasks
        foreach (var customerId in customerIds)
        {
            updateTasks.Add(Task.Run(() =>
            {
                // Simulate random score changes between -500 and 500
                var scoreChange = (decimal)(random.NextDouble() * 1000 - 500);
                return _leaderboardService.UpdateScore(customerId, scoreChange);
            }));
        }
        
        // Wait for all concurrent updates to complete
        await Task.WhenAll(updateTasks);
        stopwatch.Stop();
        
        // Verify data consistency after concurrent updates
        var metrics = _leaderboardService.GetMetrics();
        var topRankings = _leaderboardService.GetByRank(1, 100);


        Assert.Equal(recordCount, metrics.TotalCustomers);
        Assert.True(topRankings.Count > 0, "No rankings found after concurrent updates");
        Assert.True(stopwatch.ElapsedMilliseconds < 2000, "High concurrency updates took too long"); // Complete within 2 seconds
        

        foreach (var customerId in customerIds.Take(100)) // Spot check 100 random customers
        {
            var neighbors = _leaderboardService.GetWithNeighbors(customerId, 1, 1);
            Assert.True(neighbors.Any(n => n.CustomerId == customerId), "Customer not found in neighbor rankings after update");
        }
    }

    [Fact]
    public void GetWithNeighbors_In_ReturnCorrectNeighbors()
    {
        const int recordCount = 100000;
        var seedData = CreateTestData(recordCount);
        _leaderboardService.InitializeFromSeed(seedData);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        const int testCustomerId = 12345; // Use fixed ID for testing
        const int high = 50;
        const int low = 50;

        var neighbors = _leaderboardService.GetWithNeighbors(testCustomerId, high, low);
        stopwatch.Stop();


        Assert.Equal(high + low + 1, neighbors.Count);
        Assert.True(neighbors.Any(n => n.CustomerId == testCustomerId), "Target user not found");
        Assert.True(stopwatch.ElapsedMilliseconds < 150, "Neighbor ranking retrieval took too long"); // Complete within 150ms
    }

    [Fact]
    public async Task ConcurrentUpdateAndView_MaintainConsistency()
    {
        const int recordCount = 100000;
        var seedData = CreateTestData(recordCount);
        _leaderboardService.InitializeFromSeed(seedData);
        
        var random = new Random(42);
        var updateCustomerIds = new HashSet<long>();
        while (updateCustomerIds.Count < 50) // 50 unique customers to update
        {
            updateCustomerIds.Add(random.Next(1, recordCount));
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Create concurrent update and view tasks
        var updateTasks = updateCustomerIds.Select(id => Task.Run(() =>
            _leaderboardService.UpdateScore(id, random.Next(1, 100))
        )).ToList();
        
        var viewTasks = new List<Task>();
        var viewResults = new ConcurrentBag<List<CustomerRanking>>();
        
        // Add view tasks that run concurrently with updates
        for (int i = 0; i < 20; i++)
        {
            viewTasks.Add(Task.Run(() =>
            {
                // Randomly view different parts of the leaderboard
                var startRank = random.Next(1, 1000);
                var endRank = startRank + random.Next(10, 100);
                viewResults.Add(_leaderboardService.GetByRank(startRank, endRank));
            }));
        }
        
        // Run all tasks concurrently
        await Task.WhenAll(updateTasks.Concat(viewTasks));
        stopwatch.Stop();


        Assert.True(stopwatch.ElapsedMilliseconds < 3000, "Concurrent operations took too long");
        
        // Verify all view results are valid
        foreach (var results in viewResults)
        {
            Assert.True(results.Count > 0, "Empty view result set");
            Assert.True(results.Zip(results.Skip(1), (a, b) => a.Rank <= b.Rank).All(x => x),
                "View results have inconsistent ranking order");
        }
        
        // Verify final state consistency
        var finalMetrics = _leaderboardService.GetMetrics();
        Assert.Equal(recordCount, finalMetrics.TotalCustomers);
    }

    [Fact]
    public async Task ConcurrentUpdateAndNeighborView_ReturnCorrectResults()
    {
        const int recordCount = 100000;
        var seedData = CreateTestData(recordCount);
        _leaderboardService.InitializeFromSeed(seedData);
        
        var random = new Random(42);
        var testCustomerId = random.Next(1000, recordCount - 1000); 
        var updateCustomerIds = new HashSet<long>();
        while (updateCustomerIds.Count < 50) 
        {
            var id = random.Next(1, recordCount);
            if (id != testCustomerId) 
                updateCustomerIds.Add(id);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Create concurrent update tasks
        var updateTasks = updateCustomerIds.Select(id => Task.Run(() =>
            _leaderboardService.UpdateScore(id, random.Next(1, 100))
        )).ToList();
        
        // Create concurrent neighbor view tasks for our test customer (2 before, 3 after)
        var viewTasks = new List<Task<List<CustomerRanking>>>();
        for (int i = 0; i < 20; i++)
        {
            viewTasks.Add(Task.Run(() =>
                _leaderboardService.GetWithNeighbors(testCustomerId, 2, 3)
            ));
        }
        
        // Run all tasks concurrently
        await Task.WhenAll(updateTasks.Cast<Task>().Concat(viewTasks));
        stopwatch.Stop();
        
        // Get final state for verification
        var finalNeighbors = _leaderboardService.GetWithNeighbors(testCustomerId, 2, 3);
        var finalRankings = _leaderboardService.GetByRank(1, recordCount);
        var finalCustomer = finalRankings.FirstOrDefault(r => r.CustomerId == testCustomerId);

        Assert.True(stopwatch.ElapsedMilliseconds < 3000, "Concurrent neighbor operations took too long");
        
        // Verify all view results have correct count and contain our test customer
        foreach (var neighbors in viewTasks.Select(t => t.Result))
        {
            Assert.Equal(6, neighbors.Count); // 2 before + current + 3 after = 6
            Assert.True(neighbors.Any(n => n.CustomerId == testCustomerId), "Test customer not found in neighbors");
            
            // Verify ranking order
            for (int i = 0; i < neighbors.Count - 1; i++)
            {
                Assert.True(neighbors[i].Rank <= neighbors[i + 1].Rank, "Neighbor rankings out of order");
                Assert.True(neighbors[i].Score >= neighbors[i + 1].Score, "Neighbor scores out of order");
            }
        }
        
        Assert.NotNull(finalCustomer);
        Assert.Equal(6, finalNeighbors.Count);
    }

    [Fact]
    public void UpdateScore_And_GetWithSpecificNeighbors_ReturnCorrectResults()
    {
        const int recordCount = 100000;
        var seedData = CreateTestData(recordCount);
        _leaderboardService.InitializeFromSeed(seedData);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        const int testCustomerId = 50000; // Middle position for stable testing
        const decimal scoreChange = 500;
        
        // Act - Update score first
        var newScore = _leaderboardService.UpdateScore(testCustomerId, scoreChange);
        
        // Then get neighbors with specific parameters: 2 before, 3 after
        var neighbors = _leaderboardService.GetWithNeighbors(testCustomerId, 2, 3);
        stopwatch.Stop();

        // Assert
        Assert.Equal(6, neighbors.Count); // 2 before + current + 3 after = 6 records
        Assert.True(neighbors.Any(n => n.CustomerId == testCustomerId), "Target customer not found in results");
        Assert.True(stopwatch.ElapsedMilliseconds < 150, "Neighbor retrieval with specific parameters took too long");
        
        // Verify ranking order is correct
        for (int i = 0; i < neighbors.Count - 1; i++)
        {
            Assert.True(neighbors[i].Rank <= neighbors[i + 1].Rank, "Rankings are not in correct order");
            Assert.True(neighbors[i].Score >= neighbors[i + 1].Score, "Scores are not in descending order");
        }
        
        // Verify the specific positions
        var targetIndex = neighbors.FindIndex(n => n.CustomerId == testCustomerId);
        if (targetIndex > 0) // Has previous records
        {
            Assert.Equal(neighbors[targetIndex].Rank - 1, neighbors[targetIndex - 1].Rank);
            Assert.Equal(neighbors[targetIndex].Rank - 2, neighbors[targetIndex - 2].Rank);
        }
        
        if (targetIndex < neighbors.Count - 1) // Has next records
        {
            Assert.Equal(neighbors[targetIndex].Rank + 1, neighbors[targetIndex + 1].Rank);
            Assert.Equal(neighbors[targetIndex].Rank + 2, neighbors[targetIndex + 2].Rank);
            Assert.Equal(neighbors[targetIndex].Rank + 3, neighbors[targetIndex + 3].Rank);
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates test data
    /// </summary>
    /// <param name="count">Number of records</param>
    /// <returns>List of test data</returns>
    private IEnumerable<(long CustomerId, decimal Score)> CreateTestData(int count)
    {
        // Generate ordered scores to facilitate ranking testing
        for (long i = 1; i <= count; i++)
        {
            yield return (i, count - i + 1); // Scores decrease from count to 1
        }
    }

    #endregion
}