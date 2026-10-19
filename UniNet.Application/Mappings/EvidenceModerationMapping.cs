using System.Text.Json;
using UniNet.Application.DTOs.Projects;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application;

public static class EvidenceModerationMapping
{
    public static EvidenceModerationResponse ToResponse(ProjectModeration m) => new(m.Id, m.ProjectId,
        m.Version, m.SourceHash!, m.PolicyHash!, ToEvidenceStatus(m), m.SupersededAt == null, m.CreatedAt, m.CompletedAt,
        m.Summary, m.LastErrorCode, m.LastError,
        JsonSerializer.Deserialize<EvidenceSourceSnapshot>(m.SnapshotJson!)!,
        JsonSerializer.Deserialize<EvidencePolicySnapshot>(m.PolicyJson!)!,
        m.Attempts.OrderBy(a => a.Number).Select(a => new EvidenceAttemptResponse(a.Number, a.StartedAt,
            a.FinishedAt, a.ErrorCode, a.Error, a.Transient,
            a.ResultsJson == null ? [] : JsonSerializer.Deserialize<List<EvidenceCheckResult>>(a.ResultsJson)!)).ToList());

    public static EvidenceModerationStatus ToEvidenceStatus(ProjectModeration m)
    {
        if (m.Purpose != ProjectModerationPurpose.ProjectEvidence)
            throw new InvalidOperationException("A level-1 response requires evidence moderation.");
        return m.Status switch
        {
            ProjectModerationStatus.Pending => EvidenceModerationStatus.Pending,
            ProjectModerationStatus.Processing => EvidenceModerationStatus.Processing,
            ProjectModerationStatus.Passed => EvidenceModerationStatus.Passed,
            ProjectModerationStatus.Rejected => EvidenceModerationStatus.Rejected,
            ProjectModerationStatus.NeedsEvidence => EvidenceModerationStatus.NeedsEvidence,
            ProjectModerationStatus.Error => EvidenceModerationStatus.Error,
            _ => throw new InvalidOperationException("Invalid evidence moderation status.")
        };
    }
}
