using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationSlotReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReservationId",
                table: "VerificationTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssignmentEligibleAt",
                table: "ProjectVerifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BookingConfirmedAt",
                table: "ProjectVerifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "BookingStatus",
                table: "ProjectVerifications",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "ReservationId",
                table: "ProjectVerifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReservedUntil",
                table: "ProjectVerifications",
                type: "timestamp with time zone",
                nullable: true);

            // Preserve existing schedules without inferring payment acceptance or confirming old cases.
            migrationBuilder.Sql("UPDATE \"ProjectVerifications\" SET \"BookingStatus\" = 5 WHERE \"ScheduledStartAt\" IS NOT NULL OR \"ScheduledEndAt\" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerifications_BookingStatus_ReservedUntil",
                table: "ProjectVerifications",
                columns: new[] { "BookingStatus", "ReservedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerifications_ReservationId",
                table: "ProjectVerifications",
                column: "ReservationId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectVerifications_BookingStatus",
                table: "ProjectVerifications",
                sql: "\"BookingStatus\" BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectVerifications_ConfirmedBooking",
                table: "ProjectVerifications",
                sql: "\"BookingStatus\" <> 2 OR (\"BookingConfirmedAt\" IS NOT NULL AND \"AssignmentEligibleAt\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectVerifications_Reservation",
                table: "ProjectVerifications",
                sql: "\"BookingStatus\" NOT IN (1, 2) OR (\"ReservationId\" IS NOT NULL AND \"ReservedUntil\" IS NOT NULL AND \"ScheduledStartAt\" IS NOT NULL AND \"ScheduledEndAt\" IS NOT NULL AND \"ScheduledEndAt\" > \"ScheduledStartAt\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectVerifications_BookingStatus_ReservedUntil",
                table: "ProjectVerifications");

            migrationBuilder.DropIndex(
                name: "IX_ProjectVerifications_ReservationId",
                table: "ProjectVerifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectVerifications_BookingStatus",
                table: "ProjectVerifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectVerifications_ConfirmedBooking",
                table: "ProjectVerifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectVerifications_Reservation",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "VerificationTransactions");

            migrationBuilder.DropColumn(
                name: "AssignmentEligibleAt",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "BookingConfirmedAt",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "BookingStatus",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "ReservedUntil",
                table: "ProjectVerifications");
        }
    }
}
