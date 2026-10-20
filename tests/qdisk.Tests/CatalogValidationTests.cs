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
    [InlineData("qdisk.catalog.rd.json")]
    [InlineData("qdisk.catalog.pnb.json")]
    [InlineData("qdisk.catalog.tools.json")]
    [InlineData("qdisk.catalog.devteam.json")]
    public void CatalogoVersionado_CarregaSemErro(string fileName)
    {
        var items = new CatalogService().LoadFromJson(Path.Combine(ConfigDir(), fileName), includeDisabled: true);
        Assert.NotEmpty(items);
    }
}
