using Bogus;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using PGSH.Infrastructure.Database;

namespace PGSH.MigrationService
{
    internal class Seeder
    {
        

        public static async Task SeedAsync(ApplicationDbContext context, ILogger<Worker> logger, CancellationToken cancellationToken)
        {
            logger.LogInformation("Starting database seeding...");

            // 1. Independent Entities (Static Data)
            await SeedLevelsAsync(context, logger, cancellationToken);
            await SeedAcademicYearsAsync(context, logger, cancellationToken);
            await SeedCentersHospitalsServicesAsync(context, logger, cancellationToken);
            await SeedStagesAsync(context, logger, cancellationToken); // Now uses Bogus as requested

            // 2. Entities with Dependencies
            await SeedStudentsAsync(context, logger, cancellationToken); // Needs nothing

            // 3. Junction Entities
            // This one was crashing because it depends on Levels and Years being done above!
            await SeedRegistrationsAsync(context, logger, cancellationToken);
            //await SeedCohortsAndAssignmentsAsync(context, logger, cancellationToken);

            logger.LogInformation("Database seeding completed.");
        }

        private static async Task SeedAcademicYearsAsync(
            ApplicationDbContext context,
            ILogger logger,
            CancellationToken ct)
        {
            if (await context.AcademicYears.AnyAsync(ct)) return;

            var academicYear = new AcademicYear
            {
                Label = "2024-2025",
                StartDate = new DateOnly(2024, 09, 01),
                EndDate = new DateOnly(2025, 08, 31),
                IsCurrent = true
            };

            context.AcademicYears.Add(academicYear);
            await context.SaveChangesAsync(ct);

            logger.LogInformation("Seeded Academic Year: {Label}", academicYear.Label);
        }

        private static async Task SeedStudentsAsync(ApplicationDbContext context, ILogger<Worker> logger, CancellationToken ct)
        {
            if (await context.Students.AnyAsync(ct)) return;

            // 1. The "Golden" Static Student
            var staticStudent = new Student
            {
                Id = Guid.NewGuid(),
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
                DateOfBirth = new DateOnly(2005, 08, 12),
                Address = new Address("Hay Riad, Rabat")
            };

            // 2. Configure Bogus for Mass Production
            var faker = new Faker<Student>()
                .RuleFor(s => s.Id, f => Guid.NewGuid())
                .RuleFor(s => s.FirstName, f => f.Name.FirstName())
                .RuleFor(s => s.LastName, f => f.Name.LastName())
                .RuleFor(s => s.Email, (f, s) => $"{s.FirstName.ToLower()}.{s.LastName.ToLower()}{f.UniqueIndex}@um5.ac.ma")
                .RuleFor(s => s.CIN, f => f.Random.Replace("??######"))
                .RuleFor(s => s.CNE, f => f.Random.Replace("?#########"))
                .RuleFor(s => s.Appogee, f => f.Random.Number(20000000, 25000000).ToString())
                .RuleFor(s => s.AccessGrade, f => f.Random.Decimal(10, 20))
                .RuleFor(s => s.BacSeries, f => f.PickRandom<BacSeries>())
                .RuleFor(s => s.BacYear, f => f.Random.Int(2020, 2024).ToString())
                .RuleFor(s => s.Gender, f => f.PickRandom<Gender>())
                .RuleFor(s => s.Status, f => new Status(f.PickRandom<CivilStatus>(), f.PickRandom<NationalityStatus>()))
                .RuleFor(s => s.Address, f => new Address(f.Address.FullAddress()))
                .RuleFor(s => s.DateOfBirth, f => DateOnly.FromDateTime(f.Date.Past(5, DateTime.Now.AddYears(-18))));

            // 3. Generate Specific Counts
            // Amine is 1. We need 399 more Med and 3100 Pharm.
            var medStudents = faker.RuleFor(s => s.AcademicProgram, _ => AcademicProgram.Medecine).Generate(399);
            var pharmStudents = faker.RuleFor(s => s.AcademicProgram, _ => AcademicProgram.Pharmacie).Generate(3100);

            var allStudents = new List<Student> { staticStudent };
            allStudents.AddRange(medStudents);
            allStudents.AddRange(pharmStudents);

            await context.Students.AddRangeAsync(allStudents, ct);
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Seeded 3500 Students (400 Med, 3100 Pharm)");
        }
        private static async Task SeedLevelsAsync(
            ApplicationDbContext context,
            ILogger logger,
            CancellationToken ct)
        {
            if (await context.Levels.AnyAsync(ct)) return;

            var levels = new[]
            {
                new Level { Label = "1ère Année Médecine", Year = 1, AcademicProgram = AcademicProgram.Medecine },
                new Level { Label = "2ème Année Médecine", Year = 2, AcademicProgram = AcademicProgram.Medecine },
                new Level { Label = "Pharmacie – Année 1", Year = 1, AcademicProgram = AcademicProgram.Pharmacie },
             };

            await context.Levels.AddRangeAsync(levels, ct);
            await context.SaveChangesAsync(ct);

            logger.LogInformation("Seeded {Count} Levels", levels.Length);
        }



        private static async Task SeedCentersHospitalsServicesAsync(
    ApplicationDbContext context,
    ILogger logger,
    CancellationToken ct)
        {
            if (await context.Centers.AnyAsync(ct)) return;

            var center = new Center
            {
                Name = "CHU Ibn Sina - Rabat",
                CenterType = CenterType.CHU,
                City = "Rabat"
            };

            // Use Bogus to define hospital names
            var hospitalNames = new[] { "Hôpital Ibn Sina", "Hôpital des Spécialités", "Hôpital d'Enfants" };

            var serviceFaker = new Faker<Service>()
                .RuleFor(s => s.Name, f => f.PickRandom(
                    "Cardiologie", "Neurologie", "Gastro-entérologie", "Pneumologie",
                    "Urologie", "Traumatologie", "ORL", "Ophtalmologie",
                    "Réanimation", "Urgences", "Dermatologie", "Endocrinologie",
                    "Néphrologie", "Hématologie", "Oncologie", "Rhumatologie"
                ))
                // Varied capacities to test the "Skip" logic
                .RuleFor(s => s.Capacity, f => f.Random.WeightedRandom(
                    new[] { 5, 15, 30, 50 },
                    new[] { 0.1f, 0.4f, 0.4f, 0.1f } // 10% are tiny, 40% are medium, etc.
                ))
                .RuleFor(s => s.ServiceType, f => f.PickRandom<ServiceType>())
                .RuleFor(s => s.Description, f => f.Lorem.Sentence());

            foreach (var name in hospitalNames)
            {
                var hospital = new Hospital
                {
                    Name = name,
                    City = "Rabat",
                    HospitalType = HospitalType.CHU,
                    Center = center
                };

                // Generate 12 unique services per hospital
                var uniqueServices = serviceFaker.Generate(12)
                    .GroupBy(s => s.Name)
                    .Select(g => g.First())
                    .ToList();

                foreach (var s in uniqueServices) hospital.services.Add(s);
                center.Hospitals.Add(hospital);
            }

            await context.Centers.AddAsync(center, ct);
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Seeded 3 Hospitals with 36 total Services.");
        }

        private static async Task SeedRegistrationsAsync(
          ApplicationDbContext context,
          ILogger logger,
          CancellationToken ct)
        {
            // 1. Performance/Safety Check
            if (await context.Registrations.AnyAsync(ct)) return;

            // 2. Fetch Dependencies Safely
            var currentYear = await context.AcademicYears.FirstOrDefaultAsync(y => y.IsCurrent, ct);
            var medLevel = await context.Levels.FirstOrDefaultAsync(l => l.AcademicProgram == AcademicProgram.Medecine, ct);
            var pharmLevel = await context.Levels.FirstOrDefaultAsync(l => l.AcademicProgram == AcademicProgram.Pharmacie, ct);

            if (currentYear == null || medLevel == null || pharmLevel == null)
            {
                logger.LogError("CRITICAL: Registration seeding failed. Ensure AcademicYears and Levels are seeded first.");
                return;
            }

            // 3. Fetch Students to Register
            var students = await context.Students.ToListAsync(ct);
            if (!students.Any())
            {
                logger.LogWarning("No students found in database to register.");
                return;
            }

            var registrations = new List<Registration>();

            foreach (var student in students)
            {
                // Route student to the correct Level based on their Program
                var targetLevelId = student.AcademicProgram == AcademicProgram.Medecine
                    ? medLevel.Id
                    : pharmLevel.Id;

                registrations.Add(new Registration
                {
                    Id = Guid.NewGuid(),
                    StudentId = student.Id,
                    LevelId = targetLevelId,
                    AcademicYearId = currentYear.Id,
                    Status = "Active",
                    RegistrationDate = DateTime.UtcNow,
                    // Keep AcademicGroupId NULL to test your Auto-Group-Builder engine
                    AcademicGroupId = null
                });
            }

            // 4. Batch Insert for Performance
            await context.Registrations.AddRangeAsync(registrations, ct);
            await context.SaveChangesAsync(ct);

            logger.LogInformation("Successfully registered {Count} students for the {Year} academic year (Ungrouped).",
                registrations.Count, currentYear.Label);
        }
        public static async Task SeedStagesAsync(
      ApplicationDbContext context,
      ILogger logger,
      CancellationToken ct)
        {
            if (await context.Stages.AnyAsync(ct)) return;

            var medLevel = await context.Levels.FirstAsync(l => l.AcademicProgram == AcademicProgram.Medecine, ct);
            var pharmLevel = await context.Levels.FirstAsync(l => l.AcademicProgram == AcademicProgram.Pharmacie, ct);

            var stageFaker = new Faker<Stage>()
                .RuleFor(s => s.Description, f => f.Lorem.Paragraph())
                .RuleFor(s => s.Coefficient, f => f.Random.Int(1, 4))
                .RuleFor(s => s.DurationInDays, f => f.PickRandom(15, 30, 45, 60));

            var stages = new List<Stage>();

            // 20 Medicine Stages
            var medStageNames = new[] {
                "Stage de sémiologie", "Stage de pédiatrie", "Stage de gynécologie",
                "Stage de psychiatrie", "Stage de médecine légale", "Stage de santé publique" 
                // ... Bogus will fill the rest with unique titles
            };

            for (int i = 0; i < 20; i++)
            {
                var s = stageFaker.Generate();
                s.Name = i < medStageNames.Length ? medStageNames[i] : $"Stage Médical Spécialisé {i}";
                s.LevelId = medLevel.Id;
                stages.Add(s);
            }

            // 20 Pharmacy Stages
            for (int i = 0; i < 20; i++)
            {
                var s = stageFaker.Generate();
                s.Name = $"Stage Pharmacie {i + 1}";
                s.LevelId = pharmLevel.Id;
                stages.Add(s);
            }

            await context.Stages.AddRangeAsync(stages, ct);
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Seeded 40 Academic Stages via Bogus.");
        }
        private static IEnumerable<Stage> CreateStagesForLevel(Level level)
        {
            return level.AcademicProgram switch
            {
                AcademicProgram.Medecine => new[]
                {
                new Stage
                {
                    Name = "Médecine Interne",
                    Description = "Stage fondamental en médecine interne",
                    DurationInDays = 30,
                    Coefficient = 2,
                    Level = level
                },
                new Stage
                {
                    Name = "Chirurgie Générale",
                    Description = "Introduction aux bases de la chirurgie",
                    DurationInDays = 30,
                    Coefficient = 2,
                    Level = level
                },
                new Stage
                {
                    Name = "Urgences",
                    Description = "Gestion des urgences médicales",
                    DurationInDays = 15,
                    Coefficient = 1,
                    Level = level
                }
            },

                AcademicProgram.Pharmacie => new[]
                {
                new Stage
                {
                    Name = "Pharmacie Clinique",
                    DurationInDays = 30,
                    Coefficient = 2,
                    Level = level
                },
                new Stage
                {
                    Name = "Industrie Pharmaceutique",
                    DurationInDays = 30,
                    Coefficient = 1,
                    Level = level
                }
            },

                _ => Array.Empty<Stage>()
            };
        }

        private static async Task SeedCohortsAndAssignmentsAsync(
    ApplicationDbContext context,
    ILogger logger,
    CancellationToken ct)
        {
            if (await context.InternshipAssignments.AnyAsync(ct))
                return;

            var stage = await context.Stages.FirstOrDefaultAsync(ct);

            // 1. MUST GET A GROUP: Cohorts now require an AcademicGroupId
            var group = await context.AcademicGroups.FirstOrDefaultAsync(ct);

            if (stage is null || group is null)
            {
                logger.LogWarning("Cohort seeding skipped: no Stage or AcademicGroup found.");
                return;
            }

            // 2. Create Cohort - Added AcademicGroupId
            var cohort = new Cohort
            {
                Label = $"Promo {DateTime.Now.Year} - {stage.Name} - {group.Label}",
                StageId = stage.Id,
                AcademicGroupId = group.Id, // <--- THIS WAS THE MISSING LINK
            };

            var services = await context.Services.Take(2).ToListAsync(ct);
            if (!services.Any())
            {
                logger.LogWarning("Cohort seeding skipped: no Services found.");
                return;
            }

            // Define the Template
            for (int i = 0; i < services.Count; i++)
            {
                cohort.RotationTemplates.Add(new CohortRotationTemplate
                {
                    ServiceId = services[i].Id,
                    SequenceOrder = i + 1,
                    PlannedStart = new DateOnly(2026, 01, 30),
                    PlannedEnd = new DateOnly(2026, 06, 30)
                });
            }

            await context.Cohorts.AddAsync(cohort, ct);
            await context.SaveChangesAsync(ct);

            // 4. Use registrations that belong to the SAME group for consistency
            var registrations = await context.Registrations
                .Where(r => r.AcademicGroupId == group.Id) // Filter for group members
                .Take(10)
                .ToListAsync(ct);

            foreach (var registration in registrations)
            {
                var assignment = new InternshipAssignment
                {
                    Id = Guid.NewGuid(),
                    RegistrationId = registration.Id,
                    CurrentCohortId = cohort.Id,
                    Status = InternshipStatus.Planned,
                };

                assignment.MembershipHistory.Add(new CohortMembership
                {
                    Id = Guid.NewGuid(),
                    CohortId = cohort.Id,
                    StartDate = cohort.RotationTemplates.First().PlannedStart,
                    TransferReason = "Initial Assignment"
                });

                var firstTemplate = cohort.RotationTemplates.OrderBy(t => t.SequenceOrder).First();
                assignment.ServicePeriods.Add(new ServicePeriod
                {
                    Id = Guid.NewGuid(),
                    ServiceId = firstTemplate.ServiceId,
                    StartDate = firstTemplate.PlannedStart,
                    EndDate = firstTemplate.PlannedEnd,
                    IsComplete = false
                });

                await context.InternshipAssignments.AddAsync(assignment, ct);
            }

            await context.SaveChangesAsync(ct);

            logger.LogInformation(
                "Seeded Cohort '{Label}' for Group '{Group}' with {Assignments} assignments.",
                cohort.Label,
                group.Label,
                registrations.Count
            );
        }

    }
}


////using Bogus;
////using PGSH.Domain.Students;
////using PGSH.Infrastructure.Database;
////using PGSH.Domain.Users;
////using PGSH.Domain.Common.Utils;


////namespace PGSH.MigrationService
////{
////    internal class Seeder
////    {
////        public static async Task SeedDataAsync(ApplicationDbContext dbContext, ILogger<Worker> logger, CancellationToken cancellationToken)
////        {
////            var academicPrograms = Enum.GetValues(typeof(AcademicProgram)).Cast<AcademicProgram>().ToList();
////            var bacSeriesList = Enum.GetValues(typeof(BacSeries)).Cast<BacSeries>().ToList();

////            var userFaker = new Faker<User>()
////                .RuleFor(u => u.Id, f => Guid.NewGuid())
////                .RuleFor(u => u.Email, f => f.Internet.Email())
////                .RuleFor(u => u.UserName, f => f.Internet.UserName())
////                .RuleFor(u => u.Gender, f => f.PickRandom<Gender>())
////                .RuleFor(s => s.Status, f => new Status(
////                    f.PickRandom<CivilStatus>(),
////                    f.PickRandom<NationalityStatus>()
////                ))
////                .RuleFor(u => u.DateOfBirth, f => new DateOnly(f.Date.Past(25, DateTime.Now.AddYears(-18)).Year, 1, 1))
////                .RuleFor(u => u.PlaceOfBirth, f => f.Address.City());

////            var users = userFaker.Generate(30);

////            var studentFaker = new Faker<Student>()
////                .RuleFor(s => s.Id, (f, s) => Guid.NewGuid())
////                .RuleFor(s => s.Email, (f, s) => f.Internet.Email())
////                .RuleFor(s => s.UserName, (f, s) => f.Internet.UserName())
////                .RuleFor(s => s.Gender, (f, s) => f.PickRandom<Gender>())
////                .RuleFor(s => s.Status, f => new Status(
////                    f.PickRandom<CivilStatus>(),
////                    f.PickRandom<NationalityStatus>()
////                 ))
////                .RuleFor(s => s.DateOfBirth, (f, s) => new DateOnly(f.Date.Past(25, DateTime.Now.AddYears(-18)).Year, 1, 1))
////                .RuleFor(s => s.PlaceOfBirth, (f, s) => f.Address.City())
////                .RuleFor(s => s.CNE, f => f.Random.AlphaNumeric(10))
////                .RuleFor(s => s.Appogee, f => f.Random.AlphaNumeric(8))
////                .RuleFor(s => s.BacSeries, f => f.PickRandom(bacSeriesList))
////                .RuleFor(s => s.AcademicProgram, f => f.PickRandom(academicPrograms))
////                .RuleFor(s => s.AccessGrade, f => f.Random.Decimal(10, 20))
////                .RuleFor(s => s.AgreementType, f => f.PickRandom<AgreementType>())
////                .RuleFor(s => s.BacYear, f => f.Date.Past(5, DateTime.Now).Year.ToString())
////                .RuleFor(s => s.Ranking, f => f.Random.Int(1, 100))
////                //.RuleFor(s => s.registrations, f => new List<Registration>())
////                .RuleFor(s => s.history, f => new List<History>())
////                .RuleFor(s => s.Academy, f => null)
////                .RuleFor(s => s.Province, f => null);
////            // Associer chaque Student à un User existant
////            var students = users.Select(u =>
////            {
////                var student = studentFaker.Generate();
////                // Copie des propriétés héritées
////                student.Id = u.Id;
////                student.Email = u.Email;
////                student.UserName = u.UserName;
////                student.Gender = u.Gender;
////                student.Status = u.Status;
////                student.DateOfBirth = u.DateOfBirth;
////                student.PlaceOfBirth = u.PlaceOfBirth;
////                return student;
////            }).ToList();

////            await dbContext.Users.AddRangeAsync(users, cancellationToken);
////            //await dbContext.Students.AddRangeAsync(students, cancellationToken);
////            await dbContext.SaveChangesAsync(cancellationToken);
////        }
////    }
////}

