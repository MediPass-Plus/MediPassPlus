using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPassPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalRecordSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "records");

            migrationBuilder.CreateTable(
                name: "MedicalRecords",
                schema: "records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicalRecords_Users_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Diagnoses",
                schema: "records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MedicalRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<int>(type: "int", nullable: false),
                    SupersedesDiagnosisId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DiagnosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Diagnoses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Diagnoses_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "clinical",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Diagnoses_Diagnoses_SupersedesDiagnosisId",
                        column: x => x.SupersedesDiagnosisId,
                        principalSchema: "records",
                        principalTable: "Diagnoses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Diagnoses_MedicalRecords_MedicalRecordId",
                        column: x => x.MedicalRecordId,
                        principalSchema: "records",
                        principalTable: "MedicalRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                schema: "records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MedicalRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reports_MedicalRecords_MedicalRecordId",
                        column: x => x.MedicalRecordId,
                        principalSchema: "records",
                        principalTable: "MedicalRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reports_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Prescriptions",
                schema: "records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiagnosisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupersedesPrescriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MedicineName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Dosage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prescriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prescriptions_Diagnoses_DiagnosisId",
                        column: x => x.DiagnosisId,
                        principalSchema: "records",
                        principalTable: "Diagnoses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Prescriptions_Prescriptions_SupersedesPrescriptionId",
                        column: x => x.SupersedesPrescriptionId,
                        principalSchema: "records",
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Diagnoses_AppointmentId",
                schema: "records",
                table: "Diagnoses",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Diagnoses_MedicalRecordId_DiagnosedAtUtc",
                schema: "records",
                table: "Diagnoses",
                columns: new[] { "MedicalRecordId", "DiagnosedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Diagnoses_SupersedesDiagnosisId",
                schema: "records",
                table: "Diagnoses",
                column: "SupersedesDiagnosisId",
                unique: true,
                filter: "[SupersedesDiagnosisId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalRecords_PatientId",
                schema: "records",
                table: "MedicalRecords",
                column: "PatientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_DiagnosisId",
                schema: "records",
                table: "Prescriptions",
                column: "DiagnosisId");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_SupersedesPrescriptionId",
                schema: "records",
                table: "Prescriptions",
                column: "SupersedesPrescriptionId",
                unique: true,
                filter: "[SupersedesPrescriptionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_MedicalRecordId_UploadedAtUtc",
                schema: "records",
                table: "Reports",
                columns: new[] { "MedicalRecordId", "UploadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_UploadedByUserId",
                schema: "records",
                table: "Reports",
                column: "UploadedByUserId");

            // Backfill (FR-REC-01, ADR-0005): every patient who registered before the Record module existed gets
            // exactly one record, so the later record endpoints never have to invent one. The NOT EXISTS keeps the
            // statement idempotent, and the migration runs inside the transaction EF opens for it.
            migrationBuilder.Sql(
                """
                INSERT INTO [records].[MedicalRecords] ([Id], [PatientId], [CreatedAtUtc])
                SELECT NEWID(), u.[Id], SYSUTCDATETIME()
                FROM [identity].[Users] AS u
                INNER JOIN [identity].[UserRoles] AS ur ON ur.[UserId] = u.[Id]
                INNER JOIN [identity].[Roles] AS r ON r.[Id] = ur.[RoleId]
                WHERE r.[Name] = N'Patient'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [records].[MedicalRecords] AS m
                      WHERE m.[PatientId] = u.[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Prescriptions",
                schema: "records");

            migrationBuilder.DropTable(
                name: "Reports",
                schema: "records");

            migrationBuilder.DropTable(
                name: "Diagnoses",
                schema: "records");

            migrationBuilder.DropTable(
                name: "MedicalRecords",
                schema: "records");
        }
    }
}
