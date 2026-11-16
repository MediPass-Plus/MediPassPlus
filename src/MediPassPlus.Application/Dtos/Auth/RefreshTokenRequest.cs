using System.ComponentModel.DataAnnotations;

namespace MediPassPlus.Application.Dtos.Auth;

/// <summary>
/// The body of <c>POST api/Auth/refresh</c> and <c>POST api/Auth/logout</c>.
/// </summary>
public sealed class RefreshTokenRequest
{
    /// <summary>
    /// The refresh token issued by login or by the previous refresh. It is single use: the value sent here is
    /// revoked by the call and replaced by a new one.
    /// </summary>
    [Required]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Optional client label such as <c>web</c> or a device name. It is stored for support only.</summary>
    [MaxLength(128)]
    public string? DeviceId { get; set; }
}
