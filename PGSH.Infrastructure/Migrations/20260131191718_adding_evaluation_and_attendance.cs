using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class adding_evaluation_and_attendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssignmentPeriods_InternshipAssignments_AssignementId",
                schema: "public",
                table: "AssignmentPeriods");

            migrationBuilder.DropForeignKey(
                name: "FK_AssignmentPeriods_Services_ServiceId",
                schema: "public",
                table: "AssignmentPeriods");

            migrationBuilder.DropForeignKey(
                name: "FK_InternshipAssignments_StagesGroup_StageGroupId",
                schema: "public",
                table: "InternshipAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_StagesGroup_Stages_StageId",
                schema: "public",
                table: "StagesGroup");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StagesGroup",
                schema: "public",
                table: "StagesGroup");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AssignmentPeriods",
                schema: "public",
                table: "AssignmentPeriods");

            migrationBuilder.DropIndex(
                name: "IX_AssignmentPeriods_AssignementId",
                schema: "public",
                table: "AssignmentPeriods");

            migrationBuilder.DropColumn(
                name: "Score",
                schema: "public",
                table: "InternshipAssignments");

            migrationBuilder.RenameTable(
                name: "StagesGroup",
                schema: "public",
                newName: "StageGroups",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "AssignmentPeriods",
                schema: "public",
                newName: "AssignmentPeriod",
                newSchema: "public");

            migrationBuilder.RenameIndex(
                name: "IX_StagesGroup_StageId",
                schema: "public",
                table: "StageGroups",
                newName: "IX_StageGroups_StageId");

            migrationBuilder.RenameIndex(
                name: "IX_AssignmentPeriods_ServiceId",
                schema: "public",
                table: "AssignmentPeriod",
                newName: "IX_AssignmentPeriod_ServiceId");

            migrationBuilder.AddColumn<decimal>(
                name: "FinalScore",
                schema: "public",
                table: "InternshipAssignments",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Result",
                schema: "public",
                table: "InternshipAssignments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "public",
                table: "InternshipAssignments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                schema: "public",
                table: "StageGroups",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InternshipAssignmentId",
                schema: "public",
                table: "AssignmentPeriod",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "ServiceId1",
                schema: "public",
                table: "AssignmentPeriod",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_StageGroups",
                schema: "public",
                table: "StageGroups",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AssignmentPeriod",
                schema: "public",
                table: "AssignmentPeriod",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "StageGroupPeriods",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    StageGroupId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageGroupPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageGroupPeriods_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "public",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StageGroupPeriods_StageGroups_StageGroupId",
                        column: x => x.StageGroupId,
                        principalSchema: "public",
                        principalTable: "StageGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StageObjectives",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    IsMandatory = table.Column<bool>(type: "boolean", nullable: false),
                    StageId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageObjectives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageObjectives_Stages_StageId",
                        column: x => x.StageId,
                        principalSchema: "public",
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRecords",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    InternshipAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageGroupPeriodId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_InternshipAssignments_InternshipAssignmen~",
                        column: x => x.InternshipAssignmentId,
                        principalSchema: "public",
                        principalTable: "InternshipAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_StageGroupPeriods_StageGroupPeriodId",
                        column: x => x.StageGroupPeriodId,
                        principalSchema: "public",
                        principalTable: "StageGroupPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PeriodEvaluations",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InternshipAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageGroupPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupervisorComment = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PeriodEvaluations_InternshipAssignments_InternshipAssignmen~",
                        column: x => x.InternshipAssignmentId,
                        principalSchema: "public",
                        principalTable: "InternshipAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PeriodEvaluations_StageGroupPeriods_StageGroupPeriodId",
                        column: x => x.StageGroupPeriodId,
                        principalSchema: "public",
                        principalTable: "StageGroupPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ObjectiveEvaluations",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StageObjectiveId = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    PeriodEvaluationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectiveEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObjectiveEvaluations_PeriodEvaluations_PeriodEvaluationId",
                        column: x => x.PeriodEvaluationId,
                        principalSchema: "public",
                        principalTable: "PeriodEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ObjectiveEvaluations_StageObjectives_StageObjectiveId",
                        column: x => x.StageObjectiveId,
                        principalSchema: "public",
                        principalTable: "StageObjectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentPeriod_InternshipAssignmentId",
                schema: "public",
                table: "AssignmentPeriod",
                column: "InternshipAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentPeriod_ServiceId1",
                schema: "public",
                table: "AssignmentPeriod",
                column: "ServiceId1");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_InternshipAssignmentId",
                schema: "public",
                table: "AttendanceRecords",
                column: "InternshipAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_StageGroupPeriodId",
                schema: "public",
                table: "AttendanceRecords",
                column: "StageGroupPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectiveEvaluations_PeriodEvaluationId",
                schema: "public",
                table: "ObjectiveEvaluations",
                column: "PeriodEvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectiveEvaluations_StageObjectiveId",
                schema: "public",
                table: "ObjectiveEvaluations",
                column: "StageObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_PeriodEvaluations_InternshipAssignmentId",
                schema: "public",
                table: "PeriodEvaluations",
                column: "InternshipAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PeriodEvaluations_StageGroupPeriodId",
                schema: "public",
                table: "PeriodEvaluations",
                column: "StageGroupPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_StageGroupPeriods_ServiceId",
                schema: "public",
                table: "StageGroupPeriods",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_StageGroupPeriods_StageGroupId",
                schema: "public",
                table: "StageGroupPeriods",
                column: "StageGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_StageObjectives_StageId",
                schema: "public",
                table: "StageObjectives",
                column: "StageId");

            migrationBuilder.AddForeignKey(
                name: "FK_AssignmentPeriod_InternshipAssignments_InternshipAssignment~",
                schema: "public",
                table: "AssignmentPeriod",
                column: "InternshipAssignmentId",
                principalSchema: "public",
                principalTable: "InternshipAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AssignmentPeriod_Services_ServiceId",
                schema: "public",
                table: "AssignmentPeriod",
                column: "ServiceId",
                principalSchema: "public",
                principalTable: "Services",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AssignmentPeriod_Services_ServiceId1",
                schema: "public",
                table: "AssignmentPeriod",
                column: "ServiceId1",
                principalSchema: "public",
                principalTable: "Services",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InternshipAssignments_StageGroups_StageGroupId",
                schema: "public",
                table: "InternshipAssignments",
                column: "StageGroupId",
                principalSchema: "public",
                principalTable: "StageGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StageGroups_Stages_StageId",
                schema: "public",
                table: "StageGroups",
                column: "StageId",
                principalSchema: "public",
                principalTable: "Stages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssignmentPeriod_InternshipAssignments_InternshipAssignment~",
                schema: "public",
                table: "AssignmentPeriod");

            migrationBuilder.DropForeignKey(
                name: "FK_AssignmentPeriod_Services_ServiceId",
                schema: "public",
                table: "AssignmentPeriod");

            migrationBuilder.DropForeignKey(
                name: "FK_AssignmentPeriod_Services_ServiceId1",
                schema: "public",
                table: "AssignmentPeriod");

            migrationBuilder.DropForeignKey(
                name: "FK_InternshipAssignments_StageGroups_StageGroupId",
                schema: "public",
                table: "InternshipAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_StageGroups_Stages_StageId",
                schema: "public",
                table: "StageGroups");

            migrationBuilder.DropTable(
                name: "AttendanceRecords",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ObjectiveEvaluations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PeriodEvaluations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "StageObjectives",
                schema: "public");

            migrationBuilder.DropTable(
                name: "StageGroupPeriods",
                schema: "public");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StageGroups",
                schema: "public",
                table: "StageGroups");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AssignmentPeriod",
                schema: "public",
                table: "AssignmentPeriod");

            migrationBuilder.DropIndex(
                name: "IX_AssignmentPeriod_InternshipAssignmentId",
                schema: "public",
                table: "AssignmentPeriod");

            migrationBuilder.DropIndex(
                name: "IX_AssignmentPeriod_ServiceId1",
                schema: "public",
                table: "AssignmentPeriod");

            migrationBuilder.DropColumn(
                name: "FinalScore",
                schema: "public",
                table: "InternshipAssignments");

            migrationBuilder.DropColumn(
                name: "Result",
                schema: "public",
                table: "InternshipAssignments");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "public",
                table: "InternshipAssignments");

            migrationBuilder.DropColumn(
                name: "InternshipAssignmentId",
                schema: "public",
                table: "AssignmentPeriod");

            migrationBuilder.DropColumn(
                name: "ServiceId1",
                schema: "public",
                table: "AssignmentPeriod");

            migrationBuilder.RenameTable(
                name: "StageGroups",
                schema: "public",
                newName: "StagesGroup",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "AssignmentPeriod",
                schema: "public",
                newName: "AssignmentPeriods",
                newSchema: "public");

            migrationBuilder.RenameIndex(
                name: "IX_StageGroups_StageId",
                schema: "public",
                table: "StagesGroup",
                newName: "IX_StagesGroup_StageId");

            migrationBuilder.RenameIndex(
                name: "IX_AssignmentPeriod_ServiceId",
                schema: "public",
                table: "AssignmentPeriods",
                newName: "IX_AssignmentPeriods_ServiceId");

            migrationBuilder.AddColumn<decimal>(
                name: "Score",
                schema: "public",
                table: "InternshipAssignments",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                schema: "public",
                table: "StagesGroup",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_StagesGroup",
                schema: "public",
                table: "StagesGroup",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AssignmentPeriods",
                schema: "public",
                table: "AssignmentPeriods",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentPeriods_AssignementId",
                schema: "public",
                table: "AssignmentPeriods",
                column: "AssignementId");

            migrationBuilder.AddForeignKey(
                name: "FK_AssignmentPeriods_InternshipAssignments_AssignementId",
                schema: "public",
                table: "AssignmentPeriods",
                column: "AssignementId",
                principalSchema: "public",
                principalTable: "InternshipAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AssignmentPeriods_Services_ServiceId",
                schema: "public",
                table: "AssignmentPeriods",
                column: "ServiceId",
                principalSchema: "public",
                principalTable: "Services",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InternshipAssignments_StagesGroup_StageGroupId",
                schema: "public",
                table: "InternshipAssignments",
                column: "StageGroupId",
                principalSchema: "public",
                principalTable: "StagesGroup",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StagesGroup_Stages_StageId",
                schema: "public",
                table: "StagesGroup",
                column: "StageId",
                principalSchema: "public",
                principalTable: "Stages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
