namespace MediPassPlus.Infrastructure.Persistence.Seeding;

/// <summary>
/// The four application roles every environment expects to exist (FR-ACC-03). They are seeded at startup and are the
/// single source of the role names, so a seeder never repeats a string literal.
/// </summary>
public static class DefaultRoles
{
    public const string Admin = "Admin";
    public const string Patient = "Patient";
    public const string Doctor = "Doctor";
    public const string Staff = "Staff";

    /// <summary>Every role the application seeds at startup.</summary>
    public static readonly IReadOnlyList<string> All = [Admin, Patient, Doctor, Staff];
}
