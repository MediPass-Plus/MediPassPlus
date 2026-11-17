using MediPassPlus.Application.Records;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>
/// Resolves a diagnosis to the appointment that produced it and the patient who owns its record. The join is the
/// only trusted source of a diagnosis' scope; a caller-supplied appointment or patient identifier is ignored
/// (FR-REC-03, FR-REC-10, ADR-0005).
/// </summary>
public sealed class DiagnosisScopeReader : IDiagnosisScopeReader
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="DiagnosisScopeReader"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public DiagnosisScopeReader(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<DiagnosisScope?> FindAsync(
        Guid diagnosisId,
        CancellationToken cancellationToken = default) =>
        (from diagnosis in _context.Diagnoses.AsNoTracking()
         join record in _context.MedicalRecords.AsNoTracking()
             on diagnosis.MedicalRecordId equals record.Id
         where diagnosis.Id == diagnosisId
         select new DiagnosisScope(
             diagnosis.Id,
             diagnosis.AppointmentId,
             record.Id,
             record.PatientId))
        .SingleOrDefaultAsync(cancellationToken);
}
