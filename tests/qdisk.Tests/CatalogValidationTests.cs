using qdisk.Core.Services;

namespace qdisk.Tests;

// Os catalogos versionados em config/ precisam continuar validos com as regras novas (HTTPS etc.).
public class CatalogValidationTests
{
    private static string ConfigDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "qdisk.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "config");
    }

    [Theory]
    [InlineData("qdisk.rd.json")]
    [InlineData("qdisk.pnb.json")]
    [InlineData("qdisk.tools.json")]
    [InlineData("qdisk.devteam.json")]
    public void CatalogoVersionado_CarregaSemErro(string fileName)
    {
        var items = new CatalogService().LoadFromJson(Path.Combine(ConfigDir(), fileName), includeDisabled: true);
        Assert.NotEmpty(items);
    }
}
