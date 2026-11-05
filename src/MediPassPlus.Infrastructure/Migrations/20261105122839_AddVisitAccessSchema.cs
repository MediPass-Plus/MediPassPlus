using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPassPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitAccessSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "access");

            migrationBuilder.CreateTable(
                name: "AccessRequests",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<int>(type: "int", nullable: false),
                    DoctorId = table.Column<int>(type: "int", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessRequests_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "clinical",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessRequests_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalSchema: "clinical",
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessRequests_Users_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccessGrants",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<int>(type: "int", nullable: false),
                    DoctorId = table.Column<int>(type: "int", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessGrants_AccessRequests_AccessRequestId",
                        column: x => x.AccessRequestId,
                        principalSchema: "access",
                        principalTable: "AccessRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessGrants_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "clinical",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessGrants_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalSchema: "clinical",
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessGrants_Users_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VisitAccessCredentials",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SecretHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitAccessCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitAccessCredentials_AccessRequests_AccessRequestId",
                        column: x => x.AccessRequestId,
                        principalSchema: "access",
                        principalTable: "AccessRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrants_AccessRequestId",
                schema: "access",
                table: "AccessGrants",
                column: "AccessRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrants_AppointmentId",
                schema: "access",
                table: "AccessGrants",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrants_DoctorId",
                schema: "access",
                table: "AccessGrants",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrants_PatientId",
                schema: "access",
                table: "AccessGrants",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessRequests_AppointmentId",
                schema: "access",
                table: "AccessRequests",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessRequests_DoctorId",
                schema: "access",
                table: "AccessRequests",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessRequests_PatientId",
                schema: "access",
                table: "AccessRequests",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitAccessCredentials_AccessRequestId_Type",
                schema: "access",
                table: "VisitAccessCredentials",
                columns: new[] { "AccessRequestId", "Type" },
                unique: true,
                filter: "[RevokedAtUtc] IS NULL AND [ConsumedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VisitAccessCredentials_SecretHash",
                schema: "access",
                table: "VisitAccessCredentials",
                column: "SecretHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessGrants",
                schema: "access");

            migrationBuilder.DropTable(
                name: "VisitAccessCredentials",
                schema: "access");

            migrationBuilder.DropTable(
                name: "AccessRequests",
                schema: "access");
        }
    }
}
