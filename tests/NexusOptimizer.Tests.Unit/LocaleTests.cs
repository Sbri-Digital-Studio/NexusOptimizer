using NexusOptimizer.App.Services;

namespace NexusOptimizer.Tests;

/// <summary>
/// Gate della localizzazione: i due dizionari devono coprire le stesse chiavi.
/// Una chiave presente solo in italiano produce, passando all'inglese, un testo
/// misto o addirittura l'identificatore grezzo in interfaccia.
/// </summary>
public sealed class LocaleTests
{
    [Fact]
    public void PlaceholdersMatchBetweenLanguages()
    {
        var italian = Locale.DictionaryOf("it");
        var english = Locale.DictionaryOf("en");
        var mismatched = new List<string>();

        foreach (var pair in italian)
        {
            if (!english.TryGetValue(pair.Key, out var translated)) continue;
            var expected = Placeholders(pair.Value);
            var actual = Placeholders(translated);
            if (!expected.SetEquals(actual))
                mismatched.Add(pair.Key);
        }

        // Un segnaposto perso nella traduzione fa sparire un numero dal messaggio;
        // uno di troppo fa crollare string.Format (e Locale.F mostra il testo grezzo).
        Assert.True(mismatched.Count == 0,
            "Segnaposto incoerenti fra IT ed EN: " + string.Join(", ", mismatched));
    }

    private static HashSet<string> Placeholders(string text)
        => [.. System.Text.RegularExpressions.Regex.Matches(text, "{[0-9]+}").Select(m => m.Value)];

    [Theory]
    [InlineData("opt.state.applied")]
    [InlineData("opt.item.power.detail")]
    [InlineData("opt.status.aligned")]
    [InlineData("gam.rep.power")]
    [InlineData("gam.status.off")]
    [InlineData("gam.cat.launcher")]
    [InlineData("pc.sec.cpu")]
    [InlineData("pc.row.baseclock")]
    [InlineData("home.tool.gaming")]
    [InlineData("home.clean.temp")]
    [InlineData("fmt.uptime.today")]
    public void RuntimeMessages_ResolveInBothLanguages(string key)
    {
        try
        {
            Locale.Set("it");
            Assert.NotEqual(key, Locale.T(key));
            Locale.Set("en");
            Assert.NotEqual(key, Locale.T(key));
        }
        finally
        {
            Locale.Set("it");
        }
    }

    [Fact]
    public void ItalianAndEnglishCoverTheSameKeys()
    {
        var italian = Locale.KeysOf("it").ToHashSet(StringComparer.Ordinal);
        var english = Locale.KeysOf("en").ToHashSet(StringComparer.Ordinal);

        var onlyItalian = italian.Except(english).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var onlyEnglish = english.Except(italian).OrderBy(key => key, StringComparer.Ordinal).ToArray();

        Assert.True(onlyItalian.Length == 0, "Senza traduzione inglese: " + string.Join(", ", onlyItalian));
        Assert.True(onlyEnglish.Length == 0, "Senza testo italiano: " + string.Join(", ", onlyEnglish));
    }

    [Theory]
    [InlineData("notif.title")]
    [InlineData("notif.disk.title")]
    [InlineData("set.updates.enable")]
    [InlineData("gam.actions.title")]
    [InlineData("opt.list.title")]
    [InlineData("opt.item.startup.title")]
    [InlineData("pc.footer.note")]
    public void KeysUsedByTheNewSections_ResolveInBothLanguages(string key)
    {
        try
        {
            Locale.Set("it");
            var italian = Locale.T(key);
            Locale.Set("en");
            var english = Locale.T(key);

            // Una chiave mancante torna se stessa: sarebbe visibile in interfaccia.
            Assert.NotEqual(key, italian);
            Assert.NotEqual(key, english);
        }
        finally
        {
            Locale.Set("it");
        }
    }

    [Fact]
    public void Format_ResolvesPlaceholders_AndSurvivesAMalformedText()
    {
        Locale.Set("it");

        var message = Locale.F("notif.disk.low.msg", ["C:", "8", "12 GB"]);

        Assert.Contains("C:", message, StringComparison.Ordinal);
        Assert.Contains("8", message, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", message, StringComparison.Ordinal);
    }
}
