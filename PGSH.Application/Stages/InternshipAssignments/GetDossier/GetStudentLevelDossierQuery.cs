using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.InternshipAssignments.GetDossier;

/// <summary>
/// Every attempt one student has made at the stages of a single level, folded across all of their
/// registrations at that level.
/// </summary>
public sealed record GetStudentLevelDossierQuery(Guid StudentId, int LevelId)
    : IQuery<StudentLevelDossierResponse>;
