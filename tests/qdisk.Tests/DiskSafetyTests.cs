using qdisk.Core.Services;

namespace qdisk.Tests;

// D-01 / D-02: o disco do Windows nunca pode ser alvo, e um aparelho trocado e detectado.
public class DiskSafetyTests
{
    [Fact]
    public void DiscoDoWindows_EDetectado()
    {
        Assert.NotEmpty(DiskSafety.GetSystemDiskNumbers());
    }

    [Fact]
    public void DiscoDoWindows_NuncaPassaNaTrava()
    {
        var systemDisk = DiskSafety.GetSystemDiskNumbers().First();
        var identity = DiskSafety.TryQuery(systemDisk);
        Assert.NotNull(identity);
        var ex = Assert.Throws<DiskSafetyException>(() => DiskSafety.EnsureSameDisk(identity!));
        Assert.Contains("hospeda o Windows", ex.Message);
    }

    [Fact]
    public void SemIdentidade_ERecusado()
    {
        Assert.Throws<DiskSafetyException>(() => DiskSafety.EnsureSameDisk(null!));
    }

    [Fact]
    public void DiscoInexistente_ERecusado()
    {
        var fake = new DiskIdentity { DiskNumber = 250, ProductId = "X", SizeBytes = 1 };
        var ex = Assert.Throws<DiskSafetyException>(() => DiskSafety.EnsureSameDisk(fake));
        Assert.Contains("nao foi encontrado", ex.Message);
    }

    [Fact]
    public void AparelhoTrocado_NaoEOMesmo()
    {
        var a = new DiskIdentity { DiskNumber = 2, VendorId = "SanDisk", ProductId = "Ultra", SerialNumber = "123", SizeBytes = 32_000_000_000, BusType = 7, RemovableMedia = true };
        var b = new DiskIdentity { DiskNumber = 2, VendorId = "WD", ProductId = "My Passport", SerialNumber = "999", SizeBytes = 2_000_000_000_000, BusType = 7 };
        var sameDeviceOtherNumber = new DiskIdentity { DiskNumber = 3, VendorId = "SanDisk", ProductId = "Ultra", SerialNumber = "123", SizeBytes = 32_000_000_000, BusType = 7, RemovableMedia = true };
        Assert.False(a.SameDeviceAs(b));
        Assert.True(a.SameDeviceAs(sameDeviceOtherNumber));
    }

    [Theory]
    [InlineData(true, 32_000_000_000L, false)]
    [InlineData(false, 32_000_000_000L, true)]
    [InlineData(true, 512_000_000_000L, true)]
    public void ConfirmacaoReforcada(bool removable, long size, bool expected)
    {
        var id = new DiskIdentity { RemovableMedia = removable, SizeBytes = size };
        Assert.Equal(expected, DiskSafety.NeedsReinforcedConfirmation(id));
    }
}
