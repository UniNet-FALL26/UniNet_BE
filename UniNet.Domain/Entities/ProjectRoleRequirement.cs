using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniNet.Domain.Entities
{
    public sealed class ProjectRoleRequirement
    {
        public Guid Id { get; set; }

        public Guid ProjectId { get; set; }

        public string Role { get; set; } = null!;

        public int Quantity { get; set; }

        public string Requirements { get; set; } = null!;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        // Navigation property

        public Project Project { get; set; } = null!;
    }
}
