using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities
{
    public sealed class ProjectInvitation
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ProjectId { get; set; }

        public Guid InviteeId { get; set; }

        public Guid InviterId { get; set; }

        public string Role { get; set; } = null!;

        public string? Message { get; set; }

        public ProjectInvitationStatus Status { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        // Navigation properties

        public Project Project { get; set; } = null!;

        public UserProfile   Invitee { get; set; } = null!;

        public UserProfile Inviter { get; set; } = null!;
    }
}
