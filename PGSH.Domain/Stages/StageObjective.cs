using PGSH.SharedKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Domain.Stages;

public sealed class StageObjective
{
    public int Id { get; set; }

    public string Label { get; set; }
    public string? Description { get; set; }

    public int Weight { get; set; }
    public bool IsMandatory { get; set; }

    public int StageId { get; set; }
    public Stage Stage { get; set; }
}
