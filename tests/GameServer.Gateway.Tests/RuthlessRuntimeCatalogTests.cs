using System.Reflection;
using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

public sealed class RuthlessRuntimeCatalogTests : IDisposable
{
    readonly List<string> characters = [];

    public RuthlessRuntimeCatalogTests() => TestSaves.Ensure();

    ClientState Connection(bool ruthless)
    {
        string name = "ModeRuntime" + Guid.NewGuid().ToString("N");
        characters.Add(name);
        var conn = new ClientState(new(0, "ModeRuntime", 128, 128, 32, 32, null, []), 0, 1)
        {
            Character = name, ClassRow = 1, LeagueForm = ruthless ? "ruthless" : null, PlayerLife = 285, PlayerMana = 170,
            Pools = new(40, 285, 170, 100, 0, Strength: 100, Dexterity: 100, Intelligence: 100),
        };
        // Item activation reads the requirement attributes, not the pools above (socketed gems count).
        foreach (string attribute in new[] { "strength", "dexterity", "intelligence" })
            conn.PlayerStats[Program.Stats.Key($"virtual_{attribute}_for_requirements")!.Value] = 100;
        return conn;
    }

    static MethodInfo Method(string name) => typeof(Program).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    static (Dictionary<uint, int> Stats, AttributeModifiers Attributes) Sources(ClientState conn) =>
        ((Dictionary<uint, int> Stats, AttributeModifiers Attributes))
            Method("CharacterStatSources").Invoke(null, [conn, null, null])!;

    [Theory]
    [InlineData(false, "Grace")]
    public void Runtime_actor_skills_and_support_resolution_use_the_selected_mode(bool ruthless, string effectName)
    {
        var conn = Connection(ruthless);
        var catalog = Program.ItemsFor(conn);
        var gem = ItemCatalog.Create(catalog.Find("Grace")!);
        var host = ItemCatalog.Create(catalog.Find("Driftwood Wand")!) with { Sockets = [new(SocketColour.Green, gem)] };
        PlayerInventories.For(conn.Character!, catalog).Add(PlayerInventories.Weapon1, 0, 0, host);

        var gemMethod = typeof(Program).GetMethod("GemSkillEffects", BindingFlags.Static | BindingFlags.NonPublic,
            binder: null, types: [typeof(ClientState)], modifiers: null)!;
        var active = Assert.Single((IEnumerable<(ushort Id, int Effect, string Name, int Level, int Quality, uint Gem)>)
            gemMethod.Invoke(null, [conn])!);
        Assert.Equal(effectName, Program.SkillsFor(conn).EffectId(active.Effect));

        // SupportedSkill assigns its own ids. It must use the same mode effects as the Actor's gem list.
        var supported = Method("SupportedSkill").Invoke(null, [conn, active.Id])!;
        Assert.Equal(active.Effect, (int)supported.GetType().GetProperty("Effect")!.GetValue(supported)!);

        var actor = (IReadOnlyList<GrantedSkill>)Method("PlayerSkills").Invoke(null, [conn])!;
        Assert.NotNull(Assert.Single(actor, skill => skill.Id == active.Id));
    }

    public void Dispose()
    {
        foreach (string character in characters) PlayerInventories.Forget(character);
    }
}
