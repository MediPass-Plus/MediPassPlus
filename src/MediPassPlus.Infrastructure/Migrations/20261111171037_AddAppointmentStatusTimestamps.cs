using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPassPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentStatusTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DoctorSlots_DoctorId",
                schema: "clinical",
                table: "DoctorSlots");

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                schema: "clinical",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInAtUtc",
                schema: "clinical",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                schema: "clinical",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InVisitAtUtc",
                schema: "clinical",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DoctorSlots_DoctorId_StartTimeUtc",
                schema: "clinical",
                table: "DoctorSlots",
                columns: new[] { "DoctorId", "StartTimeUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DoctorSlots_DoctorId_StartTimeUtc",
                schema: "clinical",
                table: "DoctorSlots");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                schema: "clinical",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "CheckedInAtUtc",
                schema: "clinical",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                schema: "clinical",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "InVisitAtUtc",
                schema: "clinical",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorSlots_DoctorId",
                schema: "clinical",
                table: "DoctorSlots",
                column: "DoctorId");
        }
    }
}
