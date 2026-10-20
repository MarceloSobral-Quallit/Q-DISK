using qdisk.Core.Services;

namespace qdisk.Tests;

// Etapa 2 da separacao da UsbBootView (P10.1): formatacao FAT32 nativa, ordem da gravacao RAW, comparacao
// imagem x disco e particao auxiliar UEFI:NTFS, sem disco real (Stream em memoria).
public class RawAndFat32LayoutTests
{
    private const long MB = 1024L * 1024;
    private const long GB = 1024L * MB;

    [Theory]
    [InlineData(31_914_983_424L)]   // o pendrive da bancada (32 GB)
    [InlineData(16L * GB)]
    [InlineData(8L * GB)]
    [InlineData(4L * GB)]
    [InlineData(64L * GB)]
    public void Fat32_GeometriaCabeNaParticaoEFatCobreOsClusters(long diskBytes)
    {
        var g = Fat32Layout.Plan(diskBytes);

        Assert.Equal((uint)(diskBytes / 512), g.TotalDiskSectors);
        Assert.Equal(g.TotalDiskSectors - 8192u, g.PartitionSizeSectors);
        Assert.Equal(8192L + 32, g.Fat1StartSector);
        Assert.Equal(g.Fat1StartSector + g.SectorsPerFat, g.Fat2StartSector);
        Assert.Equal(g.Fat1StartSector + 2L * g.SectorsPerFat, g.DataAreaStartSector);
        Assert.Equal(g.DataAreaStartSector, g.RootDirStartSector);
        // Area de dados dentro da particao e FAT com uma entrada de 4 bytes por cluster (+2 reservadas).
        Assert.True(g.DataAreaStartSector + (long)g.TotalClusters * g.SectorsPerCluster <= 8192L + g.PartitionSizeSectors);
        Assert.True(((long)g.TotalClusters + 2) * 4 <= (long)g.SectorsPerFat * 512);
        // FAT32 exige pelo menos 65.525 clusters (abaixo disso o Windows trata como FAT16).
        Assert.True(g.TotalClusters >= 65_525);
    }

    [Theory]
    [InlineData(100L * MB, 2)]
    [InlineData(4L * GB, 8)]
    [InlineData(12L * GB, 16)]
    [InlineData(31_914_983_424L, 32)]
    [InlineData(64L * GB, 32)]   // acima de 32 GB o cluster continua 16 KiB
    public void Fat32_ClusterPeloTamanho(long diskBytes, int sectorsPerCluster)
    {
        Assert.Equal(sectorsPerCluster, Fat32Layout.Plan(diskBytes).SectorsPerCluster);
    }

    [Fact]
    public void Fat32_AcimaDe2TiB_Recusa()
    {
        Assert.Throws<IOException>(() => Fat32Layout.Plan(3L * 1024 * GB));
    }

    [Fact]
    public void Fat32_SetoresGravados()
    {
        var g = Fat32Layout.Plan(31_914_983_424L);

        var boot = Fat32Layout.BuildBootSector(g, 0x12345678u, "q_boot");
        Assert.Equal(new byte[] { 0xEB, 0x58, 0x90 }, boot[..3]);
        Assert.Equal(512, BitConverter.ToUInt16(boot, 11));
        Assert.Equal(g.SectorsPerCluster, boot[13]);
        Assert.Equal(32, BitConverter.ToUInt16(boot, 14));
        Assert.Equal(2, boot[16]);
        Assert.Equal(8192u, BitConverter.ToUInt32(boot, 28));
        Assert.Equal(g.PartitionSizeSectors, BitConverter.ToUInt32(boot, 32));
        Assert.Equal(g.SectorsPerFat, BitConverter.ToUInt32(boot, 36));
        Assert.Equal(2u, BitConverter.ToUInt32(boot, 44));
        Assert.Equal(1, BitConverter.ToUInt16(boot, 48));
        Assert.Equal(6, BitConverter.ToUInt16(boot, 50));
        Assert.Equal(0x12345678u, BitConverter.ToUInt32(boot, 67));
        Assert.Equal("Q_BOOT     ", System.Text.Encoding.ASCII.GetString(boot, 71, 11));
        Assert.Equal("FAT32   ", System.Text.Encoding.ASCII.GetString(boot, 82, 8));
        Assert.Equal(new byte[] { 0x55, 0xAA }, boot[510..]);

        var fsInfo = Fat32Layout.BuildFsInfo(g.TotalClusters);
        Assert.Equal(0x41615252u, BitConverter.ToUInt32(fsInfo, 0));
        Assert.Equal(0x61417272u, BitConverter.ToUInt32(fsInfo, 484));
        Assert.Equal(g.TotalClusters - 1, BitConverter.ToUInt32(fsInfo, 488));
        Assert.Equal(0xAA550000u, BitConverter.ToUInt32(fsInfo, 508));

        var fat = Fat32Layout.BuildFatTable(g.SectorsPerFat);
        Assert.Equal((int)g.SectorsPerFat * 512, fat.Length);
        Assert.Equal(0x0FFFFFF8u, BitConverter.ToUInt32(fat, 0));
        Assert.Equal(0x0FFFFFFFu, BitConverter.ToUInt32(fat, 4));
        Assert.Equal(0x0FFFFFFFu, BitConverter.ToUInt32(fat, 8));
        Assert.Equal(0u, BitConverter.ToUInt32(fat, 12));

        var root = Fat32Layout.BuildRootDirectory(g, "Q_BOOT");
        Assert.Equal(g.SectorsPerCluster * 512, root.Length);
        Assert.Equal("Q_BOOT     ", System.Text.Encoding.ASCII.GetString(root, 0, 11));
        Assert.Equal(0x08, root[11]);

        var mbr = Fat32Layout.BuildMbrSector(g.TotalDiskSectors);
        var partition = MbrLayout.ReadPartitions(mbr)[0];
        Assert.True(partition.IsActive);
        Assert.Equal(0x0C, partition.PartitionType);
        Assert.Equal(8192u, partition.StartLba);
        Assert.Equal(g.PartitionSizeSectors, partition.SectorCount);
    }

    [Theory]
    [InlineData(0L, new string[0])]
    [InlineData(4096L, new[] { "header:0:4096" })]
    [InlineData(1024L * 1024, new[] { "header:0:1048576" })]
    [InlineData(5L * 1024 * 1024, new[] { "body:1048576:4194304", "header:0:1048576" })]
    public void RawWritePlan_CorpoAntesDoCabecalho(long imageSize, string[] expected)
    {
        var ranges = RawWritePlan.Ranges(imageSize).Select(r => $"{r.Name}:{r.StartOffset}:{r.Length}");
        Assert.Equal(expected, ranges);
    }

    private static byte[] HybridImage(int size)
    {
        // Imagem com MBR: particao EFI (0xEF) de 64 KiB a partir do setor 128.
        var image = new byte[size];
        new Random(7).NextBytes(image);
        Array.Clear(image, 440, 4);   // sem assinatura de disco
        Array.Clear(image, 446, 64);
        image[446 + 4] = 0xEF;
        BitConverter.GetBytes(128u).CopyTo(image, 446 + 8);
        BitConverter.GetBytes(128u).CopyTo(image, 446 + 12);
        image[510] = 0x55;
        image[511] = 0xAA;
        return image;
    }

    [Fact]
    public async Task RawVerifier_IgualDepoisDaAssinaturaEComAlteracoesNoVolume()
    {
        var image = HybridImage(3 * 1024 * 1024 + 123);
        var disk = (byte[])image.Clone();
        Array.Resize(ref disk, image.Length + 4096);   // o USB e maior que a imagem
        disk[440] = 0x75; disk[441] = 0x4F;            // assinatura gravada pelo Windows
        disk[128 * 512 + 1000] ^= 0xFF;                // System Volume Information no volume EFI montado

        var divergences = new List<long>();
        var result = await RawImageVerifier.CompareAsync(new MemoryStream(image), new MemoryStream(disk), image.Length,
            (offset, _, _) => divergences.Add(offset));

        Assert.Equal(image.Length, result.BytesCompared);
        Assert.Equal(2, result.SignatureDifferences);
        Assert.Equal(1, result.VolumeDifferences);
        Assert.Equal(0, result.OtherDifferences);
        Assert.False(result.MatchesImage);
        Assert.True(result.MatchesWithVolumeChanges);
        Assert.Equal(new long[] { 440, 441, 128 * 512 + 1000 }, divergences);
    }

    [Fact]
    public async Task RawVerifier_DivergenciaRealEDiscoMenor()
    {
        var image = HybridImage(2 * 1024 * 1024);
        var disk = (byte[])image.Clone();
        disk[1_500_000] ^= 0x01;
        var result = await RawImageVerifier.CompareAsync(new MemoryStream(image), new MemoryStream(disk), image.Length);
        Assert.Equal(1, result.OtherDifferences);
        Assert.False(result.MatchesWithVolumeChanges);

        var shortDisk = new MemoryStream(image[..(1024 * 1024 + 10)]);
        var truncated = await RawImageVerifier.CompareAsync(new MemoryStream(image), shortDisk, image.Length);
        Assert.True(truncated.OtherDifferences > 0);
        Assert.False(truncated.MatchesImage);
    }

    private static byte[] UefiNtfsMbr(uint auxStart, uint auxSectors, byte auxType = 0xEF)
    {
        var mbr = new byte[512];
        mbr[446] = 0x80; mbr[446 + 4] = 0x07;
        BitConverter.GetBytes(2048u).CopyTo(mbr, 446 + 8);
        BitConverter.GetBytes(auxStart - 2048).CopyTo(mbr, 446 + 12);
        mbr[462 + 4] = auxType;
        BitConverter.GetBytes(auxStart).CopyTo(mbr, 462 + 8);
        BitConverter.GetBytes(auxSectors).CopyTo(mbr, 462 + 12);
        mbr[510] = 0x55; mbr[511] = 0xAA;
        return mbr;
    }

    [Fact]
    public async Task UefiNtfs_GravaNaParticao2ECorrigeOTipo()
    {
        var image = new byte[1024 * 1024];
        new Random(3).NextBytes(image);
        var mbr = UefiNtfsMbr(10_000, 6144, auxType: 0x07);   // diskpart sem o "set id"
        var bytes = new byte[(10_000 + 6144) * 512];
        mbr.CopyTo(bytes, 0);
        var disk = new MemoryStream(bytes);

        var (offset, typeFixed) = await UefiNtfsAuxPartition.WriteAsync(disk, mbr, image);

        Assert.Equal(10_000L * 512, offset);
        Assert.True(typeFixed);
        Assert.Equal(0xEF, bytes[462 + 4]);
        Assert.True(UefiNtfsAuxPartition.RegionMatches(disk, offset, image));
        Assert.Single(UefiNtfsAuxPartition.Candidates(bytes[..512]));

        bytes[offset + 100] ^= 0xFF;
        Assert.False(UefiNtfsAuxPartition.RegionMatches(disk, offset, image));
    }

    [Fact]
    public void UefiNtfs_ParticaoAusenteOuPequena()
    {
        Assert.Throws<IOException>(() => UefiNtfsAuxPartition.ValidateTarget(new byte[512], 1024));
        Assert.Throws<IOException>(() => UefiNtfsAuxPartition.ValidateTarget(UefiNtfsMbr(10_000, 100), 1024 * 1024));
        Assert.Equal(10_000u, UefiNtfsAuxPartition.ValidateTarget(UefiNtfsMbr(10_000, 2048), 1024 * 1024).StartLba);
        Assert.False(UefiNtfsAuxPartition.EnsureEfiType(UefiNtfsMbr(10_000, 2048)));
    }
}
