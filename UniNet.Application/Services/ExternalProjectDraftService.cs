using Microsoft.Extensions.Configuration;
using UniNet.Application.DTOs.Projects;
using UniNet.Infrastructure.Data;
namespace UniNet.Application.Services;
public sealed class ExternalProjectDraftService
{
    public ExternalProjectDraftService(UniNetDbContext db) { }
    public ExternalProjectDraftService(UniNetDbContext db, IConfiguration configuration) { }
    public Task<ExternalProjectDraftResponse> CreateAsync(Guid accountId, CancellationToken ct) => throw RetiredProjectFeatures.Gone();
    public Task<ExternalProjectDraftResponse> GetAsync(Guid id, Guid accountId, CancellationToken ct) => throw RetiredProjectFeatures.Gone();
    public Task<ExternalProjectDraftResponse> UpdateAsync(Guid id, Guid accountId, UpdateExternalProjectDraftRequest request, CancellationToken ct) => throw RetiredProjectFeatures.Gone();
    public Task<ConfirmExternalProjectDraftResponse> ConfirmAsync(Guid id, Guid accountId, ConfirmExternalProjectDraftRequest request, CancellationToken ct) => throw RetiredProjectFeatures.Gone();
}
