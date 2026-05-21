using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Domain_Cleanup_And_Schema_Improvements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Registrations_StudentId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_ServicePeriodId",
                schema: "public",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "GroupNumber",
                schema: "public",
                table: "InternshipAssignments");

            migrationBuilder.RenameIndex(
                name: "IX_ServicePeriods_ServiceId",
                schema: "public",
                table: "ServicePeriods",
                newName: "IX_ServicePeriod_ServiceId");

            migrationBuilder.RenameIndex(
                name: "IX_ServicePeriods_InternshipAssignmentId",
                schema: "public",
                table: "ServicePeriods",
                newName: "IX_ServicePeriod_AssignmentId");

            migrationBuilder.RenameIndex(
                name: "IX_InternshipAssignments_RegistrationId",
                schema: "public",
                table: "InternshipAssignments",
                newName: "IX_InternshipAssignment_RegistrationId");

            migrationBuilder.RenameIndex(
                name: "IX_InternshipAssignments_CurrentCohortId",
                schema: "public",
                table: "InternshipAssignments",
                newName: "IX_InternshipAssignment_CohortId");

            migrationBuilder.RenameIndex(
                name: "IX_CohortMembership_InternshipAssignmentId",
                schema: "public",
                table: "CohortMembership",
                newName: "IX_CohortMembership_AssignmentId");

            migrationBuilder.AddColumn<int>(
                name: "CohortRotationTemplateId",
                schema: "public",
                table: "ServicePeriods",
                type: "integer",
                nullable: true);

            // AlterColumn cannot cast text→jsonb automatically; use explicit USING clause
            migrationBuilder.Sql(
                @"ALTER TABLE public.""Registrations"" ALTER COLUMN ""failureReasons_Notes"" TYPE jsonb USING ""failureReasons_Notes""::jsonb;");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "public",
                table: "Registrations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Label",
                schema: "public",
                table: "Cohorts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Student_Appogee",
                schema: "public",
                table: "Users",
                column: "Appogee",
                unique: true,
                filter: "\"Appogee\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Student_CNE",
                schema: "public",
                table: "Users",
                column: "CNE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicePeriods_CohortRotationTemplateId",
                schema: "public",
                table: "ServicePeriods",
                column: "CohortRotationTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Registration_Student_Year",
                schema: "public",
                table: "Registrations",
                columns: new[] { "StudentId", "AcademicYearId" });

            migrationBuilder.CreateIndex(
                name: "IX_Level_Year_Program",
                schema: "public",
                table: "Levels",
                columns: new[] { "Year", "AcademicProgram" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecord_Period_Date",
                schema: "public",
                table: "AttendanceRecords",
                columns: new[] { "ServicePeriodId", "Date" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ServicePeriods_CohortRotationTemplates_CohortRotationTempla~",
                schema: "public",
                table: "ServicePeriods",
                column: "CohortRotationTemplateId",
                principalSchema: "public",
                principalTable: "CohortRotationTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServicePeriods_CohortRotationTemplates_CohortRotationTempla~",
                schema: "public",
                table: "ServicePeriods");

            migrationBuilder.DropIndex(
                name: "IX_Student_Appogee",
                schema: "public",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Student_CNE",
                schema: "public",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_ServicePeriods_CohortRotationTemplateId",
                schema: "public",
                table: "ServicePeriods");

            migrationBuilder.DropIndex(
                name: "IX_Registration_Student_Year",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropIndex(
                name: "IX_Level_Year_Program",
                schema: "public",
                table: "Levels");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecord_Period_Date",
                schema: "public",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "CohortRotationTemplateId",
                schema: "public",
                table: "ServicePeriods");

            migrationBuilder.RenameIndex(
                name: "IX_ServicePeriod_ServiceId",
                schema: "public",
                table: "ServicePeriods",
                newName: "IX_ServicePeriods_ServiceId");

            migrationBuilder.RenameIndex(
                name: "IX_ServicePeriod_AssignmentId",
                schema: "public",
                table: "ServicePeriods",
                newName: "IX_ServicePeriods_InternshipAssignmentId");

            migrationBuilder.RenameIndex(
                name: "IX_InternshipAssignment_RegistrationId",
                schema: "public",
                table: "InternshipAssignments",
                newName: "IX_InternshipAssignments_RegistrationId");

            migrationBuilder.RenameIndex(
                name: "IX_InternshipAssignment_CohortId",
                schema: "public",
                table: "InternshipAssignments",
                newName: "IX_InternshipAssignments_CurrentCohortId");

            migrationBuilder.RenameIndex(
                name: "IX_CohortMembership_AssignmentId",
                schema: "public",
                table: "CohortMembership",
                newName: "IX_CohortMembership_InternshipAssignmentId");

            migrationBuilder.Sql(
                @"ALTER TABLE public.""Registrations"" ALTER COLUMN ""failureReasons_Notes"" TYPE text USING ""failureReasons_Notes""::text;");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "public",
                table: "Registrations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "GroupNumber",
                schema: "public",
                table: "InternshipAssignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Label",
                schema: "public",
                table: "Cohorts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_StudentId",
                schema: "public",
                table: "Registrations",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_ServicePeriodId",
                schema: "public",
                table: "AttendanceRecords",
                column: "ServicePeriodId");
        }
    }
}
