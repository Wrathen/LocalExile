using GameServer.Core.World;

/// <summary>
/// Where chasing monsters stand around the player. Each monster used to aim at the cell its stop range away on the line
/// from the player toward itself, so a pack coming from one side aimed at nearly one cell: the bodies queued behind each
/// other in a wedge and only the front ones reached the player. Here the nearest monsters choose first and each takes the
/// free cell on the innermost ring around the player closest to its own bearing (or to its previous slot, so slots stay
/// put while the player stands); a full ring sends the rest to the next ring out, where they wait for a gap.
/// </summary>
static class MonsterSurround
{
    /// <summary>A monster asking for a cell: its id, its cell, its Positioned size (object_size − 1), its last slot, its
    /// attack's reach (MonsterVarieties MaximumAttackDistance) and whether that attack is ranged, in which case it stands
    /// off at its reach instead of coming to the contact ring.</summary>
    public readonly record struct Asker(uint Id, int X, int Y, int Size, (int X, int Y)? Last,
        double Reach = double.PositiveInfinity, bool Ranged = false);

    // Assign's refused cells, reused by each thread's next assignment (it is not reentrant).
    [ThreadStatic] static HashSet<(int X, int Y, int Size)>? t_refused;

    /// <summary>How many rings out a monster may be placed before it simply aims at the player.</summary>
    const int Rings = 6;
    // Radii come from catalog body sizes/reaches. Reuse the trigonometry across chasers and decisions.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<double, (double Cos, double Sin)[]> RingSamples = new();

    /// <summary>
    /// The innermost ring's nominal radius. Its rounded cells must also clear the player's square footprint:
    /// ClientPathfinder.ObjectBlocks uses Chebyshev distance, so a circular ring alone cannot guarantee clearance.
    /// </summary>
    public static int ContactRadius(int size, int playerSize = LivingCollision.PlayerSize) => size + playerSize + 2;

    /// <param name="askers">The monsters to place, any order (the nearest are placed first).</param>
    /// <param name="occupied">Bodies that are not asking (they stand, swing or are not chasing): no slot overlaps them.</param>
    /// <param name="walkable">Whether a body of the given size may stand on a cell.</param>
    public static Dictionary<uint, (int X, int Y)> Assign(IEnumerable<Asker> askers, (int X, int Y) player,
        IEnumerable<(int X, int Y, int Size)> occupied, Func<int, int, int, bool> walkable)
    {
        // Inflate reservations by one: threshold size+1 then preserves the existing size+size+2 separation.
        var taken = new BlockerSet(occupied.Select(p => new ClientPathfinder.Blocker(p.X, p.Y, p.Size + 1)));
        var ordered = askers.ToArray();
        Array.Sort(ordered, (a, b) =>
        {
            int distance = Distance2(a.X - player.X, a.Y - player.Y).CompareTo(Distance2(b.X - player.X, b.Y - player.Y));
            return distance != 0 ? distance : a.Id.CompareTo(b.Id);
        });
        var result = new Dictionary<uint, (int X, int Y)>(ordered.Length);
        // Cells found not free for a body size stay so for this whole assignment: the ground and the player do not
        // change, and reservations are only added. The chasers' searches overlap, so each is checked once.
        var refused = t_refused ??= [];
        refused.Clear();
        foreach (var a in ordered)
        {
            // Keep the last slot while it is still free and still on the innermost ring around the player; a waiting slot
            // farther out is chosen again every time, so it moves in when a gap opens.
            (int X, int Y)? slot = a.Last is { } last && OnRing(last, player, a)
                && Free(last, player, a.Size, taken, walkable, refused) ? last : null;
            if (slot is null)
            {
                // A last slot still on its ring but taken: look beside it. Once the player has moved, from the monster.
                double bearing = a.Last is { } l && (l.X, l.Y) != player && OnRing(l, player, a)
                    ? Math.Atan2(l.Y - player.Y, l.X - player.X)
                    : Math.Atan2(a.Y - player.Y, a.X - player.X);
                slot = Find(player, a, bearing, taken, walkable, refused);
            }
            if (slot is not { } s) continue;
            result[a.Id] = s;
            taken.Add(new(s.X, s.Y, a.Size + 1));
        }
        return result;
    }

    static bool Free((int X, int Y) cell, (int X, int Y) player, int size, BlockerSet taken,
        Func<int, int, int, bool> walkable, HashSet<(int X, int Y, int Size)> refused)
    {
        if (refused.Contains((cell.X, cell.Y, size))) return false;
        bool free = Math.Max(Math.Abs(cell.X - player.X), Math.Abs(cell.Y - player.Y)) >= size + LivingCollision.PlayerSize + 1
            && !taken.OverlapsFootprint(cell.X, cell.Y, size + 1) && walkable(cell.X, cell.Y, size);
        if (!free) refused.Add((cell.X, cell.Y, size));
        return free;
    }

    static double InnerRadius(Asker a) => Math.Max(a.Size + LivingCollision.PlayerSize + 1,
        Math.Min(ContactRadius(a.Size), a.Reach));

    /// <summary>A ranged chaser whose reach lies beyond the contact ring stands off: its first ring is just inside its
    /// reach (the farthest cell its attack covers: the Sand Spitter's 30), and a full ring sends it inward rather than
    /// out of range. Melee chasers (and a ranged one whose reach is the contact ring anyway) ring outward as before.</summary>
    static bool StandsOff(Asker a) => a.Ranged && !double.IsInfinity(a.Reach) && a.Reach - 1 > InnerRadius(a);

    static double RingRadius(Asker a, int ring)
    {
        int spacing = 2 * a.Size + 2;
        return StandsOff(a) ? Math.Max(InnerRadius(a), a.Reach - 1 - ring * spacing) : InnerRadius(a) + ring * spacing;
    }

    static (int X, int Y)? Find((int X, int Y) player, Asker a, double bearing, BlockerSet taken,
        Func<int, int, int, bool> walkable, HashSet<(int X, int Y, int Size)> refused)
    {
        int spacing = 2 * a.Size + 2;
        var (bearingSin, bearingCos) = Math.SinCos(bearing);
        for (int ring = 0; ring < Rings; ring++)
        {
            double radius = RingRadius(a, ring);
            // A stand-off's inward rings end at the contact ring; the same ring again finds nothing new.
            if (ring > 0 && radius == RingRadius(a, ring - 1)) break;
            // One cell along the ring per step, both sides of the bearing alternately, nearest first: the first free cell
            // packs the bodies as tightly as they may stand.
            var samples = RingSamples.GetOrAdd(radius, static r =>
            {
                double step = 1.0 / r;
                var result = new (double Cos, double Sin)[(int)Math.Ceiling(Math.PI / step) + 1];
                for (int i = 0; i < result.Length; i++)
                {
                    var (sin, cos) = Math.SinCos(i * step);
                    result[i] = (cos, sin);
                }
                return result;
            });
            for (int k = 0; k < samples.Length; k++)
                for (int side = 0; side < (k == 0 ? 1 : 2); side++)
                {
                    double sin = side == 0 ? samples[k].Sin : -samples[k].Sin, cos = samples[k].Cos;
                    var cell = ((int)Math.Round(player.X + radius * (bearingCos * cos - bearingSin * sin)),
                        (int)Math.Round(player.Y + radius * (bearingSin * cos + bearingCos * sin)));
                    if (ring == 0 && Distance2(cell.Item1 - player.X, cell.Item2 - player.Y) > a.Reach * a.Reach) continue;
                    if (Free(cell, player, a.Size, taken, walkable, refused)) return cell;
                }
        }
        return null;
    }

    static bool OnRing((int X, int Y) cell, (int X, int Y) player, Asker a)
    {
        double d = Math.Sqrt(Distance2(cell.X - player.X, cell.Y - player.Y)), r0 = RingRadius(a, 0);
        if (d > a.Reach) return false;
        int spacing = 2 * a.Size + 2;
        // Rings count outward from the contact ring, inward from a stand-off's first ring.
        double offset = StandsOff(a) ? r0 - d : d - r0;
        if (offset < -1) return false;
        double ring = offset / spacing;
        return Math.Round(ring) <= 0 && Math.Abs(ring - Math.Round(ring)) * spacing <= 1.5;
    }

    static long Distance2(int dx, int dy) => (long)dx * dx + (long)dy * dy;
}
