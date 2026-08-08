using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Students.GetParcours;

/// <summary>
/// Every stage attempt one student has made, grouped by the registration that carried it — the whole
/// parcours, not just the year in progress.
/// </summary>
public sealed record GetStudentParcoursQuery(Guid StudentId) : IQuery<StudentParcoursResponse>;
