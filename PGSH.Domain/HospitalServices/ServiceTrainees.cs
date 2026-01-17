namespace PGSH.Domain.HospitalServices;

public sealed class ServiceTrainees
{
    public TraineeType TraineeType { get; set; }
}
public enum TraineeType
{
    Intern,
    Résidant
}