using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>
/// The EF Core read store for a scanned QR token. It resolves the token hash to the credential, request and
/// appointment, then reads only the doctor, clinic and slot details the patient needs before deciding (UC-12,
/// FR-ACS-06, MP-97).
/// </summary>
public sealed class AccessRequestScanStore : IAccessRequestScanStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="AccessRequestScanStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public AccessRequestScanStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<AccessRequestScanContext?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        var row = await (
            from credential in _context.VisitAccessCredentials.AsNoTracking()
            join request in _context.AccessRequests.AsNoTracking()
                on credential.AccessRequestId equals request.Id
            join appointment in _context.Appointments.AsNoTracking()
                on request.AppointmentId equals appointment.Id
            join doctor in _context.Doctors.AsNoTracking()
                on appointment.DoctorId equals doctor.Id
            join clinic in _context.Clinics.AsNoTracking()
                on doctor.ClinicId equals clinic.Id
            join doctorUser in _context.Users.AsNoTracking()
                on doctor.UserId equals doctorUser.Id
            join slot in _context.DoctorSlots.AsNoTracking()
                on appointment.DoctorSlotId equals slot.Id
            where credential.SecretHash == tokenHash
                && credential.Type == VisitCredentialType.QrToken
            select new
            {
                Credential = credential,
                Request = request,
                Appointment = appointment,
                DoctorName = doctorUser.FullName,
                DoctorSpecialty = doctor.Specialty,
                ClinicName = clinic.Name,
                ClinicAddress = clinic.Address,
                AppointmentStartsAtUtc = slot.StartTimeUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new AccessRequestScanContext(
            row.Credential,
            row.Request,
            row.Appointment,
            row.DoctorName,
            row.DoctorSpecialty,
            row.ClinicName,
            row.ClinicAddress,
            row.AppointmentStartsAtUtc);
    }
}
