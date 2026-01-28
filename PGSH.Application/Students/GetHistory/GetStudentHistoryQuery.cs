using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Students.GetHistory;

public sealed record GetStudentHistoryQuery(Guid StudentId): IQuery<List<StudentHistoryResponse>>;
