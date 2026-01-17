using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Users;

namespace PGSH.Domain.Students;

public sealed class Student: User
{
    //public Guid Id { get; set; }
    public AcademicProgram AcademicProgram { get; set; }
    public string CNE { get; set; }
    public decimal AccessGrade { get; set; } = 10.01M;
    public string Appogee { get; set; }
    public BacSeries BacSeries { get; set; }
    public AgreementType AgreementType { get; set; } = AgreementType.None;
    public string BacYear { get; set; } 
    public ICollection<Registration> registrations { get; set; } = new List<Registration>();
    public ICollection<History> history { get; set; } = new List<History>();
    public Academy? Academy { get; set; }
    public Province? Province { get; set; }
    public int? Ranking {  get; set; }
}
