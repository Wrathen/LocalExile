using GameServer.Core.Projectiles;

namespace GameServer.Core.Tests;

// A projectile's 150-cell cap applies to one movement instance. Fork, chain and split start a new instance at the
// continuation point, so a projectile can travel farther than 15 m in total; Distance keeps accumulating for Far Shot.
public class ProjectileContinuationRangeTests
{
    sealed class Arena : IProjectileWorld
    {
        public readonly List<ProjectileActor> Targets = [];
        public readonly List<ProjectileDamage> Hits = [];
        public IReadOnlyList<ProjectileActor> Actors(long from, long until) => Targets;
        public TerrainContact? TraceTerrain(ProjectileVector from, ProjectileVector to) => null;
        public ProjectileHitResult Hit(ProjectileDamage damage) { Hits.Add(damage); return ProjectileHitResult.Hit; }
        public void Publish(ProjectileEvent value) { }
        public void Target(uint id, double x, double y = 0) => Targets.Add(new(new(id), 2, _ => new(x, y), 0));
    }

    static ProjectileUse Use(ProjectileProfile profile) =>
        new(profile, new(999), new(999), 1, new(0, 0), new(200, 0));

    [Fact]
    public void A_chain_jump_gets_a_fresh_range_budget()
    {
        var world = new Arena(); world.Target(1, 30); world.Target(2, 80);
        var runtime = new ProjectileRuntime(world);
        runtime.Release(Use(new("chain") { Speed = 100, Range = 60, Radius = 1, Chain = 1, ChainRange = 60 }));

        runtime.AdvanceTo(240000);

        Assert.Contains(world.Hits, h => h.Target.Id == 1);
        Assert.Contains(world.Hits, h => h.Target.Id == 2);
    }

    [Fact]
    public void A_fork_child_gets_a_fresh_range_budget()
    {
        var world = new Arena(); world.Target(1, 30);
        // The upper fork child leaves the first target at +30 degrees and reaches this one after 60 cells.
        world.Target(2, 30 + 60 * Math.Cos(Math.PI / 6), 60 * Math.Sin(Math.PI / 6));
        var runtime = new ProjectileRuntime(world);
        runtime.Release(Use(new("fork") { Speed = 100, Range = 60, Radius = 1, Fork = 1 }));

        runtime.AdvanceTo(240000);

        Assert.Contains(world.Hits, h => h.Target.Id == 1);
        Assert.Contains(world.Hits, h => h.Target.Id == 2);
    }
}
