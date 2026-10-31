using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using static Program;

static class QuestObjectsFeature
{
    // The rooms' server objects a quest manages (Quests.RoomObjectRemovedBy), where the room fit places them, while the
    // character lacks the flag that takes them away: The Twilight Strand's force wall (Tutorial_Blocker_3) blocks the way to
    // the gate until Hillock is dead (12-quests.md §6). Ids from QuestRoomObjectFirstId.
    internal static async Task SpawnQuestRoomObjects(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        var notes = new List<string>();
        var objects = AreaPopulation.LoadObjects(conn.Layout.Id, conn.Seed, notes)
            .Select(o => (Object: o, Flag: Quests.RoomObjectRemovedBy(conn.Layout.Id, o.Type)))
            .Where(o => o.Flag is not null).ToList();
        if (objects.Count == 0) return;
        // The walls guard the exits their flag opens; the boss's side is the room's boss point ("mapboss", "mapboss2" …,
        // AreaPopulation.BossPoint; else the arrival).
        var boss = AreaPopulation.LoadPoints(conn.Layout.Id, conn.Seed, []).FirstOrDefault(p => AreaPopulation.BossPoint(p.Name) is not null);
        var from = conn.Layout.Near(boss is null ? (conn.Layout.SpawnX, conn.Layout.SpawnY) : (boss.X, boss.Y));
        uint id = QuestRoomObjectFirstId;
        foreach (var (o, flag) in objects)
        {
            if (ClientHasQuestFlag(conn, flag!)) continue;
            var blocker = new BlockerObject(o.X, o.Y, o.Angle, o.Scale);
            await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(o.Type), blocker.Blob()),
                $"0x02b9 create room object 0x{id:x} {o.Type} at ({o.X}, {o.Y}), angle {o.Angle:0.###}, scale {o.Scale:0.##}, until quest flag {flag}");
            await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), null);
            // The server's own wall: the walkable cells across the way (QuestBarriers).
            var guarded = conn.Layout.AreaTransitions.Where(t => TransitionFlag(conn, t.Transition) == flag)
                .Select(t => conn.Layout.Near((t.Transition.GridX, t.Transition.GridY))).ToList();
            var cells = conn.Layout.Walkable is { } walkable
                ? QuestBarriers.Cells(walkable, conn.Layout.GridW, conn.Layout.GridH, (o.X, o.Y), o.Angle, from, guarded)
                : [];
            Console.WriteLine($"[gateway] {remote} room object 0x{id:x}: {cells.Count} walkable cell(s) closed on the server between ({from.X}, {from.Y}) " +
                $"and {string.Join(", ", guarded)}" + (cells.Count == 0 ? "" : $", from {cells.Min()} to {cells.Max()}"));
            lock (conn.Monsters)
            {
                conn.QuestRoomObjects[id] = (flag!, cells);
                conn.UpdateBarrier();
            }
            id++;
        }
    }

    internal static async Task SpawnOpeningTutorial(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        if (conn.Layout.Id != TutorialObjects.Strand || CharacterSaves.Load(CharacterName(conn))?.Tutorial is not { } tutorial) return;
        var objects = AreaPopulation.LoadObjects(conn.Layout.Id, conn.Seed, []);
        var exile = objects.FirstOrDefault(o => o.Type == TutorialObjects.Exile);
        conn.TutorialZombiePlacement = objects.FirstOrDefault(o => o.Type == TutorialObjects.Spawner);
        if (exile is not null && Npcs.Value.Find(TutorialObjects.Exile) is { Supported: true } npc)
            await SpawnNpc(remote, stream, ciphers, conn, npc with { TransitionState = (byte)(tutorial.Talked ? 2 : 1) }, exile.X, exile.Y, exile.Angle);
        if (conn.TutorialZombiePlacement is { } spawn)
        {
            if (tutorial.ZombieKilled)
                await DropTutorialGem(remote, stream, ciphers, conn, false, spawn.X, spawn.Y);
            else if (tutorial.Talked)
                await SpawnTutorialZombie(remote, stream, ciphers, conn);
            else
            {
                conn.TutorialSceneId = conn.NextObjectId++;
                await Send(remote, stream, ciphers, InstanceEntities.CreateObject(conn.TutorialSceneId, InstanceEntities.ObjectTypeHash(TutorialObjects.Spawner),
                    TutorialObjects.SceneBlob(spawn.X,spawn.Y,spawn.Angle)), "0x02b9 opening bite zombie scene");
                await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(conn.TutorialSceneId), null);
                await Send(remote, stream, ciphers, TutorialObjects.BiteAnimation(conn.TutorialSceneId,0), "0x01b2 opening zombie paused at bite start");
            }
        }
        // The recorder writes tile placements while the client generates the area. Wait briefly for this optional capture.
        _ = TrySpawnTutorialChest(remote, stream, ciphers, conn);
    }

    // a1q4 "Breaking Some Eggs" (GlyphQuest, 12-quests.md §2–4): The Mud Flats' three Rhoa Nests, the Glyph Wall and the
    // waterfall where the recorder captured them, and the client's invisible blockers on the server's walk estimate until
    // PlacedGlyphs. The recorder writes the capture while the client generates the area: wait briefly for it.
    internal static async Task SpawnGlyphQuest(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        await RemoveLeftoverGlyphs(remote, stream, ciphers, conn);
        if (conn.Layout.Id != GlyphQuest.Area) return;
        // Usually captured on an earlier visit: created at once, with the area. Else wait for the recorder in the background.
        if (LoadGlyphLayout(remote, conn) is { } now) { await CreateGlyphObjects(remote, stream, ciphers, conn, now); return; }
        _ = Task.Run(async () =>
        {
            for (int retry = 0; retry < 20 && !conn.Closed; retry++)
            {
                await Task.Delay(1000);
                if (LoadGlyphLayout(remote, conn) is { } later) { await CreateGlyphObjects(remote, stream, ciphers, conn, later); return; }
            }
            if (!conn.Closed) Console.WriteLine($"[gateway] {remote} a1q4: no captured Rhoa Nests or Glyph Wall for {conn.Layout.Id} seed {conn.Seed}; capture with -Walkmaps");
        });
    }

    internal static async Task OpenTutorialChest(string remote,NetworkStream stream,ConnectionCiphers ciphers,ClientState conn,InstanceTransfer.UseSkillOnObject use,TutorialObjects.Placement chest,double requestedAt)
    {
        if (!LeagueFor(conn).Rules.TutorialGifts) return;
        if(CharacterSaves.Load(CharacterName(conn))?.Tutorial is not { ChestOpened:false }) return;
        if (!TryBeginObjectWalk(conn, requestedAt, chest.X, chest.Y, 1, out var objectWalk)) return;
        var (action, now, here, stop, arrive) = objectWalk;
        await Send(remote,stream,ciphers,InstanceProtocol.ActionStarted(PlayerId,here.X,here.Y,chest.X,chest.Y,use.SkillId,use.Sequence,use.Flags,targetObjectId:use.TargetId),null,still:()=>PlayerActionCurrent(conn,action));
        if(arrive>now) await Task.Delay(TimeSpan.FromSeconds(arrive-now));
        if(!PlayerActionCurrent(conn,action) || conn.Closed) return;
        if(!GameServer.Core.World.PlayerWalk.InWalkUpRange(stop,(chest.X,chest.Y),1))
        {
            await Send(remote,stream,ciphers,InstanceProtocol.ActionEnded(PlayerId),null);
            return;
        }
        CharacterSaves.ChangeTutorial(CharacterName(conn),t=>t with { ChestOpened=true });
        await Send(remote,stream,ciphers,InstanceProtocol.RemoveObject(use.TargetId),null);
        await Send(remote,stream,ciphers,InstanceEntities.CreateObject(use.TargetId,InstanceEntities.ObjectTypeHash(chest.Type),TutorialObjects.ChestBlob(chest,true)),"0x02b9 opened support gem chest");
        await Send(remote,stream,ciphers,InstanceProtocol.EnterWakeZone(use.TargetId),null);
        await DropTutorialGem(remote,stream,ciphers,conn,true,chest.X,chest.Y);
        await Send(remote,stream,ciphers,InstanceProtocol.ActionEnded(PlayerId),null);
    }

    // C→S 0x24 on a Rhoa Nest: walk up, open it (once per instance) and drop its glyph, unless the character has it already.
    internal static async Task OpenRhoaNest(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, InstanceTransfer.UseSkillOnObject use, GlyphQuest.Nest nest)
    {
        var at = nest.At;
        if (conn.Instance.ChestOpened(at.X, at.Y)) return;
        int action = Interlocked.Increment(ref conn.ActionCount);
        double now = conn.Clock.Elapsed.TotalSeconds;
        var (here, stop, arrive) = conn.Walk.WalkUpTo(at.X, at.Y, GlyphQuest.NestSize, now);
        await Send(remote, stream, ciphers, InstanceProtocol.ActionStarted(PlayerId, here.X, here.Y, at.X, at.Y, use.SkillId, use.Sequence, use.Flags, targetObjectId: use.TargetId),
            $"0x01ab player opens Rhoa Nest 0x{use.TargetId:x} from ({here.X}, {here.Y}): reached at ({stop.X}, {stop.Y}) in {arrive - now:0.00} s");
        if (arrive > now) await Task.Delay(TimeSpan.FromSeconds(arrive - now));
        if (conn.ActionCount != action || conn.Closed) return;
        int index = conn.Glyphs is { } layout ? layout.Nests.ToList().IndexOf(nest) : -1;
        if (index < 0 || !GameServer.Core.World.PlayerWalk.InWalkUpRange(stop, (at.X, at.Y), GlyphQuest.NestSize) || !conn.Instance.OpenChest(at.X, at.Y))
        {
            await Send(remote, stream, ciphers, InstanceProtocol.ActionEnded(PlayerId), null);
            return;
        }
        // Chest has no update reader: the opened nest replaces the closed one, under its own id (the same id again made
        // the client log "Replacing awake object", live 2026-10-25).
        uint opened = OpenedNestFirstId + (uint)index;
        lock (conn.GlyphNests) { conn.GlyphNests.Remove(use.TargetId); conn.GlyphNests[opened] = nest; }
        conn.RemoveBlocker(use.TargetId);
        conn.SetBlocker(opened, at.X, at.Y, GlyphQuest.NestSize);
        await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(use.TargetId), null);
        await Send(remote, stream, ciphers, InstanceEntities.CreateObject(opened, InstanceEntities.ObjectTypeHash(nest.Type), GlyphQuest.NestBlob(at, true)),
            $"0x02b9 opened Rhoa Nest 0x{opened:x} (was 0x{use.TargetId:x})");
        await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(opened), null);
        await DropGlyph(remote, stream, ciphers, conn, nest);
        await Send(remote, stream, ciphers, InstanceProtocol.ActionEnded(PlayerId), null);
    }

    // C→S 0x24 on the Glyph Wall: walk up; seeing it sets SeenGlyphWall; with all three glyphs it takes them and sets
    // PlacedGlyphs (SetQuestFlags then opens the passage).
    internal static async Task UseGlyphWall(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, InstanceTransfer.UseSkillOnObject use)
    {
        if (conn.Glyphs?.Wall is not { } wall) return;
        int action = Interlocked.Increment(ref conn.ActionCount);
        double now = conn.Clock.Elapsed.TotalSeconds;
        var (here, stop, arrive) = conn.Walk.WalkUpTo(wall.X, wall.Y, GlyphQuest.WallSize, now);
        await Send(remote, stream, ciphers, InstanceProtocol.ActionStarted(PlayerId, here.X, here.Y, wall.X, wall.Y, use.SkillId, use.Sequence, use.Flags, targetObjectId: use.TargetId),
            $"0x01ab player uses the Glyph Wall from ({here.X}, {here.Y}): reached at ({stop.X}, {stop.Y}) in {arrive - now:0.00} s");
        if (arrive > now) await Task.Delay(TimeSpan.FromSeconds(arrive - now));
        if (conn.ActionCount != action || conn.Closed) return;
        if (GameServer.Core.World.PlayerWalk.InWalkUpRange(stop, (wall.X, wall.Y), GlyphQuest.WallSize))
        {
            await SetQuestFlags(remote, stream, ciphers, conn, [GlyphQuest.SeenGlyphWall]);
            if (!ClientHasQuestFlag(conn, GlyphQuest.PlacedGlyphs))
                await PlaceGlyphs(remote, stream, ciphers, conn);
        }
        else Console.WriteLine($"[gateway] {remote} Glyph Wall out of reach from ({stop.X}, {stop.Y})");
        await Send(remote, stream, ciphers, InstanceProtocol.ActionEnded(PlayerId), null);
    }


    internal static async Task TalkToWoundedExile(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,uint id,SpawnedNpc npc)
    {
        if (CharacterSaves.Load(CharacterName(conn))?.Tutorial is not { Talked: false } || conn.TutorialSceneRunning) return;
        bool armed = ItemsOf(conn).Items(PlayerInventories.Weapon1).Count > 0;
        conn.InNpcTalk = true;
        await Send(remote,stream,ciphers,InstanceNpcs.Talk(id,armed ? (byte)2 : (byte)1),"0x0084 dying exile: class weapon check");
        if (!armed) return;
        conn.TutorialSceneRunning = true;
        CharacterSaves.ChangeTutorial(CharacterName(conn),t=>t with { Talked=true });
        await SetQuestFlags(remote,stream,ciphers,conn,["TalkedToWoundedExile"]);
        _ = RunOpeningScene(remote,stream,ciphers,conn,id,npc);
    }

}
