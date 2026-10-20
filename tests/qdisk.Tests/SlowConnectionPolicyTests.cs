using qdisk.Core.Services;

namespace qdisk.Tests;

// Troca de conexao lenta no SharePoint e redivisao da cauda no download segmentado.
public class SlowConnectionPolicyTests
{
    [Theory]
    [InlineData("https://qsibr.sharepoint.com/sites/ImagensField/a.iso", true)]
    [InlineData("https://sharepoint.com/a.iso", true)]
    [InlineData("https://uc123.dl.dropboxusercontent.com/cd/0/get/a", false)]
    [InlineData("https://mateweb.ddns.net/TIBX/a.tibx", false)]
    [InlineData("https://notsharepoint.com/a.iso", false)]
    public void AppliesTo_SoSharePoint(string url, bool expected)
    {
        Assert.Equal(expected, SlowConnectionPolicy.AppliesTo(new Uri(url)));
    }

    [Fact]
    public void AppliesTo_SemUri_Falso()
    {
        Assert.False(SlowConnectionPolicy.AppliesTo(null));
    }

    [Fact]
    public void IsSlow_AntesDaJanela_NaoPedeTroca()
    {
        var monitor = new SlowConnectionPolicy();
        Assert.False(monitor.IsSlow(1, long.MaxValue));
        Assert.Equal(0, monitor.Swaps);
    }

    [Fact]
    public void RecordSwap_JanelaDobraAteOTeto()
    {
        var monitor = new SlowConnectionPolicy();
        Assert.Equal(SlowConnectionPolicy.InitialWindowMs, monitor.WindowMs);
        for (var i = 0; i < 10; i++) monitor.RecordSwap();
        Assert.Equal(SlowConnectionPolicy.MaxWindowMs, monitor.WindowMs);
        Assert.Equal(10, monitor.Swaps);
    }

    [Fact]
    public void Coordenador_UmaTrocaPorSegundo()
    {
        var coordinator = new SlowSwapCoordinator();
        Assert.True(coordinator.TryAcquire());
        Assert.False(coordinator.TryAcquire());
    }

    [Fact]
    public void Coordenador_RetryAfterPausaAsTrocas()
    {
        var coordinator = new SlowSwapCoordinator();
        coordinator.PauseFor(TimeSpan.FromMinutes(1));
        Assert.False(coordinator.TryAcquire());
    }

    private const long MiB = 1024 * 1024;

    [Fact]
    public void Redivisao_MetadeFinalDoTrechoComMaisBytesFaltando()
    {
        var plan = new SegmentedDownloadService.SegmentPlan("a.iso", new (long Start, long Finish)[] { (0, 100 * MiB - 1), (100 * MiB, 200 * MiB - 1) });
        var segments = plan.InitialSegments();
        plan.Accept(segments[0], (int)(10 * MiB));      // trecho 0: faltam 90 MiB
        plan.Complete(segments[1]);                     // trecho 1 terminou: a conexao fica livre

        var stolen = plan.TrySplit(16 * MiB);

        Assert.NotNull(stolen);
        Assert.Equal(55 * MiB, stolen.Start);           // 10 MiB + 90/2
        Assert.Equal(100 * MiB - 1, stolen.Finish);
        Assert.Equal(55 * MiB - 1, plan.CurrentFinish(segments[0]));
        Assert.Equal(new[] { "a.iso.part0", "a.iso.part2", "a.iso.part1" }, plan.PartPathsInOrder());
        Assert.Equal(1, plan.Splits);
    }

    [Fact]
    public void Redivisao_NaoDivideTrechoPequeno()
    {
        var plan = new SegmentedDownloadService.SegmentPlan("a.iso", new (long Start, long Finish)[] { (0, 20 * MiB - 1) });
        Assert.Null(plan.TrySplit(16 * MiB));          // 20 MiB < 2 x 16 MiB
    }

    [Fact]
    public void Accept_NaoPassaDoFimQueEncolheu()
    {
        var plan = new SegmentedDownloadService.SegmentPlan("a.iso", new (long Start, long Finish)[] { (0, 100 * MiB - 1) });
        var segment = plan.InitialSegments()[0];
        plan.TrySplit(16 * MiB);                        // fim passa para 50 MiB - 1
        plan.Accept(segment, (int)(50 * MiB - 100));
        Assert.Equal(100, plan.Accept(segment, 4096));   // so cabem os 100 bytes ate o novo fim
    }
}
