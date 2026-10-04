using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniNet.Domain.Enums
{
    public enum  ProjectJoinRequestStatus : short
    {
        Pending = 0,
        Accepted = 1,
        Rejected = 2,
        Cancelled = 3,
        Expired = 4
    }
}
