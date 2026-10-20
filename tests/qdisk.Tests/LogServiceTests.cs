using qdisk.Core.Services;

namespace qdisk.Tests;

// P-08: chamada sincrona do log a partir de um contexto de thread unica (como a UI) nao pode travar,
// mesmo com outra thread gravando no log ao mesmo tempo (semaforo disputado).
public class LogServiceTests
{
    private sealed class BlockedUiContext : SynchronizationContext
    {
        // Simula a UI bloqueada em .GetResult(): nada postado para este contexto e executado.
        public override void Post(SendOrPostCallback d, object? state) { }
    }

    [Fact]
    public void WriteAsync_SincronoComContextoBloqueadoESemaforoDisputado_NaoTrava()
    {
        var log = new LogService(Path.Combine(Path.GetTempPath(), $"quallit-test-{Guid.NewGuid():N}.log"));
        using var stop = new CancellationTokenSource();
        var background = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
                await log.WriteAsync("TESTE | P-08 | segundo plano");
        });

        var ui = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new BlockedUiContext());
            // Bloqueio proposital: e exatamente o uso sincrono feito pela UI que o teste precisa reproduzir.
#pragma warning disable xUnit1031
            for (var i = 0; i < 200; i++)
                log.WriteAsync($"TESTE | P-08 | ui {i}").GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        }) { IsBackground = true };
        ui.Start();

        var finished = ui.Join(TimeSpan.FromSeconds(20));
        stop.Cancel();
        Assert.True(finished, "WriteAsync travou com contexto bloqueado e semaforo disputado.");
    }
}
