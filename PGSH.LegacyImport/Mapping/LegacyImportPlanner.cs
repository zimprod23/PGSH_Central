using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using PGSH.LegacyImport.Legacy;

namespace PGSH.LegacyImport.Mapping;

/// <summary>
/// Turns the legacy tables into the PGSH entity graph, and reports everything it could not carry over.
///
/// Pure: it touches no database and no Access driver, so the whole mapping is testable without the
/// .mdb — which matters because that file holds real personal data and is gitignored.
///
/// The structural problem it solves is that <c>AffectStage</c> is at <em>rotation</em> grain while
/// PGSH has an assignment above the rotation. One <c>(NUMINS, CODEST)</c> pair becomes one
/// <see cref="InternshipAssignment"/>; each of its rows becomes a <see cref="ServicePeriod"/>. Legacy
/// has no planning grid at all, so <c>CohortSlotAssignmentId</c> stays null — which the schema already
/// defines as "recorded outside the published schedule", exactly what history is.
/// </summary>
public sealed class LegacyImportPlanner
{
    private readonly LegacyIdentityMapper _identity;

    // Hospitals whose city was attributed rather than read out of the legacy string — what --review
    // flags, so the check is a handful of rows instead of all sixteen.
    private readonly HashSet<string> _hospitalsWithAssumedCity = new(StringComparer.OrdinalIgnoreCase);

    public LegacyImportPlanner(string emailDomain = LegacyIdentityMapper.DefaultDomain) =>
        _identity = new LegacyIdentityMapper(emailDomain);

    public LegacyImportPlan Plan(LegacyDatabase source)
    {
        var problems = new List<LegacyImportProblem>();

        var hospitals = BuildHospitals(
            source.Services, problems, _hospitalsWithAssumedCity, out var servicesByCode, out var center);
        var levels = BuildLevels();
        var stages = BuildStages(source.Stages, levels, problems, out var stagesByCode);
        var years = BuildYears(source.AcademicYears, out var yearsByLabel);
        var students = BuildStudents(source.Students, source.Registrations, out var studentsByNoOrdre, problems);

        var groups = new List<AcademicGroup>();
        var deduplicated = ResolveDuplicateRegistrations(source.Registrations, source.StageAssignments, problems);
        var registrations = BuildRegistrations(
            deduplicated, yearsByLabel, levels, studentsByNoOrdre, groups, problems,
            out var registrationsByNumIns, out var groupOfRegistration);

        var cohorts = new List<Cohort>();
        var assignments = BuildAssignments(
            source.StageAssignments, registrationsByNumIns, groupOfRegistration,
            stagesByCode, servicesByCode, cohorts, problems);

        var report = new LegacyImportReport(
            Centers: 1,
            Hospitals: hospitals.Count,
            Services: servicesByCode.Count,
            Levels: levels.Count,
            Stages: stages.Count,
            AcademicYears: years.Count,
            AcademicGroups: groups.Count,
            Students: students.Count,
            Registrations: registrations.Count,
            Cohorts: cohorts.Count,
            Assignments: assignments.Count,
            ServicePeriods: assignments.Sum(a => a.ServicePeriods.Count),
            Evaluations: assignments.Sum(a => a.ServicePeriods.Count(p => p.Evaluation is not null)),
            StudentsWithoutCne: students.Count(s => s.CNE is null),
            Problems: problems);

        return new LegacyImportPlan(
            center, hospitals, [.. servicesByCode.Values], levels, stages,
            years, groups, students, registrations, cohorts, assignments, report,
            _hospitalsWithAssumedCity);
    }

    // ─── Reference data ───────────────────────────────────────────────────────

    // Legacy has no Center, but Hospital.CenterId is required. One synthetic root rather than several
    // invented ones: the faculty is FMPR Rabat and every service in the catalogue is its teaching network.
    private static List<Hospital> BuildHospitals(
        IReadOnlyList<LegacyService> source,
        List<LegacyImportProblem> problems,
        HashSet<string> assumedCity,
        out Dictionary<int, Service> servicesByCode,
        out Center center)
    {
        center = new Center
        {
            Name = "Centre Hospitalier Universitaire de Rabat",
            CenterType = CenterType.CHU,
            City = ServiceNameParser.DefaultCity,
        };

        var hospitals = new Dictionary<string, Hospital>(StringComparer.OrdinalIgnoreCase);
        servicesByCode = [];

        foreach (var legacy in source.OrderBy(s => s.CodeS))
        {
            var parsed = ServiceNameParser.Parse(legacy.Name);

            if (!hospitals.TryGetValue(parsed.Hospital.Name, out var hospital))
            {
                hospital = new Hospital
                {
                    Name = parsed.Hospital.Name,
                    City = parsed.Hospital.City,
                    HospitalType = parsed.Hospital.Type,
                    Center = center,
                };
                hospitals[parsed.Hospital.Name] = hospital;

                if (!parsed.Hospital.CityIsStated) assumedCity.Add(parsed.Hospital.Name);
            }

            // The professor named in the string is not an Employee record — creating one would mean
            // inventing a unique email for a person we only know by "Pr.H.Harmouch". The name is kept
            // as the description so the chef can be linked by hand later; ServiceChefId stays null.
            servicesByCode[legacy.CodeS] = new Service
            {
                Name = Truncate(parsed.Name, 100),
                Description = parsed.ChefName is null ? "" : Truncate(ServiceChefSourceNote.Format(parsed.ChefName), 500),
                ServiceType = parsed.Type,
                Hospital = hospital,
            };

            if (parsed.Hospital.Name == ServiceNameParser.UnknownHospital)
                problems.Add(new LegacyImportProblem(
                    LegacyImportProblemKind.ServiceWithoutHospital,
                    $"Service {legacy.CodeS} « {legacy.Name} » ne nomme aucun établissement."));
        }

        return [.. hospitals.Values];
    }

    private static List<Level> BuildLevels() =>
        LevelMapper.AllLevels()
            .Select(key => new Level { Label = key.Label, Year = key.Year, AcademicProgram = key.Program })
            .ToList();

    private static List<Stage> BuildStages(
        IReadOnlyList<LegacyStage> source,
        List<Level> levels,
        List<LegacyImportProblem> problems,
        out Dictionary<int, Stage> byCode)
    {
        byCode = [];
        var stages = new List<Stage>();

        foreach (var legacy in source.OrderBy(s => s.CodeSt))
        {
            var key = LevelMapper.Resolve(legacy.CodeN);
            if (key is null)
            {
                problems.Add(new LegacyImportProblem(
                    LegacyImportProblemKind.UnknownLevelCode,
                    $"Stage {legacy.CodeSt} « {legacy.Name} » porte le niveau inconnu « {legacy.CodeN} » — stage ignoré."));
                continue;
            }

            var stage = new Stage
            {
                Name = Truncate(legacy.Name, 100),
                Coefficient = Math.Max(1, legacy.Coefficient),
                DurationInDays = Math.Max(1, legacy.DurationInDays),
                Level = Find(levels, key),
            };

            stages.Add(stage);
            byCode[legacy.CodeSt] = stage;
        }

        return stages;
    }

    // "2015/2016" is the legacy label; PGSH writes "2015-2016". The academic year runs Sept–Aug.
    private static List<AcademicYear> BuildYears(
        IReadOnlyList<LegacyAcademicYear> source, out Dictionary<string, AcademicYear> byLabel)
    {
        byLabel = new Dictionary<string, AcademicYear>(StringComparer.OrdinalIgnoreCase);
        var years = new List<AcademicYear>();

        foreach (var legacy in source.OrderBy(y => y.Label, StringComparer.Ordinal))
        {
            var parts = legacy.Label.Split('/', '-');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int start)) continue;

            var year = new AcademicYear
            {
                Label = $"{parts[0]}-{parts[1]}",
                StartDate = new DateOnly(start, 9, 1),
                EndDate = new DateOnly(start + 1, 8, 31),
                IsCurrent = false,
            };

            years.Add(year);
            byLabel[legacy.Label] = year;
        }

        // Exactly one current year: `encours` is 'O' on eleven of them, so the latest wins instead.
        // Through the aggregate rather than the flag — the singleton is enforced by a unique index, and
        // MakeCurrent is the one place that knows it.
        if (years.Count > 0) years[^1].MakeCurrent();

        return years;
    }

    // ─── People ───────────────────────────────────────────────────────────────

    private List<Student> BuildStudents(
        IReadOnlyList<LegacyStudent> source,
        IReadOnlyList<LegacyRegistration> registrations,
        out Dictionary<int, Student> byNoOrdre,
        List<LegacyImportProblem> problems)
    {
        // The programme is not on the student, only on the levels they registered at.
        var programByNoOrdre = registrations
            .GroupBy(r => r.NoOrdre)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.AcademicYear, StringComparer.Ordinal)
                      .Select(r => LevelMapper.Resolve(r.LevelCode)?.Program)
                      .FirstOrDefault(p => p is not null) ?? AcademicProgram.Medecine);

        byNoOrdre = [];
        var students = new List<Student>(source.Count);

        // Ordered by NO_ORDRE so the email suffix allocation is reproducible across runs.
        foreach (var legacy in source.OrderBy(s => s.NoOrdre))
        {
            var identity = _identity.Map(legacy);

            var student = new Student
            {
                Id = Guid.NewGuid(),
                FirstName = Truncate(identity.FirstName, 100),
                LastName = Truncate(identity.LastName, 100),
                Email = identity.Email,
                CNE = identity.Cne,
                Appogee = identity.Appogee,
                Gender = identity.Gender,
                CIN = Truncate(legacy.Cin, 20),
                DateOfBirth = legacy.DateOfBirth is { } dob ? DateOnly.FromDateTime(dob) : null,
                PlaceOfBirth = legacy.PlaceOfBirth,
                // Truncated like every other string: Student.BacYear is varchar(10), and one
                // over-long ANNEE_BAC cell would abort the whole 10,203-row students batch.
                BacYear = Truncate(legacy.BacYear, 10),
                AcademicProgram = programByNoOrdre.GetValueOrDefault(legacy.NoOrdre, AcademicProgram.Medecine),
                Status = new Status(
                    string.Equals(legacy.Militaire?.Trim(), "M", StringComparison.OrdinalIgnoreCase)
                        ? CivilStatus.Militaire
                        : CivilStatus.Civil,
                    NationalityStatus.Marocaine),
            };

            if (!string.IsNullOrWhiteSpace(legacy.City) || !string.IsNullOrWhiteSpace(legacy.Address))
                student.Address = new Address(
                    FullAddress: Truncate(legacy.Address, 250),
                    City: Truncate(legacy.City, 100),
                    Street: null, ZIP: null, HouseNumber: null, Country: null);

            students.Add(student);
            byNoOrdre[legacy.NoOrdre] = student;

            if (!programByNoOrdre.ContainsKey(legacy.NoOrdre))
                problems.Add(new LegacyImportProblem(
                    LegacyImportProblemKind.StudentWithoutRegistration,
                    $"Étudiant {legacy.NoOrdre} « {legacy.Nom} » n'a aucune inscription — filière supposée Médecine."));
        }

        return students;
    }

    /// <summary>
    /// PGSH allows one registration per student per academic year — <c>IX_Registration_Student_Year</c>
    /// is unique. Access enforced no such rule and holds 3 pairs that break it (out of 43,608), each a
    /// stale row somebody re-entered rather than corrected: student 21025416 carries both a `MED00`
    /// (retrait, no rotations) and the real `MED04` with seven, in the same year.
    ///
    /// The row with the most rotations wins, breaking ties on the later <c>Numins</c> — the correction
    /// is always the later entry. On the real data that keeps the right record all three times and
    /// loses no rotations at all, because the discarded side has none in every case.
    /// </summary>
    private static List<LegacyRegistration> ResolveDuplicateRegistrations(
        IReadOnlyList<LegacyRegistration> source,
        IReadOnlyList<LegacyStageAssignment> stageRows,
        List<LegacyImportProblem> problems)
    {
        var rotations = stageRows.GroupBy(r => r.NumIns).ToDictionary(g => g.Key, g => g.Count());
        var kept = new List<LegacyRegistration>(source.Count);

        foreach (var duplicates in source.GroupBy(r => (r.NoOrdre, r.AcademicYear)))
        {
            var candidates = duplicates.ToList();
            if (candidates.Count == 1)
            {
                kept.Add(candidates[0]);
                continue;
            }

            var winner = candidates
                .OrderByDescending(r => rotations.GetValueOrDefault(r.NumIns))
                .ThenByDescending(r => r.NumIns)
                .First();

            kept.Add(winner);

            foreach (var dropped in candidates.Where(r => r.NumIns != winner.NumIns))
                problems.Add(new LegacyImportProblem(
                    LegacyImportProblemKind.DuplicateRegistration,
                    $"Étudiant {dropped.NoOrdre} a deux inscriptions en {dropped.AcademicYear} : "
                    + $"{dropped.NumIns} ({dropped.LevelCode}, {rotations.GetValueOrDefault(dropped.NumIns)} rotation(s)) "
                    + $"écartée au profit de {winner.NumIns} ({winner.LevelCode}, "
                    + $"{rotations.GetValueOrDefault(winner.NumIns)} rotation(s))."));
        }

        return kept;
    }

    private static List<Registration> BuildRegistrations(
        IReadOnlyList<LegacyRegistration> source,
        Dictionary<string, AcademicYear> yearsByLabel,
        List<Level> levels,
        Dictionary<int, Student> studentsByNoOrdre,
        List<AcademicGroup> groups,
        List<LegacyImportProblem> problems,
        out Dictionary<int, Registration> byNumIns,
        out Dictionary<int, AcademicGroup> groupOfRegistration)
    {
        byNumIns = [];
        groupOfRegistration = [];
        var registrations = new List<Registration>(source.Count);
        // Keyed on (year, promotion, number). The promotion is (année × filière) rather than the whole
        // LevelKey: several legacy codes resolve to one level with different labels, and Find matches
        // them the same way — keying on the record would split one roster in two.
        var groupIndex = new Dictionary<(string Year, int LevelYear, AcademicProgram Program, int Number), AcademicGroup>();

        foreach (var legacy in source.OrderBy(r => r.NumIns))
        {
            if (!yearsByLabel.TryGetValue(legacy.AcademicYear, out var year))
            {
                problems.Add(new LegacyImportProblem(
                    LegacyImportProblemKind.UnknownAcademicYear,
                    $"Inscription {legacy.NumIns} porte l'année inconnue « {legacy.AcademicYear} » — ignorée."));
                continue;
            }

            var key = LevelMapper.Resolve(legacy.LevelCode);
            if (key is null)
            {
                problems.Add(new LegacyImportProblem(
                    LegacyImportProblemKind.UnknownLevelCode,
                    $"Inscription {legacy.NumIns} porte le niveau inconnu « {legacy.LevelCode} » — ignorée."));
                continue;
            }

            if (!studentsByNoOrdre.TryGetValue(legacy.NoOrdre, out var student))
            {
                problems.Add(new LegacyImportProblem(
                    LegacyImportProblemKind.UnknownStudent,
                    $"Inscription {legacy.NumIns} référence l'étudiant absent {legacy.NoOrdre} — ignorée."));
                continue;
            }

            // 17,529 registrations carry no group; only 67 of those have rotations. Group 0 keeps the
            // FK satisfiable without pretending they belonged to a real group.
            int number = legacy.GroupNumber is > 0 ? legacy.GroupNumber.Value : 0;

            // ⚠ The promotion is part of a roster's identity, because GROUPE_STG restarts at 1 for
            // each of them: the 3rd year runs 1-80 and the 5th year 1-60 in the same année. Keyed on
            // (year, number) alone — as this was until 2026-08-13 — those two numberings collapse
            // into one set of rows, and 80 of the 100 rosters of 2025-2026 ended up carrying four or
            // five promotions at once. That is not a labelling problem: a roster is the unit
            // GroupScheduleConflictGuard forbids from being in two places, so the 3rd year's spring
            // placements then refused the 5th year's, and its répartition came out with two of its
            // nine columns filled.
            //
            // « Non réparti » stays one bucket per year: it belongs to no promotion by definition,
            // and splitting it would invent a roster per level that nobody is a member of.
            var indexKey = number == 0
                ? (legacy.AcademicYear, 0, default(AcademicProgram), 0)
                : (legacy.AcademicYear, key.Year, key.Program, number);

            if (!groupIndex.TryGetValue(indexKey, out var group))
            {
                var level = number == 0 ? null : Find(levels, key);
                group = new AcademicGroup
                {
                    Label = number == 0 ? "Non réparti" : $"Groupe {number} — {level!.Label}",
                    GroupNumber = number,
                    AcademicYear = year,
                    Level = level,
                };
                groupIndex[indexKey] = group;
                groups.Add(group);
            }

            var registration = new Registration
            {
                Id = Guid.NewGuid(),
                Student = student,
                StudentId = student.Id,
                AcademicYear = year,
                Level = Find(levels, key),
                AcademicGroup = group,
                Status = MapStatus(legacy),
                RegistrationDate = new DateTime(year.StartDate, TimeOnly.MinValue, DateTimeKind.Utc),
            };

            registrations.Add(registration);
            byNumIns[legacy.NumIns] = registration;
            groupOfRegistration[legacy.NumIns] = group;
        }

        return registrations;
    }

    /// <summary>
    /// Whether the student passed the <em>year</em> depends on their subject marks, which live in the
    /// half of the legacy database that is out of scope. So every live registration is imported
    /// <see cref="RegistrationStatus.Active"/> rather than inventing a pass or a fail; only the
    /// withdrawals are knowable here.
    /// </summary>
    private static RegistrationStatus MapStatus(LegacyRegistration legacy) =>
        LevelMapper.IsWithdrawal(legacy.LevelCode) || string.Equals(legacy.Statut?.Trim(), "T", StringComparison.OrdinalIgnoreCase)
            ? RegistrationStatus.Withdrawn
            : RegistrationStatus.Active;

    // ─── Stages served ────────────────────────────────────────────────────────

    private static List<InternshipAssignment> BuildAssignments(
        IReadOnlyList<LegacyStageAssignment> source,
        Dictionary<int, Registration> registrationsByNumIns,
        Dictionary<int, AcademicGroup> groupOfRegistration,
        Dictionary<int, Stage> stagesByCode,
        Dictionary<int, Service> servicesByCode,
        List<Cohort> cohorts,
        List<LegacyImportProblem> problems)
    {
        var cohortIndex = new Dictionary<(Stage, AcademicGroup), Cohort>();
        var assignments = new Dictionary<(int NumIns, int CodeSt), InternshipAssignment>();
        var ordered = new List<InternshipAssignment>();

        // Marks are known while reading the rows but can only be submitted once every rotation of the
        // assignment is closed, so they wait here keyed by the period they belong to.
        var marks = new Dictionary<Guid, decimal>();

        // One assignment per (NUMINS, CODEST); the rows of that pair are its rotations.
        foreach (var row in source.OrderBy(r => r.NumIns).ThenBy(r => r.CodeSt).ThenBy(r => r.Per1, StringComparer.Ordinal))
        {
            if (!registrationsByNumIns.TryGetValue(row.NumIns, out var registration)) continue;
            if (!stagesByCode.TryGetValue(row.CodeSt, out var stage)) continue;
            if (!servicesByCode.TryGetValue(row.CodeS, out var service)) continue;

            var windows = LegacyPeriodParser.Parse(row.Per1, row.Per2);
            if (windows.Unreadable)
            {
                problems.Add(new LegacyImportProblem(
                    LegacyImportProblemKind.UnreadablePeriod,
                    $"Inscription {row.NumIns}, stage {row.CodeSt} : dates illisibles « {row.Per1} » / « {row.Per2} » — rotation ignorée."));
                continue;
            }

            var key = (row.NumIns, row.CodeSt);
            if (!assignments.TryGetValue(key, out var assignment))
            {
                var group = groupOfRegistration[row.NumIns];
                var cohortKey = (stage, group);
                if (!cohortIndex.TryGetValue(cohortKey, out var cohort))
                {
                    cohort = new Cohort
                    {
                        Label = Truncate($"{stage.Name} — {group.Label}", 100),
                        Stage = stage,
                        AcademicGroup = group,
                    };
                    cohortIndex[cohortKey] = cohort;
                    cohorts.Add(cohort);
                }

                assignment = new InternshipAssignment
                {
                    Id = Guid.NewGuid(),
                    Registration = registration,
                    RegistrationId = registration.Id,
                    Cohort = cohort,
                };
                assignment.MembershipHistory.Add(new CohortMembership
                {
                    Id = Guid.NewGuid(),
                    InternshipAssignmentId = assignment.Id,
                    Cohort = cohort,
                    StartDate = windows.Windows[0].Start,
                });

                assignments[key] = assignment;
                ordered.Add(assignment);
            }

            // A split rotation becomes two periods carrying the same mark: the legacy row holds one
            // note for the whole interrupted stage, and leaving the second half ungraded would stop
            // the assignment ever rolling up to a verdict.
            foreach (var window in windows.Windows)
            {
                var period = new ServicePeriod
                {
                    Id = Guid.NewGuid(),
                    InternshipAssignmentId = assignment.Id,
                    Service = service,
                    CohortSlotAssignmentId = null,
                    StartDate = window.Start,
                    EndDate = window.End,
                };

                assignment.ServicePeriods.Add(period);

                // -1 and null are the legacy "never graded" sentinels. 0 is a real failing mark:
                // the threshold is 10, and everything below it is simply not validated.
                if (row.Note is { } note && note >= 0)
                    marks[period.Id] = Math.Clamp(note, 0m, 20m);
            }

            if (windows.IsSplit)
                problems.Add(new LegacyImportProblem(
                    LegacyImportProblemKind.SplitPeriod,
                    $"Inscription {row.NumIns}, stage {row.CodeSt} : rotation interrompue « {row.Per2} » — importée en {windows.Windows.Count} périodes."));

            // The parser detects a window with no end; without this it was computed and thrown away,
            // which is exactly the silent guess its own comment rules out.
            if (windows.DanglingDate)
                problems.Add(new LegacyImportProblem(
                    LegacyImportProblemKind.DanglingPeriodDate,
                    $"Inscription {row.NumIns}, stage {row.CodeSt} : date sans fin dans « {row.Per1} » / « {row.Per2} » — ignorée."));
        }

        foreach (var assignment in ordered)
            Close(assignment, marks);

        return ordered;
    }

    /// <summary>
    /// Replays the real lifecycle — start, close each rotation, submit the marks, ratify — instead of
    /// back-filling <c>Status</c>/<c>FinalScore</c>/<c>Result</c>. Those have private setters precisely
    /// so a verdict can only come from marks the domain rolled up through <c>StageScoring</c>, and an
    /// import that wrote them directly would be the one place in the system where that stops being true.
    /// </summary>
    private static void Close(InternshipAssignment assignment, Dictionary<Guid, decimal> marks)
    {
        assignment.Start();

        foreach (var period in assignment.ServicePeriods.ToList())
            assignment.CompletePeriod(period.Id);

        foreach (var period in assignment.ServicePeriods.OrderBy(p => p.StartDate).ToList())
        {
            if (!marks.TryGetValue(period.Id, out decimal mark)) continue;

            assignment.SubmitEvaluation(period.Id, new ServiceEvaluation
            {
                ServicePeriodId = period.Id,
                Mode = EvaluationMode.Numeric,
                TotalScore = mark,
            });
        }

        // Historical stages are closed business. Ratifying the fully-marked ones keeps 100k finished
        // rotations out of the administration's "to ratify" worklist; Validate() is a workflow act, so
        // a failed stage stays failed — it records an official failure rather than becoming a pass.
        if (assignment.Status == InternshipStatus.Evaluated)
            assignment.Validate();
    }

    private static Level Find(List<Level> levels, LevelKey key) =>
        levels.First(l => l.Year == key.Year && l.AcademicProgram == key.Program);

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
