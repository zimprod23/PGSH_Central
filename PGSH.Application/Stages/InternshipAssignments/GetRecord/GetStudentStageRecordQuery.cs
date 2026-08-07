using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.InternshipAssignments.GetRecord;

public sealed record GetStudentStageRecordQuery(Guid AssignmentId)
    : IQuery<StudentStageRecordResponse>;
