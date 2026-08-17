namespace PGSH.Domain.Stages;

/// <summary>
/// How a stage spends the several périodes it occupies on the rotation axis.
///
/// <para>The axis is unaffected either way: a stage taking <c>kₛ</c> columns takes them under both
/// modes, because the crossover arithmetic (<c>T = Σkₛ</c>) belongs to the block, not to one stage,
/// and the group really is present for all <c>kₛ</c> of them. What the mode decides is whether those
/// columns are <b>kₛ different services with kₛ evaluations</b> or <b>one continuous stay in one
/// service with a single evaluation</b>.</para>
///
/// <para>⚠ <b>Neither is the "normal" one.</b> Measured on the imported Access history (2026-08-14),
/// the faculty ran 5ᵉ and 6ᵉ année entirely as <see cref="SingleService"/> — 30,614 of 30,614 and
/// 21,309 of 21,310 stage placements are one service, one mark — while 3ᵉ année genuinely rotated
/// (5,385 placements over two services, 409 over three). A stage belongs to exactly one level
/// (<see cref="Stage.LevelId"/>), so per-stage is already per-promotion and no separate scoping is
/// needed.</para>
/// </summary>
public enum StageRotationMode
{
    /// <summary>
    /// One service per période: the group moves S1 → S2 → … and each stay is evaluated on its own.
    /// The stage note is the mean of the périodes' marks and every one of them must pass.
    /// </summary>
    PerPeriod = 0,

    /// <summary>
    /// One service for the whole run: the group stays put across its <c>kₛ</c> consecutive columns
    /// and is evaluated once. The chef enters one mark instead of kₛ identical ones, and the roll-up
    /// needs no special case — the mean of a single mark is that mark.
    /// </summary>
    SingleService = 1,
}
