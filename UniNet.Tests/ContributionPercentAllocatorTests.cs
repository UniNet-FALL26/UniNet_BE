using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class ContributionPercentAllocatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void CompleteMemberWeightsNeverLoseMass(int count)
    {
        var weights=Enumerable.Range(1,count).ToDictionary(x=>Guid.Parse($"00000000-0000-0000-0000-{x:000000000000}"),x=>(decimal)x);
        var result=ContributionPercentAllocator.Allocate(weights,0,true);
        Assert.Equal(100m,result.Members.Values.Sum());Assert.All(result.Members.Values,x=>Assert.InRange(x,0m,100m));
    }
    [Fact]
    public void LargestRemainderSumsToExactly100AndIgnoresInputOrdering()
    {
        var a=Guid.Parse("00000000-0000-0000-0000-000000000001");
        var b=Guid.Parse("00000000-0000-0000-0000-000000000002");
        var c=Guid.Parse("00000000-0000-0000-0000-000000000003");
        var first=ContributionPercentAllocator.Allocate(new Dictionary<Guid,decimal>{{a,1},{b,1},{c,1}},0,true);
        var second=ContributionPercentAllocator.Allocate(new Dictionary<Guid,decimal>{{c,1},{b,1},{a,1}},0,true);
        Assert.Equal(100,first.Members.Values.Sum());Assert.Equal(33.34m,first.Members[a]);
        foreach(var pair in first.Members)Assert.Equal(pair.Value,second.Members[pair.Key]);
        Assert.Equal(0,first.ExternalPercent);
    }
    [Fact]
    public void ExternalShareIsNotRedistributedAndIncompleteOrZeroHasNoFinalPercentages()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();
        var value=ContributionPercentAllocator.Allocate(new Dictionary<Guid,decimal>{{a,60},{b,20}},20,true);
        Assert.Equal(80,value.Members.Values.Sum());Assert.Equal(20,value.ExternalPercent);
        Assert.Empty(ContributionPercentAllocator.Allocate(new Dictionary<Guid,decimal>{{a,0}},0,true).Members);
        Assert.Null(ContributionPercentAllocator.Allocate(new Dictionary<Guid,decimal>{{a,1}},0,false).ExternalPercent);
    }
}
