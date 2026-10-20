using qdisk.Core.Services;

namespace qdisk.Tests;

// Logica de disco tirada da UsbBootView (P10.1): MBR, scripts do diskpart e decisao de formatar antes do TIBX.
public class UsbDiskLayoutTests
{
    private static byte[] Mbr(params (int Index, bool Active, byte Type, uint Start, uint Count)[] entries)
    {
        var mbr = new byte[512];
        mbr[510] = 0x55;
        mbr[511] = 0xAA;
        foreach (var e in entries)
        {
            var offset = 446 + e.Index * 16;
            mbr[offset] = (byte)(e.Active ? 0x80 : 0x00);
            mbr[offset + 4] = e.Type;
            BitConverter.GetBytes(e.Start).CopyTo(mbr, offset + 8);
            BitConverter.GetBytes(e.Count).CopyTo(mbr, offset + 12);
        }
        return mbr;
    }

    // Layout UEFI:NTFS de um USB de 16 GB: 1 = NTFS ativa, 2 = auxiliar 0xEF no fim.
    private static readonly byte[] UefiNtfs = Mbr((0, true, 0x07, 2048, 31_000_000), (1, false, 0xEF, 31_002_048, 2048));

    [Fact]
    public void ReadPartitions_LeAsQuatroEntradas()
    {
        var partitions = MbrLayout.ReadPartitions(UefiNtfs);

        Assert.Equal(4, partitions.Count);
        Assert.True(partitions[0].IsActive);
        Assert.Equal(0x07, partitions[0].PartitionType);
        Assert.Equal(MbrLayout.EfiSystemPartitionType, partitions[1].PartitionType);
        Assert.Equal(31_002_048L * 512L, partitions[1].StartOffsetBytes);
        Assert.Equal(2048u, partitions[1].SectorCount);
        Assert.Equal(0, partitions[2].PartitionType);
    }

    [Fact]
    public void ReadPartitions_SemAssinatura_Vazio()
    {
        var mbr = (byte[])UefiNtfs.Clone();
        mbr[511] = 0;

        Assert.False(MbrLayout.HasBootSignature(mbr));
        Assert.Empty(MbrLayout.ReadPartitions(mbr));
        Assert.Equal(-1, MbrLayout.GetActivePartitionIndex(mbr));
        Assert.Equal(0, MbrLayout.GetPartitionType(mbr, 1));
        Assert.Equal(0u, MbrLayout.GetPartitionStartLba(mbr, 1));
        Assert.False(MbrLayout.HasBootSignature(null));
    }

    [Fact]
    public void Ativa_TipoEInicio()
    {
        Assert.Equal(0, MbrLayout.GetActivePartitionIndex(UefiNtfs));
        Assert.Equal(-1, MbrLayout.GetActivePartitionIndex(Mbr((0, false, 0x0C, 2048, 100))));
        Assert.Equal(0xEF, MbrLayout.GetPartitionType(UefiNtfs, 1));
        Assert.Equal(31_002_048u, MbrLayout.GetPartitionStartLba(UefiNtfs, 1));
        Assert.Equal(0u, MbrLayout.GetPartitionStartLba(UefiNtfs, 9));
    }

    [Fact]
    public void CodigoDeBootETextoNoSetor()
    {
        Assert.False(MbrLayout.HasBootCode(UefiNtfs));
        var mbr = (byte[])UefiNtfs.Clone();
        System.Text.Encoding.ASCII.GetBytes("BOOTMGR is missing").CopyTo(mbr, 100);

        Assert.True(MbrLayout.HasBootCode(mbr));
        Assert.True(MbrLayout.SectorContainsAscii(mbr, "bootmgr"));
        Assert.False(MbrLayout.SectorContainsAscii(mbr, "GRUB"));
        Assert.False(MbrLayout.SectorContainsAscii(null, "GRUB"));
    }

    [Fact]
    public void Diskpart_ZerarUsb()
    {
        var script = DiskpartScripts.ZeroUsb(3, "NTFS", "QUALLIT");
        var lines = script.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("select disk 3", lines[0]);
        Assert.Contains("clean", lines);
        Assert.Contains("convert mbr", lines);
        Assert.Contains("format fs=NTFS quick label=QUALLIT", lines);
        Assert.Equal("exit", lines[^1]);
        Assert.EndsWith(Environment.NewLine, script);
    }

    [Fact]
    public void Diskpart_UefiNtfs_ParticaoAtivaEAuxiliarEf()
    {
        var lines = DiskpartScripts.UefiNtfsPartitions(2, 14_000).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        var main = Array.IndexOf(lines, "create partition primary size=14000");
        Assert.True(main > 0);
        Assert.Equal("active", lines[main + 1]);
        Assert.Equal("create partition primary", lines[main + 2]);
        Assert.Equal("set id=ef override", lines[main + 3]);
        Assert.DoesNotContain(lines, line => line.StartsWith("format"));
    }

    [Fact]
    public void Diskpart_UefiNtfs_FormataSoAParticao1()
    {
        var lines = DiskpartScripts.UefiNtfsFormatMain(2, "WIN11").Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(new[] { "select disk 2", "select partition 1", "format fs=ntfs quick label=WIN11", "assign", "exit" }, lines);
    }

    [Fact]
    public void Diskpart_UefiNtfs_TamanhoDeixaEspacoParaAuxiliar()
    {
        Assert.Equal(16_381L - 3L, DiskpartScripts.UefiNtfsMainPartitionMegabytes(16_381L * 1024 * 1024 + 500));
    }

    [Theory]
    // Rotulo do catalogo; senao o da ISO; senao "QDisk_" + nome do arquivo.
    [InlineData("PNB_Win11", "NTLITE", @"C:\img\PNB-Win11-x64.iso", "NTFS", "PNB_Win11")]
    [InlineData("Q_BOOT", "QUALLIT_BOOT", @"C:\img\Quallit_Boot.iso", "FAT32", "Q_BOOT")]
    [InlineData("  ", "QUALLIT_BOOT", @"C:\img\Quallit_Boot.iso", "FAT32", "QUALLIT_BOO")]
    [InlineData(null, "NTLITE", @"C:\img\PNB-Win11-x64.iso", "NTFS", "NTLITE")]
    [InlineData(null, "CentOS_6.8_Final", @"C:\img\PNB-Storex.iso", "fat32", "CENTOS_6_8_")]
    [InlineData(null, "STOREX_UBUNTU_V11_10", @"C:\img\x.iso", "NTFS", "STOREX_UBUNTU_V11_10")]
    [InlineData(null, "  ", @"C:\img\PNB-WIN7-x64.iso", "NTFS", "QDisk_PNB-WIN7-x64")]
    [InlineData(null, null, @"C:\img\Imagem com espaço (1).iso", "NTFS", "QDisk_Imagem_com_espa_o__1")]
    [InlineData(null, null, @"C:\img\Um nome de arquivo muito comprido demais.iso", "NTFS", "QDisk_Um_nome_de_arquivo_muito_c")]
    [InlineData(null, null, @"C:\img\PNB-WIN7-x64.iso", "FAT32", "QDISK_PNB-W")]
    [InlineData(null, null, "", "NTFS", "QDisk_")]
    public void Rotulo_CopiaLogica(string? catalogLabel, string? isoLabel, string isoPath, string fs, string expected)
    {
        var label = UsbVolumeLabels.ForIsoCopy(catalogLabel, isoLabel, isoPath, fs);

        Assert.Equal(expected, label);
        Assert.True(label.Length <= (fs.Equals("FAT32", StringComparison.OrdinalIgnoreCase) ? 11 : 32));
    }

    [Fact]
    public void Rotulo_TibxERaizFat32()
    {
        Assert.Equal("QDisk_TIBX", UsbVolumeLabels.Tibx);

        var entry = UsbVolumeLabels.Fat32RootLabelEntry("QDisk_Quall");
        Assert.Equal(32, entry.Length);
        Assert.Equal("QDISK_QUALL", System.Text.Encoding.ASCII.GetString(entry, 0, 11));
        Assert.Equal(0x08, entry[11]);
        Assert.Equal("QDISK      ", System.Text.Encoding.ASCII.GetString(UsbVolumeLabels.Fat32RootLabelEntry("QDisk"), 0, 11));
    }

    [Fact]
    public void RawCompare_AssinaturaEVolumeMontadoPeloWindows()
    {
        // Imagem hibrida com particao EFI (0xEF) de 4 MiB a partir do setor 64.
        var mbr = Mbr((1, false, 0xEF, 64, 8192));
        var ranges = RawImageCompare.FatPartitionRanges(mbr);
        Assert.Single(ranges);
        Assert.Equal((64L * 512, 64L * 512 + 8192L * 512), ranges[0]);

        var result = new RawCompareResult();
        RawImageCompare.Classify(result, 440, 0x00, ranges);   // assinatura gravada pelo Windows
        RawImageCompare.Classify(result, 64L * 512 + 100, 0x41, ranges);
        Assert.Equal(1, result.SignatureDifferences);
        Assert.Equal(1, result.VolumeDifferences);
        Assert.False(result.MatchesImage);
        Assert.True(result.MatchesWithVolumeChanges);

        RawImageCompare.Classify(result, 441, 0x12, ranges);   // assinatura que ja existia na imagem: divergencia real
        RawImageCompare.Classify(result, 9_000_000, 0x00, ranges);
        Assert.Equal(2, result.OtherDifferences);
        Assert.False(result.MatchesWithVolumeChanges);
    }

    [Fact]
    public void RawCompare_SoAssinatura_ContaComoIgual()
    {
        var result = new RawCompareResult();
        for (var offset = 440; offset <= 443; offset++)
            RawImageCompare.Classify(result, offset, 0x00, RawImageCompare.FatPartitionRanges(new byte[512]));

        Assert.True(result.MatchesImage);
        Assert.Equal(4, result.SignatureDifferences);
    }

    private const long GB = 1024L * 1024 * 1024;

    [Theory]
    [InlineData("NTFS", 10, 14, 15, 5, null)]
    [InlineData("ntfs", 10, 14, 15, 5, null)]
    [InlineData("CDFS", 0, 1, 15, 5, "RAW/DD")]
    [InlineData("UDF", 0, 1, 15, 5, "RAW/DD")]
    [InlineData("exFAT", 10, 14, 15, 5, "exFAT")]
    [InlineData("FAT32", 10, 14, 15, 5, "FAT32")]
    [InlineData("NTFS", 1, 2, 15, 5, "so parte do USB")]
    public void Tibx_MotivoParaFormatar(string fs, long freeGb, long totalGb, long diskGb, long tibxGb, string? expected)
    {
        var reason = TibxVolumeCheck.FormatReason(true, fs, freeGb * GB, totalGb * GB, diskGb * GB, tibxGb * GB);

        if (expected is null) Assert.Null(reason);
        else Assert.Contains(expected, reason);
    }

    [Fact]
    public void Tibx_VolumeNaoPronto_OuSemEspacoNoUsbInteiro()
    {
        Assert.NotNull(TibxVolumeCheck.FormatReason(false, null, 0, 0, 15 * GB, 5 * GB));
        // USB inteiro em NTFS, so sem espaco: formatar nao resolve, a falta de espaco e tratada na copia.
        Assert.Null(TibxVolumeCheck.FormatReason(true, "NTFS", 1 * GB, 15 * GB, 15 * GB, 5 * GB));
    }
}
