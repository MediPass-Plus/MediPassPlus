namespace MediPassPlus.Domain.Clinical;

public class Doctor
{
    public int Id { get; set; }

    // The Id of the user account (ApplicationUser.Id)
    public Guid UserId { get; set; }

    public string Specialty { get; set; } = string.Empty;

    public int ClinicId { get; set; }
    public Clinic Clinic { get; set; } = null!;

    public ICollection<DoctorSlot> Slots { get; set; } = new List<DoctorSlot>();
}
