namespace MediPassPlus.Infrastructure.Persistence.Seeding;

/// <summary>
/// Synthetic values used by <see cref="DemoDataSeeder"/>. Every value is a placeholder: names start with "Demo" and
/// every email uses the <c>.test</c> top-level domain, which RFC 2606 reserves for testing, so no real person or
/// mailbox is involved (NFR-SEC-08).
/// </summary>
public static class DemoSeedData
{
    /// <summary>Reserved test domain shared by every demo account.</summary>
    public const string EmailDomain = "@demo.medipass.test";

    public const string ClinicName = "Demo Clinic";
    public const string ClinicAddress = "1 Demo Street, Cairo";
    public const string PhoneNumber = "+20 100 000 0000";

    public const string AdminEmailLocalPart = "admin";
    public const string StaffEmailLocalPart = "staff";
    public const string PatientEmailLocalPart = "patient";
    public const string DoctorOneEmailLocalPart = "doctor1";
    public const string DoctorTwoEmailLocalPart = "doctor2";

    /// <summary>Length of one demo slot in minutes.</summary>
    public const int SlotMinutes = 30;

    /// <summary>How many future days receive demo slots, starting tomorrow.</summary>
    public const int SlotDays = 3;

    /// <summary>Start times (UTC) of the demo slots on each of the <see cref="SlotDays"/> days.</summary>
    public static readonly IReadOnlyList<TimeSpan> SlotStartTimesUtc =
    [
        new(9, 0, 0),
        new(10, 0, 0),
        new(11, 0, 0)
    ];

    /// <summary>The five demo accounts: one admin, one staff member, one patient and two doctors.</summary>
    public static readonly IReadOnlyList<DemoAccount> Accounts =
    [
        new("Demo Admin", AdminEmailLocalPart, DefaultRoles.Admin),
        new("Demo Staff", StaffEmailLocalPart, DefaultRoles.Staff),
        new("Demo Patient", PatientEmailLocalPart, DefaultRoles.Patient),
        new("Dr. Demo One", DoctorOneEmailLocalPart, DefaultRoles.Doctor),
        new("Dr. Demo Two", DoctorTwoEmailLocalPart, DefaultRoles.Doctor)
    ];

    /// <summary>The two demo doctor profiles and their specialties.</summary>
    public static readonly IReadOnlyList<DemoDoctor> Doctors =
    [
        new(DoctorOneEmailLocalPart, "Cardiology"),
        new(DoctorTwoEmailLocalPart, "Dermatology")
    ];

    /// <summary>Builds the full email address of a demo account.</summary>
    /// <param name="emailLocalPart">The local part, for example <c>patient</c>.</param>
    /// <returns>The demo email address, for example <c>patient@demo.medipass.test</c>.</returns>
    public static string Email(string emailLocalPart) => emailLocalPart + EmailDomain;
}

/// <summary>One synthetic demo account.</summary>
/// <param name="FullName">Display name; always clearly synthetic.</param>
/// <param name="EmailLocalPart">Local part of the email; <see cref="DemoSeedData.EmailDomain"/> is appended.</param>
/// <param name="Role">The application role the account belongs to.</param>
public sealed record DemoAccount(string FullName, string EmailLocalPart, string Role)
{
    /// <summary>The full demo email address.</summary>
    public string Email => DemoSeedData.Email(EmailLocalPart);
}

/// <summary>One synthetic demo doctor profile.</summary>
/// <param name="EmailLocalPart">Local part of the account email this profile belongs to.</param>
/// <param name="Specialty">The medical specialty shown in demos.</param>
public sealed record DemoDoctor(string EmailLocalPart, string Specialty)
{
    /// <summary>The full demo email address of the doctor account.</summary>
    public string Email => DemoSeedData.Email(EmailLocalPart);
}
