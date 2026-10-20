using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using qdisk.Core.Models;
using qdisk.Core.Services;

namespace qdisk.Tests;

// Teste de velocidade por tempo: origem de cada item do catalogo (nome ou link) e item padrao (o maior).
public class NetSpeedTestTests
{
    [Theory]
    [InlineData("rd-win-atual-dropbox", "https://quallit.link/rd-win-atual-dropbox", DownloadOrigin.Dropbox)]
    [InlineData("PNB-STOREX-atual-dropbox", "https://quallit.link/pnb-storex-db", DownloadOrigin.Dropbox)]
    [InlineData("rd-win-atual-sharepoint", "https://quallit.link/rd-win-atual-sharepoint", DownloadOrigin.SharePoint)]
    [InlineData("PNB-WIN7-X64-sharepoint", "https://quallit.link/pnb-win7-x64-sp", DownloadOrigin.SharePoint)]
    [InlineData("WIN7-POSITIVO", "https://mateweb.ddns.net/TIBX/PNB-WIN7/WIN7-POSITIVO.TIBX", DownloadOrigin.Outra)]
    public void ClassifyOrigin_PeloNomeOuLink(string name, string url, DownloadOrigin expected)
    {
        Assert.Equal(expected, NetSpeedTestService.ClassifyOrigin(new CatalogItem { Name = name, FileUrl = url }));
    }

    // SharePoint respondeu 429 com Retry-After: 5 no teste real de 16/10/2026 e entregou na tentativa seguinte.
    private sealed class SequenceHandler(params HttpStatusCode[] codes) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(codes[Math.Min(Calls, codes.Length - 1)]);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            Calls++;
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task SendWithRetry_429DepoisParcial_TentaDeNovo()
    {
        var handler = new SequenceHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.PartialContent);
        using var client = new HttpClient(handler);
        using var response = await NetSpeedTestService.SendWithRetryAsync(client, "http://teste/a.tibx", 0, 0, CancellationToken.None);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task SendWithRetry_429Sempre_ParaNaUltimaTentativa()
    {
        var handler = new SequenceHandler(HttpStatusCode.TooManyRequests);
        using var client = new HttpClient(handler);
        using var response = await NetSpeedTestService.SendWithRetryAsync(client, "http://teste/a.tibx", null, null, CancellationToken.None);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(NetSpeedTestService.RequestMaxAttempts, handler.Calls);
    }

    [Fact]
    public async Task SendWithRetry_403_NaoInsiste()
    {
        var handler = new SequenceHandler(HttpStatusCode.Forbidden);
        using var client = new HttpClient(handler);
        using var response = await NetSpeedTestService.SendWithRetryAsync(client, "http://teste/a.tibx", null, null, CancellationToken.None);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void ItemsForOrigin_SoDaOrigem_DoMaiorParaOMenor()
    {
        var items = new[]
        {
            new CatalogItem { Name = "acronis-dropbox", FileUrl = "https://quallit.link/acronis-dropbox", ImageSizeBytes = 723386368 },
            new CatalogItem { Name = "acronis-sharepoint", FileUrl = "https://quallit.link/acronis-sharepoint", ImageSizeBytes = 723386368 },
            new CatalogItem { Name = "rd-win-atual-dropbox", FileUrl = "https://quallit.link/rd-win-atual-dropbox", ImageSizeBytes = 20818337792 },
        };
        var dropbox = NetSpeedTestService.ItemsForOrigin(items, DownloadOrigin.Dropbox);
        Assert.Equal(new[] { "rd-win-atual-dropbox", "acronis-dropbox" }, dropbox.Select(i => i.Name));
    }
}
