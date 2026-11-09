namespace MediPassPlus.Application.Booking;

/// <summary>The request body for booking a doctor's slot.</summary>
public sealed class BookAppointmentRequest
{
    /// <summary>The doctor the patient wants to book with.</summary>
    public int DoctorId { get; set; }

    /// <summary>The slot the patient wants to book.</summary>
    public int SlotId { get; set; }
}
