namespace MediPassPlus.Application.Validation;

/// <summary>
/// Minimal request contract used to demonstrate the shared validation pipeline (NFR-SEC-06).
/// Replace it with the first real module contract when that module lands.
/// </summary>
/// <param name="Name">Display name; required, at most 100 characters.</param>
/// <param name="Email">Email address; required and must be well formed.</param>
public sealed record SampleRequest(string? Name, string? Email);
