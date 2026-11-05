namespace MediPassPlus.Domain.Clinical;

public class Appointment
{
    public int Id { get; set; }

    // The Id of the patient's user account (ApplicationUser.Id)
    public Guid PatientId { get; set; }

    public int DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;

    public int DoctorSlotId { get; set; }
    public DoctorSlot DoctorSlot { get; set; } = null!;

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Booked;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
