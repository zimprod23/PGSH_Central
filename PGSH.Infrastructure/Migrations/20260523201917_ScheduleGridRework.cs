using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ScheduleGridRework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cohorts_RotationPlans_RotationPlanId",
                schema: "public",
                table: "Cohorts");

            migrationBuilder.DropForeignKey(
                name: "FK_ServicePeriods_RotationPlanSlots_RotationPlanSlotId",
                schema: "public",
                table: "ServicePeriods");

            migrationBuilder.DropTable(
                name: "RotationPlanSlots",
                schema: "public");

            migrationBuilder.DropTable(
                name: "RotationPlans",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_Cohorts_RotationPlanId",
                schema: "public",
                table: "Cohorts");

            migrationBuilder.DropColumn(
                name: "RotationPlanId",
                schema: "public",
                table: "Cohorts");

            migrationBuilder.RenameColumn(
                name: "RotationPlanSlotId",
                schema: "public",
                table: "ServicePeriods",
                newName: "CohortSlotAssignmentId");

            migrationBuilder.RenameIndex(
                name: "IX_ServicePeriods_RotationPlanSlotId",
                schema: "public",
                table: "ServicePeriods",
                newName: "IX_ServicePeriods_CohortSlotAssignmentId");

            migrationBuilder.CreateTable(
                name: "StageSlots",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StageId = table.Column<int>(type: "integer", nullable: false),
                    PeriodNumber = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageSlots_Stages_StageId",
                        column: x => x.StageId,
                        principalSchema: "public",
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CohortSlotAssignments",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CohortId = table.Column<int>(type: "integer", nullable: false),
                    StageSlotId = table.Column<int>(type: "integer", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CohortSlotAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CohortSlotAssignments_Cohorts_CohortId",
                        column: x => x.CohortId,
                        principalSchema: "public",
                        principalTable: "Cohorts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CohortSlotAssignments_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "public",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CohortSlotAssignments_StageSlots_StageSlotId",
                        column: x => x.StageSlotId,
                        principalSchema: "public",
                        principalTable: "StageSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CohortSlotAssignment_Cohort_Slot",
                schema: "public",
                table: "CohortSlotAssignments",
                columns: new[] { "CohortId", "StageSlotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CohortSlotAssignments_ServiceId",
                schema: "public",
                table: "CohortSlotAssignments",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CohortSlotAssignments_StageSlotId",
                schema: "public",
                table: "CohortSlotAssignments",
                column: "StageSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_StageSlot_Stage_Period",
                schema: "public",
                table: "StageSlots",
                columns: new[] { "StageId", "PeriodNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ServicePeriods_CohortSlotAssignments_CohortSlotAssignmentId",
                schema: "public",
                table: "ServicePeriods",
                column: "CohortSlotAssignmentId",
                principalSchema: "public",
                principalTable: "CohortSlotAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServicePeriods_CohortSlotAssignments_CohortSlotAssignmentId",
                schema: "public",
                table: "ServicePeriods");

            migrationBuilder.DropTable(
                name: "CohortSlotAssignments",
                schema: "public");

            migrationBuilder.DropTable(
                name: "StageSlots",
                schema: "public");

            migrationBuilder.RenameColumn(
                name: "CohortSlotAssignmentId",
                schema: "public",
                table: "ServicePeriods",
                newName: "RotationPlanSlotId");

            migrationBuilder.RenameIndex(
                name: "IX_ServicePeriods_CohortSlotAssignmentId",
                schema: "public",
                table: "ServicePeriods",
                newName: "IX_ServicePeriods_RotationPlanSlotId");

            migrationBuilder.AddColumn<int>(
                name: "RotationPlanId",
                schema: "public",
                table: "Cohorts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RotationPlans",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StageId = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotationPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RotationPlans_Stages_StageId",
                        column: x => x.StageId,
                        principalSchema: "public",
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RotationPlanSlots",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RotationPlanId = table.Column<int>(type: "integer", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    PlannedEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedStart = table.Column<DateOnly>(type: "date", nullable: false),
                    SequenceOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotationPlanSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RotationPlanSlots_RotationPlans_RotationPlanId",
                        column: x => x.RotationPlanId,
                        principalSchema: "public",
                        principalTable: "RotationPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RotationPlanSlots_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "public",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cohorts_RotationPlanId",
                schema: "public",
                table: "Cohorts",
                column: "RotationPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_RotationPlans_StageId",
                schema: "public",
                table: "RotationPlans",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_RotationPlanSlots_RotationPlanId",
                schema: "public",
                table: "RotationPlanSlots",
                column: "RotationPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_RotationPlanSlots_ServiceId",
                schema: "public",
                table: "RotationPlanSlots",
                column: "ServiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Cohorts_RotationPlans_RotationPlanId",
                schema: "public",
                table: "Cohorts",
                column: "RotationPlanId",
                principalSchema: "public",
                principalTable: "RotationPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ServicePeriods_RotationPlanSlots_RotationPlanSlotId",
                schema: "public",
                table: "ServicePeriods",
                column: "RotationPlanSlotId",
                principalSchema: "public",
                principalTable: "RotationPlanSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
