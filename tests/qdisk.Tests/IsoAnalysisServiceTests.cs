using qdisk.Core.Services;

namespace qdisk.Tests;

// P-02: leitura little-endian usada na analise El Torito.
public class IsoAnalysisServiceTests
{
    [Fact]
    public void ReadUInt32Le_LeOsQuatroBytes()
    {
        // LBA 0x00000113 = 275: com o bug antigo, o resultado era 0x13 | 0x01 = 0x13.
        var buffer = new byte[] { 0x13, 0x01, 0x00, 0x00 };
        Assert.Equal(275u, IsoAnalysisService.ReadUInt32Le(buffer, 0));
        Assert.Equal(0x12345678u, IsoAnalysisService.ReadUInt32Le(new byte[] { 0x78, 0x56, 0x34, 0x12 }, 0));
    }

    [Fact]
    public void ReadUInt16Le_LeOsDoisBytes()
    {
        // LoadSegment 0x07C0: com o bug antigo, virava 0xC7.
        Assert.Equal((ushort)0x07C0, IsoAnalysisService.ReadUInt16Le(new byte[] { 0xC0, 0x07 }, 0));
    }
}
