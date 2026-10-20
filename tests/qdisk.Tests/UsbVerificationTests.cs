using System.Security.Cryptography;
using qdisk.Core.Services;

namespace qdisk.Tests;

// Etapa 3 da separacao da UsbBootView (P10.1): manifesto USB, conferencia contra o manifesto, regra da verificacao
// logica e avaliacao do modo de boot, com arquivos numa pasta temporaria e setores em memoria.
public sealed class UsbVerificationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("quallit-usb-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Put(string relative, string content)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Sha(string content) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static Task<string> HashFile(string path, CancellationToken token) =>
        Task.FromResult(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());

    [Fact]
    public void Manifesto_LeArquivosEIgnoraEntradaIncompleta()
    {
        var json = """
            {"files":[
              {"path":"boot/bootmgr","size":10,"sha256":"ABC"},
              {"path":"sources/install.wim","size":5000000000},
              {"path":"sem-tamanho.txt"}
            ]}
            """;
        var manifest = UsbManifest.Parse(json, "cache");

        Assert.Equal("cache", manifest.Source);
        Assert.Equal(2, manifest.Entries.Count);
        Assert.Equal("ABC", manifest.Entries[0].Sha256);
        Assert.Equal(5_000_000_000L, manifest.Entries[1].SizeBytes);
        Assert.Null(manifest.Entries[1].Sha256);
        Assert.Throws<InvalidDataException>(() => UsbManifest.Parse("{\"x\":1}", "local", "m.json"));
        Assert.Equal(@"efi\boot\bootx64.efi", UsbManifest.NormalizeRelativePath("/efi/boot/bootx64.efi"));
    }

    [Fact]
    public async Task Manifesto_TudoConferidoPorHash_Sucesso()
    {
        Put("bootmgr", "bm");
        Put("efi/boot/bootx64.efi", "efi");
        var entries = new List<UsbManifestEntry>
        {
            new() { RelativePath = "bootmgr", SizeBytes = 2, Sha256 = Sha("bm") },
            new() { RelativePath = "efi/boot/bootx64.efi", SizeBytes = 3, Sha256 = Sha("efi").ToUpperInvariant() },
        };
        var progress = new List<int>();

        var result = await ManifestVerifier.VerifyAsync(_root, entries, HashFile, r => { progress.Add(r.Checked); return Task.CompletedTask; });

        Assert.Equal(UsbCheckStatus.Success, result.Status);
        Assert.Equal("alta", result.Confidence);
        Assert.Equal(2, result.FilesVerified);
        Assert.Equal(new[] { 1, 2 }, progress);
        Assert.Empty(result.Details);
    }

    [Fact]
    public async Task Manifesto_AusenteTamanhoEHashDivergentes_Falha()
    {
        Put("a.txt", "aaa");
        Put("b.txt", "bbb");
        var entries = new List<UsbManifestEntry>
        {
            new() { RelativePath = "a.txt", SizeBytes = 99, Sha256 = Sha("aaa") },
            new() { RelativePath = "b.txt", SizeBytes = 3, Sha256 = Sha("outro") },
            new() { RelativePath = "c.txt", SizeBytes = 1 },
        };

        var result = await ManifestVerifier.VerifyAsync(_root, entries, HashFile);

        Assert.Equal(UsbCheckStatus.Failed, result.Status);
        Assert.Equal(2, result.MismatchCount);
        Assert.Equal(1, result.MissingCount);
        Assert.Equal(new[] { "Tamanho divergente: a.txt", "Hash divergente: b.txt", "Ausente: c.txt" }, result.Details);
    }

    [Fact]
    public async Task Manifesto_SoTamanhoOuInacessivel_Inconclusivo()
    {
        Put("grande.wim", "1234");
        Put("bloqueado.exe", "x");
        var entries = new List<UsbManifestEntry>
        {
            new() { RelativePath = "grande.wim", SizeBytes = 4 },
            new() { RelativePath = "bloqueado.exe", SizeBytes = 1, Sha256 = "00" },
        };
        Task<string> HashOrDeny(string path, CancellationToken token) =>
            path.EndsWith("bloqueado.exe") ? throw new UnauthorizedAccessException("negado") : HashFile(path, token);

        var result = await ManifestVerifier.VerifyAsync(_root, entries, HashOrDeny);

        Assert.Equal(UsbCheckStatus.Inconclusive, result.Status);
        Assert.Equal("parcial", result.Confidence);
        Assert.Equal(1, result.SizeOnlyVerified);
        Assert.Equal(1, result.InaccessibleCount);
        Assert.Contains("Inacessivel: bloqueado.exe (negado)", result.Details);
    }

    [Fact]
    public void CopiaLogica_PresencaETamanho()
    {
        Put(@"sources\boot.wim", "12345");
        Put("setup.exe", "1");
        var check = LogicalCopyCheck.CheckSizes(_root, new (string, long)[]
        {
            (@"sources\boot.wim", 5),
            ("setup.exe", 2),
            ("autorun.inf", 10),
        });

        Assert.Equal(1, check.Checked);
        Assert.Equal(new[] { "autorun.inf" }, check.MissingPaths);
        Assert.Equal(("setup.exe", 2L, 1L), check.SizeMismatches.Single());
    }

    [Theory]
    [InlineData(0, 0, 0, 0, UsbCheckStatus.Success)]
    [InlineData(0, 0, 3, 0, UsbCheckStatus.Inconclusive)]
    [InlineData(0, 0, 0, 1, UsbCheckStatus.Inconclusive)]
    [InlineData(1, 0, 0, 0, UsbCheckStatus.Failed)]
    [InlineData(0, 2, 5, 0, UsbCheckStatus.Failed)]
    public void CopiaLogica_RegraDaVerificacaoPorHash(int missing, long mismatch, int av, int readErrors, UsbCheckStatus expected)
    {
        Assert.Equal(expected, LogicalCopyCheck.ClassifyHashCheck(missing, mismatch, av, readErrors));
    }

    private static byte[] MbrWithActivePartition()
    {
        var mbr = new byte[512];
        mbr[446] = 0x80; mbr[446 + 4] = 0x0C;
        BitConverter.GetBytes(8192u).CopyTo(mbr, 446 + 8);
        mbr[510] = 0x55; mbr[511] = 0xAA;
        return mbr;
    }

    private static byte[] VbrWithBootMgr()
    {
        var vbr = new byte[512];
        System.Text.Encoding.ASCII.GetBytes("BOOTMGR is compressed").CopyTo(vbr, 400);
        vbr[510] = 0x55; vbr[511] = 0xAA;
        return vbr;
    }

    [Fact]
    public void Boot_BiosEUefiEmFat32()
    {
        var layoutChecked = false;
        var result = UsbBootDetection.Evaluate(new UsbBootFacts
        {
            RootAccessible = true, HasBootMgr = true, HasUefiBootx64 = true, DriveFormat = "FAT32",
            Mbr = MbrWithActivePartition(), VolumeBootSector = VbrWithBootMgr(),
            IsUefiNtfsLayout = () => layoutChecked = true,
        });

        Assert.True(result.HasBiosBoot);
        Assert.True(result.HasUefiBoot);
        Assert.Equal("USB bootavel detectado: BIOS + UEFI.", result.Summary);
        Assert.Equal("Boot BIOS + UEFI", result.ModeName);
        Assert.False(layoutChecked);   // em FAT nao precisa ler a particao auxiliar
        Assert.Equal("UEFI: detectado EFI\\BOOT\\BOOTX64.EFI em volume FAT/FAT32.", result.Details[0]);
        Assert.Contains("BIOS: particao ativa detectada no MBR (indice 1, tipo 0x0C).", result.Details);
    }

    [Fact]
    public void Boot_UefiNtfsPeloLayout()
    {
        var result = UsbBootDetection.Evaluate(new UsbBootFacts
        {
            RootAccessible = true, HasUefiBootx64 = true, DriveFormat = "NTFS", IsUefiNtfsLayout = () => true,
        });

        Assert.True(result.HasUefiBoot);
        Assert.False(result.HasBiosBoot);
        Assert.Equal("Boot UEFI", result.ModeName);
        Assert.StartsWith("UEFI: layout UEFI:NTFS detectado", result.Details[0]);

        var semLayout = UsbBootDetection.Evaluate(new UsbBootFacts { RootAccessible = true, HasUefiBootx64 = true, DriveFormat = "NTFS", IsUefiNtfsLayout = () => false });
        Assert.False(semLayout.HasUefiBoot);
        Assert.Contains("nao foi confirmado como FAT/FAT32 (NTFS)", semLayout.Details[0]);
    }

    [Fact]
    public void Boot_BiosExigeSetorDeBootComBootmgr()
    {
        var vbrSemBootMgr = new byte[512];
        vbrSemBootMgr[510] = 0x55; vbrSemBootMgr[511] = 0xAA;
        var result = UsbBootDetection.Evaluate(new UsbBootFacts
        {
            RootAccessible = true, HasBootMgr = true, DriveFormat = "FAT32", Mbr = MbrWithActivePartition(), VolumeBootSector = vbrSemBootMgr,
        });

        Assert.False(result.HasBiosBoot);
        Assert.Contains("BIOS: setor de boot do volume foi lido, mas nao referencia BOOTMGR.", result.Details);
        Assert.Equal("Boot USB nao confirmado para BIOS nem UEFI.", result.Summary);
        Assert.Equal("logical", result.ModeName);
    }

    [Fact]
    public void Boot_SemRaizESemSetores()
    {
        var result = UsbBootDetection.Evaluate(new UsbBootFacts { RootAccessible = false });

        Assert.Equal("Volume USB: sem letra montada ou raiz inacessivel; a verificacao por arquivos ficou limitada.", result.Details[0]);
        Assert.Contains("BIOS: MBR nao lido ou sem assinatura 55 AA.", result.Details);
        Assert.Contains("BIOS: nenhuma particao ativa detectada no MBR.", result.Details);
        Assert.False(result.HasBiosBoot || result.HasUefiBoot);

        var formatError = UsbBootDetection.Evaluate(new UsbBootFacts { RootAccessible = true, DriveFormatError = "acesso negado" });
        Assert.Equal("Sistema de arquivos: nao foi possivel consultar (acesso negado).", formatError.Details[0]);
    }
}
