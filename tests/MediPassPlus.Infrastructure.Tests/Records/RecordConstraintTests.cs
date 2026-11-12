using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.Records;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.Records;

/// <summary>
/// Proves that the database itself rejects orphan clinical rows and a second record for the same patient
/// (FR-REC-01, FR-REC-09, ADR-0005). SQLite is used because the model is relational and the test needs no SQL Server.
/// </summary>
public sealed class RecordConstraintTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private MediPassDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        // SQLite enforces foreign keys per connection, and only when the pragma is on.
        await using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new MediPassDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task A_diagnosis_without_a_record_is_rejected_by_the_database()
    {
        var (patientId, _) = await AddPatientWithRecordAsync();
        var appointmentId = await AddAppointmentAsync(patientId);

        _db.Diagnoses.Add(Diagnosis.Create(Guid.NewGuid(), appointmentId, "ciphertext", DateTime.UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_prescription_without_a_diagnosis_is_rejected_by_the_database()
    {
        await AddPatientWithRecordAsync();

        _db.Prescriptions.Add(Prescription.Create(Guid.NewGuid(), "ciphertext", "ciphertext"));

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_report_without_a_record_is_rejected_by_the_database()
    {
        var (patientId, _) = await AddPatientWithRecordAsync();

        _db.Reports.Add(Report.Create(
            Guid.NewGuid(),
            patientId,
            "stored-file.pdf",
            "application/pdf",
            1024,
            DateTime.UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_second_record_for_the_same_patient_is_rejected_by_the_database()
    {
        var (patientId, _) = await AddPatientWithRecordAsync();

        _db.MedicalRecords.Add(MedicalRecord.Create(patientId, DateTime.UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    private async Task<(Guid PatientId, MedicalRecord Record)> AddPatientWithRecordAsync()
    {
        var patientId = await AddUserAsync("Patient");

        var record = MedicalRecord.Create(patientId, DateTime.UtcNow);
        _db.MedicalRecords.Add(record);
        await _db.SaveChangesAsync();

        return (patientId, record);
    }

    private async Task<Guid> AddUserAsync(string role)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser
        {
            UserName = $"{role.ToLowerInvariant()}-{suffix}@demo.medipass.test",
            Email = $"{role.ToLowerInvariant()}-{suffix}@demo.medipass.test",
            FullName = $"Demo {role}",
            Role = role
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return user.Id;
    }

    private async Task<int> AddAppointmentAsync(Guid patientId)
    {
        var clinic = new Clinic { Name = "Demo Clinic", Address = "1 Demo Street, Cairo" };
        _db.Clinics.Add(clinic);
        await _db.SaveChangesAsync();

        var doctor = new Doctor
        {
            UserId = await AddUserAsync("Doctor"),
            Specialty = "Cardiology",
            ClinicId = clinic.Id
        };
        _db.Doctors.Add(doctor);
        await _db.SaveChangesAsync();

        var start = DateTime.UtcNow.AddDays(1);
        var slot = new DoctorSlot
        {
            DoctorId = doctor.Id,
            StartTimeUtc = start,
            EndTimeUtc = start.AddMinutes(30),
            IsAvailable = true
        };
        _db.DoctorSlots.Add(slot);
        await _db.SaveChangesAsync();

        var appointment = new Appointment
        {
            PatientId = patientId,
            DoctorId = doctor.Id,
            DoctorSlotId = slot.Id
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();

        return appointment.Id;
    }
}
