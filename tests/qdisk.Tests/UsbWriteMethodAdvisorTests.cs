using qdisk.Core.Models;
using qdisk.Core.Services;

namespace qdisk.Tests;

// P2: regra unica de metodo de gravacao (helper do GUI_ADM e app).
public class UsbWriteMethodAdvisorTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static IsoAnalysisResult Iso(bool bios, bool uefi, params (string Path, long Size)[] files)
    {
        var result = new IsoAnalysisResult { HasElTorito = bios || uefi, HasBiosElTorito = bios, HasUefiElTorito = uefi };
        foreach (var file in files)
        {
            var normalized = file.Path.Replace('/', '\\').ToUpperInvariant();
            if (normalized == "BOOTMGR") result.HasBootMgr = true;
            if (normalized == "EFI\\BOOT\\BOOTX64.EFI") result.HasEfiBootX64 = true;
            if (normalized.StartsWith("ISOLINUX\\")) result.HasSyslinux = true;
            if (normalized.StartsWith("BOOTWIZ")) result.HasAcronisBootwiz = true;
            if (file.Size > result.LargestFileSize)
            {
                result.LargestFileSize = file.Size;
                result.LargestFilePath = file.Path;
            }
            if (file.Size > 4 * Gb - 1)
            {
                result.HasFilesOver4Gb = true;
                result.FilesOver4Gb.Add(file.Path);
            }
        }
        return result;
    }

    private static void Hybrid(IsoAnalysisResult result, bool gpt = false, params byte[] partitionTypes)
    {
        result.HasMbrSignature = true;
        result.HasMbrBootCode = true;
        result.MbrPartitionTypes.AddRange(partitionTypes);
        result.HasGpt = gpt;
    }

    private static UsbWriteAssessment Assess(IsoAnalysisResult analysis) => new UsbWriteMethodAdvisor().Assess(analysis);

    [Fact]
    public void WindowsBiosUefiCabeEmFat32_CopiaLogica()
    {
        var assessment = Assess(Iso(true, true, ("bootmgr", 400_000), ("efi/boot/bootx64.efi", 1_000_000), ("sources/install.wim", 3 * Gb)));

        Assert.Equal(UsbWriteMethods.LogicalFat32, assessment.RecommendedMethod);
        Assert.Equal(new[] { "bios", "uefi" }, assessment.FindMethod(UsbWriteMethods.LogicalFat32).BootTargets);
        Assert.Null(assessment.FindMethod(UsbWriteMethods.Raw));
    }

    [Fact]
    public void WindowsComInstallWimMaiorQue4Gb_UefiNtfsESplitSoComoAlternativa()
    {
        var assessment = Assess(Iso(true, true, ("bootmgr", 400_000), ("efi/boot/bootx64.efi", 1_000_000), ("sources/install.wim", 14 * Gb)));

        Assert.Equal(UsbWriteMethods.UefiNtfs, assessment.RecommendedMethod);
        Assert.Equal(new[] { UsbWriteMethods.Fat32SplitWim }, assessment.AlternativeMethods);
        Assert.True(assessment.FindMethod(UsbWriteMethods.Fat32SplitWim).TransformsContent);
        Assert.Null(assessment.FindMethod(UsbWriteMethods.LogicalFat32));
    }

    [Fact]
    public void IsolinuxHibridaSoBios_Raw()
    {
        var analysis = Iso(true, false, ("isolinux/isolinux.bin", 24_576), ("product/pkg.nfc", 1 * Gb));
        Hybrid(analysis, false, 0x17);

        var assessment = Assess(analysis);

        Assert.Equal(UsbWriteMethods.Raw, assessment.RecommendedMethod);
        Assert.Empty(assessment.AlternativeMethods);
    }

    [Fact]
    public void AcronisHibridaComParticaoEfi_RawEUefiLogicoComoAlternativa()
    {
        var analysis = Iso(true, true, ("bootwiz.sys", 26_624), ("efi/boot/bootx64.efi", 3_774_832));
        Hybrid(analysis, false, 0xEF);

        var assessment = Assess(analysis);

        Assert.Equal(UsbWriteMethods.Raw, assessment.RecommendedMethod);
        Assert.Equal(new[] { "bios", "uefi" }, assessment.FindMethod(UsbWriteMethods.Raw).BootTargets);
        Assert.Equal(new[] { "uefi" }, assessment.FindMethod(UsbWriteMethods.LogicalFat32).BootTargets);
    }

    [Fact]
    public void IsolinuxNaoHibrida_SemMetodo()
    {
        var assessment = Assess(Iso(true, false, ("isolinux/isolinux.bin", 24_576)));

        Assert.Equal(UsbWriteMethods.None, assessment.RecommendedMethod);
        Assert.Empty(assessment.Methods);
    }

    [Fact]
    public void MargemCurtaAte4Gb_GeraMotivo()
    {
        var assessment = Assess(Iso(true, true, ("bootmgr", 400_000), ("efi/boot/bootx64.efi", 1_000_000), ("sms/pkg/x.swm", 4_294_928_584)));

        Assert.Equal(UsbWriteMethods.LogicalFat32, assessment.RecommendedMethod);
        Assert.Equal(38_711, assessment.Fat32MarginBytes);
        Assert.Contains(assessment.Reasons, reason => reason.Contains("limite de 4 GB"));
    }

    [Fact]
    public void InspectSystemArea_DetectaMbrHibridoEGpt()
    {
        var buffer = new byte[1024];
        buffer[0] = 0xEB;
        buffer[446 + 4] = 0xEE;
        buffer[510] = 0x55;
        buffer[511] = 0xAA;
        "EFI PART"u8.CopyTo(buffer.AsSpan(512));
        var result = new IsoAnalysisResult();

        IsoAnalysisService.InspectSystemArea(buffer, result);

        Assert.True(result.IsHybridMbr);
        Assert.True(result.HasGpt);
        Assert.Equal(new byte[] { 0xEE }, result.MbrPartitionTypes);
    }

    // A recomendacao antiga (rotulo da lista, confirmacoes) segue a regra unica: Win11 com install.wim > 4 GB
    // era "Raw/DD recomendado", mas a ISO nao e hibrida.
    [Fact]
    public void AnalyzeComListaDeArquivos_InstallWimGrande_RecomendaUefiNtfs()
    {
        var iso = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(iso, new byte[64 * 1024]);
            var files = new Dictionary<string, long>
            {
                ["bootmgr"] = 442_062,
                ["efi/boot/bootx64.efi"] = 1_688_024,
                ["sources/install.wim"] = 14_917_593_251,
            };

            var result = new IsoAnalysisService().Analyze(iso, files);

            Assert.Equal(IsoBootRecommendation.UefiNtfs, result.Recommendation);
            Assert.Equal("UEFI:NTFS recomendado", result.RecommendationText);
        }
        finally
        {
            File.Delete(iso);
        }
    }

    [Fact]
    public void InspectSystemArea_AreaZerada_NaoHibrida()
    {
        var result = new IsoAnalysisResult();

        IsoAnalysisService.InspectSystemArea(new byte[1024], result);

        Assert.False(result.IsHybridMbr);
        Assert.False(result.HasGpt);
    }
}
