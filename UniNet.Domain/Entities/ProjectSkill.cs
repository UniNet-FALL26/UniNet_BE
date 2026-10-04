using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniNet.Domain.Entities
{
    public sealed class ProjectSkill
    {
        public Guid ProjectId { get; set; }

        public Guid SkillId { get; set; }

        // Navigation properties

        public Project Project { get; set; } = null!;

        public Skill Skill { get; set; } = null!;
    }
}
