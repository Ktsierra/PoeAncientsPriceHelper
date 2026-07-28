using System.IO;
using System.Linq;
using PoeCurrencyHelper;

namespace PoeCurrencyHelper.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pch-cfg-" + Guid.NewGuid().ToString("N"));

    public ConfigStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void MissingFileYieldsDefaults()
    {
        var config = ConfigStore.Load(_dir);
        Assert.Equal("Runes of Aldur", config.LeagueName);
        Assert.Equal("", config.BaseCurrency);
        Assert.True(config.AlwaysOnTop);
        Assert.True(config.HideNoData);
        Assert.Equal("volume", config.SortBy);
    }

    [Fact]
    public void RoundTripsEverySetting()
    {
        var saved = new AppConfig
        {
            LeagueName = "HC Runes of Aldur",
            BaseCurrency = "exalted orb",
            WindowX = 120,
            WindowY = 340,
            WindowWidth = 500,
            WindowHeight = 700,
            AlwaysOnTop = false,
            SortBy = "name",
            HideNoData = false,
        };
        ConfigStore.Save(saved, _dir);

        var loaded = ConfigStore.Load(_dir);
        Assert.Equal("HC Runes of Aldur", loaded.LeagueName);
        Assert.Equal("exalted orb", loaded.BaseCurrency);
        Assert.Equal(120, loaded.WindowX);
        Assert.Equal(340, loaded.WindowY);
        Assert.Equal(500, loaded.WindowWidth);
        Assert.Equal(700, loaded.WindowHeight);
        Assert.False(loaded.AlwaysOnTop);
        Assert.Equal("name", loaded.SortBy);
        Assert.False(loaded.HideNoData);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaultsInsteadOfThrowing()
    {
        File.WriteAllText(Path.Combine(_dir, "config.json"), "{ this is not json");
        Assert.Equal("Runes of Aldur", ConfigStore.Load(_dir).LeagueName);
    }

    [Fact]
    public void SaveLeavesNoTempFileBehind()
    {
        ConfigStore.Save(new AppConfig(), _dir);
        ConfigStore.Save(new AppConfig { LeagueName = "HC Runes of Aldur" }, _dir);
        Assert.False(File.Exists(Path.Combine(_dir, "config.json.tmp")));
        Assert.Equal("HC Runes of Aldur", ConfigStore.Load(_dir).LeagueName);
    }

    // AvailableLeagues is [JsonIgnore] precisely so Newtonsoft's ObjectCreationHandling.Auto cannot
    // append the persisted list onto the initializer default and duplicate every entry on each load.
    [Fact]
    public void AvailableLeaguesIsNotPersistedAndNeverDuplicates()
    {
        var config = new AppConfig();
        int expected = config.AvailableLeagues.Count;
        ConfigStore.Save(config, _dir);
        ConfigStore.Save(ConfigStore.Load(_dir), _dir);

        Assert.Equal(expected, ConfigStore.Load(_dir).AvailableLeagues.Count);
        Assert.DoesNotContain("AvailableLeagues", File.ReadAllText(Path.Combine(_dir, "config.json")));
    }
}
