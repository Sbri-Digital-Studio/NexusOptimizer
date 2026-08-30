using NexusOptimizer.Core.Updates;

namespace NexusOptimizer.Tests;

/// <summary>
/// Il canale di aggiornamento e' l'unico punto con una chiamata di rete: qui si
/// verifica che accetti solo HTTPS, che rifiuti un manifest malformato e che non
/// annunci mai un downgrade.
/// </summary>
public sealed class UpdateChannelTests
{
    [Theory]
    [InlineData("https://example.org/nexus/latest.json", true)]
    [InlineData("http://example.org/nexus/latest.json", false)]
    [InlineData("ftp://example.org/latest.json", false)]
    [InlineData("example.org/latest.json", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedFeed_AcceptsOnlyAbsoluteHttps(string? url, bool expected)
        => Assert.Equal(expected, UpdateChannel.IsSupportedFeed(url));

    [Fact]
    public void Parse_ReadsVersionAndUrl()
    {
        const string json = """
            { "version": "0.3.1", "url": "https://example.org/releases/0.3.1",
              "notes": "Correzioni", "publishedUtc": "2026-03-04T10:00:00Z" }
            """;

        var manifest = UpdateChannel.Parse(json);

        Assert.NotNull(manifest);
        Assert.Equal("0.3.1", manifest!.Version);
        Assert.Equal("https://example.org/releases/0.3.1", manifest.Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("non è json")]
    [InlineData("{ \"notes\": \"senza versione\" }")]
    [InlineData("{ \"version\": \"\" }")]
    [InlineData("{ \"version\": \"ultima\" }")]
    public void Parse_RejectsUnusableManifest(string json)
        => Assert.Null(UpdateChannel.Parse(json));

    [Fact]
    public void Parse_RejectsInsecureReleasePage()
    {
        const string json = """{ "version": "0.4.0", "url": "http://example.org/releases/0.4.0" }""";

        Assert.Null(UpdateChannel.Parse(json));
    }

    [Theory]
    [InlineData("0.2.0", true)]
    [InlineData("v0.2.0", true)]
    [InlineData("0.1.1", true)]
    [InlineData("1.0.0-beta.2", true)]
    [InlineData("0.1.0", false)]
    [InlineData("0.0.9", false)]
    [InlineData("0.1.0.7", false)]
    [InlineData("qualsiasi", false)]
    [InlineData(null, false)]
    public void IsNewer_ComparesOnlyThreeSignificantNumbers(string? candidate, bool expected)
        => Assert.Equal(expected, UpdateChannel.IsNewer(candidate, new Version(0, 1, 0)));

    [Fact]
    public void ParseVersion_IgnoresPreReleaseSuffix()
        => Assert.Equal(new Version(1, 2, 3), UpdateChannel.ParseVersion("1.2.3-rc.1"));
}
