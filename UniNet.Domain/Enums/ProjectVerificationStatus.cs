using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniNet.Domain.Enums
{
    public enum  ProjectVerificationStatus : short
    {
        Pending = 0,
       Verified = 1, 
       Failed = 2,
       Cancelled = 3
    }
}
