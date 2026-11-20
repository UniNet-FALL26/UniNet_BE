namespace UniNet.Application.Services;

internal sealed record ContributionAllocation(string Status, IReadOnlyDictionary<Guid, decimal> Members, decimal? ExternalPercent);

// Rounding boundary only; it does not choose the Git/Peer/External conversion policy.
internal static class ContributionPercentAllocator
{
    public static ContributionAllocation Allocate(IReadOnlyDictionary<Guid, decimal> members, decimal externalWeight, bool complete)
    {
        if (externalWeight < 0 || members.Any(x => x.Key == Guid.Empty || x.Value < 0)) throw new ArgumentOutOfRangeException(nameof(members));
        if (!complete) return new("Incomplete", new Dictionary<Guid, decimal>(), null);
        var total = members.Values.Sum() + externalWeight;
        if (total == 0) return new("InsufficientData", new Dictionary<Guid, decimal>(), null);
        var weights = members.Select(x => (Key: x.Key.ToString("N"), Member: (Guid?)x.Key, Value: x.Value))
            .Append((Key: "external", Member: (Guid?)null, Value: externalWeight)).ToArray();
        var parts = weights.Select(x => { var exact = 10000m * x.Value / total; var floor = decimal.Floor(exact); return new { x.Key, x.Member, Floor = (int)floor, Remainder = exact - floor }; }).ToArray();
        var bonus = parts.OrderByDescending(x => x.Remainder).ThenBy(x => x.Key, StringComparer.Ordinal)
            .Take(10000 - parts.Sum(x => x.Floor)).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        var allocated = parts.ToDictionary(x => x.Key, x => (x.Floor + (bonus.Contains(x.Key) ? 1 : 0)) / 100m);
        return new("Ready", parts.Where(x => x.Member.HasValue).ToDictionary(x => x.Member!.Value, x => allocated[x.Key]), allocated["external"]);
    }
}
