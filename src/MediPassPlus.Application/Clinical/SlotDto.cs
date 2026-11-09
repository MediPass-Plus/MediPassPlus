namespace MediPassPlus.Application.Clinical;

public class SlotDto
{
    public int Id { get; init; }
    public int DoctorId { get; init; }
    public DateTime StartTimeUtc { get; init; }
    public DateTime EndTimeUtc { get; init; }
}
