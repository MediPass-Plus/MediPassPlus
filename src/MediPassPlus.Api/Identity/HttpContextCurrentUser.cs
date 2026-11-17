using System.Security.Claims;
using MediPassPlus.Application.Identity;
using Microsoft.AspNetCore.Http;

namespace MediPassPlus.Api.Identity;

/// <summary>
/// Resolves the caller from the <see cref="HttpContext"/> of the request being handled. This is the only place a
/// self-service endpoint reads the <c>NameIdentifier</c> claim, and an empty or unparsable value becomes
/// <c>null</c> instead of <see cref="Guid.Empty"/>.
/// </summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initializes a new instance of the <see cref="HttpContextCurrentUser"/> class.</summary>
    /// <param name="httpContextAccessor">Accessor for the request being handled.</param>
    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public Guid? UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?
                .User.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(value, out var userId) && userId != Guid.Empty
                ? userId
                : null;
        }
    }
}
