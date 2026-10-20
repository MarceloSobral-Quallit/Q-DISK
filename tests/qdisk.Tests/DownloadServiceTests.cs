using qdisk.Core.Services;

namespace qdisk.Tests;

// I-02 / C-03: o nome vindo do servidor nunca pode levar a gravacao para fora da pasta.
public class DownloadServiceTests
{
    [Theory]
    [InlineData("PNB-Storex-05-26.iso", "PNB-Storex-05-26.iso")]
    [InlineData(@"..\..\Startup\x.exe", "x.exe")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData(@"C:\Windows\System32\evil.dll", "evil.dll")]
    [InlineData("nome:com*invalidos?.iso", "nome_com_invalidos_.iso")]
    [InlineData("arquivo.iso. . ", "arquivo.iso")]
    public void SanitizeFileName_RemovePastasECaracteresInvalidos(string input, string expected)
    {
        Assert.Equal(expected, DownloadService.SanitizeFileName(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(@"pasta\")]
    public void SanitizeFileName_RecusaNomesVazios(string? input)
    {
        Assert.Equal(string.Empty, DownloadService.SanitizeFileName(input!));
    }
}
