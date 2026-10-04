using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities
{
    public sealed class ProjectMember
    {
        public Guid Id { get; set; }

        public Guid ProjectId { get; set; }

        public Guid UserId { get; set; }

        public string Role { get; set; } = null!;

        public ProjectMemberStatus Status { get; set; }

        public DateTimeOffset JoinedAt { get; set; }

        public DateTimeOffset? LeftAt { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        // Navigation properties

        public Project Project { get; set; } = null!;

        public UserProfile User { get; set; } = null!;
    }
}
