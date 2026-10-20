using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using qdisk.Core.Models;
using qdisk.Core.Services;

namespace qdisk.Tests;

// T-01: o token gerado pela regra publicada para o site (docs/DOCUMENTACAO_TECNICA.md, secao 14) e validado pelos
// verificadores embutidos (PBKDF2), sem o app conhecer os internal_id.
public class TokenValidationTests
{
    private static string SiteToken(DateTime date, int internalId)
    {
        var days = (date.Date - new DateTime(1970, 10, 1)).Days;
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes((days + internalId).ToString()))).ToLowerInvariant();
        var digits = new string(hash.Where(char.IsDigit).ToArray());
        return digits.Length >= 6 ? digits[..6] : digits.PadLeft(6, '0');
    }

    private static readonly (UserProfile Profile, int Id)[] Profiles =
        { (UserProfile.RD, 10911), (UserProfile.PNB, 60171), (UserProfile.DevTeam, 270777) };

    // Verificadores de hoje montados como o gerador faz (poucas iteracoes para o teste ser rapido).
    private static TokenVerifierSet TodaySet()
    {
        var set = new TokenVerifierSet { Iterations = 1000, Salt = new byte[] { 1, 2, 3, 4 }, FirstDate = DateTime.Today.AddDays(-1), LastDate = DateTime.Today.AddDays(1) };
        foreach (var (profile, id) in Profiles)
        {
            var days = new Dictionary<string, string>();
            foreach (var day in new[] { DateTime.Today.AddDays(-1), DateTime.Today, DateTime.Today.AddDays(1) })
            {
                days[day.ToString("yyyy-MM-dd")] = Convert.ToHexString(TokenVerifierSet.ComputeHash(SiteToken(day, id), profile, day, set.Salt, set.Iterations));
            }
            set.Verifiers[profile] = days;
        }
        return set;
    }

    [Theory]
    [InlineData(10911, UserProfile.RD)]
    [InlineData(60171, UserProfile.PNB)]
    [InlineData(270777, UserProfile.DevTeam)]
    public void TokenDoSite_ResolveOPerfil(int internalId, UserProfile expected)
    {
        var token = SiteToken(DateTime.Today, internalId);
        Assert.Equal(expected, new TokenValidationService(TodaySet()).ResolveProfile(token));
    }

    [Fact]
    public void TokenDeAnteontem_ERecusado()
    {
        var token = SiteToken(DateTime.Today.AddDays(-2), 10911);
        Assert.Equal(UserProfile.None, new TokenValidationService(TodaySet()).ResolveProfile(token));
    }

    [Fact]
    public void SemVerificadores_NadaValidaEAvisaVencido()
    {
        var service = new TokenValidationService(new TokenVerifierSet());
        Assert.Equal(UserProfile.None, service.ResolveProfile(SiteToken(DateTime.Today, 10911)));
        Assert.True(service.VerifiersExpired);
    }

    // O arquivo gerado pelo Python (config/qdisk.tokens.app.json) valida o token de hoje calculado com os
    // internal_id de config/qdisk.tokens.json: gerador e app usam a mesma conta. E o arquivo do app nao traz IDs.
    [Fact]
    public void ArquivoGerado_ValidaTokenDeHoje_ESemInternalId()
    {
        var configDir = ConfigDir();
        var appJson = File.ReadAllText(Path.Combine(configDir, "qdisk.tokens.app.json"));
        Assert.DoesNotContain("internalId", appJson);

        var config = TokenConfig.Parse(appJson);
        Assert.True(config.Verifiers.Covers(DateTime.Today), "verificadores nao cobrem hoje: rode tools/utils/generate_token_verifiers.py");
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(configDir, "qdisk.tokens.json")));
        var service = new TokenValidationService(config.Verifiers);
        foreach (var item in source.RootElement.GetProperty("profiles").EnumerateArray())
        {
            var profile = Enum.Parse<UserProfile>(item.GetProperty("profile").GetString()!);
            var id = int.Parse(item.GetProperty("internalId").GetString()!);
            Assert.Equal(profile, service.ResolveProfile(SiteToken(DateTime.Today, id)));
        }
    }

    [Fact]
    public void Limite_BloqueiaDepoisDeTresErros_EZeraNoAcerto()
    {
        var path = Path.Combine(Path.GetTempPath(), $"token_attempts_{Guid.NewGuid():N}.dat");
        try
        {
            var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
            var limiter = new TokenAttemptLimiter(path);
            limiter.RegisterFailure(now);
            limiter.RegisterFailure(now);
            Assert.Equal(TimeSpan.Zero, limiter.RemainingLock(now));
            limiter.RegisterFailure(now);
            Assert.Equal(TimeSpan.FromSeconds(30), limiter.RemainingLock(now));

            // Vale entre instancias (reinicio do app).
            var other = new TokenAttemptLimiter(path);
            Assert.Equal(TimeSpan.FromSeconds(30), other.RemainingLock(now));
            other.RegisterFailure(now.AddSeconds(31));
            Assert.Equal(TimeSpan.FromMinutes(1), other.RemainingLock(now.AddSeconds(31)));

            other.RegisterSuccess();
            Assert.Equal(TimeSpan.Zero, new TokenAttemptLimiter(path).RemainingLock(now.AddSeconds(31)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string ConfigDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "qdisk.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "config");
    }
}
