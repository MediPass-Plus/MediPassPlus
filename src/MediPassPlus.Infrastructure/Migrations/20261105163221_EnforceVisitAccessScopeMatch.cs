using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPassPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceVisitAccessScopeMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessGrants_Appointments_AppointmentId",
                schema: "access",
                table: "AccessGrants");

            migrationBuilder.DropForeignKey(
                name: "FK_AccessRequests_Appointments_AppointmentId",
                schema: "access",
                table: "AccessRequests");

            migrationBuilder.DropIndex(
                name: "IX_AccessRequests_AppointmentId",
                schema: "access",
                table: "AccessRequests");

            migrationBuilder.DropIndex(
                name: "IX_AccessGrants_AppointmentId",
                schema: "access",
                table: "AccessGrants");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Appointments_Id_DoctorId_PatientId",
                schema: "clinical",
                table: "Appointments",
                columns: new[] { "Id", "DoctorId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessRequests_AppointmentId_DoctorId_PatientId",
                schema: "access",
                table: "AccessRequests",
                columns: new[] { "AppointmentId", "DoctorId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrants_AppointmentId_DoctorId_PatientId",
                schema: "access",
                table: "AccessGrants",
                columns: new[] { "AppointmentId", "DoctorId", "PatientId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AccessGrants_Appointments_AppointmentId_DoctorId_PatientId",
                schema: "access",
                table: "AccessGrants",
                columns: new[] { "AppointmentId", "DoctorId", "PatientId" },
                principalSchema: "clinical",
                principalTable: "Appointments",
                principalColumns: new[] { "Id", "DoctorId", "PatientId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AccessRequests_Appointments_AppointmentId_DoctorId_PatientId",
                schema: "access",
                table: "AccessRequests",
                columns: new[] { "AppointmentId", "DoctorId", "PatientId" },
                principalSchema: "clinical",
                principalTable: "Appointments",
                principalColumns: new[] { "Id", "DoctorId", "PatientId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessGrants_Appointments_AppointmentId_DoctorId_PatientId",
                schema: "access",
                table: "AccessGrants");

            migrationBuilder.DropForeignKey(
                name: "FK_AccessRequests_Appointments_AppointmentId_DoctorId_PatientId",
                schema: "access",
                table: "AccessRequests");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Appointments_Id_DoctorId_PatientId",
                schema: "clinical",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_AccessRequests_AppointmentId_DoctorId_PatientId",
                schema: "access",
                table: "AccessRequests");

            migrationBuilder.DropIndex(
                name: "IX_AccessGrants_AppointmentId_DoctorId_PatientId",
                schema: "access",
                table: "AccessGrants");

            migrationBuilder.CreateIndex(
                name: "IX_AccessRequests_AppointmentId",
                schema: "access",
                table: "AccessRequests",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrants_AppointmentId",
                schema: "access",
                table: "AccessGrants",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AccessGrants_Appointments_AppointmentId",
                schema: "access",
                table: "AccessGrants",
                column: "AppointmentId",
                principalSchema: "clinical",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AccessRequests_Appointments_AppointmentId",
                schema: "access",
                table: "AccessRequests",
                column: "AppointmentId",
                principalSchema: "clinical",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
