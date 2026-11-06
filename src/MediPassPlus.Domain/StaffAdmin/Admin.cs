namespace MediPassPlus.Domain.StaffAdmin;

public class Admin
{
    public int Id { get; set; }

    // The Id of the user account (ApplicationUser.Id)
    public Guid UserId { get; set; }
}