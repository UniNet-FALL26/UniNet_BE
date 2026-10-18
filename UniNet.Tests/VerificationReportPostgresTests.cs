using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class VerificationReservationPostgresTests
{
    private sealed class ReportReadBarrier : DbCommandInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default)
        {
            if (command.CommandText.StartsWith("SELECT EXISTS") && command.CommandText.Contains("\"VerificationReports\"") &&
                Interlocked.Increment(ref arrivals) <= 2)
            {
                if (arrivals == 2) both.TrySetResult();
                await both.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            }
            return result;
        }
    }

    [EvidencePostgresFact]
    public Task ConcurrentReportSubmissionsForSameAssignmentFinalizeOnlyOnce() => ConcurrentReportsAsync(false);

    [EvidencePostgresFact]
    public Task DifferentAssignmentsCannotFinalizeSameCaseTwice() => ConcurrentReportsAsync(true);

    private static async Task ConcurrentReportsAsync(bool differentAssignments)
    {
        await using var store = new Store(); await store.InitializeAsync();
        Guid actorId, firstId, secondId;
        await using (var seed = store.Open())
        {
            var staff = await seed.UserProfiles.Include(p => p.Account).SingleAsync(p => p.Account.Role == AccountRole.Verificator);
            var admin = new Account { Email = "admin@test.com", Role = AccountRole.Admin, Profile = new() { FullName = "Admin", Nickname = "Admin" } };
            seed.Accounts.Add(admin); await seed.SaveChangesAsync();
            VerificationAssignment Assignment() => new() { Id = Guid.NewGuid(), ProjectVerificationId = store.First,
                AssignedToUserId = staff.Id, AdminId = admin.Profile!.Id, Status = VerificationAssignmentStatus.Assigned };
            var first = Assignment(); var second = differentAssignments ? Assignment() : first;
            seed.VerificationAssignments.Add(first); if (differentAssignments) seed.VerificationAssignments.Add(second);
            var v = await seed.ProjectVerifications.SingleAsync(v => v.Id == store.First);
            v.MeetingStartAt = DateTimeOffset.UtcNow.AddHours(-2); v.MeetingEndAt = DateTimeOffset.UtcNow.AddHours(-1);
            await seed.SaveChangesAsync(); actorId = staff.AccountId; firstId = first.Id; secondId = second.Id;
        }
        var barrier = new ReportReadBarrier();
        async Task<bool> Submit(Guid assignment)
        {
            await using var db = store.Open(barrier);
            try
            {
                await new ReportService(db).SubmitAsync(actorId, new(assignment, "Final report", "Reviewed findings", "Recommendations", ProjectVerificationStatus.Verified));
                return true;
            }
            catch (ReportException ex) when (ex.Code is "REPORT_CONFLICT" or "REPORT_EXISTS" or "VERIFICATION_LOCKED") { return false; }
        }
        var results = await Task.WhenAll(Submit(firstId), Submit(secondId));
        Assert.Single(results, r => r);
        await using var check = store.Open();
        Assert.Single(await check.VerificationReports.ToListAsync());
        Assert.Single(await check.ReportAuditLogs.ToListAsync());
        Assert.Equal(1, await check.VerificationAssignments.CountAsync(a => a.Status == VerificationAssignmentStatus.Completed));
        var finalized = await check.ProjectVerifications.SingleAsync(v => v.Id == store.First);
        Assert.Equal(ProjectVerificationStatus.Verified, finalized.Status); Assert.NotNull(finalized.FinalizedAt);
    }
}
