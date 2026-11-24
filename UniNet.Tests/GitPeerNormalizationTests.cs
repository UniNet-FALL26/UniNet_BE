using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class GitPeerNormalizationTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public void ExternalMassComesOnlyFromEligibleDeduplicatedWorkAndChangesInNewInputs()
    {
        var repo=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        var member=new ExperimentalGitActivity(ExperimentalGitActivityCategory.Commit,repo,new string('a',40),A,now,[]);
        var external=new ExperimentalGitActivity(ExperimentalGitActivityCategory.Commit,repo,new string('b',40),null,now,[]);
        var peer=new Dictionary<Guid,decimal>{{A,50},{B,50}};
        var before=ExperimentalGitScoringV1.Calculate([A,B],[member,external,external],peer);
        Assert.Equal(1,before.ExternalActivityCounts[ExperimentalGitActivityCategory.Commit]);
        var percentages=GitPeerNormalization.Calculate(before.UnroundedGitWeights,before.ExternalGitWeight,ScoreState.Ready,peer,ScoreState.Ready);
        Assert.Equal(30m,percentages.ExternalPercent);
        // A later independently verified attribution is a new calculation input, never a rewrite.
        var after=ExperimentalGitScoringV1.Calculate([A,B],[member,external with {UserId=B}],peer);
        Assert.Equal(0,after.ExternalGitWeight);
        var verified=GitPeerNormalization.Calculate(after.UnroundedGitWeights,after.ExternalGitWeight,ScoreState.Ready,peer,ScoreState.Ready);
        Assert.Equal(50m,verified.Members[A]);Assert.Equal(50m,verified.Members[B]);
        Assert.Equal(30m,percentages.ExternalPercent);
    }

    [Fact]
    public void NormalizesComponentsSeparatelyAndPreservesExternalGitShare()
    {
        var result = GitPeerNormalization.Calculate(new Dictionary<Guid,decimal>{{A,60},{B,20}},20,
            ScoreState.Ready,new Dictionary<Guid,decimal>{{A,75},{B,25}},ScoreState.Ready);
        Assert.Equal(66m,result.Members[A]); Assert.Equal(22m,result.Members[B]); Assert.Equal(12m,result.ExternalPercent);
        Assert.Equal(100m,result.Members.Values.Sum()+result.ExternalPercent);
    }

    [Fact]
    public void DoesNotNormalizeCombinedRawTotalsOrUseRoundedGitScores()
    {
        var result = GitPeerNormalization.Calculate(new Dictionary<Guid,decimal>{{A,65.5m},{B,14.5m}},0,
            ScoreState.Ready,new Dictionary<Guid,decimal>{{A,50},{B,50}},ScoreState.Ready);
        Assert.Equal(69.13m,result.Members[A]); Assert.Equal(30.87m,result.Members[B]);
        Assert.Equal(100m,result.Members.Values.Sum());
    }

    [Fact]
    public void NotApplicableTransfersWeightButIncompleteAndApplicableZeroDoNot()
    {
        var weights=new Dictionary<Guid,decimal>{{A,3},{B,1}};
        var zeros=new Dictionary<Guid,decimal>{{A,0},{B,0}};
        Assert.Equal(75m,GitPeerNormalization.Calculate(weights,0,ScoreState.Ready,zeros,ScoreState.NotApplicable).Members[A]);
        Assert.Equal(75m,GitPeerNormalization.Calculate(zeros,0,ScoreState.NotApplicable,weights,ScoreState.Ready).Members[A]);
        Assert.Empty(GitPeerNormalization.Calculate(weights,0,ScoreState.Ready,zeros,ScoreState.NotReady).Members);
        Assert.Empty(GitPeerNormalization.Calculate(weights,0,ScoreState.Ready,zeros,ScoreState.Ready).Members);
        Assert.Equal("InsufficientData",GitPeerNormalization.Calculate(zeros,0,ScoreState.NotApplicable,zeros,ScoreState.NotApplicable).Status);
        Assert.Equal("InsufficientData",GitPeerNormalization.Calculate(zeros,0,ScoreState.Ready,zeros,ScoreState.Ready).Status);
    }

    [Fact]
    public void SingleMemberAndReorderedInputsProduceDeterministicPercentages()
    {
        Assert.Equal(100m,GitPeerNormalization.Calculate(new Dictionary<Guid,decimal>{{A,1}},0,ScoreState.Ready,
            new Dictionary<Guid,decimal>{{A,0}},ScoreState.NotApplicable).Members[A]);
        var one=GitPeerNormalization.Calculate(new Dictionary<Guid,decimal>{{A,1},{B,2}},1,ScoreState.Ready,
            new Dictionary<Guid,decimal>{{B,1},{A,2}},ScoreState.Ready);
        var two=GitPeerNormalization.Calculate(new Dictionary<Guid,decimal>{{B,2},{A,1}},1,ScoreState.Ready,
            new Dictionary<Guid,decimal>{{A,2},{B,1}},ScoreState.Ready);
        Assert.Equal(one.Members[A],two.Members[A]);Assert.Equal(one.Members[B],two.Members[B]);Assert.Equal(one.ExternalPercent,two.ExternalPercent);
        Assert.Equal(100m,one.Members.Values.Sum()+one.ExternalPercent);
    }
}
