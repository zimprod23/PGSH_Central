using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Domain.Stages;

public sealed class StageGroupPeriod
{
    public Guid Id { get; set; }

    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }

    public int ServiceId { get; set; }
    public Service Service { get; set; }

    public int StageGroupId { get; set; }
    public StageGroup StageGroup { get; set; }
}
