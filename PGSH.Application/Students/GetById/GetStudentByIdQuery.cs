using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Students.GetById;

public sealed record GetStudentByIdQuery(Guid StudentId): IQuery<StudentResponse>;
