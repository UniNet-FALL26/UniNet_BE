using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniNet.Domain.Entities
{
    public sealed class UserSkill
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid SkillId { get; set; }
        public SkillLevel Level { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        // Navigation properties
        public UserProfile User { get; set; } = null!;
        public Skill Skill { get; set; } = null!;
    }
}
