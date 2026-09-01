using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Inscription;

/// <summary>
/// The inscription sheet to fill in, for one promotion of one year.
/// </summary>
/// <remarks>
/// <para><b>It carries no roll, and that is the difference from the déliberation canvas.</b> That one
/// lists the promotion because a mistyped CNE is a row belonging to nobody; here the people the file
/// is about are, by definition, not in the base — there is nothing to pre-fill. Listing the 10 204
/// students who <em>are</em> would be a tab nobody reads and a copy of the whole roll in every
/// downloaded workbook.</para>
///
/// <para>What the sheet does carry is the promotion it was cut for, so a file filled in for the 3ᵉ
/// année cannot be uploaded against the 1ʳᵉ by accident, and — above the first year — the provenance
/// columns marked as required rather than optional.</para>
/// </remarks>
public sealed record GetInscriptionTemplateQuery(
    int LevelId,
    int? AcademicYearId = null) : IQuery<InscriptionTemplateFile>;

public sealed record InscriptionTemplateFile(string FileName, byte[] Content);

internal sealed class GetInscriptionTemplateQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    ExecutionAuthorizer authorizer,
    IInscriptionSheetParser sheetParser)
    : IQueryHandler<GetInscriptionTemplateQuery, InscriptionTemplateFile>
{
    public async Task<Result<InscriptionTemplateFile>> Handle(
        GetInscriptionTemplateQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(InscriptionErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<InscriptionTemplateFile>(access.Error);

        var year = await yearResolver.ResolveWithLabelAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<InscriptionTemplateFile>(year.Error);

        (_, string yearLabel) = year.Value;

        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == request.LevelId)
            .Select(l => new { l.Label, l.Year, l.AcademicProgram })
            .FirstOrDefaultAsync(cancellationToken);

        if (level is null)
            return Result.Failure<InscriptionTemplateFile>(RegistrationErrors.MissingLevel);

        string levelLabel = level.Label ?? $"Année {level.Year} — {level.AcademicProgram}";

        if (level.Year <= 0)
            return Result.Failure<InscriptionTemplateFile>(InscriptionErrors.NotAPromotion(levelLabel));

        var template = new InscriptionTemplate(
            yearLabel, levelLabel, level.Year, OriginRequired: level.Year > 1);

        return new InscriptionTemplateFile(
            $"inscription-{Slug(levelLabel)}-{Slug(yearLabel)}.xlsx",
            sheetParser.BuildTemplate(template));
    }

    private static string Slug(string value) =>
        new(value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());
}
