using System.Net.Http;
using qdisk.Core.Services;

namespace qdisk.Tests;

// User-Agent no formato pedido pelo SharePoint Online para aplicacoes de empresa (NONISV|Empresa|App/Versao).
public class HttpIdentityTests
{
    [Fact]
    public void Apply_ProdutoDoClienteMaisIdentificacaoDaAplicacao()
    {
        using var client = new HttpClient();
        HttpIdentity.Apply(client.DefaultRequestHeaders, "QuallitDownloader/1.0");
        var header = client.DefaultRequestHeaders.UserAgent.ToString();
        Assert.StartsWith("QuallitDownloader/1.0 NONISV|Quallit|qdisk/", header);
        Assert.Matches(@"qdisk/\d+\.\d{2}\.\d{2}\.\d+$", header);
    }
}
