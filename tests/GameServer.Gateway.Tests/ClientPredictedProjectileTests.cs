using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

// Live 2026-10-29 (Frida): the client spawns the projectiles of its own gem skill use itself, every one of a volley, so
// a server object for them showed a second fireball. The server keeps simulating them but sends no object.
public partial class SpellNoDamageTests
{
    // S→C 0x2b9 with this object type: u16 id, entity header (u32, u32, u16), then the u32 type hash (big-endian).
    static bool CreatesObject(byte[] wire, string metadata)
    {
        uint type = InstanceEntities.ObjectTypeHash(metadata);
        for (int i = 0; i + 16 <= wire.Length; i++)
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(wire.AsSpan(i)) == InstanceEntities.CreateObjectId
                && System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(wire.AsSpan(i + 12)) == type) return true;
        return false;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_client_predicted_projectile_gets_no_server_object(bool predicted)
    {
        await using var runtime = new ProjectileRuntimeFixture();
        var offense = new ProjectileOffense(DamagePacket.Of(DamageType.Physical, 10, 10), true, int.MaxValue, 0, 150, "fixture")
            { ClientPredicted = predicted };
        runtime.Release(runtime.Shot(offense: offense));
        runtime.Advance(0.2);
        Assert.Equal(!predicted, CreatesObject(await runtime.Read(), "Metadata/Projectiles/DefaultArrow"));
    }

    // A spell's projectiles are the client's own; an attack gem's are not (Frida 2026-10-31: Burning Arrow's action created
    // no projectile, and without a server object none was seen). Spark's action fires through the same native
    // ProjectileAction method as Fireball's (vtable slot 0x618: 0x141bb13a0, Spark's 0x141b363a0 forwards to it), so the
    // client draws Spark's sparks itself too.
    [Theory]
    [InlineData("Fireball", "Driftwood Wand", true)]
    [InlineData("Spark", "Driftwood Wand", true)]
    [InlineData("BurningArrow", "Crude Bow", false)]
    public async Task Spell_gem_projectiles_are_client_predicted_and_attack_gem_projectiles_are_not(string skill, string weapon, bool predicted)
    {
        using var f = new Fixture(skill, weapon);
        await using var runtime = new ProjectileRuntimeFixture(f.Conn);
        var gems = (IEnumerable<(ushort Id, int Effect, string Name, int Level, int Quality, uint Gem)>)
            Method("GemSkillEffects").Invoke(null, [f.Conn])!;
        // The use's critical roll is captured at paid admission (CastGemSkill) and travels with the projectile.
        var roll = Method("CaptureSkillCriticalRoll").Invoke(null, [f.Conn, f.Skill])!;
        var use = (GameServer.Core.Projectiles.ProjectileUse)Method("CaptureGemProjectile").Invoke(null,
            [f.Conn, runtime.Session, f.Skill, gems.Single(g => g.Id == f.Skill), (100, 32), roll])!;
        Assert.Equal(predicted, Assert.IsType<ProjectileOffense>(use.Payload).ClientPredicted);
    }

    // The client's Spark action gives each of its own sparks skill_effect_duration (0x168) / 1000 seconds of life
    // (0x141b35b7a) and removes it when that runs out (0x141b36170): without the virtual stat every spark died on its
    // first tick and none was drawn.
    [Theory]
    [InlineData(0, 2000)]
    [InlineData(50, 3000)]
    public void Spark_skill_carries_its_final_effect_duration(int increased, int expected)
    {
        using var f = new Fixture("Spark", "Driftwood Wand");
        f.Set("skill_effect_duration_+%", increased);
        Assert.Equal(expected, f.Panel()[Program.Stats.Key("skill_effect_duration")!.Value]);
    }
}
