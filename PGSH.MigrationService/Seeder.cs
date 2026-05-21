using Bogus;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using PGSH.Infrastructure.Database;

namespace PGSH.MigrationService;

internal static class Seeder
{
    public static async Task SeedAsync(ApplicationDbContext context, ILogger<Worker> logger, CancellationToken ct)
    {
        logger.LogInformation("Starting database seeding...");

        await SeedStaticUsersAsync(context, logger, ct);
        await SeedAcademicYearsAsync(context, logger, ct);
        await SeedLevelsAsync(context, logger, ct);
        await SeedCentersAsync(context, logger, ct);
        await SeedStagesAsync(context, logger, ct);
        await SeedStudentsAsync(context, logger, ct);
        await SeedRegistrationsAsync(context, logger, ct);

        logger.LogInformation("Database seeding completed.");
    }

    // -------------------------------------------------------------------------
    // Static users — always the same IDs and emails so Keycloak email-matching works
    // -------------------------------------------------------------------------

    private static async Task SeedStaticUsersAsync(ApplicationDbContext context, ILogger logger, CancellationToken ct)
    {
        if (!await context.Students.AnyAsync(s => s.Email == "amine.bennani@um5.ac.ma", ct))
        {
            var student = new Student
            {
                Id = new Guid("11111111-1111-1111-1111-111111111111"),
                Email = "amine.bennani@um5.ac.ma",
                FirstName = "Amine",
                LastName = "Bennani",
                CIN = "AE112233",
                CNE = "G135000111",
                Appogee = "22003344",
                AccessGrade = 16.75m,
                AcademicProgram = AcademicProgram.Medecine,
                BacSeries = BacSeries.SVT,
                BacYear = "2023",
                Gender = Gender.Male,
                Status = new Status(CivilStatus.Civil, NationalityStatus.Marocaine),
                PlaceOfBirth = "Rabat",
                DateOfBirth = new DateOnly(2005, 8, 12),
                Address = new Address("Hay Riad, Rabat")
            };
            await context.Students.AddAsync(student, ct);
        }

        if (!await context.Employees.AnyAsync(e => e.Email == "admin.pgsh@um5.ac.ma", ct))
        {
            var admin = new Employee
            {
                Id = new Guid("22222222-2222-2222-2222-222222222222"),
                Email = "admin.pgsh@um5.ac.ma",
                FirstName = "Ahmed",
                LastName = "El Fassi",
                CIN = "BK987654",
                Gender = Gender.Male,
                Status = new Status(CivilStatus.Civil, NationalityStatus.Marocaine),
                Grade = Grade.Administrator,
                Position = Position.Normal,
                WorkPlace = WorkPlace.Fmpr,
                Label = "Responsable Scolarité"
            };
            await context.Employees.AddAsync(admin, ct);
        }

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Static users ready (student: amine.bennani@um5.ac.ma, admin: admin.pgsh@um5.ac.ma).");
    }

    // -------------------------------------------------------------------------
    // Academic years — 3 years of history + current
    // -------------------------------------------------------------------------

    private static async Task SeedAcademicYearsAsync(ApplicationDbContext context, ILogger logger, CancellationToken ct)
    {
        if (await context.AcademicYears.AnyAsync(ct)) return;

        var years = new[]
        {
            new AcademicYear { Label = "2022-2023", StartDate = new DateOnly(2022, 9, 1), EndDate = new DateOnly(2023, 8, 31), IsCurrent = false },
            new AcademicYear { Label = "2023-2024", StartDate = new DateOnly(2023, 9, 1), EndDate = new DateOnly(2024, 8, 31), IsCurrent = false },
            new AcademicYear { Label = "2024-2025", StartDate = new DateOnly(2024, 9, 1), EndDate = new DateOnly(2025, 8, 31), IsCurrent = true  },
        };

        await context.AcademicYears.AddRangeAsync(years, ct);
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} academic years.", years.Length);
    }

    // -------------------------------------------------------------------------
    // Levels — full curriculum for both programs
    // -------------------------------------------------------------------------

    private static async Task SeedLevelsAsync(ApplicationDbContext context, ILogger logger, CancellationToken ct)
    {
        if (await context.Levels.AnyAsync(ct)) return;

        var ordinals = new[] { "1ère", "2ème", "3ème", "4ème", "5ème", "6ème", "7ème" };

        var levels = new List<Level>();

        for (int y = 1; y <= 7; y++)
            levels.Add(new Level { Label = $"{ordinals[y - 1]} Année Médecine",  Year = y, AcademicProgram = AcademicProgram.Medecine  });

        for (int y = 1; y <= 6; y++)
            levels.Add(new Level { Label = $"{ordinals[y - 1]} Année Pharmacie", Year = y, AcademicProgram = AcademicProgram.Pharmacie });

        await context.Levels.AddRangeAsync(levels, ct);
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} levels.", levels.Count);
    }

    // -------------------------------------------------------------------------
    // Centers / Hospitals / Services — 4 real Moroccan medical centers
    // -------------------------------------------------------------------------

    private static async Task SeedCentersAsync(ApplicationDbContext context, ILogger logger, CancellationToken ct)
    {
        if (await context.Centers.AnyAsync(ct)) return;

        var allServiceNames = new[]
        {
            "Cardiologie", "Neurologie", "Pneumologie", "Gastro-entérologie",
            "Urologie", "Traumatologie", "ORL", "Ophtalmologie",
            "Réanimation", "Urgences", "Dermatologie", "Endocrinologie",
            "Néphrologie", "Hématologie", "Oncologie", "Rhumatologie",
            "Pédiatrie", "Gynécologie", "Psychiatrie", "Radiologie"
        };

        var serviceFaker = new Faker<Service>()
            .RuleFor(s => s.Capacity, f => f.Random.WeightedRandom(
                new[] { 8, 16, 24, 32 },
                new[] { 0.15f, 0.40f, 0.35f, 0.10f }))
            .RuleFor(s => s.ServiceType, f => f.PickRandom<ServiceType>())
            .RuleFor(s => s.Description, f => f.Lorem.Sentence());

        var centersData = new[]
        {
            new { Name = "CHU Ibn Sina",   Type = CenterType.CHU,      City = "Rabat",      HospType = HospitalType.CHU,     Hospitals = new[] { "Hôpital Ibn Sina",     "Hôpital des Spécialités", "Hôpital d'Enfants"   } },
            new { Name = "CHU Ibn Rochd",  Type = CenterType.CHU,      City = "Casablanca", HospType = HospitalType.CHU,     Hospitals = new[] { "Hôpital Ibn Rochd",    "Hôpital 20 Août",         "Hôpital Harouchi"    } },
            new { Name = "CHR Hassan II",  Type = CenterType.Regional, City = "Fès",        HospType = HospitalType.Central, Hospitals = new[] { "Hôpital Hassan II",    "Hôpital Al Ghassani"                            } },
            new { Name = "CHP Mohammed V", Type = CenterType.Regional, City = "Meknès",     HospType = HospitalType.Central, Hospitals = new[] { "Hôpital Mohammed V",   "Hôpital Sidi Said"                              } },
        };

        foreach (var cd in centersData)
        {
            var center = new Center { Name = cd.Name, CenterType = cd.Type, City = cd.City };

            foreach (var hospitalName in cd.Hospitals)
            {
                var hospital = new Hospital
                {
                    Name = hospitalName,
                    City = cd.City,
                    HospitalType = cd.HospType,
                    Center = center
                };

                // Pick 10 unique services per hospital from the shared pool
                foreach (var svcName in allServiceNames.OrderBy(_ => Guid.NewGuid()).Take(10))
                {
                    var svc = serviceFaker.Generate();
                    svc.Name = svcName;
                    hospital.services.Add(svc);
                }

                center.Hospitals.Add(hospital);
            }

            await context.Centers.AddAsync(center, ct);
        }

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} medical centers.", centersData.Length);
    }

    // -------------------------------------------------------------------------
    // Stages — realistic named stages per level
    // -------------------------------------------------------------------------

    private static async Task SeedStagesAsync(ApplicationDbContext context, ILogger logger, CancellationToken ct)
    {
        if (await context.Stages.AnyAsync(ct)) return;

        var medLevels  = await context.Levels.Where(l => l.AcademicProgram == AcademicProgram.Medecine ).OrderBy(l => l.Year).ToListAsync(ct);
        var pharmLevels = await context.Levels.Where(l => l.AcademicProgram == AcademicProgram.Pharmacie).OrderBy(l => l.Year).ToListAsync(ct);

        var stageFaker = new Faker<Stage>()
            .RuleFor(s => s.Description,    f => f.Lorem.Paragraph())
            .RuleFor(s => s.Coefficient,    f => f.Random.Int(1, 4))
            .RuleFor(s => s.DurationInDays, f => f.PickRandom(15, 30, 45, 60));

        var medStageNames = new Dictionary<int, string[]>
        {
            [1] = new[] { "Stage de sémiologie",            "Stage d'anatomie clinique"          },
            [2] = new[] { "Stage de physiologie appliquée", "Stage de biologie clinique"         },
            [3] = new[] { "Stage de médecine interne",      "Stage de pédiatrie",                "Stage de cardiologie"        },
            [4] = new[] { "Stage de chirurgie générale",    "Stage de gynécologie-obstétrique",  "Stage d'urgences médicales"  },
            [5] = new[] { "Stage de neurologie",            "Stage de psychiatrie",              "Stage de dermatologie"       },
            [6] = new[] { "Stage de médecine communautaire","Stage de santé publique"            },
            [7] = new[] { "Stage de thèse clinique",        "Stage de médecine légale"           },
        };

        var pharmStageNames = new Dictionary<int, string[]>
        {
            [1] = new[] { "Stage de pharmacognosie",        "Stage de biochimie pharmaceutique"  },
            [2] = new[] { "Stage de chimie analytique",     "Stage de microbiologie appliquée"   },
            [3] = new[] { "Stage de pharmacologie clinique","Stage de toxicologie"               },
            [4] = new[] { "Stage de pharmacie officinale",  "Stage d'industrie pharmaceutique"   },
            [5] = new[] { "Stage de biologie médicale",     "Stage de pharmacie hospitalière"    },
            [6] = new[] { "Stage de thèse pharmaceutique",  "Stage de pharmacovigilance"         },
        };

        var stages = new List<Stage>();

        void BuildStages(List<Level> levels, Dictionary<int, string[]> nameMap)
        {
            foreach (var level in levels)
            {
                var names = nameMap.TryGetValue(level.Year, out var n) ? n : new[] { $"Stage Année {level.Year}" };
                foreach (var name in names)
                {
                    var s = stageFaker.Generate();
                    s.Name    = name;
                    s.LevelId = level.Id;
                    stages.Add(s);
                }
            }
        }

        BuildStages(medLevels,   medStageNames);
        BuildStages(pharmLevels, pharmStageNames);

        await context.Stages.AddRangeAsync(stages, ct);
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} academic stages.", stages.Count);
    }

    // -------------------------------------------------------------------------
    // Students — dynamic bulk; skipped if dynamic ones already exist
    // -------------------------------------------------------------------------

    private static async Task SeedStudentsAsync(ApplicationDbContext context, ILogger logger, CancellationToken ct)
    {
        // Static student already counted; only seed if no dynamic students present
        if (await context.Students.CountAsync(ct) > 1) return;

        var faker = new Faker<Student>("fr")
            .RuleFor(s => s.Id,           _  => Guid.NewGuid())
            .RuleFor(s => s.FirstName,    f  => f.Name.FirstName())
            .RuleFor(s => s.LastName,     f  => f.Name.LastName())
            .RuleFor(s => s.Email,        (f, s) => $"{Slug(s.FirstName)}.{Slug(s.LastName)}{f.UniqueIndex}@um5.ac.ma")
            .RuleFor(s => s.CIN,          f  => f.Random.Replace("??######").ToUpper())
            .RuleFor(s => s.CNE,          f  => f.Random.Replace("?#########").ToUpper())
            .RuleFor(s => s.Appogee,      f  => f.Random.Number(20_000_000, 25_999_999).ToString())
            .RuleFor(s => s.AccessGrade,  f  => Math.Round(f.Random.Decimal(10, 20), 2))
            .RuleFor(s => s.BacSeries,    f  => f.PickRandom<BacSeries>())
            .RuleFor(s => s.BacYear,      f  => f.Random.Int(2018, 2024).ToString())
            .RuleFor(s => s.Gender,       f  => f.PickRandom<Gender>())
            .RuleFor(s => s.Status,       f  => new Status(f.PickRandom<CivilStatus>(), f.PickRandom<NationalityStatus>()))
            .RuleFor(s => s.Address,      f  => new Address(f.Address.FullAddress()))
            .RuleFor(s => s.DateOfBirth,  f  => DateOnly.FromDateTime(f.Date.Past(8, DateTime.Now.AddYears(-18))))
            .RuleFor(s => s.Ranking,      f  => f.Random.Int(1, 500));

        // 30 students per Med level (7 levels = 210), 25 per Pharm level (6 levels = 150)
        var medStudents   = faker.RuleFor(s => s.AcademicProgram, _ => AcademicProgram.Medecine ).Generate(7 * 30);
        var pharmStudents = faker.RuleFor(s => s.AcademicProgram, _ => AcademicProgram.Pharmacie).Generate(6 * 25);

        var all = medStudents.Concat(pharmStudents).ToList();
        await context.Students.AddRangeAsync(all, ct);
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} dynamic students.", all.Count);
    }

    // -------------------------------------------------------------------------
    // Registrations — distribute students evenly across levels for current year
    // -------------------------------------------------------------------------

    private static async Task SeedRegistrationsAsync(ApplicationDbContext context, ILogger logger, CancellationToken ct)
    {
        if (await context.Registrations.AnyAsync(ct)) return;

        var currentYear = await context.AcademicYears.FirstOrDefaultAsync(y => y.IsCurrent, ct);
        if (currentYear is null)
        {
            logger.LogError("Registration seeding skipped: no current academic year found.");
            return;
        }

        var medLevels   = await context.Levels.Where(l => l.AcademicProgram == AcademicProgram.Medecine ).OrderBy(l => l.Year).ToListAsync(ct);
        var pharmLevels = await context.Levels.Where(l => l.AcademicProgram == AcademicProgram.Pharmacie).OrderBy(l => l.Year).ToListAsync(ct);

        var medStudents   = await context.Students.Where(s => s.AcademicProgram == AcademicProgram.Medecine ).ToListAsync(ct);
        var pharmStudents = await context.Students.Where(s => s.AcademicProgram == AcademicProgram.Pharmacie).ToListAsync(ct);

        var registrations = new List<Registration>();
        Distribute(medStudents,   medLevels,   currentYear.Id, registrations);
        Distribute(pharmStudents, pharmLevels, currentYear.Id, registrations);

        await context.Registrations.AddRangeAsync(registrations, ct);
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Registered {Count} students for {Year}.", registrations.Count, currentYear.Label);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void Distribute(List<Student> students, List<Level> levels, int yearId, List<Registration> output)
    {
        if (levels.Count == 0) return;
        for (int i = 0; i < students.Count; i++)
        {
            output.Add(new Registration
            {
                Id               = Guid.NewGuid(),
                StudentId        = students[i].Id,
                LevelId          = levels[i % levels.Count].Id,
                AcademicYearId   = yearId,
                Status           = RegistrationStatus.Active,
                RegistrationDate = DateTime.UtcNow
            });
        }
    }

    private static string Slug(string name) =>
        name.ToLower()
            .Replace(" ", "")
            .Replace("é", "e").Replace("è", "e").Replace("ê", "e")
            .Replace("à", "a").Replace("â", "a")
            .Replace("ô", "o").Replace("ù", "u").Replace("û", "u")
            .Replace("ç", "c").Replace("î", "i").Replace("ï", "i");
}
