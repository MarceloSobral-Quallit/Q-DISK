using qdisk.Core.Models;

namespace qdisk.Tests;

// Padrao de fabrica do download: varias conexoes ligadas, 8 conexoes (configuracao validada no teste
// real de 17/10/2026). Vale para instalacao nova; configuracao ja salva mantem o valor gravado.
public class AppSettingsDefaultsTests
{
    [Fact]
    public void CreateDefault_VariasConexoesLigadasCom8()
    {
        var settings = AppSettings.CreateDefault();
        Assert.True(settings.EnableMultiConnectionDownload);
        Assert.Equal(8, settings.MaxDownloadConnections);
    }

    [Fact]
    public void NovaInstancia_MesmoPadrao()
    {
        var settings = new AppSettings();
        Assert.True(settings.EnableMultiConnectionDownload);
        Assert.Equal(8, settings.MaxDownloadConnections);
    }

    // Verificacao de imagens na abertura: ligada por padrao, como a verificacao de versao nova do app.
    [Fact]
    public void VerificacaoDeImagens_LigadaPorPadrao()
    {
        Assert.True(AppSettings.CreateDefault().AutoValidateMd5OnStartup);
        Assert.True(AppSettings.CreateDefault().AutoUpdateApp);
        Assert.True(new AppSettings().AutoValidateMd5OnStartup);
        Assert.False(AppSettings.ApplyDefaultUpgrades(AppSettings.CreateDefault()));
    }

    [Fact]
    public void ConfiguracaoSalvaAntiga_LigaUmaVez_DepoisRespeitaOTecnico()
    {
        // settings.json gravado ate a 3.11: a opcao vinha False (sem efeito) e nao havia a marca do ajuste.
        var json = "{\"AutoValidateMd5OnStartup\": false, \"AutoUpdateApp\": true}";
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.False(settings.AutoValidateMd5OnStartup);

        Assert.True(AppSettings.ApplyDefaultUpgrades(settings));
        Assert.True(settings.AutoValidateMd5OnStartup);

        // O tecnico desligou depois do ajuste: continua desligada.
        settings.AutoValidateMd5OnStartup = false;
        Assert.False(AppSettings.ApplyDefaultUpgrades(settings));
        Assert.False(settings.AutoValidateMd5OnStartup);
    }
}
