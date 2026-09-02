using FluentAssertions;
using PGSH.Domain.Registrations;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// The flag that lets the faculty's own document be applied without PGSH pretending to agree with it.
///
/// <para>A hold withdraws a registration from planning — no roster, no cohort affectation, no new
/// stage work — and does nothing else: it is not a status, it annuls nothing, and it removes nothing
/// that already exists. Every rule below exists because the alternative is a silent exclusion, which
/// is the failure the mechanism was built to replace.</para>
/// </summary>
public class RegistrationHoldTests
{
    private static Registration NewRegistration() => new()
    {
        Id = Guid.NewGuid(),
        StudentId = Guid.NewGuid(),
        AcademicYearId = 1,
        LevelId = 3,
    };

    private static readonly DateTime RaisedOn = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Places a hold and gives it the key the store would have given it.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The id is assigned here and never by production code.</b> <c>PlaceOnHold</c> leaves the key
    /// to the store — pre-setting one on a child of a tracked parent makes EF classify it
    /// <c>Modified</c> — so a hold is anonymous until it is saved, and
    /// <c>ReleaseHold(Guid.Empty, …)</c> is refused for exactly that reason. These are pure domain
    /// tests with no store, so this stands in for the save; everything downstream of a real release
    /// is exercised in <c>RegistrationHoldWorkflowTests</c>, against a context.
    /// </remarks>
    private static RegistrationHold Raise(
        Registration registration, RegistrationHoldReason reason, string evidence, DateTime? on = null)
    {
        var placed = registration.PlaceOnHold(reason, evidence, on ?? RaisedOn);
        placed.IsSuccess.Should().BeTrue();

        var hold = placed.Value;
        if (hold.Id == Guid.Empty) hold.Id = Guid.NewGuid();
        return hold;
    }

    [Fact]
    public void Placing_a_hold_freezes_the_registration_and_raises_an_event()
    {
        var registration = NewRegistration();

        var result = registration.PlaceOnHold(
            RegistrationHoldReason.OutstandingPriorStages,
            "2 stage(s) antérieur(s) non validés.",
            RaisedOn);

        result.IsSuccess.Should().BeTrue();
        registration.IsOnHold.Should().BeTrue();
        RegistrationHoldPolicy.IsOnHold(registration).Should().BeTrue();
        RegistrationHoldPolicy.IsPlannable(registration).Should().BeFalse();

        // The widest act in the area — it decides that a student takes no part in the year's
        // répartition — so it is observable, like the verdict and the CNPN stamp.
        registration.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RegistrationHeldDomainEvent>()
            .Which.Reason.Should().Be(RegistrationHoldReason.OutstandingPriorStages);
    }

    /// <summary>
    /// ⚠ The evidence is what the operator acts on. « Signalé » with nothing attached is a row nobody
    /// can do anything about, which is why it is refused rather than defaulted.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_hold_without_evidence_is_refused(string evidence)
    {
        var registration = NewRegistration();

        var result = registration.PlaceOnHold(
            RegistrationHoldReason.AbsentFromReinscriptionRoll, evidence, RaisedOn);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RegistrationHolds.EvidenceRequired");
        registration.IsOnHold.Should().BeFalse();
    }

    /// <summary>
    /// ⚠ <b>Idempotent per reason, because the réinscription roll is re-runnable by design.</b> Two
    /// uploads of the same file must not stack two identical flags on one student, and — the half
    /// that actually bites — must not rewrite the evidence somebody is in the middle of acting on.
    /// The snapshot is the first one taken.
    /// </summary>
    [Fact]
    public void Raising_the_same_reason_twice_keeps_the_first_hold_and_its_evidence()
    {
        var registration = NewRegistration();

        registration.PlaceOnHold(
            RegistrationHoldReason.OutstandingPriorStages, "Constat initial.", RaisedOn);

        var second = registration.PlaceOnHold(
            RegistrationHoldReason.OutstandingPriorStages, "Constat réécrit.", RaisedOn.AddDays(1));

        second.IsSuccess.Should().BeTrue();
        registration.Holds.Should().ContainSingle();
        registration.Holds.Single().Evidence.Should().Be("Constat initial.");
        registration.Holds.Single().RaisedOn.Should().Be(RaisedOn);

        registration.DomainEvents.Should().ContainSingle("the second raise changed nothing");
    }

    /// <summary>Two different reasons are two different questions, so they coexist.</summary>
    [Fact]
    public void Two_reasons_stand_at_once()
    {
        var registration = NewRegistration();

        registration.PlaceOnHold(RegistrationHoldReason.OutstandingPriorStages, "Dette.", RaisedOn);
        registration.PlaceOnHold(RegistrationHoldReason.AbsentFromReinscriptionRoll, "Absent.", RaisedOn);

        registration.Holds.Should().HaveCount(2);
        registration.IsOnHold.Should().BeTrue();
    }

    [Fact]
    public void Releasing_the_only_hold_unfreezes_the_registration()
    {
        var registration = NewRegistration();
        var holdId = Raise(registration, RegistrationHoldReason.OutstandingPriorStages, "Dette.").Id;
        registration.ClearDomainEvents();

        var result = registration.ReleaseHold(
            holdId, "Évaluations saisies, tout est validé.", RaisedOn.AddDays(3));

        result.IsSuccess.Should().BeTrue();
        registration.IsOnHold.Should().BeFalse();
        RegistrationHoldPolicy.IsPlannable(registration).Should().BeTrue();

        // ⚠ The row survives its own release. « Qui l'a débloqué et sur quoi » is the half of the
        // record an audit actually asks for, and deleting it throws that away.
        var hold = registration.Holds.Single();
        hold.ReleasedOn.Should().Be(RaisedOn.AddDays(3));
        hold.ReleaseNote.Should().Be("Évaluations saisies, tout est validé.");
        hold.Evidence.Should().Be("Dette.", "the original snapshot is not overwritten by the release");

        registration.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RegistrationHoldReleasedDomainEvent>();
    }

    /// <summary>Releasing one of two leaves the registration frozen — hence <c>StillHeld</c>.</summary>
    [Fact]
    public void Releasing_one_of_two_leaves_the_registration_held()
    {
        var registration = NewRegistration();
        var first = Raise(registration, RegistrationHoldReason.OutstandingPriorStages, "Dette.");
        Raise(registration, RegistrationHoldReason.AbsentFromReinscriptionRoll, "Absent.");

        registration.ReleaseHold(first.Id, "Stages validés.", RaisedOn.AddDays(1))
            .IsSuccess.Should().BeTrue();

        registration.IsOnHold.Should().BeTrue("the absence has still not been explained");
        registration.Holds.Count(h => h.ReleasedOn is null).Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_release_without_a_note_is_refused(string note)
    {
        var registration = NewRegistration();
        var holdId = Raise(registration, RegistrationHoldReason.OutstandingPriorStages, "Dette.").Id;

        var result = registration.ReleaseHold(holdId, note, RaisedOn.AddDays(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RegistrationHolds.ReleaseNoteRequired");
        registration.IsOnHold.Should().BeTrue();
    }

    [Fact]
    public void Releasing_an_unknown_hold_is_refused()
    {
        var registration = NewRegistration();

        var result = registration.ReleaseHold(Guid.NewGuid(), "Vérifié.", RaisedOn);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RegistrationHolds.NotFound");
    }

    /// <summary>
    /// Returning success on a hold somebody else lifted days earlier would tell the caller he had
    /// just freed a student, which is a different fact from the one on the screen.
    /// </summary>
    [Fact]
    public void Releasing_twice_is_refused()
    {
        var registration = NewRegistration();
        var holdId = Raise(registration, RegistrationHoldReason.OutstandingPriorStages, "Dette.").Id;

        registration.ReleaseHold(holdId, "Vérifié.", RaisedOn.AddDays(1)).IsSuccess.Should().BeTrue();

        var second = registration.ReleaseHold(holdId, "Vérifié à nouveau.", RaisedOn.AddDays(2));

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("RegistrationHolds.AlreadyReleased");
    }

    /// <summary>
    /// ⚠ The store key is left unset. Assigning one to a child added to an already-tracked parent
    /// makes EF classify it <c>Modified</c> rather than <c>Added</c> — <c>UPDATE … WHERE Id = &lt;new
    /// guid&gt;</c>, nought rows, <c>DbUpdateConcurrencyException</c>. It has bitten this codebase
    /// before, in <c>InternshipAssignment.Delocalize</c>.
    /// </summary>
    [Fact]
    public void A_new_hold_carries_no_pre_set_key()
    {
        var registration = NewRegistration();
        registration.PlaceOnHold(RegistrationHoldReason.OutstandingPriorStages, "Dette.", RaisedOn);

        registration.Holds.Single().Id.Should().Be(Guid.Empty);
    }

    /// <summary>
    /// ⚠ <b>Whether a signalement freezes is a property of the reason, and the whole design turns on
    /// it.</b> « Dossier à compléter » says we are missing his paperwork, not that anything is wrong:
    /// he is cut into a roster and planned like anyone else while somebody finishes the file.
    /// Collapsing it into the blocking reasons would freeze a student over a missing date de
    /// naissance; the other way round would let an unexplained absence plan itself.
    /// </summary>
    [Fact]
    public void An_advisory_reason_flags_without_freezing()
    {
        var registration = NewRegistration();

        registration.PlaceOnHold(
            RegistrationHoldReason.IncompleteStudentFile,
            "Créé depuis « Réinscriptions » : seul le numéro Apogée est connu.",
            RaisedOn).IsSuccess.Should().BeTrue();

        registration.IsFlagged.Should().BeTrue("it is on the worklist");
        registration.IsOnHold.Should().BeFalse("but nothing about it blocks planning");
        RegistrationHoldPolicy.IsPlannable(registration).Should().BeTrue();
        RegistrationHoldPolicy.IsFlagged(registration).Should().BeTrue();
    }

    /// <summary>
    /// A blocking reason standing beside an advisory one still blocks — the registration is as frozen
    /// as its strictest flag.
    /// </summary>
    [Fact]
    public void One_blocking_reason_beside_an_advisory_one_still_freezes()
    {
        var registration = NewRegistration();

        registration.PlaceOnHold(RegistrationHoldReason.IncompleteStudentFile, "Fiche à compléter.", RaisedOn);
        registration.PlaceOnHold(RegistrationHoldReason.OutstandingPriorStages, "Dette.", RaisedOn);

        registration.IsOnHold.Should().BeTrue();
        RegistrationHoldPolicy.IsPlannable(registration).Should().BeFalse();
        registration.IsFlagged.Should().BeTrue();

        // ⚠ Releasing the blocking one is proved in RegistrationHoldWorkflowTests, not here: the key
        // is store-generated, so both of these carry Guid.Empty until they are saved and neither can
        // be named. That is exactly what the guard below refuses.
    }

    /// <summary>
    /// ⚠ <b>An unsaved hold has no identity, and releasing « by id » would lift an arbitrary one.</b>
    /// The key is store-generated, so every hold added in one unit of work carries
    /// <see cref="Guid.Empty"/> until it is saved — and <c>FirstOrDefault(h =&gt; h.Id == holdId)</c>
    /// would then match whichever sits first in the collection. Nothing on the worklist can carry an
    /// empty id, so reaching this is a defect in the caller rather than a user action.
    /// </summary>
    [Fact]
    public void An_unsaved_hold_cannot_be_released_by_id()
    {
        var registration = NewRegistration();
        registration.PlaceOnHold(RegistrationHoldReason.IncompleteStudentFile, "Fiche.", RaisedOn);
        registration.PlaceOnHold(RegistrationHoldReason.OutstandingPriorStages, "Dette.", RaisedOn);

        var result = registration.ReleaseHold(Guid.Empty, "Vérifié.", RaisedOn.AddDays(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RegistrationHolds.NotFound");
        registration.Holds.Should().OnlyContain(h => h.ReleasedOn == null, "nothing was lifted");
    }

    /// <summary>
    /// The blocking set and the label table must cover every reason: a value added to the enum and
    /// forgotten here would silently default to « does not freeze », which is the dangerous direction.
    /// </summary>
    [Fact]
    public void Every_reason_declares_whether_it_freezes_and_carries_wording()
    {
        foreach (var reason in Enum.GetValues<RegistrationHoldReason>())
        {
            reason.Label().Should().NotBe(reason.ToString(),
                $"{reason} has no French wording — the worklist would print its identifier");
            reason.Remedy().Should().NotBeNullOrWhiteSpace();
            reason.BlocksPlanning().Should().Be(
                RegistrationHoldReasonExtensions.Blocking.Contains(reason));
        }

        RegistrationHoldReasonExtensions.Blocking.Should().NotBeEmpty();
    }

    /// <summary>
    /// The expression and the delegate are one rule — the delegate is compiled from the expression —
    /// and this is what says so. Two hand-written copies, one for EF and one for memory, is the drift
    /// <c>RegistrationHoldPolicy</c> exists to remove.
    /// </summary>
    [Fact]
    public void The_policy_agrees_with_itself_in_both_forms()
    {
        var plannable = RegistrationHoldPolicy.Plannable.Compile();
        var onHold = RegistrationHoldPolicy.OnHold.Compile();

        var free = NewRegistration();
        var held = NewRegistration();
        held.PlaceOnHold(RegistrationHoldReason.AbsentFromReinscriptionRoll, "Absent.", RaisedOn);

        var released = NewRegistration();
        var toLift = Raise(released, RegistrationHoldReason.AbsentFromReinscriptionRoll, "Absent.");
        released.ReleaseHold(toLift.Id, "Soutenance confirmée.", RaisedOn.AddDays(1))
            .IsSuccess.Should().BeTrue();

        foreach (var registration in new[] { free, held, released })
        {
            plannable(registration).Should().Be(RegistrationHoldPolicy.IsPlannable(registration));
            onHold(registration).Should().Be(RegistrationHoldPolicy.IsOnHold(registration));
            plannable(registration).Should().Be(!onHold(registration), "the two partition the set");
        }

        plannable(free).Should().BeTrue();
        plannable(held).Should().BeFalse();
        plannable(released).Should().BeTrue("a released hold no longer holds");

        // ⚠ And the advisory case, which is the one the expression could most easily get wrong: it
        // must be plannable *and* flagged at the same time.
        var advisory = NewRegistration();
        advisory.PlaceOnHold(RegistrationHoldReason.IncompleteStudentFile, "Fiche à compléter.", RaisedOn);

        plannable(advisory).Should().BeTrue();
        onHold(advisory).Should().BeFalse();
        RegistrationHoldPolicy.IsFlagged(advisory).Should().BeTrue();
    }
}
