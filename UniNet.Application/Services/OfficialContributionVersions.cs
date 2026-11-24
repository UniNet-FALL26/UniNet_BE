using System.Data;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
namespace UniNet.Application.Services;
public sealed partial class OfficialContributionService
{
    private async Task RequireVersionSchema(CancellationToken ct) => Check(await VersionSchema(ct),
        "OFFICIAL_VERSION_SCHEMA_REQUIRED", "Deploy the reviewed publication version migration before changing results.",503);
    private async Task<bool> VersionSchema(CancellationToken ct) => db.Database.IsNpgsql() &&
        await db.Database.SqlQuery<int>($"SELECT CASE WHEN to_regclass('public.\"OfficialContributionReleases\"') IS NOT NULL THEN 1 ELSE 0 END AS \"Value\"").SingleAsync(ct)==1;
    public async Task<OfficialVersionCapabilities> VersionCapabilitiesAsync(Guid project,Guid account,CancellationToken ct)
    {
        await Actor(project,account,true,ct);await RequireV2(project,ct);
        if(!await VersionSchema(ct))return new(false,false,false,null);
        var current=await CurrentRelease(project,ct);
        var active=await db.Set<ExperimentalPeerCohort>().AnyAsync(x=>x.ProjectId==project&&x.Status!=ExperimentalPeerCohortStatus.Closed,ct);
        return new(true,true,!active,current?.RunId);
    }
    private Task<OfficialContributionRelease?> CurrentRelease(Guid project,CancellationToken ct)=>
        db.Set<OfficialContributionRelease>().AsNoTracking().Where(x=>x.ProjectId==project).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(ct);

    // Existing multi-step APIs remain available; the common path performs all states in one transaction.
    public Task<OfficialRunResponse> ApproveAndPublishAsync(Guid project,Guid id,Guid account,OfficialApprovalRequest request,CancellationToken ct)=>Concurrency(async()=>
    {
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await LockProject(project,ct);var actor=await Actor(project,account,true,ct);await RequireV2(project,ct);await RequireVersionSchema(ct);
        var row=await Run(project,id,ct);await db.Entry(row).ReloadAsync(ct);
        var already=await db.Set<OfficialContributionRelease>().AsNoTracking().SingleOrDefaultAsync(x=>x.RunId==id&&x.ProjectId==project,ct);
        if(already is not null){var replay=await Map(row,account,ct);await tx.CommitAsync(ct);return replay;}
        Check(row.Revision==request.ExpectedRevision,"OFFICIAL_REVISION_CONFLICT","Reload the preview before approval.");
        Check(request.Reason is null || request.Reason.Length<=2000,"OFFICIAL_FEEDBACK_INVALID","Reason maximum is 2000 characters.",400);
        var current=await CurrentRelease(project,ct);
        Check(current?.RunId==request.ExpectedPublishedRunId,"OFFICIAL_PUBLICATION_CHANGED","The current published version changed; reload before approving.");
        Check(row.State is "Draft" or "Rejected" or "Review" or "Approved" or "Finalized","OFFICIAL_STATE_INVALID","Run cannot be approved.");
        await Validate(row,account,ct);
        while(row.State!="Finalized")
        {
            row.State=NextState(row.State,row.State is "Draft" or "Rejected"?"submit":row.State=="Review"?"approve":"finalize",request.Reason);
            row.Revision++;
            if(row.State=="Finalized"){row.FinalizedAt=DateTimeOffset.UtcNow;row.FinalizedById=actor;}
            db.Add(new OfficialContributionAudit{RunId=id,Revision=row.Revision,Action=row.State,ActorId=actor,Feedback=request.Reason});
            await db.SaveChangesAsync(ct);
        }
        await PublishRelease(row,actor,request.Reason,ct);
        var response=await Map(row,account,ct);await tx.CommitAsync(ct);return response;
    });
    private async Task PublishRelease(OfficialContributionRun row,Guid actor,string? reason,CancellationToken ct)
    {
        await RequireVersionSchema(ct);
        var prior=await CurrentRelease(row.ProjectId,ct);
        // A historical release can be replayed but never made current again.
        if(await db.Set<OfficialContributionRelease>().AnyAsync(x=>x.RunId==row.Id,ct))return;
        db.Add(new OfficialContributionRelease{RunId=row.Id,ProjectId=row.ProjectId,Version=(prior?.Version??0)+1,
            PreviousRunId=prior?.RunId,PublishedById=actor,Reason=reason});
        // Preserve the first-publication legacy receipt. It is never updated to point at a new result.
        if(!await db.Set<OfficialContributionPublication>().AnyAsync(x=>x.ProjectId==row.ProjectId,ct))
            db.Add(new OfficialContributionPublication{ProjectId=row.ProjectId,RunId=row.Id,PublishedById=actor});
        db.Add(new OfficialContributionAudit{RunId=row.Id,Revision=row.Revision+1,Action="Published",ActorId=actor,Feedback=reason});
        await db.SaveChangesAsync(ct);
    }
    // Creator-only history: existing member audience is own current Published result. No new disclosure policy.
    public async Task<OfficialPage<OfficialVersionResponse>> VersionsAsync(Guid project,Guid account,int page,int size,CancellationToken ct)
    {
        await Actor(project,account,true,ct);await RequireV2(project,ct);await RequireVersionSchema(ct);Page(page,size);
        var all=await db.Set<OfficialContributionRelease>().AsNoTracking().Where(x=>x.ProjectId==project).OrderByDescending(x=>x.Version).ToArrayAsync(ct);
        return new("1",page,size,all.Length,all.Skip((page-1)*size).Take(size).Select(x=>{
            var next=all.SingleOrDefault(n=>n.Version==x.Version+1);
            return new OfficialVersionResponse(x.Version,x.RunId,x.PublishedAt,next is null,next?.RunId,next?.PublishedAt,x.Reason);
        }).ToArray());
    }
}
