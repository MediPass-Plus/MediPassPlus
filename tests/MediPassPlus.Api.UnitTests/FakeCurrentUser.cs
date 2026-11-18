using MediPassPlus.Application.Identity;

namespace MediPassPlus.Api.UnitTests;

/// <summary>
/// The <see cref="ICurrentUser"/> a controller unit test hands to its subject: the value the real implementation
/// would have read from the token, without an HTTP request in the way. A <c>null</c> id is the unauthenticated case.
/// </summary>
internal sealed class FakeCurrentUser : ICurrentUser
{
    /// <summary>Initializes a new instance of the <see cref="FakeCurrentUser"/> class.</summary>
    /// <param name="userId">The caller id, or <c>null</c> when the request carries no usable token.</param>
    public FakeCurrentUser(Guid? userId)
    {
        UserId = userId;
    }

    /// <inheritdoc />
    public Guid? UserId { get; }
}
