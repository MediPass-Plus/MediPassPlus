using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// A test-only endpoint that always throws, so the pipeline's 500 path can be exercised: the correlation header
/// after the exception handler cleared the response (NFR-REL-02), and the redaction of an exception message
/// (FR-PRV-08). It is excluded from the generated document because it is not part of the product API.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/test/throw")]
public sealed class ThrowingTestController : ControllerBase
{
    /// <summary>
    /// A synthetic patient detail — a made-up name and the placeholder number <c>010</c> plus eight zeros, not a real
    /// person — that must never reach a log line (FR-PRV-08). The tests assert this exact value is absent from the
    /// captured output, so it is a canary: realistic enough that a leak looks like the leak it is meant to catch.
    /// </summary>
    public const string SensitiveValue = "Nagham Ahmed 01000000000";

    /// <summary>Throws so the shared exception handler writes the <c>500</c> response.</summary>
    /// <returns>Never returns; the expression always throws.</returns>
    [HttpGet]
    public IActionResult Get() => throw new InvalidOperationException(
        $"Patient {SensitiveValue} could not be loaded.");
}
