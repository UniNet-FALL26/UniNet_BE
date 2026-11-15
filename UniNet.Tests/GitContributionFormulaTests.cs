using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class GitContributionFormulaTests
{
    [Fact]
    public void AppliesConfirmedWeightsAndDoesNotRedistributeEmptyCategory()
    {
        var result = GitContributionFormula.Compute(new(ScoreState.Ready, 80), new(ScoreState.Ready, 40),
            new(ScoreState.NotApplicable, null), new(ScoreState.Ready, 20), 50, MidpointRounding.ToEven);
        Assert.Equal(43m, result.GitScore);
        Assert.Equal(45.8m, result.Total);
    }
    [Fact]
    public void RejectsNotReadyOrInvalidScoresWithoutProducingResult()
    {
        var x = GitContributionFormula.Compute(new(ScoreState.NotReady, null), new(ScoreState.NotApplicable, null),
            new(ScoreState.NotApplicable, null), new(ScoreState.NotApplicable, null), 50, MidpointRounding.ToEven);
        Assert.Null(x.Total); Assert.Null(x.GitScore);
        Assert.Throws<ArgumentOutOfRangeException>(() => GitContributionFormula.Compute(new(ScoreState.Ready, 101),
            new(ScoreState.NotApplicable, null), new(ScoreState.NotApplicable, null), new(ScoreState.NotApplicable, null), 50, MidpointRounding.ToEven));
    }
    [Fact]
    public void ShareIncludesExternalButDoesNotReturnExternalScore()
    {
        var member = Guid.NewGuid();
        var scores = GitContributionFormula.Normalize(new[] { new RawShare(member, 3), new RawShare(null, 1) });
        Assert.Single(scores); Assert.Equal(75m, scores[member]);
    }
}
