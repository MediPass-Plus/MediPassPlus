namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Why a patient's approve or reject decision was refused. The API maps <see cref="RequestNotFound"/> to <c>404</c>,
/// <see cref="PatientMismatch"/> to <c>403</c>, and the remaining failures to <c>409</c>.
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
    RequestConflict
}
