using qdisk.Core.Models;
using qdisk.Core.Services;

namespace qdisk.Tests;

// "Verificar atualizacoes de imagens na inicializacao": imagem baixada com SHA-256 diferente do catalogo atual.
public class ImageUpdateCheckTests
{
    private const string Old = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string New = "2222222222222222222222222222222222222222222222222222222222222222";

    private static CatalogItem Item(string name, string file, string sha) => new() { Name = name, FileName = file, Sha256 = sha };

    private static LocalImageRecord Record(string name, string path, string sha, bool downloaded = true) =>
        new() { CatalogName = name, FilePath = path, ImageSha256 = sha, Downloaded = downloaded };

    private static readonly Func<string, bool> AllExist = _ => true;

    [Fact]
    public void ShaDiferente_VersaoNova()
    {
        var outdated = ImageUpdateCheck.FindOutdated(
            new[] { Item("rd-linux-sharepoint", "RD-LINUX.TIBX", New) },
            new[] { Record("rd-linux-sharepoint", @"C:\img\RD-LINUX.TIBX", Old) }, AllExist);

        var image = Assert.Single(outdated);
        Assert.Equal("RD-LINUX.TIBX", image.FileName);
        Assert.Equal(Old, image.LocalSha256);
        Assert.Equal(New, image.CatalogSha256);
    }

    [Fact]
    public void MesmoSha_EmDia()
    {
        Assert.Empty(ImageUpdateCheck.FindOutdated(
            new[] { Item("a", "A.ISO", New.ToUpperInvariant()) },
            new[] { Record("a", @"C:\img\A.ISO", New) }, AllExist));
    }

    [Fact]
    public void ParSharepointDropbox_UmAvisoPorArquivo_EOutroRegistroEmDia()
    {
        var catalog = new[] { Item("x-sharepoint", "X.ISO", New), Item("x-dropbox", "X.ISO", New) };

        // So o item -sharepoint foi baixado: o -dropbox acha o mesmo arquivo pelo nome; um aviso so.
        var oneRecord = ImageUpdateCheck.FindOutdated(catalog, new[] { Record("x-sharepoint", @"C:\img\X.ISO", Old) }, AllExist);
        Assert.Single(oneRecord);

        // O arquivo ja foi baixado de novo pelo -dropbox (registro com o SHA novo): em dia.
        var updated = ImageUpdateCheck.FindOutdated(catalog, new[]
        {
            Record("x-sharepoint", @"C:\img\X.ISO", Old),
            Record("x-dropbox", @"C:\img\X.ISO", New),
        }, AllExist);
        Assert.Empty(updated);
    }

    [Fact]
    public void SemArquivoSemHashOuNaoBaixado_Ignora()
    {
        var catalog = new[] { Item("a", "A.ISO", New), Item("b", "B.ISO", New), Item("c", "C.ISO", New), Item("d", "D.ISO", "") };
        var records = new[]
        {
            Record("a", @"C:\img\A.ISO", Old),                    // arquivo apagado
            Record("b", @"C:\img\B.ISO", ""),                     // sem SHA-256 registrado
            Record("c", @"C:\img\C.ISO", Old, downloaded: false), // download nao concluido
            Record("d", @"C:\img\D.ISO", Old),                    // item sem SHA-256 no catalogo
        };

        Assert.Empty(ImageUpdateCheck.FindOutdated(catalog, records, path => !path.EndsWith("A.ISO")));
    }
}
