using UniNet.Application.Services;
using UniNet.Application;
using UniNet.Domain;
using Xunit;

namespace UniNet.Tests;

public sealed class OfficialContributionTests
{
    [Theory]
    [InlineData("Draft", "submit", "Review")]
    [InlineData("Rejected", "submit", "Review")]
    [InlineData("Review", "approve", "Approved")]
    [InlineData("Review", "reject", "Rejected")]
    [InlineData("Approved", "finalize", "Finalized")]
    public void LifecycleRequiresExplicitApproval(string state, string action, string next) =>
        Assert.Equal(next, OfficialContributionService.NextState(state, action, "reviewed"));
    [Theory]
    [InlineData("Draft", "finalize")]
    [InlineData("Draft", "approve")]
    [InlineData("Rejected", "finalize")]
    [InlineData("Review", "finalize")]
    [InlineData("Finalized", "reject")]
    public void CannotSkipOrReverseApproval(string state, string action) =>
        Assert.Equal("OFFICIAL_TRANSITION_INVALID", Assert.Throws<ProjectException>(() => OfficialContributionService.NextState(state, action, "feedback")).Code);
    [Fact]
    public void RejectRequiresFeedback() => Assert.Equal("OFFICIAL_FEEDBACK_REQUIRED",
        Assert.Throws<ProjectException>(() => OfficialContributionService.NextState("Review", "reject", " ")).Code);
}
