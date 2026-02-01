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
        //public static async Task SeedDataAsync(ApplicationDbContext dbContext, ILogger<Worker> logger, CancellationToken cancellationToken)
        //{
        //    var academicPrograms = Enum.GetValues(typeof(AcademicProgram)).Cast<AcademicProgram>().ToList();
        //    var bacSeriesList = Enum.GetValues(typeof(BacSeries)).Cast<BacSeries>().ToList();

        //    // --- 1. Define Faker for base User properties (inherited by Student) ---
        //    var userFaker = new Faker<User>()
        //        .RuleFor(u => u.Id, f => Guid.NewGuid())
        //        // Use .Unique.Email() to minimize the chance of duplicates in case of a large batch, 
        //        // though the core fix is in the usage below.
        //        .RuleFor(u => u.Email, f => f.Internet.Email(uniqueSuffix: f.Random.Hash()))
        //        .RuleFor(u => u.UserName, f => f.Internet.UserName(f.Random.AlphaNumeric(5)))
        //        .RuleFor(u => u.FirstName, f => f.Name.FirstName())
        //        .RuleFor(u => u.LastName, f => f.Name.LastName())
        //        .RuleFor(u => u.CIN, f => f.Random.Replace("##-###-####"))
        //        .RuleFor(u => u.Gender, f => f.PickRandom<Gender>())
        //        .RuleFor(u => u.Status, f => new Status(
        //            f.PickRandom<CivilStatus>(),
        //            f.PickRandom<NationalityStatus>()
        //        ))
        //        .RuleFor(u => u.DateOfBirth, f => new DateOnly(f.Date.Past(25, DateTime.Now.AddYears(-18)).Year, 1, 1))
        //        .RuleFor(u => u.PlaceOfBirth, f => f.Address.City());

        //    // --- 2. Define Faker for Student-specific properties ---
        //    // Note: This Faker only provides the student-specific properties (CNE, BacSeries, etc.)
        //    var studentSpecificFaker = new Faker<Student>()
        //        // IMPORTANT: Do NOT define Id, Email, or other inherited properties here, 
        //        // as they will be populated by userFaker below.
        //        .RuleFor(s => s.CNE, f => f.Random.AlphaNumeric(10))
        //        .RuleFor(s => s.Appogee, f => f.Random.AlphaNumeric(8))
        //        .RuleFor(s => s.BacSeries, f => f.PickRandom(bacSeriesList))
        //        .RuleFor(s => s.AcademicProgram, f => f.PickRandom(academicPrograms))
        //        .RuleFor(s => s.AccessGrade, f => f.Random.Decimal(10, 20))
        //        .RuleFor(s => s.AgreementType, f => f.PickRandom<AgreementType>())
        //        .RuleFor(s => s.BacYear, f => f.Date.Past(5, DateTime.Now).Year.ToString())
        //        .RuleFor(s => s.Ranking, f => f.Random.Int(1, 100))
        //        .RuleFor(s => s.history, f => new List<History>())
        //        .RuleFor(s => s.Academy, f => null)
        //        .RuleFor(s => s.Province, f => null);


        //    // --- 3. Create 30 Student objects (which are also Users) ---
        //    var students = Enumerable.Range(0, 30).Select(_ =>
        //    {
        //        var student = new Student();

        //        // Populate base User properties (Id, Email, UserName, etc.) onto the Student instance
        //        userFaker.Populate(student);

        //        // Populate derived Student-specific properties
        //        studentSpecificFaker.Populate(student);

        //        // Ensure the UserType is set if you use a discriminator property
        //        // student.UserType = UserType.Student; 

        //        return student;
        //    }).ToList();


        //    // --- 4. FIX APPLIED: Only add the derived entities (Students) ---
        //    // In EF Core TPH/TPT inheritance, adding the derived type is sufficient.
        //    // EF Core will automatically perform the necessary inserts into the base table ("Users").
        //    await dbContext.Students.AddRangeAsync(students, cancellationToken);

        //    // The previous code included dbContext.Users.AddRangeAsync(users, cancellationToken); 
        //    // which caused the duplicate key violation. That line is now REMOVED.

        //    await dbContext.SaveChangesAsync(cancellationToken);
        //    logger.LogInformation("Successfully seeded {Count} Student entities.", students.Count);
        //}

        public static async Task SeedAsync(ApplicationDbContext context, ILogger<Worker> logger, CancellationToken cancellationToken)
        {
            logger.LogInformation("Starting database seeding...");

            await SeedLevelsAsync(context, logger, cancellationToken);
            await SeedCentersHospitalsServicesAsync(context, logger, cancellationToken);
            await SeedStudentsAsync(context, logger, cancellationToken);
            await SeedRegistrationsAsync(context, logger, cancellationToken);
            await SeedStagesAsync(context, logger, cancellationToken);
            await SeedStageGroupsAndAssignmentsAsync(context, logger, cancellationToken);

            logger.LogInformation("Database seeding completed.");
        }

        private static async Task SeedStudentsAsync(ApplicationDbContext context, ILogger<Worker> logger, CancellationToken cancellationToken)
        {
            // 1. Performance Guard: Don't seed if data exists
            if (await context.Students.AnyAsync()) return;

            // 2. Define the "Golden" Static Student
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
                //Address = new UserAddress { FullAddress = "Hay Riad, Rabat" },
                PlaceOfBirth = "Rabat",
                DateOfBirth = new DateOnly(2005, 08, 12)
            };

            // 3. Configure Faker for Bulk Data
            var faker = new Faker<Student>()
                .RuleFor(s => s.Id, f => Guid.NewGuid())
                .RuleFor(s => s.FirstName, f => f.Name.FirstName())
                .RuleFor(s => s.LastName, f => f.Name.LastName())
                .RuleFor(s => s.Email, (f, s) => $"{s.FirstName.ToLower()}.{s.LastName.ToLower()}@um5.ac.ma")
                .RuleFor(s => s.CNE, f => f.Random.Replace("?#########")) // Letter + 9 digits
                .RuleFor(s => s.Appogee, f => f.Random.Number(20000000, 25000000).ToString())
                .RuleFor(s => s.AccessGrade, f => f.Random.Decimal(10, 20))
                .RuleFor(s => s.AcademicProgram, f => f.PickRandom<AcademicProgram>())
                .RuleFor(s => s.BacSeries, f => f.PickRandom<BacSeries>())
                .RuleFor(s => s.BacYear, f => f.Random.Int(2020, 2025).ToString())
                .RuleFor(s => s.Gender, f => f.PickRandom<Gender>())
                // Matching your Domain Structure (Owned Entities)
                .RuleFor(s => s.Status, f => new Status(f.PickRandom<CivilStatus>(), f.PickRandom<NationalityStatus>()))
                .RuleFor(s => s.Address, f => new Address(f.Address.FullAddress()))
                .RuleFor(s => s.DateOfBirth, f => DateOnly.FromDateTime(f.Date.Past(20, DateTime.Now.AddYears(-18))));

            // 4. Generate 50 fake students
            var fakeStudents = faker.Generate(50);

            // 5. Combine and Insert
            var allStudents = new List<Student> { staticStudent };
            allStudents.AddRange(fakeStudents);

            // Optimal Performance: Single Batch Save
            await context.Students.AddRangeAsync(allStudents);
            await context.SaveChangesAsync();

            logger.LogInformation("Successfully seeded {Count} Student entities.", allStudents.Count);
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
                Name = "Centre Hospitalier Universitaire Ibn Sina",
                CenterType = CenterType.CHU,
                City = "Rabat"
            };

            var hospital = new Hospital
            {
                Name = "Hôpital Ibn Sina",
                City = "Rabat",
                HospitalType = HospitalType.CHU,
                Center = center
            };

            hospital.services.Add(new Service
            {
                Name = "Chirurgie Générale",
                ServiceType = ServiceType.Chirurgie,
                Description = "Service de chirurgie générale"
            });

            hospital.services.Add(new Service
            {
                Name = "Biologie Médicale",
                ServiceType = ServiceType.Biologie,
                Description = "Laboratoire central"
            });

            hospital.services.Add(new Service
            {
                Name = "Médecine Interne",
                ServiceType = ServiceType.Medical,
                Description = "Service de médecine interne"
            });

            center.Hospitals.Add(hospital);

            await context.Centers.AddAsync(center, ct);
            await context.SaveChangesAsync(ct);

            logger.LogInformation("Seeded Centers, Hospitals and Services");
        }

        private static async Task SeedRegistrationsAsync(
            ApplicationDbContext context,
            ILogger logger,
            CancellationToken ct)
        {
            if (await context.Registrations.AnyAsync(ct)) return;

            var level = await context.Levels.FirstAsync(ct);
            var students = await context.Students.ToListAsync(ct);

            var registrations = students.Select(s => new Registration
            {
                Id = Guid.NewGuid(),
                StudentId = s.Id,
                LevelId = level.Id,
                AcademicYear = new DateOnly(2024, 09, 01),
                Status = "Active"
            }).ToList();

            await context.Registrations.AddRangeAsync(registrations, ct);
            await context.SaveChangesAsync(ct);

            logger.LogInformation("Seeded {Count} Registrations", registrations.Count);
        }


        public static async Task SeedStagesAsync(
        ApplicationDbContext context,
        ILogger logger,
        CancellationToken cancellationToken)
        {
            if (await context.Stages.AnyAsync(cancellationToken))
                return;

            var levels = await context.Levels
                //.Include(l => l.AcademicProgram)
                .ToListAsync(cancellationToken);

            if (!levels.Any())
            {
                logger.LogWarning("Stage seeding skipped: no Levels found.");
                return;
            }

            var stages = new List<Stage>();

            foreach (var level in levels)
            {
                stages.AddRange(CreateStagesForLevel(level));
            }

            await context.Stages.AddRangeAsync(stages, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Seeded {Count} academic stages.", stages.Count);
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

        private static async Task SeedStageGroupsAndAssignmentsAsync(
            ApplicationDbContext context,
            ILogger logger,
            CancellationToken ct)
        {
            // Guard
            if (await context.InternshipAssignments.AnyAsync(ct))
                return;

            // 1. Get an existing Stage from DB
            var stage = await context.Stages
                .Include(s => s.Level)
                .FirstOrDefaultAsync(ct);

            if (stage is null)
            {
                logger.LogWarning("StageGroup seeding skipped: no Stage found.");
                return;
            }

            // 2. Create StageGroup linked to existing Stage
            var group = new StageGroup
            {
                Label = $"Groupe A – {stage.Name}",
                StageId = stage.Id
            };

            // 3. Pick some services
            var services = await context.Services
                .Take(2)
                .ToListAsync(ct);

            if (!services.Any())
            {
                logger.LogWarning("StageGroup seeding skipped: no Services found.");
                return;
            }

            foreach (var service in services)
            {
                group.Periods.Add(new StageGroupPeriod
                {
                    Start = new DateOnly(2025, 01, 01),
                    End = new DateOnly(2025, 01, 30),
                    ServiceId = service.Id
                });
            }

            // 4. Pick registrations
            var registrations = await context.Registrations
                .Take(20)
                .ToListAsync(ct);

            foreach (var registration in registrations)
            {
                group.InternshipAssignments.Add(new InternshipAssignment
                {
                    Id = Guid.NewGuid(),
                    RegistrationId = registration.Id,
                    PlannedStart = group.Periods.Min(p => p.Start),
                    PlannedEnd = group.Periods.Max(p => p.End),
                    Status = InternshipStatus.Planned
                });
            }

            // 5. Persist
            await context.StageGroups.AddAsync(group, ct);
            await context.SaveChangesAsync(ct);

            logger.LogInformation(
                "Seeded StageGroup '{Label}' with {Periods} periods and {Assignments} assignments.",
                group.Label,
                group.Periods.Count,
                group.InternshipAssignments.Count
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

