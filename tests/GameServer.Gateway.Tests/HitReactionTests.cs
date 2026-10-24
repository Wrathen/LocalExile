using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public class HitReactionTests
{
    static SpawnedMonster Monster(uint maxLife = 100, params int[] flinches) =>
        new("Rhoa@1", new WalkEstimator(0, 0, 30, null, 2000, 2000), aggroRange: 60, stopRange: 3, maxLife: maxLife)
        {
            HitAnimations = flinches,
        };

    [Fact]
    public void A_hit_below_a_tenth_of_max_life_does_not_flinch()
    {
        var m = Monster(100, 7);
        Assert.False(HitReaction.TryFlinch(m, 9, now: 10));
        Assert.True(HitReaction.TryFlinch(m, 10, now: 10));
    }

    [Fact]
    public void The_threshold_follows_the_max_life()
    {
        var m = Monster(1000, 7);
        Assert.False(HitReaction.TryFlinch(m, 99, now: 10));
        Assert.True(HitReaction.TryFlinch(m, 100, now: 10));
    }

    [Fact]
    public void A_monster_flinches_at_most_every_0_4_s()
    {
        var m = Monster(100, 7);
        Assert.True(HitReaction.TryFlinch(m, 50, now: 10));
        Assert.False(HitReaction.TryFlinch(m, 50, now: 10.3));
        Assert.True(HitReaction.TryFlinch(m, 50, now: 10.4));
    }

    [Fact]
    public void A_dead_monster_does_not_flinch()
    {
        var m = Monster(100, 7);
        m.Life = 0;
        Assert.False(HitReaction.TryFlinch(m, 50, now: 0));
    }

    [Fact]
    public void A_variety_without_a_TakeHit_clip_does_not_flinch()
    {
        Assert.False(HitReaction.TryFlinch(Monster(100), 50, now: 0));
    }
}
