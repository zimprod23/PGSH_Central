using Bogus;
using PGSH.Domain.Students;
using PGSH.Infrastructure.Database;
using PGSH.Domain.Users;
using PGSH.Domain.Common.Utils;
using Microsoft.EntityFrameworkCore;

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

