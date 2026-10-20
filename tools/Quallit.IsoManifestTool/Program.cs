using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Udf;
using qdisk.Core.Services;

namespace Quallit.IsoManifestTool;

internal static class Program
{
    // Identifica a regra que produziu recommendedMethod/methods; o GUI_ADM trata outros valores como analise parcial.
    private const string AnalysisEngine = "core-1";

    // Manifestos publicados sao UTF-8 sem BOM (o manifestSha256 e calculado sobre os bytes exatos).
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Mesma ordem do gerador Python (caminho em minusculas, ordinal), que define o treeHash dos manifestos publicados.
    private static readonly IComparer<string> ManifestPathOrder = Comparer<string>.Create(
        (left, right) => string.CompareOrdinal(left.ToLowerInvariant(), right.ToLowerInvariant()));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ToolOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            var result = await GenerateManifestAsync(options);
            if (!options.Quiet)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERRO: {ex.Message}");
            return 1;
        }
    }

    private static async Task<ToolResult> GenerateManifestAsync(ToolOptions options)
    {
        var isoPath = Path.GetFullPath(options.IsoPath ?? throw new InvalidOperationException("Informe --iso."));
        if (!File.Exists(isoPath))
        {
            throw new FileNotFoundException("ISO nao encontrada.", isoPath);
        }

        if (!string.IsNullOrWhiteSpace(options.ReanalyzeManifest))
        {
            return await ReanalyzeManifestAsync(isoPath, Path.GetFullPath(options.ReanalyzeManifest), options);
        }

        var outDir = Path.GetFullPath(options.OutDir ?? "config/manifests");
        Directory.CreateDirectory(outDir);

        var imageMetadata = await CalculateImageMetadataAsync(isoPath);
        var files = ReadIsoFiles(isoPath);
        var totalBytes = files.Sum(file => file.Size);
        var treeHash = ComputeTreeHash(files);
        var analysis = BuildAnalysis(isoPath, files);
        var unhashedFiles = files
            .Where(file => string.IsNullOrWhiteSpace(file.Sha256))
            .Select(file => new UnhashedFile(file.Path, file.Size, file.Sha256Error ?? "Arquivo nao pode ser lido para hash."))
            .ToList();

        var manifestName = $"{SuggestNameFromImage(isoPath)}.manifest.json";
        var manifestPath = Path.Combine(outDir, manifestName);
        var manifest = new ManifestDocument(
            Schema: 1,
            GeneratedAtUtc: DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Image: new ImageMetadata(Path.GetFileName(isoPath), imageMetadata.SizeBytes, imageMetadata.Md5, imageMetadata.Sha256),
            Logical: new LogicalManifest(files.Count, totalBytes, treeHash, ChooseSentinelFiles(files), unhashedFiles, analysis),
            Files: files);

        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine, Utf8NoBom);
        return await WriteSummaryAsync(manifest, manifestPath, options);
    }

    // Refaz so a analise de um manifesto existente: mantem lista de arquivos, hashes e treeHash.
    // Usado quando a regra de analise muda e a imagem e a mesma (evita reler e re-hashear todos os arquivos).
    private static async Task<ToolResult> ReanalyzeManifestAsync(string isoPath, string manifestPath, ToolOptions options)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Manifesto para reanalise nao encontrado.", manifestPath);
        }

        var existing = JsonSerializer.Deserialize<ManifestDocument>(await File.ReadAllTextAsync(manifestPath), JsonOptions)
            ?? throw new InvalidDataException($"Manifesto invalido: {manifestPath}");
        if (existing.Files is null || existing.Files.Count == 0)
        {
            throw new InvalidDataException($"Manifesto sem lista de arquivos: {manifestPath}");
        }

        var isoSize = new FileInfo(isoPath).Length;
        if (isoSize != existing.Image.SizeBytes)
        {
            throw new InvalidDataException($"A ISO nao corresponde ao manifesto: tamanho {isoSize} diferente de {existing.Image.SizeBytes}.");
        }

        await using (var stream = File.OpenRead(isoPath))
        {
            var sha256 = ToHex(await SHA256.HashDataAsync(stream));
            if (!string.Equals(sha256, existing.Image.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"A ISO nao corresponde ao manifesto: SHA-256 {sha256} diferente de {existing.Image.Sha256}.");
            }
        }

        var manifest = existing with { Logical = existing.Logical with { Analysis = BuildAnalysis(isoPath, existing.Files) } };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine, Utf8NoBom);
        return await WriteSummaryAsync(manifest, manifestPath, options);
    }

    private static async Task<ToolResult> WriteSummaryAsync(ManifestDocument manifest, string manifestPath, ToolOptions options)
    {
        var summary = BuildSummary(manifest, manifestPath, options.ManifestUrlBase ?? "manifests");
        if (!string.IsNullOrWhiteSpace(options.SummaryOut))
        {
            await File.WriteAllTextAsync(Path.GetFullPath(options.SummaryOut), JsonSerializer.Serialize(summary, JsonOptions) + Environment.NewLine, Utf8NoBom);
        }

        return new ToolResult(manifestPath, summary);
    }

    private static async Task<(long SizeBytes, string Md5, string Sha256)> CalculateImageMetadataAsync(string path)
    {
        var info = new FileInfo(path);
        await using var md5Stream = File.OpenRead(path);
        var md5Bytes = await MD5.HashDataAsync(md5Stream);
        await using var shaStream = File.OpenRead(path);
        var shaBytes = await SHA256.HashDataAsync(shaStream);
        return (info.Length, ToHex(md5Bytes), ToHex(shaBytes));
    }

    private static List<ManifestFile> ReadIsoFiles(string isoPath)
    {
        var iso9660Files = TryReadIsoFiles(isoPath, useUdf: false);
        var udfFiles = TryReadIsoFiles(isoPath, useUdf: true);
        var discUtilsFiles = udfFiles.Count > iso9660Files.Count ? udfFiles : iso9660Files;

        var isoSize = new FileInfo(isoPath).Length;
        var discUtilsTotalBytes = discUtilsFiles.Sum(file => file.Size);
        var discUtilsLooksComplete = discUtilsFiles.Count >= 16 && discUtilsTotalBytes >= isoSize * 8 / 10;
        if (discUtilsLooksComplete)
        {
            return discUtilsFiles;
        }

        var mountedFiles = TryReadWindowsMountedIsoFiles(isoPath);
        var mountedTotalBytes = mountedFiles.Sum(file => file.Size);
        if (mountedFiles.Count > discUtilsFiles.Count || mountedTotalBytes > discUtilsTotalBytes)
        {
            return mountedFiles;
        }

        return discUtilsFiles;
    }

    private static List<ManifestFile> TryReadIsoFiles(string isoPath, bool useUdf)
    {
        try
        {
            using var isoStream = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024);
            using DiscFileSystem fileSystem = useUdf ? new UdfReader(isoStream) : new CDReader(isoStream, true);
            var directFiles = new List<ManifestFile>();
            EnumerateFiles(fileSystem, directFiles);
            var directoryFiles = new List<ManifestFile>();
            EnumerateFiles(fileSystem.Root, string.Empty, directoryFiles);
            var files = directoryFiles.Count > directFiles.Count ? directoryFiles : directFiles;
            return files.OrderBy(file => file.Path, ManifestPathOrder).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static List<ManifestFile> TryReadWindowsMountedIsoFiles(string isoPath)
    {
        string? driveLetter = null;
        try
        {
            driveLetter = MountIsoAndGetDriveLetter(isoPath);
            if (string.IsNullOrWhiteSpace(driveLetter))
            {
                return [];
            }

            var root = $"{driveLetter}:\\";
            var files = new List<ManifestFile>();
            EnumerateMountedFiles(root, root, files);
            return files.OrderBy(file => file.Path, ManifestPathOrder).ToList();
        }
        catch
        {
            return [];
        }
        finally
        {
            try
            {
                DismountIso(isoPath);
            }
            catch
            {
            }
        }
    }

    private static void EnumerateMountedFiles(string root, string directory, List<ManifestFile> files)
    {
        IEnumerable<string> childFiles;
        try
        {
            childFiles = Directory.EnumerateFiles(directory).ToList();
        }
        catch
        {
            childFiles = [];
        }

        foreach (var filePath in childFiles)
        {
            var info = new FileInfo(filePath);
            var relativePath = NormalizeManifestPath(Path.GetRelativePath(root, filePath));
            string? sha256 = null;
            string? sha256Error = null;
            if (ShouldSkipManifestHash(relativePath))
            {
                sha256Error = "Arquivo ignorado para hash por historico de bloqueio de antivirus.";
            }
            else
            {
                try
                {
                    using var fileStream = File.OpenRead(filePath);
                    sha256 = ToHex(SHA256.HashData(fileStream));
                }
                catch (Exception ex)
                {
                    sha256Error = ex.Message;
                }
            }

            files.Add(new ManifestFile(relativePath, info.Length, sha256, sha256Error));
        }

        IEnumerable<string> childDirectories;
        try
        {
            childDirectories = Directory.EnumerateDirectories(directory).ToList();
        }
        catch
        {
            childDirectories = [];
        }

        foreach (var childDirectory in childDirectories)
        {
            EnumerateMountedFiles(root, childDirectory, files);
        }
    }

    private static string MountIsoAndGetDriveLetter(string isoPath)
    {
        var command = "$ErrorActionPreference='Stop'; " +
                      $"$image = Mount-DiskImage -ImagePath '{EscapePowerShellSingleQuotedString(isoPath)}' -PassThru; " +
                      "$volume = $image | Get-Volume | Where-Object DriveLetter | Select-Object -First 1; " +
                      "if ($null -eq $volume) { $volume = Get-DiskImage -ImagePath $image.ImagePath | Get-Volume | Where-Object DriveLetter | Select-Object -First 1 }; " +
                      "if ($null -ne $volume) { $volume.DriveLetter }";
        return RunPowerShell(command).Trim().TrimEnd(':').ToUpperInvariant();
    }

    private static void DismountIso(string isoPath)
    {
        var command = $"Dismount-DiskImage -ImagePath '{EscapePowerShellSingleQuotedString(isoPath)}' -ErrorAction SilentlyContinue";
        RunPowerShell(command);
    }

    private static string RunPowerShell(string command)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "`\"")}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"PowerShell retornou {process.ExitCode}." : error.Trim());
        }

        return output;
    }

    private static string EscapePowerShellSingleQuotedString(string value)
    {
        return value.Replace("'", "''");
    }

    private static void EnumerateFiles(DiscFileSystem fileSystem, List<ManifestFile> files)
    {
        foreach (var filePath in fileSystem.GetFiles(string.Empty, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = NormalizeManifestPath(filePath);
            var fileInfo = fileSystem.GetFileInfo(filePath);
            string? sha256 = null;
            string? sha256Error = null;
            if (ShouldSkipManifestHash(relativePath))
            {
                sha256Error = "Arquivo ignorado para hash por historico de bloqueio de antivirus.";
            }
            else
            {
                try
                {
                    using var fileStream = fileSystem.OpenFile(filePath, FileMode.Open, FileAccess.Read);
                    sha256 = ToHex(SHA256.HashData(fileStream));
                }
                catch (Exception ex)
                {
                    sha256Error = ex.Message;
                }
            }

            files.Add(new ManifestFile(relativePath, fileInfo.Length, sha256, sha256Error));
        }
    }

    private static void EnumerateFiles(DiscDirectoryInfo directory, string relativeRoot, List<ManifestFile> files)
    {
        foreach (var entry in directory.GetFileSystemInfos().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = NormalizeManifestPath(string.IsNullOrEmpty(relativeRoot) ? entry.Name : $"{relativeRoot}/{entry.Name}");
            if (entry is DiscDirectoryInfo childDirectory)
            {
                EnumerateFiles(childDirectory, relativePath, files);
                continue;
            }

            if (entry is not DiscFileInfo fileInfo)
            {
                continue;
            }

            string? sha256 = null;
            string? sha256Error = null;
            if (ShouldSkipManifestHash(relativePath))
            {
                sha256Error = "Arquivo ignorado para hash por historico de bloqueio de antivirus.";
            }
            else
            {
                try
                {
                    using var fileStream = fileInfo.OpenRead();
                    sha256 = ToHex(SHA256.HashData(fileStream));
                }
                catch (Exception ex)
                {
                    sha256Error = ex.Message;
                }
            }

            files.Add(new ManifestFile(relativePath, fileInfo.Length, sha256, sha256Error));
        }
    }

    private static string ComputeTreeHash(IReadOnlyCollection<ManifestFile> files)
    {
        var input = string.Join("\n", files.Select(file => $"{file.Path.ToLowerInvariant()}|{file.Size}|{file.Sha256 ?? string.Empty}"));
        return ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private static ManifestAnalysis BuildAnalysis(string isoPath, IReadOnlyCollection<ManifestFile> files)
    {
        // Analise e metodo de gravacao vem do Core (mesma regra do app), sobre a lista de arquivos ja lida aqui.
        var isoAnalysis = new IsoAnalysisService().Analyze(isoPath, files.Select(file => new KeyValuePair<string, long>(file.Path, file.Size)));
        var assessment = new UsbWriteMethodAdvisor().Assess(isoAnalysis);

        var largest = files.OrderByDescending(file => file.Size).FirstOrDefault();
        var hasFilesOver4Gb = isoAnalysis.HasFilesOver4Gb;
        var paths = files.Select(file => file.Path.ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bootArtifacts = new BootArtifacts(
            BiosBootmgr: paths.Contains("bootmgr"),
            UefiBootx64: paths.Contains("efi/boot/bootx64.efi"),
            BootBcd: paths.Contains("boot/bcd"),
            BootWim: paths.Contains("sources/boot.wim"));

        var bootTargets = new List<string>();
        if (bootArtifacts.BiosBootmgr) bootTargets.Add("bios");
        if (bootArtifacts.UefiBootx64) bootTargets.Add("uefi");

        return new ManifestAnalysis(
            LargestFile: largest is null ? null : new SentinelFile(largest.Path, largest.Size, largest.Sha256),
            HasFilesOver4Gb: hasFilesOver4Gb,
            BootArtifacts: bootArtifacts,
            FilesystemCompatibility: new FilesystemCompatibility(Fat32: !hasFilesOver4Gb, Ntfs: true),
            RecommendedMethod: assessment.RecommendedMethod,
            AlternativeMethods: assessment.AlternativeMethods,
            BootTargets: bootTargets,
            Reasons: assessment.Reasons,
            AnalysisEngine: AnalysisEngine,
            ImageBootTargets: assessment.ImageBootTargets,
            Methods: assessment.Methods.Select(item => new WriteMethodOption(item.Method, item.BootTargets, item.TransformsContent)).ToList(),
            Fat32MarginBytes: assessment.Fat32MarginBytes,
            Bootloaders: new Bootloaders(
                BiosElTorito: isoAnalysis.HasBiosElTorito,
                UefiElTorito: isoAnalysis.HasUefiElTorito,
                Bootmgr: isoAnalysis.HasBootMgr,
                EfiBootx64: isoAnalysis.HasEfiBootX64,
                EfiBootia32: isoAnalysis.HasEfiBootIa32,
                Syslinux: isoAnalysis.HasSyslinux,
                Grub: isoAnalysis.HasGrub,
                AcronisBootwiz: isoAnalysis.HasAcronisBootwiz),
            IsoHybrid: new IsoHybrid(
                Mbr: isoAnalysis.IsHybridMbr,
                Gpt: isoAnalysis.HasGpt,
                MbrPartitionTypes: isoAnalysis.MbrPartitionTypes.Select(type => $"0x{type:x2}").ToList()));
    }

    private static List<SentinelFile> ChooseSentinelFiles(IReadOnlyCollection<ManifestFile> files)
    {
        string[] preferredPaths = ["bootmgr", "bootmgr.efi", "EFI/BOOT/BOOTX64.EFI", "boot/bcd", "sources/boot.wim"];
        // ISO Linux pode ter caminhos que so diferem em maiusculas (ex.: Packages/perl-Dev.rpm e perl-dev.rpm).
        var hashableByPath = new Dictionary<string, ManifestFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(file => !string.IsNullOrWhiteSpace(file.Sha256)))
        {
            hashableByPath.TryAdd(file.Path, file);
        }

        var sentinels = new List<SentinelFile>();
        foreach (var preferredPath in preferredPaths)
        {
            if (hashableByPath.TryGetValue(preferredPath.ToLowerInvariant(), out var match))
            {
                sentinels.Add(new SentinelFile(match.Path, match.Size, match.Sha256));
            }
        }

        if (sentinels.Count == 0)
        {
            var first = files.FirstOrDefault(file => !string.IsNullOrWhiteSpace(file.Sha256));
            if (first is not null)
            {
                sentinels.Add(new SentinelFile(first.Path, first.Size, first.Sha256));
            }
        }

        return sentinels;
    }

    private static ManifestSummary BuildSummary(ManifestDocument manifest, string manifestPath, string manifestUrlBase)
    {
        var manifestUrl = string.Join('/', new[] { manifestUrlBase.Trim('/'), Path.GetFileName(manifestPath) }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return new ManifestSummary(
            Schema: 1,
            Mode: "logical",
            BootTargets: manifest.Logical.Analysis.BootTargets,
            FileCount: manifest.Logical.FileCount,
            TotalBytes: manifest.Logical.TotalBytes,
            TreeHash: manifest.Logical.TreeHash,
            ManifestUrl: manifestUrl,
            ManifestSha256: CalculateFileSha256(manifestPath),
            SentinelFiles: manifest.Logical.SentinelFiles,
            TechnicalAnalysis: new TechnicalAnalysisSummary(
                LargestFile: manifest.Logical.Analysis.LargestFile,
                HasFilesOver4Gb: manifest.Logical.Analysis.HasFilesOver4Gb,
                RecommendedMethod: manifest.Logical.Analysis.RecommendedMethod,
                AlternativeMethods: manifest.Logical.Analysis.AlternativeMethods,
                Reasons: manifest.Logical.Analysis.Reasons,
                AnalysisEngine: manifest.Logical.Analysis.AnalysisEngine,
                ImageBootTargets: manifest.Logical.Analysis.ImageBootTargets,
                Methods: manifest.Logical.Analysis.Methods));
    }

    private static string CalculateFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return ToHex(SHA256.HashData(stream));
    }

    private static string SuggestNameFromImage(string isoPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(isoPath).Trim();
        var safe = new string(fileName.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "image" : safe;
    }

    private static string NormalizeManifestPath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static bool ShouldSkipManifestHash(string relativePath)
    {
        return NormalizeManifestPath(relativePath).Equals("Programs/ProduKey/ProduKey.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToHex(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Quallit.IsoManifestTool --iso <imagem.iso> --out <diretorio> [--manifest-url-base <url>] [--summary-out <arquivo>] [--quiet]");
        Console.WriteLine("Quallit.IsoManifestTool --iso <imagem.iso> --reanalyze <manifesto.json> [--manifest-url-base <url>] [--summary-out <arquivo>] [--quiet]");
    }
}

internal sealed record ToolOptions(string? IsoPath, string? OutDir, string? ManifestUrlBase, string? SummaryOut, string? ReanalyzeManifest, bool Quiet, bool ShowHelp)
{
    public static ToolOptions Parse(string[] args)
    {
        string? isoPath = null;
        string? outDir = null;
        string? manifestUrlBase = null;
        string? summaryOut = null;
        string? reanalyzeManifest = null;
        var quiet = false;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--iso":
                    isoPath = RequireValue(args, ref index, arg);
                    break;
                case "--out":
                    outDir = RequireValue(args, ref index, arg);
                    break;
                case "--manifest-url-base":
                    manifestUrlBase = RequireValue(args, ref index, arg);
                    break;
                case "--summary-out":
                    summaryOut = RequireValue(args, ref index, arg);
                    break;
                case "--reanalyze":
                    reanalyzeManifest = RequireValue(args, ref index, arg);
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Argumento desconhecido: {arg}");
            }
        }

        return new ToolOptions(isoPath, outDir, manifestUrlBase, summaryOut, reanalyzeManifest, quiet, showHelp);
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} requer um valor.");
        }

        index++;
        return args[index];
    }
}

internal sealed record ToolResult(string ManifestPath, ManifestSummary Summary);
internal sealed record ManifestDocument(int Schema, string GeneratedAtUtc, ImageMetadata Image, LogicalManifest Logical, List<ManifestFile> Files);
internal sealed record ImageMetadata(string FileName, long SizeBytes, string Md5, string Sha256);
internal sealed record LogicalManifest(int FileCount, long TotalBytes, string TreeHash, List<SentinelFile> SentinelFiles, List<UnhashedFile> UnhashedFiles, ManifestAnalysis Analysis);
internal sealed record ManifestFile(string Path, long Size, string? Sha256, string? Sha256Error);
internal sealed record SentinelFile(string Path, long Size, string? Sha256);
internal sealed record UnhashedFile(string Path, long Size, string Reason);
internal sealed record ManifestAnalysis(SentinelFile? LargestFile, bool HasFilesOver4Gb, BootArtifacts BootArtifacts, FilesystemCompatibility FilesystemCompatibility, string RecommendedMethod, List<string> AlternativeMethods, List<string> BootTargets, List<string> Reasons, string AnalysisEngine, List<string> ImageBootTargets, List<WriteMethodOption> Methods, long? Fat32MarginBytes, Bootloaders Bootloaders, IsoHybrid IsoHybrid);
internal sealed record BootArtifacts(bool BiosBootmgr, bool UefiBootx64, bool BootBcd, bool BootWim);
internal sealed record FilesystemCompatibility(bool Fat32, bool Ntfs);
internal sealed record WriteMethodOption(string Method, List<string> BootTargets, bool TransformsContent);
internal sealed record Bootloaders(bool BiosElTorito, bool UefiElTorito, bool Bootmgr, bool EfiBootx64, bool EfiBootia32, bool Syslinux, bool Grub, bool AcronisBootwiz);
internal sealed record IsoHybrid(bool Mbr, bool Gpt, List<string> MbrPartitionTypes);
internal sealed record ManifestSummary(int Schema, string Mode, List<string> BootTargets, int FileCount, long TotalBytes, string TreeHash, string ManifestUrl, string ManifestSha256, List<SentinelFile> SentinelFiles, TechnicalAnalysisSummary TechnicalAnalysis);
internal sealed record TechnicalAnalysisSummary(SentinelFile? LargestFile, bool HasFilesOver4Gb, string RecommendedMethod, List<string> AlternativeMethods, List<string> Reasons, string AnalysisEngine, List<string> ImageBootTargets, List<WriteMethodOption> Methods);