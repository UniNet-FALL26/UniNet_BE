using System.Text.Json;
using UniNet.Application.DTOs.Projects;
using UniNet.Domain.Entities;

namespace UniNet.Application;

public static class EvidenceModerationMapping
{
    public static EvidenceModerationResponse ToResponse(ProjectEvidenceModeration m) => new(m.Id, m.ProjectId,
        m.Version, m.SourceHash, m.PolicyHash, m.Status, m.SupersededAt == null, m.CreatedAt, m.CompletedAt,
        m.Summary, m.LastErrorCode, m.LastError,
        JsonSerializer.Deserialize<EvidenceSourceSnapshot>(m.SnapshotJson)!,
        JsonSerializer.Deserialize<EvidencePolicySnapshot>(m.PolicyJson)!,
        m.Attempts.OrderBy(a => a.Number).Select(a => new EvidenceAttemptResponse(a.Number, a.StartedAt,
            a.FinishedAt, a.ErrorCode, a.Error, a.Transient,
            a.ResultsJson == null ? [] : JsonSerializer.Deserialize<List<EvidenceCheckResult>>(a.ResultsJson)!)).ToList());
}
