using System.Net;
using System.Net.Http;
using qdisk.Core.Services;

namespace qdisk.Tests;

// Requisicao inicial do download: nova tentativa em erro passageiro (o mateweb respondeu 404 uma vez
// para um arquivo existente no teste real de 16/10/2026); erro definitivo nao insiste.
public class DownloadRetryTests
{
    private sealed class SequenceHandler(params HttpStatusCode[] codes) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var code = codes[Math.Min(Calls, codes.Length - 1)];
            Calls++;
            return Task.FromResult(new HttpResponseMessage(code));
        }
    }

    private static readonly TimeSpan[] NoDelay = [TimeSpan.Zero];

    [Fact]
    public async Task GetWithRetry_404DepoisOk_TentaDeNovoEDevolveOk()
    {
        var handler = new SequenceHandler(HttpStatusCode.NotFound, HttpStatusCode.OK);
        var retries = new List<string>();
        using var client = new HttpClient(handler);
        using var response = await DownloadService.GetWithRetryAsync(client, "http://teste/a.tibx", retries, CancellationToken.None, NoDelay);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Calls);
        Assert.Single(retries);
    }

    [Fact]
    public async Task GetWithRetry_ErroPassageiroSempre_ParaNaUltimaTentativaComOErro()
    {
        var handler = new SequenceHandler(HttpStatusCode.ServiceUnavailable);
        var retries = new List<string>();
        using var client = new HttpClient(handler);
        using var response = await DownloadService.GetWithRetryAsync(client, "http://teste/a.tibx", retries, CancellationToken.None, NoDelay);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(DownloadService.RequestMaxAttempts, handler.Calls);
        Assert.Equal(DownloadService.RequestMaxAttempts - 1, retries.Count);
    }

    [Fact]
    public async Task GetWithRetry_ErroDefinitivo_NaoInsiste()
    {
        var handler = new SequenceHandler(HttpStatusCode.Forbidden);
        using var client = new HttpClient(handler);
        using var response = await DownloadService.GetWithRetryAsync(client, "http://teste/a.tibx", new List<string>(), CancellationToken.None, NoDelay);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("www.dropbox.com", true)]
    [InlineData("uc123.dl.dropboxusercontent.com", true)]
    [InlineData("qsibr.sharepoint.com", true)]
    [InlineData("mateweb.ddns.net", false)]
    public void IsEligibleOrigin_MultiConexaoNoDropboxENoSharePoint(string host, bool expected)
    {
        Assert.Equal(expected, SegmentedDownloadService.IsEligibleOrigin(host));
    }
}
