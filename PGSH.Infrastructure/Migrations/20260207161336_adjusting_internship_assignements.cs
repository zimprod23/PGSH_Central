using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class adjusting_internship_assignements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceRecords_InternshipAssignments_InternshipAssignmen~",
                schema: "public",
                table: "AttendanceRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceRecords_StageGroupPeriods_StageGroupPeriodId",
                schema: "public",
                table: "AttendanceRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_InternshipAssignments_StageGroups_StageGroupId",
                schema: "public",
                table: "InternshipAssignments");

            migrationBuilder.DropTable(
                name: "AssignmentPeriod",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ObjectiveEvaluations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PeriodEvaluations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "StageGroupPeriods",
                schema: "public");

            migrationBuilder.DropTable(
                name: "StageGroups",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_StageGroupPeriodId",
                schema: "public",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "PlannedEnd",
                schema: "public",
                table: "InternshipAssignments");

            migrationBuilder.DropColumn(
                name: "PlannedStart",
                schema: "public",
                table: "InternshipAssignments");

            migrationBuilder.DropColumn(
                name: "StageGroupPeriodId",
                schema: "public",
                table: "AttendanceRecords");

            migrationBuilder.RenameColumn(
                name: "StageGroupId",
                schema: "public",
                table: "InternshipAssignments",
                newName: "CurrentCohortId");

            migrationBuilder.RenameIndex(
                name: "IX_InternshipAssignments_StageGroupId",
                schema: "public",
                table: "InternshipAssignments",
                newName: "IX_InternshipAssignments_CurrentCohortId");

            migrationBuilder.RenameColumn(
                name: "InternshipAssignmentId",
                schema: "public",
                table: "AttendanceRecords",
                newName: "ServicePeriodId");

            migrationBuilder.RenameIndex(
                name: "IX_AttendanceRecords_InternshipAssignmentId",
                schema: "public",
                table: "AttendanceRecords",
                newName: "IX_AttendanceRecords_ServicePeriodId");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                schema: "public",
                table: "Stages",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "Cohorts",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StageId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cohorts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cohorts_Stages_StageId",
                        column: x => x.StageId,
                        principalSchema: "public",
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServicePeriods",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InternshipAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServicePeriods_InternshipAssignments_InternshipAssignmentId",
                        column: x => x.InternshipAssignmentId,
                        principalSchema: "public",
                        principalTable: "InternshipAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServicePeriods_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "public",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CohortMembership",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InternshipAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CohortId = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TransferReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CohortMembership", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CohortMembership_Cohorts_CohortId",
                        column: x => x.CohortId,
                        principalSchema: "public",
                        principalTable: "Cohorts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CohortMembership_InternshipAssignments_InternshipAssignment~",
                        column: x => x.InternshipAssignmentId,
                        principalSchema: "public",
                        principalTable: "InternshipAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CohortRotationTemplates",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CohortId = table.Column<int>(type: "integer", nullable: false),
                    PlannedStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    SequenceOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CohortRotationTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CohortRotationTemplates_Cohorts_CohortId",
                        column: x => x.CohortId,
                        principalSchema: "public",
                        principalTable: "Cohorts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CohortRotationTemplates_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "public",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceEvaluation",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServicePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    SupervisorComment = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceEvaluation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceEvaluation_ServicePeriods_ServicePeriodId",
                        column: x => x.ServicePeriodId,
                        principalSchema: "public",
                        principalTable: "ServicePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ObjectiveScores",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceEvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageObjectiveId = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectiveScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObjectiveScores_ServiceEvaluation_ServiceEvaluationId",
                        column: x => x.ServiceEvaluationId,
                        principalSchema: "public",
                        principalTable: "ServiceEvaluation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ObjectiveScores_StageObjectives_StageObjectiveId",
                        column: x => x.StageObjectiveId,
                        principalSchema: "public",
                        principalTable: "StageObjectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CohortMembership_CohortId",
                schema: "public",
                table: "CohortMembership",
                column: "CohortId");

            migrationBuilder.CreateIndex(
                name: "IX_CohortMembership_InternshipAssignmentId",
                schema: "public",
                table: "CohortMembership",
                column: "InternshipAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CohortRotationTemplates_CohortId",
                schema: "public",
                table: "CohortRotationTemplates",
                column: "CohortId");

            migrationBuilder.CreateIndex(
                name: "IX_CohortRotationTemplates_ServiceId",
                schema: "public",
                table: "CohortRotationTemplates",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Cohorts_StageId",
                schema: "public",
                table: "Cohorts",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectiveScores_ServiceEvaluationId",
                schema: "public",
                table: "ObjectiveScores",
                column: "ServiceEvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectiveScores_StageObjectiveId",
                schema: "public",
                table: "ObjectiveScores",
                column: "StageObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceEvaluation_ServicePeriodId",
                schema: "public",
                table: "ServiceEvaluation",
                column: "ServicePeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicePeriods_InternshipAssignmentId",
                schema: "public",
                table: "ServicePeriods",
                column: "InternshipAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePeriods_ServiceId",
                schema: "public",
                table: "ServicePeriods",
                column: "ServiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceRecords_ServicePeriods_ServicePeriodId",
                schema: "public",
                table: "AttendanceRecords",
                column: "ServicePeriodId",
                principalSchema: "public",
                principalTable: "ServicePeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InternshipAssignments_Cohorts_CurrentCohortId",
                schema: "public",
                table: "InternshipAssignments",
                column: "CurrentCohortId",
                principalSchema: "public",
                principalTable: "Cohorts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceRecords_ServicePeriods_ServicePeriodId",
                schema: "public",
                table: "AttendanceRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_InternshipAssignments_Cohorts_CurrentCohortId",
                schema: "public",
                table: "InternshipAssignments");

            migrationBuilder.DropTable(
                name: "CohortMembership",
                schema: "public");

            migrationBuilder.DropTable(
                name: "CohortRotationTemplates",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ObjectiveScores",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Cohorts",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ServiceEvaluation",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ServicePeriods",
                schema: "public");

            migrationBuilder.RenameColumn(
                name: "CurrentCohortId",
                schema: "public",
                table: "InternshipAssignments",
                newName: "StageGroupId");

            migrationBuilder.RenameIndex(
                name: "IX_InternshipAssignments_CurrentCohortId",
                schema: "public",
                table: "InternshipAssignments",
                newName: "IX_InternshipAssignments_StageGroupId");

            migrationBuilder.RenameColumn(
                name: "ServicePeriodId",
                schema: "public",
                table: "AttendanceRecords",
                newName: "InternshipAssignmentId");

            migrationBuilder.RenameIndex(
                name: "IX_AttendanceRecords_ServicePeriodId",
                schema: "public",
                table: "AttendanceRecords",
                newName: "IX_AttendanceRecords_InternshipAssignmentId");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                schema: "public",
                table: "Stages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PlannedEnd",
                schema: "public",
                table: "InternshipAssignments",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "PlannedStart",
                schema: "public",
                table: "InternshipAssignments",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<Guid>(
                name: "StageGroupPeriodId",
                schema: "public",
                table: "AttendanceRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssignmentPeriod",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InternshipAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId1 = table.Column<int>(type: "integer", nullable: false),
                    AssignementId = table.Column<Guid>(type: "uuid", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentPeriod", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentPeriod_InternshipAssignments_InternshipAssignment~",
                        column: x => x.InternshipAssignmentId,
                        principalSchema: "public",
                        principalTable: "InternshipAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssignmentPeriod_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "public",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssignmentPeriod_Services_ServiceId1",
                        column: x => x.ServiceId1,
                        principalSchema: "public",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StageGroups",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StageId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageGroups_Stages_StageId",
                        column: x => x.StageId,
                        principalSchema: "public",
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StageGroupPeriods",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    StageGroupId = table.Column<int>(type: "integer", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false)
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
                    PeriodEvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageObjectiveId = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: false)
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
                name: "IX_AttendanceRecords_StageGroupPeriodId",
                schema: "public",
                table: "AttendanceRecords",
                column: "StageGroupPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentPeriod_InternshipAssignmentId",
                schema: "public",
                table: "AssignmentPeriod",
                column: "InternshipAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentPeriod_ServiceId",
                schema: "public",
                table: "AssignmentPeriod",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentPeriod_ServiceId1",
                schema: "public",
                table: "AssignmentPeriod",
                column: "ServiceId1");

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
                name: "IX_StageGroups_StageId",
                schema: "public",
                table: "StageGroups",
                column: "StageId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceRecords_InternshipAssignments_InternshipAssignmen~",
                schema: "public",
                table: "AttendanceRecords",
                column: "InternshipAssignmentId",
                principalSchema: "public",
                principalTable: "InternshipAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceRecords_StageGroupPeriods_StageGroupPeriodId",
                schema: "public",
                table: "AttendanceRecords",
                column: "StageGroupPeriodId",
                principalSchema: "public",
                principalTable: "StageGroupPeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InternshipAssignments_StageGroups_StageGroupId",
                schema: "public",
                table: "InternshipAssignments",
                column: "StageGroupId",
                principalSchema: "public",
                principalTable: "StageGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
