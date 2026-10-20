using System.Security.Cryptography;
using DiscUtils;
using DiscUtils.Iso9660;
using qdisk.Core.Models;
using qdisk.Core.Services;

namespace qdisk.Tests;

// Identificacao de USB RAW pelos arquivos: le a ISO direto do "disco" e confere com o manifesto.
public class RawFileManifestVerifierTests
{
    private static readonly Dictionary<string, byte[]> Files = new()
    {
        ["boot/bootmgr"] = Fill(70_000, 1),
        ["EFI/BOOT/efiboot.img"] = Fill(300_000, 2),
        ["sources/install.wim"] = Fill(1_200_000, 3),
        ["readme.txt"] = Fill(1_000, 4),
    };

    private static byte[] Fill(int size, byte seed)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] BuildIso()
    {
        var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "TESTE" };
        foreach (var (path, data) in Files) builder.AddFile(path.Replace('/', '\\'), data);
        using var output = new MemoryStream();
        builder.Build(output);
        return output.ToArray();
    }

    private static List<UsbManifestEntry> Manifest() =>
        Files.Select(f => new UsbManifestEntry { RelativePath = f.Key, SizeBytes = f.Value.Length, Sha256 = Sha(f.Value) }).ToList();

    private static (long Start, long End) Extent(byte[] iso, string path)
    {
        using var reader = new CDReader(new MemoryStream(iso), true);
        var extent = ((IClusterBasedFileSystem)reader).PathToExtents(path.Replace('/', '\\')).Single();
        return (extent.Start, extent.Start + extent.Length);
    }

    private static string Describe(RawFileCheckResult r) =>
        $"verificados={r.FilesVerified} ausentes={r.MissingCount} divergentes={r.MismatchCount} ilegiveis={r.UnreadableCount} | " + string.Join("; ", r.Details);

    private static RawFileCheckResult Run(byte[] disk, IReadOnlyList<(long StartByte, long EndByte)>? fat = null, List<UsbManifestEntry>? manifest = null)
    {
        using var stream = new AlignedReadStream(new StrictSectorStream(disk), disk.Length);
        var systems = RawFileManifestVerifier.OpenIsoFileSystems(stream);
        var iso = systems.First(s => s.Name == "ISO9660");
        try
        {
            return RawFileManifestVerifier.Verify(iso.FileSystem, iso.Paths, iso.Name, manifest ?? Manifest(), fat ?? Array.Empty<(long, long)>());
        }
        finally
        {
            foreach (var s in systems) s.FileSystem.Dispose();
        }
    }

    [Fact]
    public void DiscoIgualAImagem_TodosConferem()
    {
        var iso = BuildIso();
        var disk = iso.Concat(new byte[1024 * 1024]).ToArray();   // o USB e maior que a imagem

        var result = Run(disk);

        Assert.True(result.Matches, Describe(result));
        Assert.Equal(4, result.FilesVerified);
        Assert.Empty(result.ToleratedFiles);
    }

    [Fact]
    public void ArquivoAlteradoForaDaParticaoFat_Reprova()
    {
        var iso = BuildIso();
        var (start, _) = Extent(iso, "sources/install.wim");
        iso[start + 10] ^= 0xFF;

        var result = Run(iso);

        Assert.False(result.Matches);
        Assert.Equal(1, result.MismatchCount);
        Assert.Contains("Hash divergente: sources/install.wim", result.Details);
    }

    [Fact]
    public void ArquivoDaParticaoEfiAlteradoPeloWindows_Tolerado()
    {
        var iso = BuildIso();
        var efi = Extent(iso, "EFI/BOOT/efiboot.img");
        iso[efi.Start + 500] ^= 0xFF;   // "System Volume Information" escrito no volume FAT montado

        var result = Run(iso, new[] { (efi.Start, efi.End) });

        Assert.True(result.Matches);
        Assert.Equal(new[] { "EFI/BOOT/efiboot.img" }, result.ToleratedFiles);
        Assert.Equal(3, result.FilesVerified);
    }

    [Fact]
    public void ArquivoAusenteNoDisco_Reprova()
    {
        var manifest = Manifest();
        manifest.Add(new UsbManifestEntry { RelativePath = "nao/existe.bin", SizeBytes = 1, Sha256 = "00" });

        var result = Run(BuildIso(), manifest: manifest);

        Assert.False(result.Matches);
        Assert.Equal(1, result.MissingCount);
    }

    [Fact]
    public void Sentinelas()
    {
        var iso = BuildIso();
        using var stream = new AlignedReadStream(new StrictSectorStream(iso), iso.Length);
        var systems = RawFileManifestVerifier.OpenIsoFileSystems(stream);
        var fs = systems.First(s => s.Name == "ISO9660");
        var ok = new[] { new UsbManifestFile { Path = "boot/bootmgr", Size = 70_000, Sha256 = Sha(Files["boot/bootmgr"]) } };
        var wrong = new[] { new UsbManifestFile { Path = "boot/bootmgr", Size = 70_000, Sha256 = new string('0', 64) } };

        Assert.True(RawFileManifestVerifier.SentinelsMatch(fs.FileSystem, fs.Paths, ok));
        Assert.False(RawFileManifestVerifier.SentinelsMatch(fs.FileSystem, fs.Paths, wrong));
        Assert.False(RawFileManifestVerifier.SentinelsMatch(fs.FileSystem, fs.Paths, Array.Empty<UsbManifestFile>()));
        foreach (var s in systems) s.FileSystem.Dispose();
    }

    [Theory]
    [InlineData("EFI\\BOOT\\BOOTX64.EFI;1", "efi/boot/bootx64.efi")]
    [InlineData("/Sources/Install.wim", "sources/install.wim")]
    [InlineData("a;b", "a;b")]
    [InlineData(@"BOOT\BOOTMGR.;1", "boot/bootmgr")]
    public void ChaveDoCaminho(string path, string expected) => Assert.Equal(expected, RawFileManifestVerifier.NormalizeKey(path));

    // Disco fisico: so aceita leitura com offset e tamanho multiplos de 512.
    private sealed class StrictSectorStream(byte[] data) : MemoryStream(data, writable: false)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position % 512 != 0 || count % 512 != 0) throw new IOException("leitura desalinhada");
            return base.Read(buffer, offset, count);
        }
    }
}
