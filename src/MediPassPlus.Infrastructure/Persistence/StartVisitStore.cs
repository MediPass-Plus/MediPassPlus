using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>The EF Core unit of work for starting a visit.</summary>
public sealed class StartVisitStore : IVisitStartStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="StartVisitStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public StartVisitStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<StartVisitContext?> LoadAsync(
        int appointmentId,
        int doctorId,
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await _context.Appointments
            .Include(appointment => appointment.DoctorSlot)
            .SingleOrDefaultAsync(
                appointment => appointment.Id == appointmentId
                    && appointment.DoctorId == doctorId
                    && appointment.PatientId == patientId,
                cancellationToken);

        if (appointment is null)
        {
            return null;
        }

        var doctor = await _context.Doctors
            .SingleOrDefaultAsync(doctor => doctor.Id == doctorId, cancellationToken);

        if (doctor is null)
        {
            return null;
        }

        var requests = await _context.AccessRequests
            .Where(request => request.AppointmentId == appointmentId)
            .OrderByDescending(request => request.CreatedAtUtc)
            .ThenByDescending(request => request.Id)
            .ToListAsync(cancellationToken);

        var requestIds = requests.Select(request => request.Id).ToArray();

        List<VisitAccessCredential> liveQrCredentials;
        if (requestIds.Length == 0)
        {
            liveQrCredentials = [];
        }
        else
        {
            liveQrCredentials = await _context.VisitAccessCredentials
                .Where(credential => requestIds.Contains(credential.AccessRequestId)
                    && credential.Type == VisitCredentialType.QrToken
                    && credential.RevokedAtUtc == null
                    && credential.ConsumedAtUtc == null)
                .ToListAsync(cancellationToken);
        }

        return new StartVisitContext(appointment, doctor, requests, liveQrCredentials);
    }

    /// <inheritdoc />
    public void AddRequest(AccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _context.AccessRequests.Add(request);
    }

    /// <inheritdoc />
    public void AddCredential(VisitAccessCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _context.VisitAccessCredentials.Add(credential);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
