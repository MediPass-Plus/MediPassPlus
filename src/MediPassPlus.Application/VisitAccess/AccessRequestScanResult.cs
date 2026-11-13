namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The outcome of scanning a QR token: either the details the patient must see before deciding, or a refusal
/// reason. A result is never both.
/// </summary>
public sealed class AccessRequestScanResult
{
    private AccessRequestScanResult(
        AccessRequestScanResponse? response,
        AccessRequestScanFailure failure)
    {
        Response = response;
        Failure = failure;
    }

    /// <summary>The scanned request details, or null when the scan was refused.</summary>
    public AccessRequestScanResponse? Response { get; }

    /// <summary>The refusal reason, or <see cref="AccessRequestScanFailure.None"/> when the scan succeeded.</summary>
    public AccessRequestScanFailure Failure { get; }

    /// <summary>True when <see cref="Response"/> is available.</summary>
    public bool IsSuccess => Response is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="response">The details the patient sees before deciding.</param>
    public static AccessRequestScanResult Success(AccessRequestScanResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new AccessRequestScanResult(response, AccessRequestScanFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="AccessRequestScanFailure.None"/>.</param>
    public static AccessRequestScanResult Denied(AccessRequestScanFailure failure)
    {
        if (failure == AccessRequestScanFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new AccessRequestScanResult(null, failure);
    }
}
