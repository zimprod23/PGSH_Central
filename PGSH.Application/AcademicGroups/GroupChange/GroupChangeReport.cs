namespace PGSH.Application.AcademicGroups.GroupChange;

/// <summary>
/// What a « changement de groupe » actually re-pointed.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Silent toward the student's file is not silent toward the operator.</b> The act leaves
/// nothing on the dossier, nothing on the parcours and no <c>CohortMembership</c> saying a move
/// happened — which is exactly why the person who asked for it has to be shown, once, what it moved.
/// A confirmation reading « fait » on an act that rewrote seven affectations and thirty périodes, and
/// that nothing afterwards can be asked about, is the shape of an act nobody agreed to.</para>
///
/// <para><see cref="AdHocPeriodsKept"/> is the count worth reading: a délocalisation, a revalidation or
/// an imported période hangs off no cell, came from no répartition and cannot be reproduced by one, so
/// it travels with the affectation untouched and keeps naming its own service.</para>
/// </remarks>
/// <param name="FromGroupLabel">
/// The roster the student came from — and the only place it is ever written down, since the act
/// deliberately leaves no record of it. It is what the audit entry carries.
/// </param>
public sealed record GroupChangeReport(
    Guid RegistrationId,
    string StudentName,
    string FromGroupLabel,
    string ToGroupLabel,
    int AffectationsMoved,
    int AffectationsCreated,
    int PeriodsCreated,
    int PeriodsReplaced,
    int AdHocPeriodsKept);

/// <summary>
/// An échange: two students exchange rosters in one act.
/// </summary>
/// <remarks>
/// It carries the two changements rather than summarising them, because they can differ — one student
/// may pick up an affectation the other's roster has and he did not, and only the per-student figures
/// say so.
/// </remarks>
public sealed record GroupSwapReport(GroupChangeReport First, GroupChangeReport Second);
