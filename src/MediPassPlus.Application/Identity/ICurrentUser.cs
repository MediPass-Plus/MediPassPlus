namespace MediPassPlus.Application.Identity;

/// <summary>
/// The authenticated caller of the current request. A controller reads the user id from here instead of parsing
/// <c>ClaimTypes.NameIdentifier</c> itself, so the value is a <see cref="Guid"/> and can never be mistaken for a
/// raw string (FR-ACC-07, FR-ACC-08).
/// </summary>
public interface ICurrentUser
{
    /// <summary>Gets the authenticated user's id, or <c>null</c> when the request has no usable token.</summary>
    Guid? UserId { get; }
}
