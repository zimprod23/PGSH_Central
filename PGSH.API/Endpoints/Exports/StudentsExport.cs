using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Export;

namespace PGSH.API.Endpoints.Exports;

/// <summary>
/// The roll as a spreadsheet. <c>academicYearId</c> omitted resolves to the current year — an export
/// of « les étudiants » must never quietly mean every promotion the base has ever held.
/// </summary>
public sealed class StudentsExport : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("students/export", async (
            [AsParameters] GetStudentsExportQuery query,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);

            return result.Match(
                file => Results.File(file.Content, ExportContentType.Xlsx, file.FileName),
                CustomResults.Problem);
        })
        .WithName("ExportStudents")
        .WithTags(Tags.Students)
        .RequireAuthorization();
    }
}
