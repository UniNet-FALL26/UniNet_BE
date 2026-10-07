using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniNet.Domain.Enums
{
    public enum  ProjectModerationStatus : short
    {
        Pending = 0,
        Processing = 1,
        ReviewRequired = 2,
        Approved = 3,
        Rejected = 4,
        Error = 5
    }
}
