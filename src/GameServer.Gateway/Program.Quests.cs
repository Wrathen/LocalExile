using System.Text.Json;
using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.Diagnostics;

partial class Program
{
    // Sets or clears quest flags for the character (saved at once) and sends the player's flag set again; one change at a
    // time per connection (QuestGate), so a background sync never interleaves with another change.
    internal static async Task SetQuestFlags(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, IEnumerable<string> flags, bool on = true)
    {
        await conn.QuestGate.WaitAsync();
        try { await SetQuestFlagsHeld(remote, stream, ciphers, conn, flags, on); }
        finally { conn.QuestGate.Release(); }
    }

    internal sealed record QuestFlagCommit(List<string> Changed, List<int> All,
        List<string> Effective, List<uint> Removed);

    // Gameplay and persistence commit before any notification. Caller holds the combat lock when used by simulation.
    internal static QuestFlagCommit? CommitQuestFlags(ClientState conn, IEnumerable<string> flags, bool on = true)
    {
        lock (conn.Monsters)
        {
            // Queued work cannot mutate progress after a committed Hardcore death.
            if (HardcoreProgressBlocked(conn)) return null;
            var wanted = flags.Distinct().ToList();
            var clientBefore = PlayerQuestFlags(conn);
            var ownBefore = conn.QuestFlagsSnapshot();
            var had = wanted.ToDictionary(f => f, f => Quests.ClientHas(f, ownBefore, clientBefore));
            var changed = new List<string>();
            foreach (string flag in wanted)
                if (on ? conn.QuestFlags.Add(flag) : conn.QuestFlags.Remove(flag)) changed.Add(flag);
            if (changed.Count == 0) return null;
            CharacterSaves.ChangeQuestFlags(CharacterName(conn), changed, on);
            if (ChangesResistancePenalty(changed))
            {
                conn.Pools = PoolsFor(conn, conn.Pools.Level);
                conn.ApplyMovementSpeed();
            }
            var all = PlayerQuestFlags(conn);
            var own = conn.QuestFlagsSnapshot();
            bool Has(string flag) => Quests.ClientHas(flag, own, all);
            var effective = Quests.EffectiveChanges(changed, f => had[f], Has);
            List<uint> gone = [.. conn.QuestRoomObjects.Where(o => effective.Contains(o.Value.Flag) && Has(o.Value.Flag)).Select(o => o.Key)];
            foreach (uint id in gone) conn.QuestRoomObjects.Remove(id);
            conn.UpdateBarrier();
            return new(changed, all, effective, gone);
        }
    }

    internal static async Task SetQuestFlagsHeld(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, IEnumerable<string> flags, bool on = true)
    {
        QuestFlagCommit? committed;
        lock (conn.Monsters) committed = CommitQuestFlags(conn, flags, on);
        if (committed is not null) await PublishQuestFlags(remote, stream, ciphers, conn, committed, on);
    }

    internal static async Task PublishQuestFlags(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, QuestFlagCommit change, bool on = true)
    {
        foreach (string flag in change.Changed) Console.WriteLine($"[gateway] {remote} {CharacterName(conn)}: quest flag {flag} {(on ? "set" : "cleared")}");
        if (!conn.PlayerSent) return;
        await Send(remote, stream, ciphers, InstanceEntities.PlayerQuestFlagsUpdate(PlayerId, change.All), $"0x02ba player quest flags: {change.All.Count}");
        if (ChangesResistancePenalty(change.Changed))
            await SendCharacterStats(remote, stream, ciphers, conn, "Kitava resistance penalty changed");
        bool Has(string flag) => ClientHasQuestFlag(conn, flag);
        foreach (var (id, transition) in conn.Layout.AreaTransitions)
        {
            if (TransitionFlag(conn, transition) is not { } flag || !change.Effective.Contains(flag)) continue;
            if (Has(flag))
            {
                await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(transition.Type), transition.Blob()),
                    $"0x02b9 create area transition 0x{id:x} {transition} (quest flag {flag})");
                await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), null);
            }
            else await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(id), $"0x02bb remove area transition 0x{id:x} (quest flag {flag} cleared)");
        }
        if (change.Effective.Contains(GlyphQuest.PlacedGlyphs))
            await ChangeSubmergedPassage(remote, stream, ciphers, conn, Has(GlyphQuest.PlacedGlyphs));
        foreach (uint id in change.Removed)
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(id), $"0x02bb remove room object 0x{id:x} (quest flag set)");
    }

    // The quest flag an area transition needs (Quests.FlagToEnter), or null. Such an exit is created only once the character
    // has the flag: The Twilight Strand's gate into Lioneye's Watch appears when Hillock dies (a1q1, 12-quests.md §6).
    internal static string? TransitionFlag(ClientState conn, AreaTransitionObject transition) =>
        AreaTransitionCatalog.IdOf(transition.DestinationHash16) is { } dest ? Quests.FlagToEnter(conn.Layout.Id, dest) : null;

    internal static bool TransitionShown(ClientState conn, AreaTransitionObject transition) =>
        TransitionFlag(conn, transition) is not { } flag || ClientHasQuestFlag(conn, flag);

    // A quest flag as the client has it: the character's own, or one the World panel's access flags carry (an activated
    // waypoint's area, 16-waypoints.md §4: the Submerged Passage's waypoint brings a1q4-PlacedGlyphs). The client opens
    // its ways by these, so the server's checks follow them.
    internal static bool ClientHasQuestFlag(ClientState conn, string flag) => Quests.ClientHas(flag, conn.QuestFlagsSnapshot(), PlayerQuestFlags(conn));
    internal static async Task TrySpawnTutorialChest(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        if (!LeagueFor(conn).Rules.TutorialGifts) return;
        try
        {
            for (int retry=0; retry<20 && !conn.Closed; retry++)
            {
                var named=AreaPopulation.LoadPoints(conn.Layout.Id,conn.Seed,[]).FirstOrDefault(p=>p.Name==TutorialObjects.Chest);
                if ((TutorialObjects.ChestPlacement(conn.Layout.Id,conn.Seed) ?? (named is null ? null : TutorialObjects.ChestAt(named))) is { } chest)
                {
                    conn.TutorialChest = chest; conn.TutorialChestId = conn.NextObjectId++;
                    bool opened = CharacterSaves.Load(CharacterName(conn))?.Tutorial?.ChestOpened == true;
                    await Send(remote,stream,ciphers,InstanceEntities.CreateObject(conn.TutorialChestId,InstanceEntities.ObjectTypeHash(chest.Type),
                        TutorialObjects.ChestBlob(chest,opened)), $"0x02b9 tutorial support chest at ({chest.X},{chest.Y}), opened={opened}");
                    await Send(remote,stream,ciphers,InstanceProtocol.EnterWakeZone(conn.TutorialChestId),null);
                    if (opened) await DropTutorialGem(remote,stream,ciphers,conn,true,chest.X,chest.Y);
                    return;
                }
                await Task.Delay(1000);
            }
            if (!conn.Closed) Console.WriteLine($"[gateway] {remote} tutorial chest has no captured placement for {conn.Layout.Id} seed {conn.Seed}; capture with -Walkmaps");
        }
        catch (Exception ex) when (ex is IOException or JsonException or ObjectDisposedException)
        { if (!conn.Closed) Console.WriteLine($"[gateway] tutorial chest: {ex.Message}"); }
    }

    internal static async Task RunOpeningScene(string remote,NetworkStream stream,ConnectionCiphers ciphers,ClientState conn,uint id,SpawnedNpc npc)
    {
        try
        {
            double start=conn.Clock.Elapsed.TotalSeconds;
            // NPCTalk's actual script: triggerer.TalkToWoundedExile(8.2,22.8). Use the paused instance clock.
            async Task Until(double elapsed) { while (!conn.Closed && conn.Clock.Elapsed.TotalSeconds < start+elapsed) await Task.Delay(50); }
            await Until(8.2); if(conn.Closed) return;
            int component=npc.Npc.Components!.ToList().IndexOf("Transitionable");
            if(component>=0) await Send(remote,stream,ciphers,InstanceEntities.TransitionableUpdate(id,(byte)component,2),"0x02ba wounded exile bitten");
            if(conn.TutorialSceneId!=0) await Send(remote,stream,ciphers,TutorialObjects.BiteAnimation(conn.TutorialSceneId,1),"0x01b2 opening zombie bites exile");
            await Until(22.8); if(conn.Closed) return;
            if(conn.TutorialSceneId!=0) await Send(remote,stream,ciphers,InstanceProtocol.RemoveObject(conn.TutorialSceneId),null);
            if(conn.TalkingTo==id) { conn.InNpcTalk=false;conn.TalkingTo=0;await Send(remote,stream,ciphers,InstanceNpcs.Talk(0),null); }
            await SpawnTutorialZombie(remote,stream,ciphers,conn);
        }
        catch(Exception ex) when(ex is IOException or ObjectDisposedException)
        { if(!conn.Closed) Console.WriteLine($"[gateway] opening scene: {ex.Message}"); }
        finally { conn.TutorialSceneRunning=false; }
    }

    internal static async Task SpawnTutorialZombie(string remote,NetworkStream stream,ConnectionCiphers ciphers,ClientState conn)
    {
        if(Environment.GetEnvironmentVariable("Game_MONSTERS")=="0" || conn.TutorialZombiePlacement is not { } spawn) return;
        lock(conn.Monsters) if(conn.Monsters.Values.Any(m=>m.Variety?.Id==TutorialObjects.Zombie)) return;
        if(Monsters.Find(TutorialObjects.Zombie) is { Supported:true } zombie)
            await SpawnMonster(remote,stream,ciphers,conn,zombie,1,MonsterRarity.Normal,spawn.X,spawn.Y);
    }

    // Ids of the invisible SunkenPassageBlockers on the server's walk estimate (blockers only: the client spawns them).
    internal const uint PassageBlockerFirstId = 0xff000000;
    // a1q4's objects have ids of their own, between the quest room objects (QuestRoomObjectFirstId) and the waypoint, so
    // they never meet the ids other code takes from NextObjectId: the nests (+ their index), the wall, the waterfall and
    // the opened nests (+ 0x20 + their index).
    internal const uint GlyphObjectFirstId = 0x2e80, GlyphWallObjectId = GlyphObjectFirstId + 0x10, GlyphWaterObjectId = GlyphObjectFirstId + 0x11,
        OpenedNestFirstId = GlyphObjectFirstId + 0x20;

    internal static GlyphQuest.Layout? LoadGlyphLayout(string remote, ClientState conn)
    {
        try { return GlyphQuest.Load(conn.Layout.Id, conn.Seed); }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            Console.WriteLine($"[gateway] {remote} a1q4 capture: {ServerLog.Describe(ex)}");
            return null;
        }
    }

    internal static async Task CreateGlyphObjects(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, GlyphQuest.Layout layout)
    {
        try
        {
            conn.Glyphs = layout;
            bool placed = ClientHasQuestFlag(conn, GlyphQuest.PlacedGlyphs);
            if (!placed) SetPassageBlockers(conn, true);
            else
                // Glyphs left on this instance's ground are not needed any more (their .it remove_flag).
                foreach (var (gid, _) in conn.Instance.Ground.All().Where(e => GlyphIndex(e.Item.Item) is not null))
                    if (conn.Instance.Ground.Take(gid) is not null)
                        await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(gid), $"0x02bb remove glyph 0x{gid:x}: the glyphs are placed");
            for (int i = 0; i < layout.Nests.Count && i < 0x10; i++)
            {
                var nest = layout.Nests[i];
                bool opened = conn.Instance.ChestOpened(nest.At.X, nest.At.Y);
                uint id = (opened ? OpenedNestFirstId : GlyphObjectFirstId) + (uint)i;
                lock (conn.GlyphNests) conn.GlyphNests[id] = nest;
                conn.SetBlocker(id, nest.At.X, nest.At.Y, GlyphQuest.NestSize);
                await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(nest.Type), GlyphQuest.NestBlob(nest.At, opened)),
                    $"0x02b9 Rhoa Nest 0x{id:x} {nest.Type} at ({nest.At.X}, {nest.At.Y}) with glyph {nest.Glyph + 1}, opened={opened}");
                await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), null);
            }
            if (layout.Wall is { } wall)
            {
                await Send(remote, stream, ciphers, InstanceEntities.CreateObject(GlyphWallObjectId, InstanceEntities.ObjectTypeHash(GlyphQuest.Wall), GlyphQuest.WallBlob(wall, placed)),
                    $"0x02b9 Glyph Wall 0x{GlyphWallObjectId:x} at ({wall.X}, {wall.Y}), {(placed ? "glyphs placed" : "waiting for the glyphs")}");
                await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(GlyphWallObjectId), null);
                conn.GlyphWallId = GlyphWallObjectId;
            }
            if (layout.Water is { } water)
            {
                await Send(remote, stream, ciphers, InstanceEntities.CreateObject(GlyphWaterObjectId, InstanceEntities.ObjectTypeHash(GlyphQuest.Water), GlyphQuest.WaterBlob(water.X, water.Y, water.Rotation, placed)),
                    $"0x02b9 waterfall 0x{GlyphWaterObjectId:x} at ({water.X}, {water.Y}), transitioned={placed}");
                await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(GlyphWaterObjectId), null);
                conn.GlyphWaterId = GlyphWaterObjectId;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or ObjectDisposedException or KeyNotFoundException or InvalidOperationException)
        { if (!conn.Closed) Console.WriteLine($"[gateway] a1q4 objects: {ServerLog.Describe(ex)}"); }
    }

    // Which glyph (0–2) an item is, or null.
    internal static int? GlyphIndex(Item item) => GlyphQuest.GlyphOf(Items.ByHash(item.TypeHash)?.Id);

    internal static void SetPassageBlockers(ClientState conn, bool on)
    {
        if (conn.Glyphs is not { } layout) return;
        for (int i = 0; i < layout.Blockers.Count; i++)
            if (on) conn.SetBlocker(PassageBlockerFirstId + (uint)i, layout.Blockers[i].X, layout.Blockers[i].Y, GlyphQuest.BlockerSize);
            else conn.RemoveBlocker(PassageBlockerFirstId + (uint)i);
    }

    // PlacedGlyphs as the client has it changed: the wall and the waterfall play their transition (0x2ba to
    // Transitionable), and the invisible blockers leave the server's walk estimate (the client drops its own when it gets
    // the flag). Cleared (dev): the other way.
    internal static async Task ChangeSubmergedPassage(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, bool open)
    {
        lock (conn.Monsters)
        {
            if (HardcoreProgressBlocked(conn) || conn.Glyphs is null) return;
            SetPassageBlockers(conn, !open);
        }
        byte state = open ? GlyphQuest.Transitioned : GlyphQuest.Untransitioned;
        foreach (var (id, what) in new[] { (conn.GlyphWallId, "Glyph Wall"), (conn.GlyphWaterId, "waterfall") })
            if (id != 0)
                await Send(remote, stream, ciphers, InstanceEntities.TransitionableUpdate(id, GlyphQuest.TransitionableIndex, state),
                    $"0x02ba {what} 0x{id:x} Transitionable → {state}");
    }

    // The HaveGlyph flags follow the glyphs the character carries (GlyphQuest.SyncFlags), whatever took them away (a drop,
    // a sale, the stash). Runs from the player tick: it reads the flags as a snapshot and changes them inside
    // conn.QuestGate, and it changes no inventory (RemoveLeftoverGlyphs does that on the message path).
    internal static async Task SyncGlyphs(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        await conn.QuestGate.WaitAsync();
        try
        {
            var carried = GlyphQuest.Carried(ItemsOf(conn)).Select(c => GlyphIndex(c.Item.Item)).OfType<int>().ToHashSet();
            var own = conn.QuestFlagsSnapshot();
            bool placed = Quests.ClientHas(GlyphQuest.PlacedGlyphs, own, PlayerQuestFlags(conn));
            var (set, clear) = GlyphQuest.SyncFlags(own, placed, placed ? new HashSet<int>() : carried);
            if (set.Count > 0) await SetQuestFlagsHeld(remote, stream, ciphers, conn, set, true);
            if (clear.Count > 0) await SetQuestFlagsHeld(remote, stream, ciphers, conn, clear, false);
        }
        finally { conn.QuestGate.Release(); }
    }

    // Once the glyphs are placed, glyphs still carried go (the .it remove_flag): a copy taken back from the stash or given.
    // On entering an area, from the message path, so its 0xbf sends keep their order with the other inventory changes.
    internal static async Task RemoveLeftoverGlyphs(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        var items = ItemsOf(conn);
        var carried = GlyphQuest.Carried(items).Where(c => GlyphIndex(c.Item.Item) is not null).ToList();
        if (carried.Count == 0 || !ClientHasQuestFlag(conn, GlyphQuest.PlacedGlyphs)) return;
        foreach (var row in carried.GroupBy(c => c.Row))
        {
            List<uint> gone;
            lock (conn.Monsters)
            {
                if (HardcoreProgressBlocked(conn)) return;
                gone = row.Where(c => items.Remove(row.Key, c.Item.Id) is not null).Select(c => c.Item.Id).ToList();
            }
            if (gone.Count > 0)
                await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(row.Key), items.NextRevision(row.Key), gone, []),
                    $"0x00bf inventory {PlayerInventories.InventoryId(row.Key)}: glyphs {string.Join(", ", gone)} removed, the glyphs are placed");
        }
    }

    // A nest's glyph on the instance's ground, unless the glyphs are placed or a copy is carried, in the stash or on this
    // instance's ground (GlyphQuest.NestDrops): a glyph lost another way drops again.
    internal static async Task DropGlyph(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, GlyphQuest.Nest nest)
    {
        int g = nest.Glyph;
        if (Items.Find(GlyphQuest.Glyphs[g]) is not { Supported: true } type)
        {
            Console.WriteLine($"[gateway] {remote} a1q4: {GlyphQuest.Glyphs[g]} is not in the item data");
            return;
        }
        var stash = PersonalStashOf(conn);
        if (!GlyphQuest.NestDrops(type.Hash, ClientHasQuestFlag(conn, GlyphQuest.PlacedGlyphs), ItemsOf(conn), stash.CountOf, conn.Instance.Ground))
        {
            Console.WriteLine($"[gateway] {remote} Rhoa Nest at ({nest.At.X}, {nest.At.Y}): no {type.Name}, the character has it or the glyphs are placed");
            return;
        }
        var ground = new WorldItemObject(nest.At.X, nest.At.Y, ItemCatalog.Create(type));
        uint id = conn.Instance.Ground.Add(ground);
        await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(WorldItemObject.MetadataPath), ground.Blob()),
            $"0x02b9 {type.Name} 0x{id:x} from the Rhoa Nest at ({nest.At.X}, {nest.At.Y})");
        await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), null);
    }

    internal static async Task PlaceGlyphs(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        var items = ItemsOf(conn);
        List<(byte Row, InventoryItem Item)>? removed;
        lock (conn.Monsters)
        {
            if (HardcoreProgressBlocked(conn)) return;
            removed = GlyphQuest.Place(CharacterName(conn), items, GlyphIndex);
        }
        if (removed is null)
        {
            Console.WriteLine($"[gateway] {remote} Glyph Wall: needs all three glyphs");
            return;
        }
        foreach (var row in removed.GroupBy(r => r.Row))
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(row.Key), items.NextRevision(row.Key), [.. row.Select(r => r.Item.Id)], []),
                $"0x00bf inventory {PlayerInventories.InventoryId(row.Key)}: glyphs {string.Join(", ", row.Select(r => r.Item.Id))} placed in the Glyph Wall");
        await SetQuestFlags(remote, stream, ciphers, conn, [GlyphQuest.PlacedGlyphs]);
        await SetQuestFlags(remote, stream, ciphers, conn, GlyphQuest.HaveGlyph, on: false);
    }



    internal static async Task DropTutorialGem(string remote,NetworkStream stream,ConnectionCiphers ciphers,ClientState conn,bool support,int x,int y)
    {
        if (!LeagueFor(conn).Rules.TutorialGifts && support) return;
        if(CharacterSaves.Load(CharacterName(conn))?.Tutorial is not { } tutorial || (support ? tutorial.SupportClaimed : tutorial.SkillClaimed)) return;
        var item=CharacterSaves.FromSaved(support?tutorial.SupportGem:tutorial.SkillGem,Items);
        var ground=new WorldItemObject(x,y,item);
        if(conn.Instance.PlaceTutorialItem(TutorialObjects.DropKey(CharacterName(conn),tutorial,support),ground) is not uint id) return;
        await Send(remote,stream,ciphers,InstanceEntities.CreateObject(id,InstanceEntities.ObjectTypeHash(WorldItemObject.MetadataPath),ground.Blob()),
            $"0x02b9 tutorial {(support?"support":"skill")} gem {Items.GemName(item)} at ({x},{y})");
        await Send(remote,stream,ciphers,InstanceProtocol.EnterWakeZone(id),null);
    }
}
