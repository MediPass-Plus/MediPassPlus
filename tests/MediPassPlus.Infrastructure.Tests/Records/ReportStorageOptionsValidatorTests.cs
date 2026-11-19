using MediPassPlus.Infrastructure.Records;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace MediPassPlus.Infrastructure.Tests.Records;

/// <summary>
/// Configuration tests for the report storage root: patient files are never written inside the repository and never
/// behind <c>wwwroot</c>, and a missing path is fatal in Production but tolerated in Development and Testing
/// (FR-PRV-05, NFR-SEC-09, ADR-0015 decision 6).
/// </summary>
public sealed class ReportStorageOptionsValidatorTests
{
    [Fact]
    public void Missing_Path_Fails_In_Production()
    {
        using var layout = new TemporaryLayout();
        var validator = CreateValidator(Environments.Production, layout.ContentRoot);

        var result = validator.Validate(null, new ReportStorageOptions());

        Assert.True(result.Failed);
        Assert.Contains("Reports:Storage:RootPath", result.FailureMessage);
        Assert.Contains("Production", result.FailureMessage);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Missing_Path_Is_Allowed_Outside_Production(string environmentName)
    {
        using var layout = new TemporaryLayout();
        var validator = CreateValidator(environmentName, layout.ContentRoot);

        var result = validator.Validate(null, new ReportStorageOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Relative_Path_Is_Refused(string environmentName)
    {
        using var layout = new TemporaryLayout();
        var validator = CreateValidator(environmentName, layout.ContentRoot);

        var result = validator.Validate(
            null,
            new ReportStorageOptions { RootPath = Path.Combine("reports", "files") });

        Assert.True(result.Failed);
        Assert.Contains("absolute", result.FailureMessage);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Path_Inside_The_Repository_Is_Refused(string environmentName)
    {
        using var layout = new TemporaryLayout();
        var validator = CreateValidator(environmentName, layout.ContentRoot);

        var result = validator.Validate(
            null,
            new ReportStorageOptions { RootPath = layout.InsideRepositoryPath });

        Assert.True(result.Failed);
        Assert.Contains("outside the checkout", result.FailureMessage);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Path_Inside_The_Web_Root_Is_Refused(string environmentName)
    {
        using var layout = new TemporaryLayout();
        var validator = CreateValidator(environmentName, layout.ContentRoot);

        var result = validator.Validate(
            null,
            new ReportStorageOptions { RootPath = layout.InsideWebRootPath });

        Assert.True(result.Failed);
        Assert.Contains("wwwroot", result.FailureMessage);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Path_Outside_The_Repository_Is_Accepted(string environmentName)
    {
        using var layout = new TemporaryLayout();
        var validator = CreateValidator(environmentName, layout.ContentRoot);

        var result = validator.Validate(
            null,
            new ReportStorageOptions { RootPath = layout.OutsideRepositoryPath });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void The_Content_Root_Itself_Is_Refused()
    {
        using var layout = new TemporaryLayout();
        var validator = CreateValidator(Environments.Development, layout.ContentRoot);

        var result = validator.Validate(null, new ReportStorageOptions { RootPath = layout.ContentRoot });

        Assert.True(result.Failed);
    }

    [Fact]
    public void The_Application_Directory_Is_Refused()
    {
        var validator = CreateValidator(Environments.Development, AppContext.BaseDirectory);

        var result = validator.Validate(
            null,
            new ReportStorageOptions { RootPath = Path.Combine(AppContext.BaseDirectory, "report-files") });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Default_Development_Path_Is_Not_Inside_The_Repository_Or_The_Web_Root()
    {
        Assert.False(ReportStoragePath.IsRefused(ReportStoragePath.DefaultDevelopmentPath, AppContext.BaseDirectory));

        Assert.Equal(ReportStoragePath.DefaultDevelopmentPath, ReportStoragePath.Resolve(null));
        Assert.Equal(ReportStoragePath.DefaultDevelopmentPath, ReportStoragePath.Resolve("   "));
    }

    private static ReportStorageOptionsValidator CreateValidator(string environmentName, string contentRoot) =>
        new(new FakeHostEnvironment(environmentName, contentRoot));

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName, string contentRootPath)
        {
            EnvironmentName = environmentName;
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new NullFileProvider();
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "MediPassPlus.Tests";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; }
    }

    /// <summary>A throw-away checkout layout: a marked repository root, the API content root with a wwwroot, and a
    /// directory beside the repository that must be accepted.</summary>
    private sealed class TemporaryLayout : IDisposable
    {
        public TemporaryLayout()
        {
            Root = Path.Combine(Path.GetTempPath(), "MediPassPlus.Tests", "report-options", Guid.NewGuid().ToString("N"));
            RepositoryRoot = Path.Combine(Root, "repository");
            ContentRoot = Path.Combine(RepositoryRoot, "src", "MediPassPlus.Api");

            Directory.CreateDirectory(ContentRoot);
            Directory.CreateDirectory(Path.Combine(ContentRoot, ReportStoragePath.WebRootDirectoryName));
            File.WriteAllText(
                Path.Combine(RepositoryRoot, "MediPassPlus.slnx"),
                "<Solution />");

            InsideRepositoryPath = Path.Combine(RepositoryRoot, "report-files");
            InsideWebRootPath = Path.Combine(ContentRoot, ReportStoragePath.WebRootDirectoryName, "reports");
            OutsideRepositoryPath = Path.Combine(Root, "report-files");
        }

        public string Root { get; }

        public string RepositoryRoot { get; }

        public string ContentRoot { get; }

        public string InsideRepositoryPath { get; }

        public string InsideWebRootPath { get; }

        public string OutsideRepositoryPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
