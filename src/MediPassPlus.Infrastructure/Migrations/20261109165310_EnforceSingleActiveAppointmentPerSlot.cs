using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPassPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveAppointmentPerSlot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorSlotId_Active",
                schema: "clinical",
                table: "Appointments",
                column: "DoctorSlotId",
                unique: true,
                filter: "[Status] <> 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_DoctorSlotId_Active",
                schema: "clinical",
                table: "Appointments");
        }
    }
}
