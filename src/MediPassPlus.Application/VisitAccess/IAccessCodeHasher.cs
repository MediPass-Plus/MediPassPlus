namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Hashes and verifies the six-digit fallback code. The stored form must be a keyed HMAC-SHA-256, because a plain
/// SHA-256 over a one-million-value keyspace is precomputable (ADR-0001, ADR-0007 decision 5).
/// </summary>
public interface IAccessCodeHasher
{
    /// <summary>Computes the protected hash that is stored for a plaintext code.</summary>
    /// <param name="code">The plaintext six-digit code.</param>
    /// <returns>The Base64-encoded HMAC-SHA-256 hash.</returns>
    string Hash(string code);

    /// <summary>Compares a presented code with a stored hash in fixed time.</summary>
    /// <param name="code">The plaintext code presented by the doctor.</param>
    /// <param name="storedHash">The stored hash, as produced by <see cref="Hash"/>.</param>
    /// <returns>True only when the code matches; a malformed stored hash returns false instead of throwing.</returns>
    bool Verify(string code, string storedHash);
}
