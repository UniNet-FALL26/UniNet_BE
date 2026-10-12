using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities
{
    public sealed class Package
    {
        public Guid Id { get; set; } 
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public PackageType Type { get; set; }
        public TargetRole Role { get; set; }
        public decimal Price { get; set; }
        public int? DurationInDays { get; set; }
        public bool IsActive { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public List<Transaction> Transactions { get; set; } = [];
    }
}
