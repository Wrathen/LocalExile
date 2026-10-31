using GameServer.Modules.Ruthless;
using System.Text.Json;

public class RuthlessRulesTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "localexile-ruthless-rules-" + Guid.NewGuid());
    string Settings => Path.Combine(folder, "ruthless.json");
    readonly Clock clock = new();
    int version;

    sealed class Clock : TimeProvider
    {
        DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = now.AddSeconds(2);
    }

    void Write(string json)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Settings, json);
        File.SetLastWriteTimeUtc(Settings, new DateTime(2026, 1, 1).AddSeconds(++version));
        clock.Advance();
    }

    [Fact]
    public void Valid_edits_replace_the_snapshot_without_restarting_the_reader()
    {
        Write("""{"itemDropMultiplier":0.15,"gemExperienceMultiplier":0.4}""");
        var file = new RuthlessRulesFile(Settings, time: clock);
        var before = file.Current;
        Assert.Equal(0.15, before.ItemDropMultiplier);
        Assert.Equal(0.4, before.GemExperienceMultiplier);
        Write("""{"itemDropMultiplier":0.6,"gemExperienceMultiplier":0.8}""");
        var after = file.Current;
        Assert.Equal(0.6, after.ItemDropMultiplier);
        Assert.Equal(0.8, after.GemExperienceMultiplier);
        Assert.Equal(0.15, before.ItemDropMultiplier); // Existing operations keep their complete snapshot.
    }

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}
