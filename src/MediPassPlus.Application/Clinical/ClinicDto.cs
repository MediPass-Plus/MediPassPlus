namespace MediPassPlus.Application.Clinical;

public class ClinicDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string? Phone { get; init; }
}
