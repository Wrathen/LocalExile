/// <summary>
/// A Strike skill's extra targets (Agents/docs/protocol/11-skills.md §10d): its spirit strikes
/// (<c>melee_attack_number_of_spirit_strikes</c>: Ancestral Reach) and its strike at the previous location it was used
/// (<c>melee_strike_skill_strike_previous_location</c>: Ancestral Echo). The client picks both itself when its melee
/// action starts (<c>0x141c9c070</c> → <c>0x141c9ad30</c>) and draws them; the server mirrors the choice, so the damage
/// lands on the monsters the client strikes.
/// </summary>
static class StrikeTargets
{
    /// <summary>The client caps the candidates at 20 (<c>0x141c9ad30</c>'s list, filled by <c>0x141ca3f30</c>).</summary>
    public const int MaxCandidates = 20;

    /// <summary>The client's <c>virtual_number_of_spirit_strikes</c> (<c>0x1428b12d0</c>): none with
    /// <c>no_spirit_strikes</c>, for a non-melee attack, a non-Strike or a Vaal skill, else
    /// <c>melee_attack_number_of_spirit_strikes</c>. Shortcut: its unarmed term
    /// (<c>unarmed_attack_number_of_spirit_strikes</c>) is left out, no item the server rolls has it.</summary>
    public static int SpiritStrikes(Func<string, int> stat, bool strike, bool vaal, bool melee) =>
        stat("no_spirit_strikes") != 0 || !melee || !strike || vaal ? 0 : Math.Max(0, stat("melee_attack_number_of_spirit_strikes"));

    /// <summary>The scan radius in cells: (100 + <c>extra_target_targeting_distance_+%</c>) / 100 × 30, truncated
    /// (<c>0x141c9ad30</c>, f32 arithmetic): 30, or 45 with Ancestral Reach's 50%.</summary>
    public static int Range(int extraDistancePct) => (int)((extraDistancePct + 100) / 100f * 30f);

    public readonly record struct Candidate(uint Id, (int X, int Y) At, int Size);

    /// <summary>
    /// The spirit strike targets, as the client picks them (<c>0x141c9ad30</c>, <c>0x141ca6010</c>, <c>0x141ca3f30</c>):
    /// the living hostile candidates within the range plus their own size of the player's cell, more than 9.95 cells
    /// from it (squared distance over 99), not already the action's target; sorted by object id (<c>0x141a300b0</c>),
    /// shuffled front to back with the client's TinyMT32 seeded by the action's sequence and the player's object id
    /// (<c>0x1413cbe50</c>, a draw per place with <c>0x1404d03d0</c>), and the first <paramref name="count"/> taken.
    /// Not mirrored: the client's line-of-sight test (<c>0x141d87160</c>), and which 20 it keeps when more qualify
    /// (its grid order; the server keeps the 20 lowest ids).
    /// </summary>
    public static List<uint> Pick(IEnumerable<Candidate> living, (int X, int Y) player, int range, int count,
        ushort sequence, uint playerId, IReadOnlySet<uint> targeted)
    {
        if (count <= 0 || range <= 0) return [];
        var found = living.Where(c => !targeted.Contains(c.Id) && Within(c, player, range))
            .Select(c => c.Id).Order().Take(MaxCandidates).ToList();
        var random = new ClientRandom(sequence, playerId);
        for (int i = 0; i < found.Count; i++)
        {
            int j = i + (int)random.Pick((uint)(found.Count - i));
            (found[i], found[j]) = (found[j], found[i]);
        }
        return found.Count > count ? found.GetRange(0, count) : found;
    }

    static bool Within(Candidate c, (int X, int Y) player, int range)
    {
        long dx = c.At.X - player.X, dy = c.At.Y - player.Y, reach = range + c.Size, d2 = dx * dx + dy * dy;
        return d2 <= reach * reach && d2 > 99;
    }

    /// <summary>The client's <c>melee_strike_skill_number_of_previous_locations_to_strike</c> (<c>0x142896310</c>):
    /// a melee main hand (its <c>main_hand_weapon_type</c> table), a Strike and the stat.</summary>
    public static bool StrikesPreviousLocation(Func<string, int> stat, bool strike, bool meleeWeapon) =>
        meleeWeapon && strike && stat("melee_strike_skill_strike_previous_location") != 0;

    /// <summary>Each Strike skill's last use in this area and life: where the player stood and where its hit landed. The
    /// client keeps them in the <c>skill_location_memory</c> buff (BuffDefinitions 1451, CancelOnDeath), one per skill.
    /// Guarded by the Monsters lock.</summary>
    public sealed class Memory
    {
        readonly Dictionary<ushort, ((int X, int Y) Player, (int X, int Y) Centre)> last = [];
        readonly Dictionary<ushort, ushort> buffs = [];

        /// <summary>Stores this use and returns the previous use's centre to strike again, unless the player stood
        /// within 5 cells of where it stands now (the client skips a location whose squared distance is under 25,
        /// <c>0x141ca6eb0</c>).</summary>
        public (int X, int Y)? Use(ushort skill, (int X, int Y) player, (int X, int Y) centre)
        {
            bool had = last.TryGetValue(skill, out var previous);
            last[skill] = (player, centre);
            if (!had) return null;
            long dx = previous.Player.X - player.X, dy = previous.Player.Y - player.Y;
            return dx * dx + dy * dy < 25 ? null : previous.Centre;
        }

        /// <summary>The skill's <c>skill_location_memory</c> buff instance, or a new one (true) to send.</summary>
        public (ushort Buff, bool Added) Buff(ushort skill, Func<ushort> next)
        {
            if (buffs.TryGetValue(skill, out ushort buff)) return (buff, false);
            return (buffs[skill] = next(), true);
        }

        /// <summary>Death ends the buffs (CancelOnDeath) and their locations.</summary>
        public void Clear() { last.Clear(); buffs.Clear(); }
    }

    /// <summary>The client's small generator seeded from a key (TinyMT32's <c>init_by_array</c>, <c>0x1413cbe50</c>,
    /// checked against its folded first step), with <c>0x140484fa0</c>'s uniform pick. The step is the client's, as in
    /// GameServer.Data's LabyrinthPass.TinyMt (seeded from one word there).</summary>
    struct ClientRandom
    {
        const uint Mat1 = 0x8f7011ee, Mat2 = 0xfc78ff1f, TMat = 0x3793fdff;
        uint _s0, _s1, _s2, _s3;

        public ClientRandom(params ReadOnlySpan<uint> key)
        {
            const int size = 4, mid = 1, lag = 1;
            Span<uint> st = [0, Mat1, Mat2, TMat];
            int count = Math.Max(key.Length + 1, 8);
            uint r = F1(st[0] ^ st[mid] ^ st[size - 1]);
            st[mid] += r;
            r += (uint)key.Length;
            st[mid + lag] += r;
            st[0] = r;
            count--;
            int i = 1, j = 0;
            for (; j < count; j++)
            {
                r = F1(st[i] ^ st[(i + mid) % size] ^ st[(i + size - 1) % size]);
                st[(i + mid) % size] += r;
                r += (j < key.Length ? key[j] : 0) + (uint)i;
                st[(i + mid + lag) % size] += r;
                st[i] = r;
                i = (i + 1) % size;
            }
            for (j = 0; j < size; j++)
            {
                r = F2(st[i] + st[(i + mid) % size] + st[(i + size - 1) % size]);
                st[(i + mid) % size] ^= r;
                r -= (uint)i;
                st[(i + mid + lag) % size] ^= r;
                st[i] = r;
                i = (i + 1) % size;
            }
            if ((st[0] & 0x7fffffff) == 0 && st[1] == 0 && st[2] == 0 && st[3] == 0) st = [(uint)'T', (uint)'I', (uint)'N', (uint)'Y'];
            (_s0, _s1, _s2, _s3) = (st[0], st[1], st[2], st[3]);
            for (i = 0; i < 8; i++) Next();
        }

        static uint F1(uint x) => (x ^ (x >> 27)) * 1664525u;
        static uint F2(uint x) => (x ^ (x >> 27)) * 1566083941u;

        uint Next()
        {
            uint x = (_s0 & 0x7fffffff) ^ _s1 ^ _s2;
            uint y = _s3 ^ (_s3 << 1);
            x = (x >> 1) ^ x ^ y;
            uint s1 = _s1;
            _s0 = s1;
            _s1 = (0u - (x & 1) & Mat1) ^ _s2;
            _s2 = (0u - (x & 1) & Mat2) ^ (x << 10) ^ y;
            _s3 = x;
            uint t = (_s2 >> 8) + s1;
            return (0u - (t & 1) & TMat) ^ t ^ x;
        }

        public uint Pick(uint n)
        {
            if (n <= 1) return 0;
            uint v;
            do v = Next();
            while (uint.MaxValue / n <= v / n && uint.MaxValue % n != n - 1);
            return v % n;
        }
    }
}
