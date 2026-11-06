namespace MediPassPlus.Domain.StaffAdmin;

using MediPassPlus.Domain.Clinical;

public class ClinicStaff
{
    public int Id { get; set; }

    // The Id of the user account (ApplicationUser.Id)
    public Guid UserId { get; set; }

    public int ClinicId { get; set; }
    public Clinic Clinic { get; set; } = null!;
}