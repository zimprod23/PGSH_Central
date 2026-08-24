using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicYears.Manage;

/// <summary>
/// Moves « l'année en cours » onto one year, in the only order the database allows.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Demotion is saved as its own statement, before the promotion.</b>
/// <c>IX_AcademicYear_IsCurrent</c> is unique and filtered, and Postgres checks it at the end of each
/// statement — so two flagged rows is a constraint violation, not a transient state EF can order its
/// way out of. Letting one <c>SaveChanges</c> emit both updates leaves that order to EF, which has no
/// reason to pick the safe one.</para>
///
/// <para>⚠ <b>…and it goes through the aggregate, not <c>ExecuteUpdateAsync</c>.</b> That helper is
/// unsupported by the in-memory provider, so the demote it used to perform was the one part of this
/// flow no test could reach — and it is the part that can leave the base with <em>no</em> current year
/// at all.</para>
///
/// <para>The residual exposure is a crash between the two saves, which leaves nothing flagged. That is
/// deliberately the failure that is left: <c>AcademicYearResolver</c> then refuses loudly rather than
/// guessing, and running the designation again fixes it. The alternative — one explicit transaction —
/// would need a transaction surface on <c>IApplicationDbContext</c> that nothing else in the codebase
/// has, and the in-memory provider cannot honour it, so every test through these handlers would have
/// to suppress a warning to keep working.</para>
///
/// <para>Shared by create and set-current so the ordering is stated once. Callers <b>must</b> have
/// established that the target is not already current — <see cref="AcademicYear.MakeCurrent"/> refuses
/// that, and by then the demote has already run.</para>
/// </remarks>
/// <param name="PreviousLabel">The year that stood down, or null when none was current.</param>
public sealed record CurrentYearChange(string? PreviousLabel);

public sealed class CurrentYearDesignation(IApplicationDbContext dbContext)
{
    /// <remarks>
    /// ⚠ The outcome is wrapped rather than returned as <c>Result&lt;string?&gt;</c>: that type cannot
    /// carry a null success — its implicit operator turns null into <c>Error.NullValue</c> — so « no
    /// year stood down », the ordinary state of a fresh base, would come back as a failure.
    /// </remarks>
    public async Task<Result<CurrentYearChange>> PromoteAsync(AcademicYear target, CancellationToken ct)
    {
        var sitting = await dbContext.AcademicYears
            .Where(y => y.IsCurrent && y.Id != target.Id)
            .ToListAsync(ct);

        string? previousLabel = sitting.FirstOrDefault()?.Label;

        if (sitting.Count > 0)
        {
            foreach (var year in sitting) year.Relinquish();
            await dbContext.SaveChangesAsync(ct);
        }

        var promoted = target.MakeCurrent();
        if (promoted.IsFailure)
            return Result.Failure<CurrentYearChange>(promoted.Error);

        await dbContext.SaveChangesAsync(ct);

        return new CurrentYearChange(previousLabel);
    }
}
