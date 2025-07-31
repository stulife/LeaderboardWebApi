namespace LeaderboardWebApi.Models;

public class CustomerScore : IComparable<CustomerScore>
{
    public long CustomerId { get; }
    public decimal Score { get; }

    public CustomerScore(long customerId, decimal score)
    {
        CustomerId = customerId;
        Score = score;
    }

    public int CompareTo(CustomerScore? other)
    {
        if (other == null) return 1;
        if (Score != other.Score)
            return other.Score.CompareTo(Score); 
        return CustomerId.CompareTo(other.CustomerId); 
    }

    public override bool Equals(object? obj)
    {
        return obj is CustomerScore other &&
               CustomerId == other.CustomerId &&
               Score == other.Score;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(CustomerId, Score);
    }
}