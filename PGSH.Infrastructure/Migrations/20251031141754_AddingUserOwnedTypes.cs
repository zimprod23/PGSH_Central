using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddingUserOwnedTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                schema: "public",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                schema: "public",
                table: "Users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                schema: "public",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "FirstName",
                schema: "public",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "public",
                table: "Users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "Academy",
                schema: "public",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AccessGrade",
                schema: "public",
                table: "Users",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_City",
                schema: "public",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_Country",
                schema: "public",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_FullAddress",
                schema: "public",
                table: "Users",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_HouseNumber",
                schema: "public",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_Street",
                schema: "public",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_ZIP",
                schema: "public",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AgreementType",
                schema: "public",
                table: "Users",
                type: "integer",
                nullable: true,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Appogee",
                schema: "public",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BacSeries",
                schema: "public",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BacYear",
                schema: "public",
                table: "Users",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CIN",
                schema: "public",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CNE",
                schema: "public",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                schema: "public",
                table: "Users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                schema: "public",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Grade",
                schema: "public",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Label",
                schema: "public",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PPR",
                schema: "public",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceOfBirth",
                schema: "public",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                schema: "public",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Province",
                schema: "public",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PvSignatureDate",
                schema: "public",
                table: "Users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Ranking",
                schema: "public",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Specialty",
                schema: "public",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status_CivilStatus",
                schema: "public",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status_NationalityStatus",
                schema: "public",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserType",
                schema: "public",
                table: "Users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Centers",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CenterType = table.Column<string>(type: "text", nullable: false),
                    City = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    X = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Y = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Z = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Centers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "History",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HistoryData = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_History", x => x.Id);
                    table.ForeignKey(
                        name: "FK_History_Users_StudentId",
                        column: x => x.StudentId,
                        principalSchema: "public",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Registrations",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYear = table.Column<DateOnly>(type: "date", nullable: false),
                    failureReasons_Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    failureReasons_Notes = table.Column<string>(type: "text", nullable: true),
                    failureReasons_Cheat = table.Column<bool>(type: "boolean", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Level_Id = table.Column<int>(type: "integer", nullable: false),
                    Level_Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Level_Year = table.Column<int>(type: "integer", nullable: false),
                    Level_AcademicProgram = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Registrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Registrations_Users_StudentId",
                        column: x => x.StudentId,
                        principalSchema: "public",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Stages",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Coefficient = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DurationInDays = table.Column<int>(type: "integer", nullable: false),
                    Level_Id = table.Column<int>(type: "integer", nullable: false),
                    Level_Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Level_Year = table.Column<int>(type: "integer", nullable: false),
                    Level_AcademicProgram = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Hospitals",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CenterId = table.Column<int>(type: "integer", nullable: false),
                    HospitalType = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    X = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Y = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Z = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hospitals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hospitals_Centers_CenterId",
                        column: x => x.CenterId,
                        principalSchema: "public",
                        principalTable: "Centers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StagesGroup",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StageId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagesGroup", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StagesGroup_Stages_StageId",
                        column: x => x.StageId,
                        principalSchema: "public",
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Services",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ServiceType = table.Column<string>(type: "text", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    HospitalId = table.Column<int>(type: "integer", nullable: false),
                    _serviceChefId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceChefId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Services_Hospitals_HospitalId",
                        column: x => x.HospitalId,
                        principalSchema: "public",
                        principalTable: "Hospitals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Services_Users_ServiceChefId",
                        column: x => x.ServiceChefId,
                        principalSchema: "public",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Services_Users__serviceChefId",
                        column: x => x._serviceChefId,
                        principalSchema: "public",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InternshipAssignments",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlannedStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    StageGroupId = table.Column<int>(type: "integer", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternshipAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InternshipAssignments_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalSchema: "public",
                        principalTable: "Registrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InternshipAssignments_StagesGroup_StageGroupId",
                        column: x => x.StageGroupId,
                        principalSchema: "public",
                        principalTable: "StagesGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeService",
                schema: "public",
                columns: table => new
                {
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    StaffId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeService", x => new { x.ServiceId, x.StaffId });
                    table.ForeignKey(
                        name: "FK_EmployeeService_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "public",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeService_Users_StaffId",
                        column: x => x.StaffId,
                        principalSchema: "public",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssignmentPeriods",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: false),
                    AssignementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentPeriods_InternshipAssignments_AssignementId",
                        column: x => x.AssignementId,
                        principalSchema: "public",
                        principalTable: "InternshipAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssignmentPeriods_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "public",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentPeriods_AssignementId",
                schema: "public",
                table: "AssignmentPeriods",
                column: "AssignementId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentPeriods_ServiceId",
                schema: "public",
                table: "AssignmentPeriods",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeService_StaffId",
                schema: "public",
                table: "EmployeeService",
                column: "StaffId");

            migrationBuilder.CreateIndex(
                name: "IX_History_StudentId",
                schema: "public",
                table: "History",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Hospitals_CenterId",
                schema: "public",
                table: "Hospitals",
                column: "CenterId");

            migrationBuilder.CreateIndex(
                name: "IX_InternshipAssignments_RegistrationId",
                schema: "public",
                table: "InternshipAssignments",
                column: "RegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_InternshipAssignments_StageGroupId",
                schema: "public",
                table: "InternshipAssignments",
                column: "StageGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_StudentId",
                schema: "public",
                table: "Registrations",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Services__serviceChefId",
                schema: "public",
                table: "Services",
                column: "_serviceChefId");

            migrationBuilder.CreateIndex(
                name: "IX_Services_HospitalId",
                schema: "public",
                table: "Services",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_Services_ServiceChefId",
                schema: "public",
                table: "Services",
                column: "ServiceChefId");

            migrationBuilder.CreateIndex(
                name: "IX_StagesGroup_StageId",
                schema: "public",
                table: "StagesGroup",
                column: "StageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssignmentPeriods",
                schema: "public");

            migrationBuilder.DropTable(
                name: "EmployeeService",
                schema: "public");

            migrationBuilder.DropTable(
                name: "History",
                schema: "public");

            migrationBuilder.DropTable(
                name: "InternshipAssignments",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Services",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Registrations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "StagesGroup",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Hospitals",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Stages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Centers",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "Academy",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AccessGrade",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Address_City",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Address_Country",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Address_FullAddress",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Address_HouseNumber",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Address_Street",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Address_ZIP",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AgreementType",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Appogee",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BacSeries",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BacYear",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CIN",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CNE",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Gender",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Grade",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Label",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PPR",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PlaceOfBirth",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Position",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Province",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PvSignatureDate",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Ranking",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Specialty",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Status_CivilStatus",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Status_NationalityStatus",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UserType",
                schema: "public",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                schema: "public",
                table: "Users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                schema: "public",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                schema: "public",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "FirstName",
                schema: "public",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "public",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);
        }
    }
}
