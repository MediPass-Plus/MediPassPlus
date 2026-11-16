namespace MediPassPlus.Application.Dtos.Auth;

/// <summary>
/// The pair a successful login or refresh returns. The refresh token is the only value the client has to keep;
/// the access token is short-lived and is sent as a bearer token.
/// </summary>
/// <param name="AccessToken">The signed JWT for the <c>Authorization</c> header.</param>
/// <param name="RefreshToken">The single-use token to exchange at <c>POST api/Auth/refresh</c>.</param>
/// <param name="RefreshTokenExpiresAtUtc">When the refresh token stops being accepted, in UTC.</param>
/// <param name="AccessTokenExpiresIn">The access token lifetime in seconds.</param>
/// <param name="Roles">The roles the account holds right now; the access token carries the same values.</param>
public sealed record AuthTokensResponse(
    string AccessToken,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    int AccessTokenExpiresIn,
    IReadOnlyList<string> Roles);
