using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using UniNet.API.Controllers;
using UniNet.Application;
using UniNet.Application.Common;
using UniNet.Application.Services;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;
public sealed partial class GitExperimentalContributionPostgresHttpTests
{
    [Fact]
    public void PublicContributionContractPreservesZeroWithoutPrivateFields()
    {
        var member=new PublishedContributionMember(Guid.NewGuid(),0m);
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(member,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(0m,json.RootElement.GetProperty("contributionPercent").GetDecimal());
        Assert.Equal(2,json.RootElement.EnumerateObject().Count());
    }

    [OfficialPostgresFact]
    public async Task PublicPublishedReadSelectsLedgerPreservesRosterAndNeverDisclosesDraftOrPrivateInputs()
    {
        var f=await OfficialFixture(); await using var db=OfficialDb();
        await db.Projects.Where(x=>x.Id==f.Project).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Visibility,ProjectVisibility.Public));
        var reader=new PublishedContributionReader(db);
        Assert.Empty((await reader.ReadAsync(f.Project,f.Accounts[3],default)).Members);
        var v1=await OfficialService(db).CreateAsync(f.Project,f.Accounts[0],new(Guid.NewGuid(),f.Snapshot,f.Peer),default);
        Assert.Empty((await reader.ReadAsync(f.Project,f.Accounts[3],default)).Members);
        foreach(var transition in new[]{"submit","approve","finalize"})
        {
            v1=await OfficialService(db).TransitionAsync(f.Project,v1.Id,f.Accounts[0],transition,new(v1.Revision),default);
            Assert.Empty((await reader.ReadAsync(f.Project,f.Accounts[3],default)).Members);
        }
        await OfficialService(db).ApproveAndPublishAsync(f.Project,v1.Id,f.Accounts[0],new(v1.Revision,null),default);
        var visible=await reader.ReadAsync(f.Project,f.Accounts[3],default);
        Assert.Equal(1,visible.Version);Assert.Equal(v1.Id,visible.PublishedRunId);Assert.Equal(3,visible.Members.Count);
        Assert.Equal(100m,visible.Members.Sum(x=>x.ContributionPercent));
        Assert.Contains(visible.Members,x=>x.UserProfileId==f.Profiles[2]);
        Assert.Equal(visible.Members, (await reader.ReadAsync(f.Project,f.Accounts[1],default)).Members);
        var json=JsonSerializer.Serialize(visible,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using(var document=JsonDocument.Parse(json))
        {
            Assert.Equal(new[]{"members","projectId","publishedAt","publishedRunId","version"},document.RootElement.EnumerateObject().Select(x=>x.Name).Order().ToArray());
            Assert.All(document.RootElement.GetProperty("members").EnumerateArray(),x=>Assert.Equal(new[]{"contributionPercent","userProfileId"},x.EnumerateObject().Select(p=>p.Name).Order().ToArray()));
        }
        var git2=await OfficialService(db).CaptureAsync(f.Project,f.Accounts[0],new(DateTimeOffset.UtcNow),default);
        var draft=await OfficialService(db).CreateAsync(f.Project,f.Accounts[0],new(Guid.NewGuid(),git2,f.Peer),default);
        Assert.NotEqual(v1.Id,draft.Id);Assert.Equal(v1.Id,(await reader.ReadAsync(f.Project,f.Accounts[3],default)).PublishedRunId);
        await OfficialService(db).ApproveAndPublishAsync(f.Project,draft.Id,f.Accounts[0],new(0,v1.Id),default);
        Assert.Equal(2,(await reader.ReadAsync(f.Project,f.Accounts[3],default)).Version);
        await db.ProjectMembers.Where(x=>x.ProjectId==f.Project&&x.UserId==f.Profiles[1]).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,ProjectMemberStatus.Left));
        Assert.Contains((await reader.ReadAsync(f.Project,f.Accounts[3],default)).Members,x=>x.UserProfileId==f.Profiles[1]);
        // Private Git authorization is irrelevant to already-published aggregate percentages.
        await db.ProjectGitRepositories.Where(x=>x.ProjectId==f.Project).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AccessStatus,ProjectGitRepositoryAccessStatus.AccessLost));
        Assert.Equal(draft.Id,(await reader.ReadAsync(f.Project,f.Accounts[3],default)).PublishedRunId);
        Assert.Equal(v1.ResultHash,(await db.Set<OfficialContributionRun>().AsNoTracking().SingleAsync(x=>x.Id==v1.Id)).ResultHash);
        await db.Projects.Where(x=>x.Id==f.Project).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Visibility,ProjectVisibility.Private));
        Assert.Equal("FORBIDDEN",(await Assert.ThrowsAsync<ProjectException>(()=>reader.ReadAsync(f.Project,f.Accounts[3],default))).Code);
        Assert.Equal(draft.Id,(await reader.ReadAsync(f.Project,f.Accounts[0],default)).PublishedRunId);
        var other=await db.Projects.AsNoTracking().FirstAsync(x=>x.Id!=f.Project&&x.CreatorId==f.Profiles[0]);
        Assert.Null((await reader.ReadAsync(other.Id,f.Accounts[0],default)).PublishedRunId);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [OfficialPostgresFact]
    public async Task PublicContributionHttpAllowsStudentOutsidersButRetainsGuestAndPrivateRouteGuards()
    {
        var f=await OfficialFixture();await using var db=OfficialDb();
        await db.Projects.Where(x=>x.Id==f.Project).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Visibility,ProjectVisibility.Public));
        var run=await OfficialService(db).CreateAsync(f.Project,f.Accounts[0],new(Guid.NewGuid(),f.Snapshot,f.Peer),default);
        await OfficialService(db).ApproveAndPublishAsync(f.Project,run.Id,f.Accounts[0],new(0,null),default);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Testing"});
        builder.Logging.ClearProviders();builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<UniNet.Infrastructure.Data.UniNetDbContext>(o=>o.UseNpgsql(db.Database.GetConnectionString()));
        builder.Services.AddSingleton(TimeProvider.System);builder.Services.AddGitExperimentalContributionServices();
        builder.Services.AddControllers().AddApplicationPart(typeof(PublishedContributionController).Assembly);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{o.MapInboundClaims=false;o.TokenValidationParameters=new(){ValidateIssuer=true,ValidIssuer=Issuer,ValidateAudience=true,ValidAudience=Issuer,ValidateIssuerSigningKey=true,IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),RoleClaimType=ClaimTypes.Role};});
        builder.Services.AddAuthorization();await using var app=builder.Build();
        app.UseExceptionHandler(e=>e.Run(async c=>{var ex=c.Features.Get<IExceptionHandlerFeature>()?.Error;c.Response.StatusCode=ex switch{ProjectException p=>p.Status,GitHubIntegrationException g=>g.Status,_=>500};await c.Response.WriteAsJsonAsync(new{code="REQUEST_REJECTED"});}));
        app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
        try
        {
            var path=$"/api/projects/{f.Project}/contribution/v2";
            using var creator=Client(app,f.Accounts[0]);using var member=Client(app,f.Accounts[1]);using var outsider=Client(app,f.Accounts[3]);
            using var guest=new HttpClient{BaseAddress=creator.BaseAddress};
            Assert.Equal(HttpStatusCode.Unauthorized,(await guest.GetAsync(path+"/results/public")).StatusCode);
            foreach(var client in new[]{creator,member,outsider})
            {
                var response=await client.GetAsync(path+"/results/public");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
                Assert.True(response.Headers.CacheControl?.NoStore);
                Assert.Equal(run.Id,(await response.Content.ReadFromJsonAsync<PublishedContributionResponse>())!.PublishedRunId);
            }
            Assert.Equal(HttpStatusCode.Forbidden,(await outsider.GetAsync(path+$"/runs/{run.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await outsider.GetAsync(path+"/results/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await guest.GetAsync(path+"/versions")).StatusCode);
            await db.Projects.Where(x=>x.Id==f.Project).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Visibility,ProjectVisibility.Private));
            Assert.Equal(HttpStatusCode.Forbidden,(await outsider.GetAsync(path+"/results/public")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await member.GetAsync(path+"/results/public")).StatusCode);
        }
        finally {await app.StopAsync();}
    }
}
