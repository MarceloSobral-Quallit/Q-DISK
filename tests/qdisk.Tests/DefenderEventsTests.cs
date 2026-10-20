using qdisk.Core.Services;

namespace qdisk.Tests;

// Arquivo que some do USB depois da copia: apontar o Defender como causa (eventos 1116/1117/1121).
public class DefenderEventsTests
{
    private const string Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    private static string Event(int id, params (string Name, string Value)[] data) =>
        $"<Event xmlns='{Ns}'><System><EventID>{id}</EventID></System><EventData>" +
        string.Concat(data.Select(d => $"<Data Name='{d.Name}'>{d.Value}</Data>")) +
        "</EventData></Event>";

    private static readonly string Xml =
        Event(1117, ("Threat Name", "HackTool:Win32/ProductKey!MSR"), ("Path", @"file:_D:\Programs\ProduKey\ProduKey.exe")) +
        Event(1121, ("ID", "56A863A9-875E-4185-98A7-B882C64B5CE5"), ("Path", @"D:\Programs\Macrium Reflect\psmounterex.sys")) +
        Event(1121, ("ID", "C0033C00-D16D-4114-A5A0-DC9B3A7D2CEB"), ("Path", @"D:\boot\bootsect.exe"));

    [Fact]
    public void Parse_LeAmeacaERegraAsr()
    {
        var events = DefenderEvents.Parse(Xml);

        Assert.Equal(3, events.Count);
        Assert.Equal(@"D:\Programs\ProduKey\ProduKey.exe", events[0].Paths.Single());
        Assert.Equal("antivirus: HackTool:Win32/ProductKey!MSR", events[0].Description);
        Assert.Equal("regra ASR: driver assinado vulneravel", events[1].Description);
    }

    [Fact]
    public void MatchMissingFiles_SoOsAusentesComEvento()
    {
        var matches = DefenderEvents.MatchMissingFiles(DefenderEvents.Parse(Xml), @"D:\",
            new[] { @"Programs\ProduKey\ProduKey.exe", @"Programs\Outro\nao_bloqueado.exe" });

        Assert.Single(matches);
        Assert.Equal("antivirus: HackTool:Win32/ProductKey!MSR", matches[@"Programs\ProduKey\ProduKey.exe"]);
    }

    [Fact]
    public void Parse_SemEventos_ListaVazia()
    {
        Assert.Empty(DefenderEvents.Parse(""));
    }

    [Fact]
    public void BuildWevtutilArguments_FiltraIdsETempo()
    {
        var args = DefenderEvents.BuildWevtutilArguments(120, 200);

        Assert.Contains("EventID=1116 or EventID=1117 or EventID=1121", args);
        Assert.Contains("timediff(@SystemTime) <= 7200000", args);
        Assert.Contains("/f:xml", args);
    }
}
