using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The tracked state a token-based decision needs: the credential whose hash matched the presented token, and the
/// full decision context (request, appointment and every credential) that must be consumed or revoked together.
/// </summary>
/// <param name="Credential">The tracked credential that proved the request.</param>
/// <param name="Decision">The tracked request, appointment and credentials.</param>
public sealed record AccessRequestTokenContext(
    VisitAccessCredential Credential,
    AccessRequestDecisionContext Decision);
