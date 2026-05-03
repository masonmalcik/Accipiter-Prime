using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Enums
{
    public enum OpportunityStatus
    {
        Discovered,
        Scored,
        Simulated,
        Executing,
        Executed,
        Reverted,
        Skipped,
        Expired
    }
}
