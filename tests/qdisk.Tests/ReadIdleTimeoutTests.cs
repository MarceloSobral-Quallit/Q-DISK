using qdisk.Core.Services;

namespace qdisk.Tests;

// I-06: conexao que para de enviar dados nao pode esperar para sempre.
public class ReadIdleTimeoutTests
{
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    [Fact]
    public async Task ConexaoTravada_ViraTimeout()
    {
        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            DownloadService.ReadWithIdleTimeoutAsync(new StalledStream(), new byte[16], CancellationToken.None, TimeSpan.FromMilliseconds(300)));
        Assert.Contains("travada", ex.Message);
    }

    [Fact]
    public async Task CancelamentoDoUsuario_ContinuaSendoCancelamento()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DownloadService.ReadWithIdleTimeoutAsync(new StalledStream(), new byte[16], cts.Token, TimeSpan.FromSeconds(30)));
    }
}
