namespace MediPassPlus.Application.Clinical;

public class DoctorDto
{
    public int Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Specialty { get; init; } = string.Empty;
    public int ClinicId { get; init; }
    public string ClinicName { get; init; } = string.Empty;
}
