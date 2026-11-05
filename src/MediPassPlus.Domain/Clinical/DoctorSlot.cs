namespace MediPassPlus.Domain.Clinical;

public class DoctorSlot
{
    public int Id { get; set; }

    public int DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;

    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }
    public bool IsAvailable { get; set; } = true;
}
