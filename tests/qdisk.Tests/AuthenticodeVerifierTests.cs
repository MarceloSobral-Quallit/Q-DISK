using qdisk.Core.Services;

namespace qdisk.Tests;

// U-01: o instalador de atualizacao so e aceito com assinatura integra de um certificado autorizado.
public class AuthenticodeVerifierTests
{
    [Fact]
    public void ArquivoSemAssinatura_ERecusado()
    {
        var unsigned = typeof(AuthenticodeVerifierTests).Assembly.Location;
        var result = AuthenticodeVerifier.Verify(unsigned);
        Assert.False(result.IsTrusted);
        Assert.Contains("nao tem assinatura", result.Message);
    }

    [Fact]
    public void ArquivoAssinadoPorOutroCertificado_ERecusado()
    {
        // dotnet.exe tem assinatura Authenticode embutida da Microsoft.
        var dotnet = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", "..", "dotnet.exe");
        Assert.True(File.Exists(dotnet), $"dotnet.exe nao encontrado em {dotnet}");
        var result = AuthenticodeVerifier.Verify(dotnet);
        Assert.False(result.IsTrusted);
        Assert.Contains("nao autorizado", result.Message);
    }

    [Fact]
    public void ArquivoInexistente_ERecusado()
    {
        Assert.False(AuthenticodeVerifier.Verify(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")).IsTrusted);
    }
}
