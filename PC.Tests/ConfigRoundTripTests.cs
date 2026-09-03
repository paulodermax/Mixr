using Mixr.Models;
using Mixr.Services;
using Xunit;

namespace Mixr.Tests;

public class ConfigRoundTripTests
{
    [Fact]
    public void Writer_DoesNotPersistIgdbSecrets()
    {
        var cfg = new MixrConfig
        {
            ComPort = "COM9",
            BaudRate = 115200,
            IgdbClientId = "id-should-not-leak",
            IgdbClientSecret = "secret-should-not-leak",
        };

        var path = Path.Combine(Path.GetTempPath(), $"mixr-cfg-{Guid.NewGuid():N}.yaml");
        try
        {
            MixrConfigWriter.Save(cfg, path);
            var yaml = File.ReadAllText(path);
            Assert.Contains("com_port: COM9", yaml);
            Assert.Contains("baud_rate: 115200", yaml);
            Assert.DoesNotContain("should-not-leak", yaml);
            Assert.DoesNotContain("igdb", yaml);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AtomicFile_ReplacesExistingContentAndLeavesNoTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mixr-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.txt");
            AtomicFile.WriteAllText(path, "one");
            AtomicFile.WriteAllText(path, "two");
            Assert.Equal("two", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SecretsFile_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mixr-secrets-{Guid.NewGuid():N}.yaml");
        try
        {
            IgdbCredentialResolver.WriteSecretsFile(path, "abc", "xyz");
            var cfg = new MixrConfig();
            IgdbCredentialResolver.LoadFromDisk(cfg, path);
            var (id, secret) = IgdbCredentialResolver.GetFileValues();
            Assert.Equal("abc", id);
            Assert.Equal("xyz", secret);
        }
        finally
        {
            File.Delete(path);
            IgdbCredentialResolver.LoadFromDisk(new MixrConfig(), path);
        }
    }
}
