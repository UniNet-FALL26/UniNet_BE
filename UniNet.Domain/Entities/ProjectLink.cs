using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities
{
    public sealed class ProjectLink
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProjectId { get; set; }
        public string Url { get; set; } = null!;
        public string? NormalizedUrl { get; set; }
        public string? ResolvedUrl { get; set; }          
        public ModerationCheckResult CheckResult { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public Project Project { get; set; } = null!;
    }
}
