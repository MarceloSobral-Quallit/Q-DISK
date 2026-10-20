using qdisk.Core.Services;

namespace qdisk.Tests;

// P3: manifesto de USB tambem no fallback (mateweb) quando o servidor principal falha.
public class ManifestSourcesTests
{
    private const string Primary = "https://installer.quallit.com.br/qdisk/";
    private const string Fallback = "https://mateweb.ddns.net/qdisk/";

    [Fact]
    public void UrlDoPrincipal_GanhaOMesmoCaminhoNoFallback()
    {
        var urls = ManifestSources.GetCandidateUrls(Primary + "manifests/PNB-Win11-x64.manifest.json", Primary, Fallback);

        Assert.Equal(new[]
        {
            Primary + "manifests/PNB-Win11-x64.manifest.json",
            Fallback + "manifests/PNB-Win11-x64.manifest.json",
        }, urls);
    }

    [Fact]
    public void UrlDeOutroServidor_SoElaMesma()
    {
        var urls = ManifestSources.GetCandidateUrls("https://exemplo.com/m.json", Primary, Fallback);

        Assert.Single(urls);
    }

    [Fact]
    public void BasesSemBarraFinal_AindaReconhece()
    {
        var urls = ManifestSources.GetCandidateUrls(Primary + "manifests/a.json", Primary.TrimEnd('/'), Fallback.TrimEnd('/'));

        Assert.Equal(Fallback + "manifests/a.json", urls[1]);
    }

    [Fact]
    public void UrlVazia_ListaVazia()
    {
        Assert.Empty(ManifestSources.GetCandidateUrls("  ", Primary, Fallback));
    }
}
