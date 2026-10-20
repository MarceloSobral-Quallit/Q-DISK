using qdisk.Core.Services;

namespace qdisk.Tests;

// A versao exibida deve ser igual a do Directory.Build.props (3.00.10.26), nao a do System.Version (3.0.10.26).
public class AppVersionFormatTests
{
    [Theory]
    [InlineData("3.00.10.26", "3.00.10.26")]
    [InlineData("2.16.07.26", "2.16.07.26")]
    [InlineData("2.22.7.26", "2.22.07.26")]
    [InlineData("1.142.07.26", "1.142.07.26")]
    public void Format_RestauraZerosAEsquerda(string input, string expected)
    {
        Assert.Equal(expected, AppVersionFormat.Format(Version.Parse(input)));
    }

    [Fact]
    public void Format_Nulo_RetornaNulo()
    {
        Assert.Null(AppVersionFormat.Format(null!));
    }
}
