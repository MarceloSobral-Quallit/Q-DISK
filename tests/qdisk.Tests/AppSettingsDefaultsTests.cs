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
    }

    [Fact]
    public void ConfiguracaoSalva_OpcaoDesligadaContinuaDesligada()
    {
        // O tecnico desmarcou "Verificar atualizacoes de imagens na inicializacao": gravar e reler mantem False.
        var settings = AppSettings.CreateDefault();
        settings.AutoValidateMd5OnStartup = false;
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var lida = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.False(lida.AutoValidateMd5OnStartup);
    }
}
