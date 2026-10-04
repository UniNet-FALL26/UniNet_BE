using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniNet.Domain.Enums
{
    public enum ModerationCheckResult : short
    {
        Pending = 0,
        Passed = 1,
        Failed = 2  
    }
}
