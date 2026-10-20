using qdisk.Core.Services;

namespace qdisk.Tests;

// Aviso antes de baixar uma imagem que ja esta na pasta.
public class DownloadOverwriteCheckTests
{
    private const string A = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string B = "2222222222222222222222222222222222222222222222222222222222222222";

    [Theory]
    [InlineData(false, true, A, A, DownloadOverwriteCase.NotDownloaded)]
    [InlineData(true, true, A, A, DownloadOverwriteCase.SameAsCatalog)]
    [InlineData(true, true, A, "2222222222222222222222222222222222222222222222222222222222222222", DownloadOverwriteCase.NewVersionInCatalog)]
    [InlineData(true, false, A, A, DownloadOverwriteCase.UnverifiedFile)]
    [InlineData(true, true, "", A, DownloadOverwriteCase.UnverifiedFile)]
    [InlineData(true, true, A, "", DownloadOverwriteCase.SameAsCatalog)]
    public void Classifica(bool exists, bool verified, string stored, string catalog, DownloadOverwriteCase expected)
    {
        Assert.Equal(expected, DownloadOverwriteCheck.Classify(exists, verified, stored, catalog));
    }

    [Fact]
    public void ComparaSemDiferenciarMaiusculas()
    {
        Assert.Equal(DownloadOverwriteCase.SameAsCatalog, DownloadOverwriteCheck.Classify(true, true, A.ToUpperInvariant(), A));
        Assert.Equal(DownloadOverwriteCase.NewVersionInCatalog, DownloadOverwriteCheck.Classify(true, true, A, B));
    }
}
