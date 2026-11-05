using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPassPlus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceAppointmentDoctorSlotMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_DoctorSlots_DoctorSlotId",
                schema: "clinical",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_DoctorSlotId",
                schema: "clinical",
                table: "Appointments");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DoctorSlots_Id_DoctorId",
                schema: "clinical",
                table: "DoctorSlots",
                columns: new[] { "Id", "DoctorId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorSlotId_DoctorId",
                schema: "clinical",
                table: "Appointments",
                columns: new[] { "DoctorSlotId", "DoctorId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_DoctorSlots_DoctorSlotId_DoctorId",
                schema: "clinical",
                table: "Appointments",
                columns: new[] { "DoctorSlotId", "DoctorId" },
                principalSchema: "clinical",
                principalTable: "DoctorSlots",
                principalColumns: new[] { "Id", "DoctorId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_DoctorSlots_DoctorSlotId_DoctorId",
                schema: "clinical",
                table: "Appointments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DoctorSlots_Id_DoctorId",
                schema: "clinical",
                table: "DoctorSlots");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_DoctorSlotId_DoctorId",
                schema: "clinical",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorSlotId",
                schema: "clinical",
                table: "Appointments",
                column: "DoctorSlotId");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_DoctorSlots_DoctorSlotId",
                schema: "clinical",
                table: "Appointments",
                column: "DoctorSlotId",
                principalSchema: "clinical",
                principalTable: "DoctorSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
