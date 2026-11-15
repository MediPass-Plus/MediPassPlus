using System;
using System.Collections.Generic;
using System.Text;

namespace MediPassPlus.Application.Dtos.Auth;

/// <summary>
/// The body of <c>POST api/Auth/login</c>.
/// </summary>
/// <param name="Email">The account email.</param>
/// <param name="Password">The account password.</param>
/// <param name="DeviceId">Optional client label stored with the refresh token for support only.</param>
public record LoginDto(string Email, string Password, string? DeviceId = null);

