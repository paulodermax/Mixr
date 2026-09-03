using System.Text;
using Mixr.Services;
using Xunit;

namespace Mixr.Tests;

public class FirmwareImageTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.2", true)]
    [InlineData("v1.3.0", "1.2.9", true)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.2.3", "1.10.0", false)]
    [InlineData("1.2.3-5-gabcdef", "1.2.3", false)]
    [InlineData("1.2.4-5-gabcdef", "1.2.3", true)]
    [InlineData("1.0.0", null, true)]
    [InlineData("1.0.0", "", true)]
    public void IsNewerThan_ComparesNumericPrefix(string candidate, string? installed, bool expected)
    {
        Assert.Equal(expected, FirmwareImage.IsNewerThan(candidate, installed));
    }

    [Theory]
    [InlineData("256c126-dirty", "256c126-dirty", false)]
    [InlineData("256c126-dirty", "0e84c15", true)]
    [InlineData("1.2.0", "256c126-dirty", true)]
    public void IsNewerThan_NonNumericVersionsCompareByEquality(string candidate, string installed, bool expected)
    {
        Assert.Equal(expected, FirmwareImage.IsNewerThan(candidate, installed));
    }

    [Fact]
    public void Load_ParsesEspAppDescriptor()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mixr-fw-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, BuildFakeImage("1.4.2", "Mixr"));
            var img = FirmwareImage.Load(path);
            Assert.Equal("1.4.2", img.Version);
            Assert.Equal("Mixr", img.ProjectName);
            Assert.Equal(32, img.Sha256.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsNonEspImage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mixr-fw-{Guid.NewGuid():N}.bin");
        try
        {
            var bytes = BuildFakeImage("1.0.0", "Mixr");
            bytes[0] = 0x00;
            File.WriteAllBytes(path, bytes);
            Assert.Throws<InvalidDataException>(() => FirmwareImage.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Minimales ESP-IDF-Image: Header 0xE9, esp_app_desc_t bei 0x20 (Magic, version @+0x10, project_name @+0x30).</summary>
    internal static byte[] BuildFakeImage(string version, string project)
    {
        var bytes = new byte[0x400];
        bytes[0] = 0xE9;
        BitConverter.GetBytes(0xABCD5432u).CopyTo(bytes, 0x20);
        Encoding.UTF8.GetBytes(version).CopyTo(bytes, 0x20 + 0x10);
        Encoding.UTF8.GetBytes(project).CopyTo(bytes, 0x20 + 0x30);
        return bytes;
    }
}
