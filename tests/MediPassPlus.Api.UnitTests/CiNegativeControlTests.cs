namespace MediPassPlus.Api.UnitTests;

/// <summary>
/// The CI negative control. It passes in every normal run and fails only when the backend job asks for it, so the
/// pipeline can prove that a failing test really turns the build red (MP-54).
/// </summary>
/// <remarks>
/// The workflow runs this class with <c>MEDIPASS_CI_NEGATIVE=1</c> and fails the step when <c>dotnet test</c>
/// returns <c>0</c>. Do not delete it to make a red build green: its failure is the expected result there.
/// </remarks>
public sealed class CiNegativeControlTests
{
    [Fact]
    public void IntentionalFailure_FailsOnlyWhenTheNegativeControlIsRequested()
    {
        if (Environment.GetEnvironmentVariable("MEDIPASS_CI_NEGATIVE") != "1")
        {
            return;
        }

        Assert.Fail("The CI negative control. This failure is expected and must fail the pipeline.");
    }
}
