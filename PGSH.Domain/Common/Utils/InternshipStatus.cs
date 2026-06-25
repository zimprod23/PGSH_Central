using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Domain.Common.Utils;

public enum InternshipStatus
{
    Planned,
    Ongoing,
    Completed,
    Evaluated,
    Validated,
    Rejected,
    // Not a persisted assignment status: used only as a per-period suivi count bucket for a
    // rotation suspended mid-stage (e.g. an exam week). The assignment itself stays Ongoing.
    Paused
}

