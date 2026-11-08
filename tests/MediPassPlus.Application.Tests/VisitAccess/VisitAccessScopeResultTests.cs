using MediPassPlus.Application.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

public class VisitAccessScopeResultTests
{
    [Fact]
    public void Allowed_WithNullScope_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => VisitAccessScopeResult.Allowed(null!));
    }

    [Fact]
    public void Denied_WithoutAReason_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VisitAccessScopeResult.Denied(VisitAccessScopeFailure.None));
    }
}
