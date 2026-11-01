using UniNet.Domain;
using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class StaffRoleContractTests
{
    [Fact]
    public void PersistedRoleContractMatchesRequestedTable()
    {
        Assert.Equal(0, (short)AccountRole.Student);
        Assert.Equal(1, (short)AccountRole.Partner);
        Assert.Equal(2, (short)AccountRole.Assessor);
        Assert.Equal(3, (short)AccountRole.Verifier);
        Assert.Equal(4, (short)AccountRole.Moderator);
        Assert.Equal(5, (short)AccountRole.Accountant);
        Assert.Equal(6, (short)AccountRole.Admin);
    }

    [Fact]
    public void RolesHaveSeparateOperationalScopes()
    {
        Assert.Contains(AccountRole.Verifier, NotificationPermissionPolicy.Roles(NotificationPermissionPolicy.StudentVerificationReview));
        Assert.DoesNotContain(AccountRole.Assessor, NotificationPermissionPolicy.Roles(NotificationPermissionPolicy.StudentVerificationReview));
        Assert.Contains(AccountRole.Assessor, NotificationPermissionPolicy.Roles(NotificationPermissionPolicy.EvidenceReview));
        Assert.Contains(AccountRole.Accountant, NotificationPermissionPolicy.Roles("finance.view"));
        Assert.DoesNotContain(AccountRole.Moderator, NotificationPermissionPolicy.Roles("finance.view"));
        Assert.All(NotificationPermissionPolicy.All, p => Assert.Contains(AccountRole.Admin, NotificationPermissionPolicy.Roles(p)));
    }
}
