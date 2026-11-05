namespace MediPassPlus.Domain.Clinical;

public class Clinic
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Phone { get; set; }

    public ICollection<Doctor> Doctors { get; set; } = new List<Doctor>();
}
