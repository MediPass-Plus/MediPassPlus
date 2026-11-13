namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Why a scanned QR token was refused. The API maps <see cref="TokenNotFound"/> to <c>403</c> (the same answer for
/// an unknown token and another patient's token), <see cref="TokenUnusable"/> and <see cref="VisitNotOpen"/> to
/// <c>410</c>, and <see cref="ImageNotReadable"/> to <c>400</c>.
/// </summary>
public enum AccessRequestScanFailure
{
    /// <summary>The scan succeeded.</summary>
    None = 0,

    /// <summary>No token matches, or the token belongs to another patient's appointment.</summary>
    TokenNotFound,

    /// <summary>The token is expired, consumed, revoked or locked, or its request was already answered.</summary>
    TokenUnusable,

    /// <summary>The appointment is no longer in progress, so no new approval can be made.</summary>
    VisitNotOpen,

    /// <summary>The uploaded image is missing, corrupt or contains no readable QR code.</summary>
    ImageNotReadable
}
