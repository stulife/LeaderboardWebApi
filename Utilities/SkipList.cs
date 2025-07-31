using System;
using System.Collections.Generic;

namespace LeaderboardWebApi.Utilities;

public class SkipListNode<T> where T : IComparable<T>
{
    public T Value { get; }
    public SkipListNode<T>[] Next { get; }
    public int[] Span { get; } // Span array for calculating rank

    public SkipListNode(T value, int level)
    {
        Value = value;
        Next = new SkipListNode<T>[level];
        Span = new int[level];
    }
}

public class SkipList<T> where T : IComparable<T>
{
    private const double Probability = 0.5;
    private const int MaxLevel = 32;
    private readonly SkipListNode<T> _head = new(default, MaxLevel);
    private readonly Random _random = new();
    private int _currentLevel = 1;
    public int Count { get; private set; }

    // Add element and return rank
    public (bool Success, int Rank) Add(T value)
    {
        var update = new SkipListNode<T>[MaxLevel];
        var rank = new int[MaxLevel];
        var current = _head;

        // Calculate predecessor nodes and ranks for each level
        for (int i = _currentLevel - 1; i >= 0; i--)
        {
            rank[i] = i == (_currentLevel - 1) ? 0 : rank[i + 1];
            while (current.Next[i] != null && current.Next[i].Value.CompareTo(value) < 0)
            {
                rank[i] += current.Span[i];
                current = current.Next[i];
            }
            update[i] = current;
        }

        // If value already exists, don't add
        if (current.Next[0] != null && current.Next[0].Value.CompareTo(value) == 0)
        {
            return (false, -1);
        }

        // Randomly generate level
        int level = RandomLevel();
        if (level > _currentLevel)
        {
            for (int i = _currentLevel; i < level; i++)
            {
                update[i] = _head;
                update[i].Span[i] = Count;
                rank[i] = 0;
            }
            _currentLevel = level;
        }

        // Create new node
        var newNode = new SkipListNode<T>(value, level);
        int nodeRank = rank[0] + 1;

        for (int i = 0; i < level; i++)
        {
            newNode.Next[i] = update[i].Next[i];
            update[i].Next[i] = newNode;

            // Calculate span
            newNode.Span[i] = update[i].Span[i] - (rank[0] - rank[i]);
            update[i].Span[i] = rank[0] - rank[i] + 1;
        }

        // Update span for higher levels
        for (int i = level; i < _currentLevel; i++)
        {
            update[i].Span[i]++;
        }

        Count++;
        return (true, nodeRank);
    }

    // Remove element and return original rank
    public (bool Success, int Rank) Remove(T value)
    {
        var update = new SkipListNode<T>[MaxLevel];
        var current = _head;
        int rankSum = 0;

        // Find the node to delete
        for (int i = _currentLevel - 1; i >= 0; i--)
        {
            while (current.Next[i] != null && current.Next[i].Value.CompareTo(value) < 0)
            {
                rankSum += current.Span[i];
                current = current.Next[i];
            }
            update[i] = current;
        }

        current = current.Next[0];
        if (current == null || current.Value.CompareTo(value) != 0)
        {
            return (false, -1);
        }

        int originalRank = rankSum + 1;

        // Update predecessor node pointers and spans
        for (int i = 0; i < _currentLevel; i++)
        {
            if (update[i].Next[i] == current)
            {
                update[i].Span[i] += current.Span[i] - 1;
                update[i].Next[i] = current.Next[i];
            }
            else
            {
                update[i].Span[i]--;
            }
        }

        // Clean up top level null pointers
        while (_currentLevel > 1 && _head.Next[_currentLevel - 1] == null)
        {
            _currentLevel--;
        }

        Count--;
        return (true, originalRank);
    }

    // Query by rank range
    public IEnumerable<T> GetByRankRange(int startRank, int endRank)
    {
        if (startRank < 1 || startRank > endRank)
        {
            yield break;
        }
        if (endRank > Count)
        {
            endRank = Count;
        }
        var current = _head;
        int currentRank = 0;

        // First position to start rank
        for (int i = _currentLevel - 1; i >= 0; i--)
        {
            while (current.Next[i] != null && currentRank + current.Span[i] <= startRank)
            {
                currentRank += current.Span[i];
                current = current.Next[i];
            }
        }

        // Traverse nodes in range
        int count = endRank - startRank + 1;
        for (int i = 0; i < count; i++)
        {
            if (current == null) break;
            yield return current.Value;
            current = current.Next[0];
        }
    }

    // Get element's rank
    public int? GetRank(T value)
    {
        var current = _head;
        int rank = 0;

        for (int i = _currentLevel - 1; i >= 0; i--)
        {
            while (current.Next[i] != null && current.Next[i].Value.CompareTo(value) <= 0)
            {
                if (current.Next[i].Value.CompareTo(value) == 0)
                {
                    return rank + current.Span[i];
                }

                rank += current.Span[i];
                current = current.Next[i];
            }
        }

        return null;
    }

    // Get element and its rank
    public (T Value, int Rank)? GetByCustomerId(long customerId, Func<T, long> idSelector)
    {
        var current = _head;
        int rank = 0;

        for (int i = _currentLevel - 1; i >= 0; i--)
        {
            while (current.Next[i] != null)
            {
                var nextId = idSelector(current.Next[i].Value);
                if (nextId == customerId)
                {
                    return (current.Next[i].Value, rank + current.Span[i]);
                }

                if (nextId < customerId)
                {
                    rank += current.Span[i];
                    current = current.Next[i];
                }
                else
                {
                    break;
                }
            }
        }

        return null;
    }
    

    private int RandomLevel()
    {
        int level = 1;
        while (_random.NextDouble() < Probability && level < MaxLevel)
        {
            level++;
        }
        return level;
    }
}