using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.Diagnostics;

partial class Program
{
    // A change of the flask bar and its S→C 0xbf as one step under the connection's flask-bar gate: the crit, hit, kill and
    // every-3-seconds gains and the flask uses run on their own tasks, and the client must see their revisions in order,
    // each with the flask's latest charges. `change` makes the change and returns what to send (removed ids and the changed
    // items), or null for nothing.
    internal static async Task ChangeFlaskBar(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        Func<PlayerInventories, (uint[] Removed, List<InventoryItem> Changed)?> change, Func<List<InventoryItem>, string> log)
    {
        await conn.FlaskBarGate.WaitAsync();
        try
        {
            byte[] packet;
            string message;
            lock (conn.Monsters)
            {
                if (HardcoreProgressBlocked(conn)) return;
                var items = ItemsOf(conn);
                if (change(items) is not { } done || done.Changed.Count == 0) return;
                packet = InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.Flask),
                    items.NextRevision(PlayerInventories.Flask), done.Removed, done.Changed);
                message = log(done.Changed);
                if (conn.ProjectileRuntime is { Active: true } session)
                {
                    session.Packet(packet, message);
                    return;
                }
            }
            await Send(remote, stream, ciphers, packet, message);
        }
        finally
        {
            conn.FlaskBarGate.Release();
        }
    }

    // A use's charges taken from the flask (still in its cell, with enough charges: checked again under the gate, as a
    // gain or another use may have run meanwhile) and sent (S→C 0xbf): the spent flask, or null when there were too few.
    internal static async Task<InventoryItem?> SpendFlask(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        InventoryItem flask, Flasks.Flask charges, uint slot)
    {
        InventoryItem? spent = null;
        await ChangeFlaskBar(remote, stream, ciphers, conn, items =>
        {
            bool enough = false;
            var done = items.Change(PlayerInventories.Flask, flask.Id, i =>
            {
                var after = Flasks.Spend(i, charges);
                enough = after is not null;
                return after ?? i;
            });
            if (!enough || done is null) return null;
            spent = done;
            return ([done.Id], [done]);
        }, changed => $"0x00bf flask {slot}: {changed[0].Item.Charges}/{charges.MaxCharges} charges");
        if (spent is not null) await EndGracePeriod(remote, stream, ciphers, conn, action: true);
        return spent;
    }

    // Flask charge gains (13-items.md §8b, FlaskCharges): each flask in the bar that `chargesFor` gives something gets it
    // times its gain multiplier (the character's increased flask charges gained, the flask's increased charge recovery),
    // whole charges now and the rest kept, up to its maximum with its mods; the changed flasks in one S→C 0xbf, saved with
    // the next save. The character's stats are only worked out when some flask that is not full gains.
    internal static async Task GainFlaskCharges(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        Func<InventoryItem, IReadOnlyDictionary<string, int>, double> chargesFor, string why)
    {
        var gaining = new List<(InventoryItem Flask, IReadOnlyDictionary<string, int> Local, Flasks.Flask Charges, double Gain)>();
        foreach (var flask in ItemsOf(conn).Items(PlayerInventories.Flask))
        {
            if (ItemsFor(conn).ByHash(flask.Item.TypeHash) is not { } type || ItemsFor(conn).FlaskChargesOf(type) is not { } baseCharges) continue;
            var local = ItemModStats.Local(flask.Item, ItemsFor(conn).Mods);
            var charges = FlaskCharges.Of(baseCharges, local);
            double gain = chargesFor(flask, local);
            if (gain > 0 && flask.Item.Charges < charges.MaxCharges) gaining.Add((flask, local, charges, gain));
        }
        if (gaining.Count == 0) return;
        var character = CharacterPlayerStats(conn);
        int Get(string name) => Stats.Key(name) is uint key ? character.GetValueOrDefault(key) : 0;
        await ChangeFlaskBar(remote, stream, ciphers, conn, items =>
        {
            List<InventoryItem> changed = [];
            foreach (var (flask, local, charges, gain) in gaining)
            {
                if (!CanGainFlaskCharges(conn, flask)) continue;
                int whole = conn.FlaskChargeCarry.Take(flask.Id, gain * FlaskCharges.GainMultiplier(local, Get));
                if (whole > 0 && items.ChangeUnsaved(PlayerInventories.Flask, flask.Id, i => Flasks.Gain(i, charges, whole)) is { } done)
                    changed.Add(done);
            }
            return ([.. changed.Select(c => c.Id)], changed);
        }, changed => $"0x00bf flasks: {string.Join(", ", changed.Select(c => $"{c.X}: {c.Item.Charges}"))} charges ({why})");
    }

    internal static bool CanGainFlaskCharges(ClientState conn, InventoryItem flask)
    {
        var rules = LeagueFor(conn).Rules;
        if (rules.UtilityFlaskChargesWhileActive || ItemsFor(conn).ByHash(flask.Item.TypeHash) is not { } type
            || ItemsFor(conn).UtilityFlaskOf(type) is null) return true;
        var active = conn.UtilityFlaskEffects.Of(flask.Id);
        bool running = active is not null && active.Ends > conn.Clock.Elapsed.TotalSeconds;
        bool exception = rules.UtilityFlaskExceptionStat is { } stat && Stats.Key(stat) is uint key
            && CharacterPlayerStats(conn).GetValueOrDefault(key) > 0;
        return LeagueGameplay.CanGainUtilityCharges(!rules.UtilityFlaskChargesWhileActive, running, exception);
    }

    // A kill: 1 / 3.5 / 6 / 11 charges to every flask by the monster's rarity (FlaskCharges.KillCharges), or with
    // Game_FLASK_CHARGES=full every flask full.
    internal static Task GainKillCharges(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, MonsterRarity rarity, string why) =>
        FlaskCharges.FullShortcut ? GainFlaskCharges(remote, stream, ciphers, conn, int.MaxValue, why)
            : GainFlaskCharges(remote, stream, ciphers, conn, (_, _) => FlaskCharges.KillCharges(rarity, ItemsFor(conn).Mods), why);

    // A critical strike the player dealt (a hit that landed): each flask with "#% chance to gain a Flask Charge when you deal
    // a Critical Strike" (local_recharge_on_crit_%) rolls for one charge, and the character's chance and sure charges
    // (FlaskCharges.CritSources) go to the first flask that is not full. One cooldown for all of them
    // (FlaskCharges.CritCooldown), looked at before anything else.
    internal static async Task GainFlaskChargesOnCrit(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        double now = conn.Clock.Elapsed.TotalSeconds;
        if (!conn.FlaskChargeCarry.CritReady(now)) return;
        var bar = ItemsOf(conn).Items(PlayerInventories.Flask).Select(f => (f.Id,
            Chance: ItemModStats.Local(f.Item, ItemsFor(conn).Mods).GetValueOrDefault("local_recharge_on_crit_%"),
            Full: ItemsFor(conn).ByHash(f.Item.TypeHash) is not { } t || ItemsFor(conn).FlaskChargesOf(t) is not { } c
                || f.Item.Charges >= FlaskCharges.Of(c, ItemModStats.Local(f.Item, ItemsFor(conn).Mods)).MaxCharges)).ToList();
        if (bar.All(f => f.Full)) return;
        var character = CharacterPlayerStats(conn);
        int Get(string name) => Stats.Key(name) is uint key ? character.GetValueOrDefault(key) : 0;
        bool precision;
        lock (conn.Monsters) precision = conn.Auras.Values.Any(a => a.EffectId == "Precision");
        var gains = FlaskCharges.OnCrit(bar, FlaskCharges.CritSources(Get, precision), conn.FlaskChargeCarry, now, Random.Shared.Next);
        if (gains.Count > 0)
            await GainFlaskCharges(remote, stream, ciphers, conn, (f, _) => gains.GetValueOrDefault(f.Id), "critical strike");
    }

    // The player was hit (not evaded or blocked): "Gain # Charges when you are Hit by an Enemy" (local_flask_gain_X_charges_when_hit).
    internal static Task GainFlaskChargesWhenHit(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn) =>
        GainFlaskCharges(remote, stream, ciphers, conn, (_, local) => local.GetValueOrDefault("local_flask_gain_X_charges_when_hit"), "hit");

    // "Flasks gain # charges every 3 seconds" (FlaskCharges.PerThreeSeconds: every flask, utility, life, life on low life,
    // mana), every 3 s of game time (a pause holds it) while the connection is open.
    internal static async Task RunFlaskChargeRegen(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        double next = conn.Clock.Elapsed.TotalSeconds + 3;
        try
        {
            while (!conn.Closed && !conn.RageRetired)
            {
                await Task.Delay(250);
                if (conn.Closed || conn.RageRetired) break;
                if (conn.LeavingArea) { next = conn.Clock.Elapsed.TotalSeconds + 3; continue; }
                if (conn.Clock.Elapsed.TotalSeconds < next || conn.PlayerLife == 0) continue;
                next += 3;
                var character = CharacterPlayerStats(conn);
                int Get(string name) => Stats.Key(name) is uint key ? character.GetValueOrDefault(key) : 0;
                bool lowLife = FlaskRecovery.LowLife(conn.PlayerLife, conn.PlayerMaxLife);
                await GainFlaskCharges(remote, stream, ciphers, conn, (f, _) => ItemsFor(conn).ByHash(f.Item.TypeHash) is not { } t ? 0
                    : FlaskCharges.PerThreeSeconds(Get, life: ItemsFor(conn).FlaskOf(t)?.LifePerUse > 0, mana: ItemsFor(conn).FlaskOf(t)?.ManaPerUse > 0,
                        utility: ItemsFor(conn).UtilityFlaskOf(t) is not null, lowLife),
                    "every 3 seconds");
            }
        }
        catch (Exception ex)
        {
            if (!conn.Closed) Console.WriteLine($"[gateway] {remote} flask charges every 3 s: {ServerLog.Describe(ex)}");
        }
    }

    // A flask put on the flask bar in a hostile area has no charges (FlaskCharges.WhenEquipped).
    internal static InventoryItem EmptyIfEquippedInHostileArea(ClientState conn, PlayerInventories items, InventoryItem placed)
    {
        var equipped = FlaskCharges.WhenEquipped(placed.Item, safeArea: InTown(conn) || HideoutCatalog.ByArea(conn.Layout.Hash) is not null);
        return equipped == placed.Item ? placed : items.Change(PlayerInventories.Flask, placed.Id, _ => equipped) ?? placed;
    }
}
