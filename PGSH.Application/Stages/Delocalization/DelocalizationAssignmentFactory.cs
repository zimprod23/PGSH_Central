using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Delocalization;

/// <summary>
/// The blank assignment a délocalisation stands on when the stage was never planned for the student.
/// </summary>
/// <remarks>
/// Shared by the single act and the bulk one because they must produce the same record: a student
/// délocalisé one by one and a student délocalisé inside a roster differ in how somebody clicked,
/// and in nothing the dossier should be able to see. The membership entry is part of that — without
/// it the assignment belongs to a cohorte with no trace of when it joined.
/// </remarks>
internal static class DelocalizationAssignmentFactory
{
    public static InternshipAssignment CreateFor(Guid registrationId, int cohortId, DateOnly on)
    {
        var assignmentId = Guid.NewGuid();

        var assignment = new InternshipAssignment
        {
            Id              = assignmentId,
            RegistrationId  = registrationId,
            CurrentCohortId = cohortId,
        };

        assignment.MembershipHistory.Add(new CohortMembership
        {
            Id                     = Guid.NewGuid(),
            InternshipAssignmentId = assignmentId,
            CohortId               = cohortId,
            StartDate              = on,
        });

        return assignment;
    }
}
