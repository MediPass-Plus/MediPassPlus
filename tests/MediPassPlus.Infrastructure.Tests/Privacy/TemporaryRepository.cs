using MediPassPlus.Infrastructure.Privacy;

namespace MediPassPlus.Infrastructure.Tests.Privacy;

/// <summary>
/// A throw-away checkout layout: a repository root marked by <c>MediPassPlus.slnx</c>, a nested API content root,
/// a key directory inside the repository and one beside it, so the key-path rules can be exercised without
/// touching the real checkout (ADR-0007 decision 5).
/// </summary>
internal sealed class TemporaryRepository : IDisposable
{
    /// <summary>Creates the temporary layout under the operating system's temp path.</summary>
    public TemporaryRepository()
    {
        Root = Path.Combine(Path.GetTempPath(), "MediPassPlus.Tests", Guid.NewGuid().ToString("N"));
        RepositoryRoot = Path.Combine(Root, "repository");
        ContentRoot = Path.Combine(RepositoryRoot, "src", "MediPassPlus.Api");

        Directory.CreateDirectory(ContentRoot);
        File.WriteAllText(
            Path.Combine(RepositoryRoot, DataProtectionKeyPath.RepositoryMarkerFileName),
            "<Solution />");

        InsideRepositoryPath = Path.Combine(RepositoryRoot, "keys");
        OutsideRepositoryPath = Path.Combine(Root, "keys");
    }

    /// <summary>The temp directory that holds the whole layout.</summary>
    public string Root { get; }

    /// <summary>The simulated repository root.</summary>
    public string RepositoryRoot { get; }

    /// <summary>The simulated API content root, nested inside the repository.</summary>
    public string ContentRoot { get; }

    /// <summary>A key directory inside the repository, which must be refused.</summary>
    public string InsideRepositoryPath { get; }

    /// <summary>A key directory beside the repository, which must be accepted.</summary>
    public string OutsideRepositoryPath { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
