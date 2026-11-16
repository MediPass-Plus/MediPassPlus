using MediPassPlus.Infrastructure.Privacy;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace MediPassPlus.Infrastructure.Tests.Privacy;

/// <summary>
/// Configuration tests for the key-ring path: keys are never written inside the repository, and a missing path is
/// fatal in Production but tolerated in Development and Testing (FR-PRV-04, NFR-SEC-05, ADR-0007 decision 5).
/// </summary>
public sealed class DataProtectionOptionsValidatorTests
{
    [Fact]
    public void Missing_Path_Fails_In_Production()
    {
        using var repository = new TemporaryRepository();
        var validator = CreateValidator(Environments.Production, repository.ContentRoot);

        var result = validator.Validate(null, new DataProtectionOptions());

        Assert.True(result.Failed);
        Assert.Contains("DataProtection:KeysPath", result.FailureMessage);
        Assert.Contains("Production", result.FailureMessage);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Missing_Path_Is_Allowed_Outside_Production(string environmentName)
    {
        using var repository = new TemporaryRepository();
        var validator = CreateValidator(environmentName, repository.ContentRoot);

        var result = validator.Validate(null, new DataProtectionOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Relative_Path_Is_Refused(string environmentName)
    {
        using var repository = new TemporaryRepository();
        var validator = CreateValidator(environmentName, repository.ContentRoot);

        var result = validator.Validate(null, new DataProtectionOptions { KeysPath = Path.Combine("keys", "dp") });

        Assert.True(result.Failed);
        Assert.Contains("absolute", result.FailureMessage);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Path_Inside_The_Repository_Is_Refused(string environmentName)
    {
        using var repository = new TemporaryRepository();
        var validator = CreateValidator(environmentName, repository.ContentRoot);

        var result = validator.Validate(
            null,
            new DataProtectionOptions { KeysPath = repository.InsideRepositoryPath });

        Assert.True(result.Failed);
        Assert.Contains("outside", result.FailureMessage);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Path_Outside_The_Repository_Is_Accepted(string environmentName)
    {
        using var repository = new TemporaryRepository();
        var validator = CreateValidator(environmentName, repository.ContentRoot);

        var result = validator.Validate(
            null,
            new DataProtectionOptions { KeysPath = repository.OutsideRepositoryPath });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Repository_Root_Itself_Is_Refused()
    {
        using var repository = new TemporaryRepository();
        var validator = CreateValidator(Environments.Development, repository.ContentRoot);

        var result = validator.Validate(
            null,
            new DataProtectionOptions { KeysPath = repository.RepositoryRoot });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Application_Directory_Is_Refused()
    {
        var validator = CreateValidator(Environments.Development, AppContext.BaseDirectory);

        var result = validator.Validate(
            null,
            new DataProtectionOptions { KeysPath = Path.Combine(AppContext.BaseDirectory, "dp-keys") });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Default_Development_Path_Is_Outside_The_Repository()
    {
        Assert.False(
            DataProtectionKeyPath.IsInsideRepository(
                DataProtectionKeyPath.DefaultDevelopmentPath,
                AppContext.BaseDirectory));

        Assert.Equal(
            DataProtectionKeyPath.DefaultDevelopmentPath,
            DataProtectionKeyPath.Resolve(null));
    }

    private static DataProtectionOptionsValidator CreateValidator(string environmentName, string contentRoot) =>
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
}
