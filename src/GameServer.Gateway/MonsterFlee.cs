using GameServer.Core.World;

/// <summary>
/// Fleeing (wiki Fleeing): a hit with a chance to cause monsters to flee makes the monster run to a random point away
/// from the player; it uses no offensive skill until it gets there or its path ends short, with no fixed duration.
/// Magic monsters avoid it half of the time and rare and unique ones always, through their rarity's own mods
/// (MonsterMagic7, MonsterRare7, MonsterUnique7: monster_chance_to_not_flee_% 50, 100, 100). The AI walks it
/// (Program.Monsters RunMonsterAi).
/// </summary>
static class MonsterFlee
{
    /// <summary>How far it runs, in cells (10 to the metre). The real game picks the point on its server; this distance
    /// is not verified.</summary>
    internal const int Distance = 50;

    /// <summary>A hit's chance as the client's calculators sum it: an attack's main_hand_hit_causes_monster_flee_%
    /// (0x1427906b0: the global stat plus the main-hand weapon's local one) or a spell's global stat, plus the skill's own
    /// global stat (Chance to Flee Support).</summary>
    internal static int Chance(bool spell, Func<string, int> player, Func<string, int> skill) =>
        player(spell ? "global_hit_causes_monster_flee_%" : "main_hand_hit_causes_monster_flee_%")
        + skill("global_hit_causes_monster_flee_%");

    /// <summary>One roll per hit at the chance, then the monster's chance not to flee.</summary>
    internal static bool Rolls(int chance, int notFlee, Random random) =>
        RandomRolls.Chance(chance, 100, random) && !RandomRolls.Chance(notFlee, 100, random);

    /// <summary>A floor cell <see cref="Distance"/> from the monster within 60° of straight away from the player, eight
    /// tries, then eight at half the distance; null when none (it gives up at once).</summary>
    internal static (int X, int Y)? Point((int X, int Y) monster, (int X, int Y) player, Func<int, int, bool> floor, Random random)
    {
        double away = monster == player ? random.NextDouble() * 2 * Math.PI : Math.Atan2(monster.Y - player.Y, monster.X - player.X);
        foreach (int distance in new[] { Distance, Distance / 2 })
            for (int i = 0; i < 8; i++)
            {
                double angle = away + (random.NextDouble() * 2 - 1) * Math.PI / 3;
                (int X, int Y) to = ((int)Math.Round(monster.X + distance * Math.Cos(angle)),
                    (int)Math.Round(monster.Y + distance * Math.Sin(angle)));
                if (floor(to.X, to.Y)) return to;
            }
        return null;
    }
}
