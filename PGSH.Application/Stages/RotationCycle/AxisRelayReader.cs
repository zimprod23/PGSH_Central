using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Calendar;
using PGSH.Domain.Calendar;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>Ce qu'un recalcul ferait d'une rotation publiée.</summary>
internal enum AxisRelayAction
{
    /// <summary>Sa fenêtre entière se déplace — rien n'a encore commencé ni été enregistré.</summary>
    Move,

    /// <summary>Son début reste, sa fin est repoussée : elle a commencé, on ne réécrit pas cela.</summary>
    Extend,

    /// <summary>
    /// Son début reste, sa fin revient en arrière — le retour d'une fenêtre révoquée. ⚠ Distinct de
    /// <see cref="Extend"/> parce que la garde ne l'est pas : raccourcir peut orpheliner une journée
    /// pointée, allonger ne le peut pas.
    /// </summary>
    Shorten,

    /// <summary>Rien n'est possible : close, notée, pointée, ou interrompue.</summary>
    Blocked,
}

internal sealed record AxisRelayPeriodChange(
    Guid ServicePeriodId,
    Guid InternshipAssignmentId,
    DateOnly FromStart, DateOnly FromEnd,
    DateOnly ToStart, DateOnly ToEnd,
    AxisRelayAction Action);

/// <param name="ColumnLength">
/// La longueur d'une colonne en jours ouvrables, <b>dérivée</b> et non demandée — voir
/// <see cref="AxisRelayReader"/>.
/// </param>
/// <param name="ColumnsAgreeingOnLength">
/// Combien de colonnes portent effectivement cette longueur. ⚠ Séparé du total à dessein : « 6 sur 6 »
/// et « 4 sur 6 » appellent des gestes différents, et un seul chiffre les confondrait. Une minorité
/// discordante est normale (ce sont les colonnes que la fenêtre ampute) ; une majorité discordante
/// veut dire que l'axe n'a pas été posé en jours ouvrables et que le recalcul n'est pas l'outil.
/// </param>
/// <param name="PeriodsToShorten">
/// Celles dont la fin revient en arrière — l'axe revient d'une fenêtre révoquée. Comptées à part de
/// <paramref name="PeriodsToExtend"/> : ce sont deux directions, et l'opérateur doit voir laquelle
/// il applique.
/// </param>
/// <param name="PeriodsBlocked">
/// ⚠ Celles que l'acte ne peut pas rattraper. Elles ne font pas échouer le recalcul — une note est un
/// fait, et le reste de la promotion a quand même besoin d'être poussé — mais elles se comptent, sinon
/// « 4 010 rotations déplacées » laisserait croire que tout le monde a été rattrapé.
/// </param>
internal sealed record AxisRelayReport(
    int AcademicYearId,
    int LevelId,
    int ColumnLength,
    int ColumnsAgreeingOnLength,
    int ColumnCount,
    int FromPeriodNumber,
    IReadOnlyList<RelaidColumn> Columns,
    int SlotsToRelay,
    int PeriodsToMove,
    int PeriodsToExtend,
    int PeriodsToShorten,
    int PeriodsBlocked,
    int WorkingDaysChanged,
    DateOnly AxisEndsOn,
    AxisRelayCrossings Crossings,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Ce que l'opérateur confirme côté écriture.</summary>
    public int PeriodsAffected => PeriodsToMove + PeriodsToExtend + PeriodsToShorten;

    /// <summary>
    /// L'axe revient-il en arrière ? ⚠ Lu du <b>signe</b> des jours, pas d'un drapeau séparé : deux
    /// sources pour un même fait finissent par se contredire.
    /// </summary>
    public bool IsRollingBack => WorkingDaysChanged < 0;
}

/// <summary>
/// Assemble le recalcul d'un axe depuis le magasin : lit les colonnes, <b>dérive</b> leur longueur,
/// passe l'arithmétique à <see cref="AxisRelayPlanner"/>, puis traduit le déplacement des colonnes en
/// ce qu'il faut faire à chaque rotation publiée.
/// </summary>
/// <remarks>
/// <para>⚠ <b>La longueur d'une colonne est dérivée, jamais demandée à l'appelant.</b> Elle ne se lit
/// pas non plus sur les dates courantes : ces dates sont précisément ce que la fenêtre a abîmé, donc
/// une colonne amputée de 5 jours se reposerait à 17 et la perte deviendrait définitive. Elle se
/// mesure sur le <b>calendrier de la promotion tel qu'il est</b>, en prenant la valeur que les
/// colonnes portent le plus souvent : une colonne posée en enjambant une fenêtre déjà déclarée tient
/// toujours ses <c>n</c> jours (c'est la définition de <c>Lay</c>), seules celles qu'une fenêtre
/// <i>postérieure</i> traverse en tiennent moins. Mesuré sur la base vivante le 19/09/2026, les six
/// colonnes de la 4ᵉ MED portent 22 jours ouvrables pour 30 à 35 jours calendaires — la longueur en
/// jours ouvrables est donc bien la constante, et le calendaire ne l'est pas.</para>
///
/// <para>⚠ <b>La fenêtre d'un séjour est le min/max de ses cellules, jamais un décalage.</b> Sous
/// <c>StageRotationMode.SingleService</c> une période couvre une suite de colonnes : lui ajouter le
/// delta de l'une d'elles écrirait un séjour plus long que celui qui a lieu. C'est la règle de
/// <c>CohortStayFolder</c> à la publication et de <c>PublishedPeriodShifter</c> au déplacement, et il
/// n'y en a pas de troisième.</para>
///
/// <para>⚠ <b>Une rotation bloquée n'arrête pas l'acte.</b> Une note est un fait ; le reste de la
/// promotion a quand même besoin d'être poussé. Elles sont comptées et rapportées — un acte qui
/// épargne des lignes doit dire combien, sinon ce qui a été rattrapé et ce qui ne l'a pas été se
/// lisent pareil.</para>
/// </remarks>
internal sealed class AxisRelayReader(
    IApplicationDbContext dbContext,
    WorkingDayProvider workingDays,
    AxisRelayCrossingReader crossings)
{
    public async Task<Result<AxisRelayReport>> ReadAsync(
        int academicYearId, int levelId, int? fromPeriodNumber, DateOnly today, CancellationToken ct)
    {
        var slots = await SlotsQuery(dbContext, academicYearId, levelId).AsNoTracking().ToListAsync(ct);

        if (slots.Count == 0)
            return Result.Failure<AxisRelayReport>(RotationCycleErrors.NoColumnsToRelay);

        var warnings = new List<string>();

        var columns = slots
            .GroupBy(s => s.PeriodNumber)
            .OrderBy(g => g.Key)
            .Select(g => new AxisColumn(
                g.Key,
                g.Min(s => s.StartDate),
                g.Max(s => s.EndDate),
                g.Any(s => s.Source == SlotSource.MovedByHand)))
            .ToList();

        // ⚠ Une colonne est UNE date : RotationCyclePlanner pose tous les stages d'un bloc depuis la
        // même liste de fenêtres. Si deux stages divergent sur un même numéro, l'axe a été édité
        // ailleurs et le recalcul le dirait en écrasant la divergence sans l'avoir annoncée.
        var drifted = slots
            .GroupBy(s => s.PeriodNumber)
            .Where(g => g.Select(s => (s.StartDate, s.EndDate)).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (drifted.Count > 0)
            warnings.Add(
                $"{drifted.Count} colonne(s) ne portent pas les mêmes dates d'un stage à l'autre "
                + $"(P{string.Join(", P", drifted)}) : le recalcul les alignera sur la fenêtre la plus "
                + "large. Vérifiez qu'aucune n'a été éditée stage par stage.");

        var calendar = await workingDays.ForPromotionAsync(academicYearId, levelId, ct);

        var (length, agreeing) = DeriveColumnLength(columns, calendar);
        if (length < 1)
            return Result.Failure<AxisRelayReport>(RotationCycleErrors.ColumnLengthNotDerivable);

        if (agreeing * 2 <= columns.Count)
            warnings.Add(
                $"Seules {agreeing} colonnes sur {columns.Count} portent {length} jours ouvrables. Un axe "
                + "posé en semaines ou en mois ne se recalcule pas en jours ouvrables sans changer sa "
                + "forme — vérifiez la longueur avant d'appliquer.");

        int from = fromPeriodNumber
            ?? AxisRelayPlanner.FirstDivergentColumn(columns, calendar, length)
            ?? columns[^1].Number + 1;

        if (from > columns[^1].Number)
            return Result.Failure<AxisRelayReport>(RotationCycleErrors.NothingToRecover);

        var plan = AxisRelayPlanner.Plan(columns, calendar, length, from);
        if (plan.IsFailure)
            return Result.Failure<AxisRelayReport>(plan.Error);

        var moved = plan.Value.Columns
            .Where(c => c.Moved)
            .ToDictionary(c => c.Number, c => (c.ToStart, c.ToEnd));

        var periods = await PlanPeriodsAsync(academicYearId, levelId, columns, moved, today, ct);

        // ⚠ Un rapport, jamais une garde (règle du 12/09/2026) : cette faculté dépasse la capacité
        // de ses services dans la plupart des cas, et rien ici ne refuse. Ce que personne ne pouvait
        // voir jusqu'ici est où la promotion poussée arrive sur une autre.
        var crossed = await crossings.ReadAsync(levelId, moved, ct);

        if (crossed.ServicesWherePeakRises > 0)
            warnings.Add(
                $"{crossed.ServicesWherePeakRises} service(s) sur {crossed.ServicesExamined} porteront "
                + "plus de monde qu'aujourd'hui à leur heure de pointe une fois l'axe poussé. Ce n'est "
                + "pas un refus — le dépassement est le fonctionnement de cette faculté — mais les "
                + "cohortes concernées croiseront d'autres promotions.");

        // ⚠ La moitié qu'un compte de pics seul ne dit pas : allonger une colonne qui chevauchait
        // déjà celle d'une autre promotion ne fait pas monter la charge, elle la fait durer. Sans
        // cette phrase, « aucun service plus chargé » se lirait « rien ne change ».
        if (crossed.ServicesWhereBusyLasts > 0)
            warnings.Add(
                $"{crossed.ServicesWhereBusyLasts} service(s) ne porteront pas plus de monde, mais "
                + "resteront à leur charge de pointe plus longtemps qu'aujourd'hui.");

        return new AxisRelayReport(
            academicYearId,
            levelId,
            length,
            agreeing,
            columns.Count,
            from,
            plan.Value.Columns,
            SlotsToRelay: slots.Count(s => moved.ContainsKey(s.PeriodNumber)),
            PeriodsToMove: periods.Count(p => p.Action == AxisRelayAction.Move),
            PeriodsToExtend: periods.Count(p => p.Action == AxisRelayAction.Extend),
            PeriodsToShorten: periods.Count(p => p.Action == AxisRelayAction.Shorten),
            PeriodsBlocked: periods.Count(p => p.Action == AxisRelayAction.Blocked),
            plan.Value.WorkingDaysChanged,
            plan.Value.AxisEndsOn,
            crossed,
            warnings);
    }

    /// <summary>
    /// Ce qu'il faut faire à chaque rotation publiée, une fois les colonnes reposées.
    /// </summary>
    public async Task<IReadOnlyList<AxisRelayPeriodChange>> PlanPeriodsAsync(
        int academicYearId,
        int levelId,
        IReadOnlyList<AxisColumn> columns,
        IReadOnlyDictionary<int, (DateOnly Start, DateOnly End)> movedColumns,
        DateOnly today,
        CancellationToken ct)
    {
        if (movedColumns.Count == 0)
            return [];

        var coverage = await CoverageQuery(dbContext, academicYearId, levelId).AsNoTracking().ToListAsync(ct);

        // Les colonnes que le recalcul ne touche pas gardent leurs dates : une période à cheval sur
        // une colonne déplacée et une colonne intacte tire sa fenêtre des deux.
        var windowOf = columns.ToDictionary(
            c => c.Number,
            c => movedColumns.TryGetValue(c.Number, out var w) ? w : (c.StartDate, c.EndDate));

        var changes = new List<AxisRelayPeriodChange>();

        foreach (var covered in coverage.GroupBy(c => c.ServicePeriodId))
        {
            if (!covered.Any(c => movedColumns.ContainsKey(c.PeriodNumber)))
                continue;

            var first = covered.First();

            // ⚠ min/max sur les cellules couvertes, jamais « ajouter le delta » : sous SingleService
            // une période couvre une suite de colonnes, et déplacer celle du milieu ne bouge pas le
            // séjour. Même règle que CohortStayFolder et PublishedPeriodShifter.
            var spans = covered
                .Where(c => windowOf.ContainsKey(c.PeriodNumber))
                .Select(c => windowOf[c.PeriodNumber])
                .ToList();

            if (spans.Count == 0)
                continue;

            var toStart = spans.Min(w => w.Item1);
            var toEnd = spans.Max(w => w.Item2);

            if (toStart == first.StartDate && toEnd == first.EndDate)
                continue;

            changes.Add(new AxisRelayPeriodChange(
                covered.Key, first.InternshipAssignmentId,
                first.StartDate, first.EndDate,
                toStart, toEnd,
                Classify(first, toStart, toEnd, today)));
        }

        return changes;
    }

    /// <summary>
    /// ⚠ <b>La règle datée, pas <c>Movable</c>.</b> <c>Start()</c> est un <i>whole-student start</i>,
    /// donc 305 périodes de la 4ᵉ MED portent <c>IsStarted</c> avec une fenêtre entièrement à venir
    /// (mesuré 19/09/2026) : sous <c>Movable</c> le recalcul refuserait de pousser exactement les
    /// rotations futures qu'il existe pour pousser.
    /// </summary>
    private static AxisRelayAction Classify(
        CoverageRow row, DateOnly toStart, DateOnly toEnd, DateOnly today)
    {
        if (ServicePeriodLifecycle.IsMovableOn(
                row.IsComplete, row.IsInterrupted, row.HasEvaluation,
                row.AttendanceCount > 0, row.StartDate, today))
            return AxisRelayAction.Move;

        // Le début ne peut plus bouger. Ne reste que sa fin, et le sens compte : allonger ne peut
        // rien orpheliner, raccourcir le peut — d'où deux actes et deux gardes.
        bool startHolds = toStart == row.StartDate;
        bool open = ServicePeriodLifecycle.IsExtendable(
            row.IsComplete, row.IsInterrupted, row.HasEvaluation);

        if (!startHolds || !open)
            return AxisRelayAction.Blocked;

        if (toEnd >= row.EndDate)
            return AxisRelayAction.Extend;

        // ⚠ Une journée pointée après la nouvelle fin se retrouverait hors de la fenêtre. L'agrégat
        // refuse, et le rapport doit le dire *avant* plutôt que de laisser l'acte échouer.
        return row.AttendanceCount > 0
            ? AxisRelayAction.Blocked
            : AxisRelayAction.Shorten;
    }

    /// <summary>
    /// La longueur que les colonnes portent le plus souvent, et combien la portent.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Le mode, pas la moyenne ni le maximum.</b> La moyenne serait tirée vers le bas par les
    /// colonnes amputées — c'est-à-dire exactement par ce que l'acte vient réparer. Le maximum serait
    /// juste dans le cas courant et faux dès qu'une colonne a été rallongée à la main. Le mode dit ce
    /// que l'axe <i>est</i>, et <c>agreeing</c> dit à quel point on peut s'y fier.
    /// </remarks>
    private static (int Length, int Agreeing) DeriveColumnLength(
        IReadOnlyList<AxisColumn> columns, WorkingDayCalendar calendar)
    {
        var counts = columns
            .Select(c => calendar.Count(c.StartDate, c.EndDate))
            .Where(n => n > 0)
            .GroupBy(n => n)
            .Select(g => (Length: g.Key, Agreeing: g.Count()))
            .OrderByDescending(x => x.Agreeing)
            .ThenByDescending(x => x.Length)
            .ToList();

        return counts.Count == 0 ? (0, 0) : counts[0];
    }

    /// <summary>
    /// Les créneaux de l'axe d'une promotion. ⚠ Par <c>Stage.LevelId</c> <b>et</b> l'année : une
    /// promotion est la paire, et le niveau seul ramènerait tous les axes que ce niveau a jamais eus.
    /// </summary>
    internal static IQueryable<SlotRow> SlotsQuery(
        IApplicationDbContext dbContext, int academicYearId, int levelId) =>
        dbContext.StageSlots
            .Where(s => s.Stage.LevelId == levelId && s.AcademicYearId == academicYearId)
            .OrderBy(s => s.PeriodNumber)
            .Select(s => new SlotRow(
                s.Id, s.StageId, s.PeriodNumber, s.StartDate, s.EndDate, s.Source));

    /// <summary>
    /// Une ligne par (période, cellule couverte) — <b>plate et au premier niveau</b>, pliée en mémoire
    /// ensuite.
    /// </summary>
    /// <remarks>
    /// ⚠ Une sous-requête de collection dans une projection est la forme que Npgsql refuse, et elle a
    /// déjà tué le macro-plan une fois avec toute la suite au vert. <c>Attendance.Count</c> est un
    /// agrégat scalaire, pas une collection projetée : c'est traduisible, et
    /// <c>SqlTranslationTests</c> le vérifie.
    /// </remarks>
    internal static IQueryable<CoverageRow> CoverageQuery(
        IApplicationDbContext dbContext, int academicYearId, int levelId) =>
        dbContext.ServicePeriodSlotCoverage
            .Where(c => c.CohortSlotAssignment.StageSlot.Stage.LevelId == levelId
                     && c.CohortSlotAssignment.StageSlot.AcademicYearId == academicYearId)
            .Select(c => new CoverageRow(
                c.ServicePeriodId,
                c.ServicePeriod.InternshipAssignmentId,
                c.CohortSlotAssignment.StageSlot.PeriodNumber,
                c.ServicePeriod.StartDate,
                c.ServicePeriod.EndDate,
                c.ServicePeriod.IsComplete,
                c.ServicePeriod.IsInterrupted,
                c.ServicePeriod.Evaluation != null,
                c.ServicePeriod.Attendance.Count));

    internal sealed record SlotRow(
        int Id, int StageId, int PeriodNumber, DateOnly StartDate, DateOnly EndDate, SlotSource Source);

    internal sealed record CoverageRow(
        Guid ServicePeriodId,
        Guid InternshipAssignmentId,
        int PeriodNumber,
        DateOnly StartDate,
        DateOnly EndDate,
        bool IsComplete,
        bool IsInterrupted,
        bool HasEvaluation,
        int AttendanceCount);
}
