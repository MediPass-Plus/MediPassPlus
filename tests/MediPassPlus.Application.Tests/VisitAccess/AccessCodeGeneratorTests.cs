using System.Globalization;
using System.Text.RegularExpressions;
using MediPassPlus.Application.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

public class AccessCodeGeneratorTests
{
    [Fact]
    public void Generate_AlwaysReturnsSixAsciiDigits()
    {
        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var code = AccessCodeGenerator.Generate();

            Assert.Equal(6, code.Length);
            Assert.Matches(new Regex("^[0-9]{6}$"), code);
            Assert.InRange(int.Parse(code, CultureInfo.InvariantCulture), 0, 999_999);
        }
    }

    [Fact]
    public void Generate_ReturnsDifferentValuesAcrossCalls()
    {
        var codes = Enumerable.Range(0, 50)
            .Select(_ => AccessCodeGenerator.Generate())
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(codes.Count > 1);
    }
}
