using qdisk.Core.Models;
using qdisk.Core.Services;

namespace qdisk.Tests;

// P2 etapa 5: o app so usa a usbWritePolicy coerente com o item e com a ISO local.
public class UsbWritePolicyResolverTests
{
    private const string Sha = "1ab8f7d50c804abf209fe9819916c5035d4e9eb1d76f79304e519f56865194ce";
    private const long Size = 15_703_867_392;

    private static CatalogItem Item(string name = "PNB-Win11-x64-sharepoint", UsbWritePolicyInfo? policy = null) => new()
    {
        Name = name,
        FileName = "PNB-Win11-x64.iso",
        Sha256 = Sha,
        ImageSizeBytes = Size,
        UsbWritePolicy = policy ?? Policy("uefi-ntfs", "uefi-ntfs"),
    };

    private static UsbWritePolicyInfo Policy(string preferred, params string[] allowed) => new()
    {
        ImageSha256 = Sha,
        PreferredMethod = preferred,
        AllowedMethods = allowed.ToList(),
        PreserveSourceImage = true,
        AllowContentTransformations = false,
    };

    private static readonly UsbWritePolicyResolver Resolver = new();

    [Fact]
    public void PoliticaCoerente_EAplicada()
    {
        var decision = Resolver.Resolve(Item(), Size);

        Assert.True(decision.IsApplied);
        Assert.Equal("uefi-ntfs", decision.PreferredMethod);
        Assert.Equal(new[] { "uefi-ntfs" }, decision.AllowedMethods);
    }

    [Fact]
    public void SemPolitica_UsaAnaliseLocal()
    {
        var item = Item();
        item.UsbWritePolicy = null;

        var decision = Resolver.Resolve(item, Size);

        Assert.False(decision.IsApplied);
        Assert.Contains("sem usbWritePolicy", decision.IgnoredReason);
    }

    [Fact]
    public void TamanhoLocalDiferente_Ignora()
    {
        Assert.False(Resolver.Resolve(Item(), Size - 1).IsApplied);
    }

    [Fact]
    public void PoliticaDeOutraImagem_Ignora()
    {
        var policy = Policy("raw", "raw");
        policy.ImageSha256 = new string('0', 64);

        var decision = Resolver.Resolve(Item(policy: policy), Size);

        Assert.False(decision.IsApplied);
        Assert.Contains("outra imagem", decision.IgnoredReason);
    }

    [Fact]
    public void SplitSemPermissaoExplicita_Ignora()
    {
        var policy = Policy("uefi-ntfs", "uefi-ntfs", "fat32-split-wim");
        policy.AllowContentTransformations = null;

        Assert.False(Resolver.Resolve(Item(policy: policy), Size).IsApplied);
    }

    [Fact]
    public void PreferidoForaDosPermitidos_Ignora()
    {
        Assert.False(Resolver.Resolve(Item(policy: Policy("raw", "uefi-ntfs")), Size).IsApplied);
    }

    [Fact]
    public void FindCatalogItem_PeloNomeDoDownload()
    {
        var items = new[] { Item("a"), Item("b") };

        var found = Resolver.FindCatalogItem(items, "b", "outro.iso", 1, out var reason);

        Assert.Equal("b", found?.Name);
        Assert.Null(reason);
    }

    [Fact]
    public void FindCatalogItem_ParesComMesmaPolitica_PorArquivoETamanho()
    {
        var items = new[] { Item("x-sharepoint"), Item("x-dropbox") };

        var found = Resolver.FindCatalogItem(items, "", "pnb-win11-x64.ISO", Size, out _);

        Assert.Equal("x-sharepoint", found?.Name);
    }

    [Fact]
    public void FindCatalogItem_ParesComPoliticasDiferentes_NaoEscolhe()
    {
        var items = new[] { Item("x-sharepoint"), Item("x-dropbox", Policy("raw", "raw")) };

        var found = Resolver.FindCatalogItem(items, "", "PNB-Win11-x64.iso", Size, out var reason);

        Assert.Null(found);
        Assert.Contains("politicas diferentes", reason);
    }

    [Fact]
    public void FindCatalogItem_IsoAvulsa_NaoEncontra()
    {
        var found = Resolver.FindCatalogItem(new[] { Item() }, "", "STOREX_UBUNTU.iso", 5_215_164_416, out var reason);

        Assert.Null(found);
        Assert.Contains("fora dos catalogos", reason);
    }
}
