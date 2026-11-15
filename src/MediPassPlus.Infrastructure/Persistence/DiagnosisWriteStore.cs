using MediPassPlus.Application.Records;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Records;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>
/// The EF Core unit of work for the doctor write path. The read side resolves the appointment status and the
/// patient's permanent record from the verified grant's patient; the write side stages the append-only medical row so
/// it is committed by the same <c>SaveChanges</c> as its audit entry (FR-REC-03, FR-REC-04, ADR-0005).
/// </summary>
public sealed class DiagnosisWriteStore : IDiagnosisWriteStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="DiagnosisWriteStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public DiagnosisWriteStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<DiagnosisWriteContext?> LoadForAppointmentAsync(
        int appointmentId,
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var status = await _context.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.Id == appointmentId && appointment.PatientId == patientId)
            .Select(appointment => (AppointmentStatus?)appointment.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            return null;
        }

        // One record per patient (ADR-0005). A missing row is an invariant breach, not a reason to create another.
        var medicalRecordId = await _context.MedicalRecords
            .AsNoTracking()
            .Where(record => record.PatientId == patientId)
            .Select(record => (Guid?)record.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return new DiagnosisWriteContext(status.Value, medicalRecordId);
    }

    /// <inheritdoc />
    public void Add(Diagnosis diagnosis)
    {
        ArgumentNullException.ThrowIfNull(diagnosis);
        _context.Diagnoses.Add(diagnosis);
    }

    /// <inheritdoc />
    public void Add(Prescription prescription)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        _context.Prescriptions.Add(prescription);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
