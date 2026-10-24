namespace GameServer.Data.Terrain.Generation;

/// <summary>The level generator's random numbers, as client 3.29.3b draws them
/// (Agents/docs/re/011-level-generation.md §2).
/// <list type="bullet">
/// <item>A thread-local <c>std::mt19937</c> seeded with the area seed when generation starts (<c>0x14204d820</c>);
/// <see cref="NextRaw"/> is one draw (<c>0x140a0f990</c>).</item>
/// <item><see cref="Next(uint)"/> is <c>0x141ff9720</c>: MSVC's <c>_Rng_from_urng</c> (<c>0x141f5d130</c>) over [0, n), no draw
/// for n ≤ 1, always 0 while the area disables randomness (WorldAreas byte +0x14e).</item>
/// <item><see cref="PickIndex"/> / <see cref="PickOrientation"/> are <c>0x142046a70</c>: one MT draw seeds the
/// MSVC <c>rand()</c> LCG (TLS +0x170, <c>0x140e3b310</c>), which then picks by rejection.</item>
/// </list></summary>
public sealed class LevelRandom
{
    const int N = 624, M = 397;
    readonly uint[] _mt = new uint[N];
    int _index;

    public LevelRandom(uint seed, bool disabled = false)
    {
        Seed = seed;
        Disabled = disabled;
        _mt[0] = seed;
        for (int i = 1; i < N; i++) _mt[i] = 0x6c078965u * (_mt[i - 1] ^ (_mt[i - 1] >> 30)) + (uint)i;
        _index = N;
    }

    public uint Seed { get; }

    /// <summary>The area turns random draws off (<see cref="Next(uint)"/> returns 0); raw draws still advance.</summary>
    public bool Disabled { get; }

    /// <summary>Raw draws so far (for comparing with a client trace).</summary>
    public long Draws { get; private set; }

    /// <summary>When set, every <see cref="Next(uint)"/> ('n'), direct <see cref="Uniform"/> ('u') and LCG bit draw ('l')
    /// as (kind, argument, result), the way the generation trace records them.</summary>
    public List<(char Kind, uint Arg, uint Result)>? Log { get; set; }

    int _depth;

    /// <summary>One <c>mt19937</c> output.</summary>
    public uint NextRaw()
    {
        if (_index >= N) Twist();
        uint y = _mt[_index++];
        y ^= y >> 11;
        y ^= (y << 7) & 0x9d2c5680u;
        y ^= (y << 15) & 0xefc60000u;
        y ^= y >> 18;
        Draws++;
        return y;
    }

    void Twist()
    {
        for (int i = 0; i < N; i++)
        {
            uint y = (_mt[i] & 0x80000000u) | (_mt[(i + 1) % N] & 0x7fffffffu);
            _mt[i] = _mt[(i + M) % N] ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908b0dfu : 0);
        }
        _index = 0;
    }

    /// <summary><c>0x141ff9720</c>: a number in [0, n) (n = 0 counts as 1).</summary>
    public uint Next(uint n)
    {
        uint r = 0;
        if (!Disabled)
        {
            _depth++;
            r = Uniform(n == 0 ? 1 : n);
            _depth--;
        }
        Log?.Add(('n', n, r));
        return r;
    }

    public int Next(int n) => (int)Next((uint)n);

    /// <summary><c>0x141f5d130</c>, MSVC <c>_Rng_from_urng&lt;uint, mt19937&gt;(n)</c>: one 32-bit draw, rejected while it
    /// falls in the last, incomplete bucket. Shuffles call it directly (no <see cref="Disabled"/> check).</summary>
    public uint Uniform(uint n)
    {
        uint result = 0;
        if (n > 1)
            while (true)
            {
                uint ret = NextRaw();
                const uint mask = uint.MaxValue;
                if (mask / n <= ret / n && mask % n != n - 1) continue;
                result = ret % n;
                break;
            }
        if (_depth == 0) Log?.Add(('u', n, result));
        return result;
    }

    /// <summary>The generator's state, to put back with <see cref="Restore"/> (the doodad stage copies TLS +400 aside
    /// around its random doodads, <c>0x1420425d0</c>).</summary>
    public (uint[] Mt, int Index) Save() => ((uint[])_mt.Clone(), _index);

    public void Restore((uint[] Mt, int Index) state)
    {
        state.Mt.CopyTo(_mt, 0);
        _index = state.Index;
    }

    /// <summary>A percent roll as the generator writes it: chance ≤ 0 never, ≥ 100 always (no draw), else
    /// <c>Next(100) &lt; chance</c> (e.g. <c>0x142054a50</c>).</summary>
    public bool Chance(int percent, uint outOf = 100)
    {
        if (percent <= 0) return false;
        if ((uint)percent >= outOf) return true;
        return Next(outOf) < (uint)percent;
    }

    /// <summary>The in-place forward shuffle (<c>0x141f4fb80</c>, <c>0x14212d7f0</c>): element i swaps with
    /// i + <see cref="Uniform"/>(remaining).</summary>
    public void Shuffle<T>(IList<T> list, int count = int.MaxValue)
    {
        int remaining = list.Count;
        for (int i = 0; remaining != 0 && count-- > 0; i++, remaining--)
        {
            int j = i + (int)Uniform((uint)remaining);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary><c>0x142046a70</c>'s pick: one <see cref="Next(uint)"/>(0xffffffff) seeds the LCG, which draws
    /// bit-length(count − 1) bits until the value is below count. No LCG draws for count &lt; 2 (the MT draw happens).</summary>
    public int PickIndex(int count) => LcgPick(Next(uint.MaxValue), count);

    /// <summary><c>0x142046a70</c> with an orientation: the same MT draw seeds the LCG for the index and, again from
    /// the start, for the orientation (a value in [0, 839], then <see cref="OrientationFromMask"/>).</summary>
    public (int Index, int Orientation) PickIndexAndOrientation(int count, byte mask) => PickIndexAndOrientation(count, _ => mask);

    /// <summary>As above, with the orientation mask of the picked item.</summary>
    public (int Index, int Orientation) PickIndexAndOrientation(int count, Func<int, byte> maskOf)
    {
        uint seed = Next(uint.MaxValue);
        int index = LcgPick(seed, count);
        byte mask = maskOf(index);
        uint state = seed;
        uint r;
        do r = LcgBits(ref state, 10); while (r > 0x347);
        return (index, OrientationFromMask(mask, r));
    }

    int LcgPick(uint seed, int count)
    {
        if (count < 2) return 0;
        uint state = seed;
        int bits = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)(count - 1));
        uint r;
        do r = LcgBits(ref state, bits); while (r >= (uint)count);
        return (int)r;
    }

    /// <summary><c>0x140e3b310</c>: <paramref name="bits"/> bits from MSVC <c>rand()</c>, at most 15 per step.</summary>
    uint LcgBits(ref uint state, int bits)
    {
        int asked = bits;
        uint result = 0;
        while (bits > 0)
        {
            int take = Math.Min(bits, 15);
            state = state * 0x41c64e6du + 0x3039u;
            result = (result << take) + ((state >> 16) & 0x7fffu & ((1u << take) - 1));
            bits -= take;
        }
        Log?.Add(('l', (uint)asked, result));
        return result;
    }

    /// <summary><c>0x14299da50</c>: the r-th (mod count) allowed orientation of a mask (bit k = code k); a mask of 0 or
    /// 0xff allows all eight (r &amp; 7).</summary>
    public static int OrientationFromMask(byte mask, uint r)
    {
        if (mask is 0 or 0xff) return (int)(r & 7);
        Span<int> allowed = stackalloc int[8];
        int n = 0;
        for (int k = 0; k < 8; k++) if ((mask >> k & 1) != 0) allowed[n++] = k;
        return allowed[(int)(r % (uint)n)];
    }

    /// <summary>A topology's orientation list (Topologies +0x1c) as the mask <c>0x142046340</c> builds: 8 = all.</summary>
    public static byte OrientationMask(IEnumerable<int> orientations)
    {
        uint mask = 0;
        foreach (int o in orientations) mask = o == 8 ? 0xff : (mask & 0xff) | (1u << (o & 31));
        return (byte)mask;
    }
}
