namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Why a patient's approve or reject decision was refused. The API maps <see cref="RequestNotFound"/> and
/// <see cref="TokenNotFound"/> to <c>403</c> or <c>404</c> depending on the route, <see cref="PatientMismatch"/> to
/// <c>403</c>, <see cref="TokenUnusable"/> and <see cref="VisitNotOpen"/> to <c>410</c> on the token path, and the
/// remaining failures to <c>409</c>.
/// </summary>
public enum AccessRequestDecisionFailure
{
    /// <summary>The decision succeeded.</summary>
    None = 0,

    /// <summary>No access request has the requested identifier, or its scope cannot be resolved.</summary>
    RequestNotFound,

    /// <summary>The authenticated patient does not own the request's appointment.</summary>
    PatientMismatch,

    /// <summary>The request was already approved or rejected.</summary>
    AlreadyAnswered,

    /// <summary>Another decision won the race, or the appointment already has a grant.</summary>
    RequestConflict,

    /// <summary>No QR credential matches, or it belongs to another patient's appointment.</summary>
    TokenNotFound,

    /// <summary>The QR token is expired, consumed, revoked or locked, or its request was already answered.</summary>
    TokenUnusable,

    /// <summary>The appointment is no longer in progress, so a new approval would be meaningless.</summary>
    VisitNotOpen
}
