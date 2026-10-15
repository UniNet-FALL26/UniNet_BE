using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniNet.Domain.Enums
{
    public enum VerificationAssignmentStatus : short
    {
       Assigned = 0,
       Completed = 1,
       Overdue = 2, 
       Cancelled = 3
    }
}
