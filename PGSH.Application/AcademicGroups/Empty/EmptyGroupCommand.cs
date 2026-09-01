using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.Empty;

/// <summary>
/// Takes every student out of one roster.
/// </summary>
/// <param name="DropAffectations">
/// Also remove the affectations those students hold in this roster's cohortes, and the périodes
/// planned on them. Required whenever any exist — the caller having read what the refusal named —
/// because a roster emptied without them is a roster that only <i>reads</i> empty. Never enough on
/// its own once a rotation has begun: that refusal cannot be forced from here.
/// </param>
public sealed record EmptyGroupCommand(int GroupId, bool DropAffectations = false)
    : ICommand<EmptyGroupReport>;

/// <param name="Unassigned">Registrations whose roster pointer was cleared.</param>
/// <param name="AffectationsRemoved">Affectations deleted with them — 0 unless asked for.</param>
/// <param name="PeriodsRemoved">Périodes de service that went with those affectations.</param>
public sealed record EmptyGroupReport(int Unassigned, int AffectationsRemoved, int PeriodsRemoved);
