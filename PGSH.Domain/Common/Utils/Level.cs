using System.ComponentModel.DataAnnotations;

namespace PGSH.Domain.Common.Utils;

public sealed class Level
{
    public int Id { get; set; }
    public string? Label { get; set; }
    [Range(0, 10)]
    public int Year { get; set; }
    public AcademicProgram AcademicProgram { get; set; }
}
