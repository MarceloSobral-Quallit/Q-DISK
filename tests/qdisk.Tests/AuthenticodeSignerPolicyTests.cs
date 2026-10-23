using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using qdisk.Core.Services;

namespace qdisk.Tests;

// U-01b: regra do assinante do instalador de atualizacao (AuthenticodeVerifier.EvaluateSigner):
// chave publica fixada, impressao digital fixada ou certificado da Quallit emitido pela DigiCert com cadeia confiavel.
public class AuthenticodeSignerPolicyTests
{
    private const int TrustedChain = 0;
    private const int UntrustedRoot = unchecked((int)0x800B0109);
    private const string Quallit = AuthenticodeVerifier.TrustedSignerOrganization;
    private const string DigiCert = AuthenticodeVerifier.TrustedIssuerOrganization;

    // Certificado de assinante emitido por uma "CA" de teste com a organizacao informada.
    private static X509Certificate2 CreateSigner(string organization, string country, string issuerOrganization)
    {
        using var caKey = RSA.Create(2048);
        var caName = new X500DistinguishedNameBuilder();
        caName.AddCommonName("Test Code Signing CA");
        caName.AddOrganizationName(issuerOrganization);
        var caRequest = new CertificateRequest(caName.Build(), caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var ca = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        using var key = RSA.Create(2048);
        var name = new X500DistinguishedNameBuilder();
        name.AddCommonName(organization);
        name.AddOrganizationName(organization);
        name.AddCountryOrRegion(country);
        var request = new CertificateRequest(name.Build(), key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.Create(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMonths(6), new byte[] { 1, 2, 3, 4 });
    }

    private static AuthenticodeResult Evaluate(int trustCode, X509Certificate2 signer, string[]? keys = null, string[]? thumbs = null) =>
        AuthenticodeVerifier.EvaluateSigner(trustCode, signer, keys ?? Array.Empty<string>(), thumbs ?? Array.Empty<string>());

    [Fact]
    public void ChavePublicaFixada_Aceita_MesmoSemCadeiaConfiavel()
    {
        using var signer = CreateSigner("Outra Empresa", "US", "Outra CA");
        var result = Evaluate(UntrustedRoot, signer, keys: new[] { AuthenticodeVerifier.PublicKeyHash(signer).ToUpperInvariant() });
        Assert.True(result.IsTrusted);
        Assert.Contains("chave publica", result.Message);
    }

    [Fact]
    public void ImpressaoDigitalFixada_Aceita_MesmoSemCadeiaConfiavel()
    {
        using var signer = CreateSigner("Outra Empresa", "US", "Outra CA");
        var result = Evaluate(UntrustedRoot, signer, thumbs: new[] { signer.Thumbprint.ToLowerInvariant() });
        Assert.True(result.IsTrusted);
        Assert.Contains("certificado autorizado", result.Message);
    }

    [Fact]
    public void RenovacaoComChaveNova_QuallitEmitidoPelaDigiCert_CadeiaConfiavel_Aceita()
    {
        using var signer = CreateSigner(Quallit, "BR", DigiCert);
        var result = Evaluate(TrustedChain, signer);
        Assert.True(result.IsTrusted);
        Assert.Contains("DigiCert", result.Message);
    }

    [Fact]
    public void QuallitEmitidoPelaDigiCert_SemCadeiaConfiavel_ERecusado()
    {
        using var signer = CreateSigner(Quallit, "BR", DigiCert);
        Assert.False(Evaluate(UntrustedRoot, signer).IsTrusted);
    }

    [Theory]
    [InlineData("QUALLIT SERVICOS DE INFORMATICA", "BR", DigiCert)]   // titular parecido
    [InlineData(Quallit, "US", DigiCert)]                              // outro pais
    [InlineData(Quallit, "BR", "Outra CA, Inc.")]                      // outro emissor
    public void TitularOuEmissorDiferente_ComCadeiaConfiavel_ERecusado(string organization, string country, string issuer)
    {
        using var signer = CreateSigner(organization, country, issuer);
        var result = Evaluate(TrustedChain, signer);
        Assert.False(result.IsTrusted);
        Assert.Contains("nao autorizado", result.Message);
    }

    [Fact]
    public void ListasFixadas_TemValoresNoFormatoEsperado()
    {
        Assert.NotEmpty(AuthenticodeVerifier.TrustedSignerPublicKeys);
        Assert.All(AuthenticodeVerifier.TrustedSignerPublicKeys, k => Assert.Matches("^[0-9a-f]{64}$", k));
        Assert.All(AuthenticodeVerifier.TrustedSignerThumbprints, t => Assert.Matches("^[0-9A-F]{40}$", t));
    }
}
