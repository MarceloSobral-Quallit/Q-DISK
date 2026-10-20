using qdisk.Core.Services;

namespace qdisk.Tests;

// U-03: redirecionamentos e URLs de catalogo so em HTTPS.
public class UrlPolicyTests
{
    [Theory]
    [InlineData("https://installer.quallit.com.br/qdisk/x.json", true)]
    [InlineData("  https://quallit.link/pnb-storex-sp  ", true)]
    [InlineData("http://installer.quallit.com.br/qdisk/x.json", false)]
    [InlineData("ftp://host/x.iso", false)]
    [InlineData("file:///C:/Windows/x.exe", false)]
    [InlineData("/relativo/x.json", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsHttps(string? url, bool expected)
    {
        Assert.Equal(expected, UrlPolicy.IsHttps(url!));
    }
}
