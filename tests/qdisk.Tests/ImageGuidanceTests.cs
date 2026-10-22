using qdisk.Core.Models;
using qdisk.Core.Services;

namespace qdisk.Tests;

// Descricao das imagens no app: texto do catalogo + "Como gravar" gerado da politica (ISO) ou fixo (TIBX).
public class ImageGuidanceTests
{
    private static CatalogItem Iso(string method, params string[] targets) => new()
    {
        FileName = "x.iso",
        Description = "Descricao",
        UsbWritePolicy = new UsbWritePolicyInfo { PreferredMethod = method, PreferredBootTargets = targets.ToList() },
    };

    [Theory]
    [InlineData("logical-fat32", "Cópia lógica FAT32 — Compatibilidade BIOS e UEFI", "bios", "uefi")]
    [InlineData("uefi-ntfs", "UEFI:NTFS — Compatibilidade somente UEFI", "uefi")]
    [InlineData("raw", "RAW/DD — Compatibilidade BIOS", "bios")]
    [InlineData("raw", "RAW/DD — Compatibilidade BIOS e UEFI", "bios", "uefi")]
    public void ComoGravar_DaPolitica(string method, string expected, params string[] targets)
    {
        Assert.Equal(expected, ImageGuidance.HowToWrite(Iso(method, targets)));
    }

    [Fact]
    public void Tibx_PeloNomeOuPelaUrl()
    {
        Assert.Equal(ImageGuidance.TibxGuidance, ImageGuidance.HowToWrite(new CatalogItem { FileName = "RD-WIN.TIBX" }));
        Assert.Equal(ImageGuidance.TibxGuidance, ImageGuidance.HowToWrite(new CatalogItem { FileUrl = "https://x/pnb7-force.tibx" }));
    }

    [Fact]
    public void Describe_JuntaDescricaoEComoGravar()
    {
        var text = ImageGuidance.Describe(Iso("raw", "bios"));
        Assert.Equal($"Descricao{Environment.NewLine}Como gravar: RAW/DD — Compatibilidade BIOS", text);
    }

    [Fact]
    public void SemDescricaoNemPolitica_Vazio()
    {
        Assert.Equal(string.Empty, ImageGuidance.Describe(new CatalogItem { FileName = "x.iso" }));
        Assert.Equal(string.Empty, ImageGuidance.Describe(null));
    }
}
