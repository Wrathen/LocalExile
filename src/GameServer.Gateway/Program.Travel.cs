using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using PlayerWalk = GameServer.Core.World.PlayerWalk;

partial class Program
{
    // Reserve an object walk and its original token atomically with the same recovery/read-time check as skills.
    // An interaction dispatched before a hit may not capture a new token after that hit cancels the old action.
    internal static bool TryBeginObjectWalk(ClientState conn, double requestedAt, int x, int y, double reach,
        out (int Action, double Now, (int X, int Y) Here, (int X, int Y) Stop, double Arrive) started,
        bool centreRange = false)
    {
        started = default;
        using (ProjectileMutation(conn))
        {
            double now = ProjectileWorldTime(conn);
            if (conn.Closed || conn.PlayerLife == 0 || PlayerActionBlocked(conn, requestedAt, now)) return false;
            int action = Interlocked.Increment(ref conn.ActionCount);
            conn.ChannelSkill = 0;
            var (here, stop, arrive) = centreRange ? conn.Walk.Approach(x, y, reach, now) : conn.Walk.WalkUpTo(x, y, (int)reach, now);
            started = (action, now, here, stop, arrive);
            return true;
        }
    }

    // An area transition click. Plain: change area (0x2b2 now, the move itself once the client acknowledges with 0x2cb).
    // With Ctrl: list the destination's instances (0xae) for the client's instance chooser.
    static async Task OnUseObject(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        InstanceTransfer.UseSkillOnObject use, double requestedAt)
    {
        lock (conn.Monsters)
            if (PlayerActionBlocked(conn, requestedAt, conn.Clock.Elapsed.TotalSeconds)) return;
        if (InTown(conn) && (SkillsFor(conn).IsGemSkill(use.SkillId) || use.SkillId == SkillCatalog.FirstSkillId)) return;
        if (SkillsFor(conn).IsGemSkill(use.SkillId) || use.SkillId == SkillCatalog.FirstSkillId)
            await EndGracePeriod(remote, stream, ciphers, conn, action: true);
        SpawnedMonster? target;
        lock (conn.Monsters) target = conn.Monsters.GetValueOrDefault(use.TargetId);
        if (target is not null)
        {
            if (use.SkillId != SkillCatalog.FirstSkillId) await CastOn(remote, stream, ciphers, conn, use, target, requestedAt);
            else await Attack(remote, stream, ciphers, conn, use, target, requestedAt);
            return;
        }
        // A self-cast skill (Righteous Fire) targets the player itself: echo it in place, then cast.
        if (use.TargetId == PlayerId && SkillsFor(conn).IsGemSkill(use.SkillId))
        {
            var gem = GemSkillEffects(conn).FirstOrDefault(g => g.Id == use.SkillId);
            double at = conn.Clock.Elapsed.TotalSeconds;
            (int X, int Y) self = default;
            if (!TryBeginSkillCooldownUse(conn, use.SkillId, gem.Effect,
                gem.Name is null ? null : CooldownFor(conn, gem.Effect, gem.Level, gem.Quality), false,
                () =>
                {
                    Interlocked.Increment(ref conn.ActionCount);
                    self = conn.Walk.Position(at);
                    conn.Walk.Order(self.X, self.Y, at);
                }, out var cooldownUse, requestedAt: requestedAt)) return;
            bool acknowledged = false;
            await Send(remote, stream, ciphers,
                InstanceProtocol.ActionStarted(PlayerId, self.X, self.Y, self.X, self.Y, use.SkillId, use.Sequence, use.Flags, targetObjectId: PlayerId),
                $"0x01ab player skill 0x{use.SkillId:x4} cast on itself at ({self.X}, {self.Y})",
                still: () => acknowledged = OwnAuraAcknowledgementAllowed(conn, gem.Effect, use.SkillId,
                    () => cooldownUse?.CommitNativeUse(conn, use.SkillId) ?? true));
            if (!acknowledged) return;
            await CastGemSkill(remote, stream, ciphers, conn, use.SkillId, self, self, PlayerId, sequence: use.Sequence,
                actionToken: cooldownUse?.ActionToken, cooldownUse: cooldownUse);
            return;
        }
        if (conn.Instance.Ground.Get(use.TargetId) is { } ground)
        {
            if (await PickUpQuestItem(remote, stream, ciphers, conn, use.TargetId, ground, use, requestedAt)) return;   // a1q5's Medicine Chest
            await PickUpGroundItem(remote, stream, ciphers, conn, use.TargetId, ground, use, requestedAt);
            return;
        }
        if (conn.TutorialChestId == use.TargetId && conn.TutorialChest is { } tutorialChest)
        {
            await OpenTutorialChest(remote, stream, ciphers, conn, use, tutorialChest, requestedAt);
            return;
        }
        GlyphQuest.Nest? nest;
        lock (conn.GlyphNests) nest = conn.GlyphNests.GetValueOrDefault(use.TargetId);
        if (nest is not null)
        {
            await OpenRhoaNest(remote, stream, ciphers, conn, use, nest);
            return;
        }
        if (conn.GlyphWallId != 0 && use.TargetId == conn.GlyphWallId)
        {
            await UseGlyphWall(remote, stream, ciphers, conn, use);
            return;
        }
        if (use.TargetId == WaypointCatalog.ObjectId && conn.Layout.Waypoint is { } waypoint)
        {
            await UseWaypoint(remote, stream, ciphers, conn, use, waypoint, requestedAt);
            return;
        }
        SpawnedNpc? npc;
        lock (conn.Npcs) npc = conn.Npcs.GetValueOrDefault(use.TargetId);
        if (npc is not null)
        {
            await TalkToNpc(remote, stream, ciphers, conn, use, npc, requestedAt);
            return;
        }
        if (conn.BackPortal is { } home && use.TargetId == home.Id && OpenPortals.TryGetValue(CharacterName(conn), out var origin))
        {
            if (!TryBeginObjectWalk(conn, requestedAt, home.X, home.Y, AreaTransitionObjectSize, out var homeWalk)) return;
            var (homeAction, homeNow, _, _, homeArrive) = homeWalk;
            if (homeArrive > homeNow) await Task.Delay(TimeSpan.FromSeconds(homeArrive - homeNow));
            if (!PlayerActionCurrent(conn, homeAction)) return;
            await ChangeArea(remote, stream, ciphers, conn, new PendingTransfer(origin.Area, origin.Instance, false, ArriveAt: (origin.X, origin.Y)),
                "back through the town portal");
            return;
        }
        if (conn.ReturnPortal is { } back && use.TargetId == back.Id)
        {
            await UseMapReturnPortal(remote, stream, ciphers, conn, use, (back.X, back.Y), requestedAt);
            return;
        }
        (int X, int Y, int Index)? portal;
        lock (conn.MapPortals) portal = conn.MapPortals.TryGetValue(use.TargetId, out var at) ? at : null;
        if (portal is { } p)
        {
            await UseMapPortal(remote, stream, ciphers, conn, use, p, requestedAt);
            return;
        }
        HideoutCatalog.Doodad? doodad;
        lock (conn.HideoutDoodads) doodad = conn.HideoutDoodads.GetValueOrDefault(use.TargetId);
        if (doodad is not null)
        {
            await UseMapDevice(remote, stream, ciphers, conn, use, doodad, requestedAt);
            return;
        }
        if (conn.DelveCrawlerObject != 0 && use.TargetId == conn.DelveCrawlerObject)
        {
            await OpenDelveChart(remote, stream, ciphers, conn, use, requestedAt);
            return;
        }
        if (conn.StashObject != 0 && use.TargetId == conn.StashObject)
        {
            await OpenStash(remote, stream, ciphers, conn, use, requestedAt);
            return;
        }
        if (conn.GuildStashObject != 0 && use.TargetId == conn.GuildStashObject)
        {
            await OpenGuildStash(remote, stream, ciphers, conn, use, requestedAt);
            return;
        }
        if (conn.HeistLockerObject != 0 && use.TargetId == conn.HeistLockerObject)
        {
            await OpenHeistLocker(remote, stream, ciphers, conn);
            return;
        }
        if (conn.CraftingBenchObject != 0 && use.TargetId == conn.CraftingBenchObject)
        {
            await OpenCraftingBench(remote, stream, ciphers, conn, use, requestedAt);
            return;
        }
        if (conn.HorticraftingObject != 0 && use.TargetId == conn.HorticraftingObject)
        {
            await OpenHorticrafting(remote, stream, ciphers, conn, requestedAt);
            return;
        }
        var (_, transition) = conn.Layout.AreaTransitions.FirstOrDefault(e => e.Id == use.TargetId);
        if (transition is null)
        {
            Console.WriteLine($"[gateway] {remote} <= 0x0024 use object {use} (not an area transition)");
            return;
        }
        ushort dest = transition.DestinationHash16;
        // A quest can close an exit: The Twilight Strand's way into Lioneye's Watch needs Hillock dead (a1q1).
        if (AreaTransitionCatalog.IdOf(dest) is { } destId && Quests.FlagToEnter(conn.Layout.Id, destId) is { } needed && !ClientHasQuestFlag(conn, needed))
        {
            Console.WriteLine($"[gateway] {remote} <= 0x0024 area transition 0x{use.TargetId:x} to {destId}: closed until quest flag {needed}");
            return;
        }
        // The client walks to the area transition first; follow it and act once the player is there (a move meanwhile cancels).
        if (!TryBeginObjectWalk(conn, requestedAt, transition.GridX, transition.GridY, AreaTransitionObjectSize, out var objectWalk)) return;
        var (action, now, here, stop, arrive) = objectWalk;
        Console.WriteLine($"[gateway] {remote} <= 0x0024 area transition 0x{use.TargetId:x} from ({here.X}, {here.Y}): reached at ({stop.X}, {stop.Y}) in {arrive - now:0.00} s");
        if (arrive > now) await Task.Delay(TimeSpan.FromSeconds(arrive - now));
        if (!PlayerActionCurrent(conn, action)) return;
        if (!PlayerWalk.InWalkUpRange(stop, (transition.GridX, transition.GridY), AreaTransitionObjectSize))
        {
            Console.WriteLine($"[gateway] {remote} area transition 0x{use.TargetId:x} out of reach from ({stop.X}, {stop.Y})");
            return;
        }
        // A gate within the area (a map's boss arena): next to its pair, in the same instance.
        if (transition.TeleportTo is { } pair)
        {
            var (tx, ty) = conn.Layout.Near(pair);
            using (ProjectileMutation(conn))
            {
                if (!PlayerActionCurrent(conn, action)) return;
                conn.Walk.Teleport(tx, ty, ProjectileWorldTime(conn));
            }
            await Send(remote, stream, ciphers, InstanceProtocol.Teleport(PlayerId, tx, ty),
                $"0x01b7 player through the gate 0x{use.TargetId:x} to ({tx}, {ty}), next to its pair at ({pair.X}, {pair.Y})",
                still: () => PlayerActionCurrent(conn, action));
            return;
        }
        if (use.Ctrl)
        {
            await ShowInstances(remote, stream, ciphers, conn, dest, atWaypoint: false);
            return;
        }
        await ChangeArea(remote, stream, ciphers, conn, new PendingTransfer(dest, 0, false), use.ToString());
    }

    // The destination's instances (0xae) for the client's instance chooser; its pick (0xaf) changes area.
    static async Task ShowInstances(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, ushort dest, bool atWaypoint)
    {
        var list = await InstanceControl.CallAsync(new ControlRequest { Op = "list", Ticket = conn.Ticket, Area = dest });
        var instances = list.Instances ?? [];
        var entries = instances
            .Select(i => new InstanceTransfer.InstanceListEntry((ulong)i.CreatedUnix, (uint)i.SecondsLeft, 0, i.Players)).ToList();
        conn.Chooser = new ChooserList(dest, [.. instances.Select(i => i.Id)], atWaypoint);
        await Send(remote, stream, ciphers, InstanceTransfer.InstanceList(dest, entries),
            $"0x00ae instance list for 0x{dest:x4}: {entries.Count} instance(s) [{string.Join(", ", instances.Select(i => $"{i.Id} ({i.SecondsLeft} s left): {string.Join("/", i.Players)}"))}]");
    }

    // A waypoint click (C→S 0x24 on the waypoint, Agents/docs/protocol/16-waypoints.md): walk there, activate it if it is
    // not yet, then open the World panel for travel (S→C 0x7b kind 0). The panel's pick arrives as C→S 0x4d.
    static async Task UseWaypoint(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        InstanceTransfer.UseSkillOnObject use, WaypointObject waypoint, double requestedAt)
    {
        if (!TryBeginObjectWalk(conn, requestedAt, waypoint.GridX, waypoint.GridY, WaypointObjectSize, out var objectWalk)) return;
        var (action, now, here, stop, arrive) = objectWalk;
        Console.WriteLine($"[gateway] {remote} <= 0x0024 waypoint ({use}) from ({here.X}, {here.Y}): reached at ({stop.X}, {stop.Y}) in {arrive - now:0.00} s");
        if (arrive > now) await Task.Delay(TimeSpan.FromSeconds(arrive - now));
        if (!PlayerActionCurrent(conn, action)) return;
        if (!PlayerWalk.InWalkUpRange(stop, (waypoint.GridX, waypoint.GridY), WaypointObjectSize))
        {
            Console.WriteLine($"[gateway] {remote} waypoint out of reach from ({stop.X}, {stop.Y})");
            return;
        }
        await ActivateWaypoint(remote, stream, ciphers, conn, "clicked");
        await Send(remote, stream, ciphers, InstanceWaypoints.OpenUi(InstanceWaypoints.WorldPanelUi, WaypointCatalog.ObjectId),
            "0x007b open the World panel for waypoint travel", still: () => PlayerActionCurrent(conn, action));
    }

    // Activates this area's waypoint for the character (saved, added to the saved ones) and tells the client: a new 0x4c with
    // the area's bit set plays the waypoint's activation. Towns activate on entry, other waypoints when the player comes near
    // or clicks them.
    static async Task ActivateWaypoint(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string why)
    {
        if (WaypointCatalog.Areas.Find(conn.Layout.Hash) is not { } entry) return;
        lock (conn.Monsters)
        {
            if (HardcoreProgressBlocked(conn)) return;
            lock (conn.Waypoints) if (!conn.Waypoints.Add(entry.Id)) return;
            CharacterSaves.AddWaypoints(CharacterName(conn), [entry.Id]);
        }
        await SendWaypointList(remote, stream, ciphers, conn, $"waypoint of {entry.Name} ({entry.Id}) activated: {why}");
    }

    // After the player is in: a town's waypoint activates on entry, then the character's waypoints (S→C 0x4c).
    static async Task SendWaypoints(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        if (WaypointCatalog.Areas.Entries.Count == 0) return;
        if (WaypointCatalog.Areas.Find(conn.Layout.Hash) is { IsTown: true } town)
        {
            lock (conn.Monsters)
            {
                if (HardcoreProgressBlocked(conn)) return;
                bool added;
                lock (conn.Waypoints) added = conn.Waypoints.Add(town.Id);
                if (added) CharacterSaves.AddWaypoints(CharacterName(conn), [town.Id]);
            }
        }
        await SendWaypointList(remote, stream, ciphers, conn, "the character's waypoints");
    }

    static async Task SendWaypointList(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string why)
    {
        var entries = WaypointCatalog.Areas.Entries;
        List<int> bits;
        // The World panel's Labyrinth pin (SpecialMapPins Labyrinth) shows only with its area's waypoint bit: always set.
        lock (conn.Waypoints) bits = [.. Enumerable.Range(0, entries.Count)
            .Where(i => conn.Waypoints.Contains(entries[i].Id) || entries[i].Id == WaypointCatalog.LabyrinthPlaza)];
        // The pins need their areas' access quest flags first (the player object carried the ones known at creation).
        if (conn.PlayerSent)
        {
            var flags = PlayerQuestFlags(conn);
            await Send(remote, stream, ciphers, InstanceEntities.PlayerQuestFlagsUpdate(PlayerId, flags), $"0x02ba player quest flags: {flags.Count}");
        }
        await Send(remote, stream, ciphers, InstanceWaypoints.Waypoints(bits), $"0x004c waypoints: {bits.Count} activated ({why})");
    }

    // The player's quest flags as the client keeps them (QuestFlags rows, 16-waypoints.md §3): the flags the character's
    // quests set (12-quests.md §5; the client checks them too: Lioneye's Watch is accessible only with a1q1-KilledHillock),
    // and what the World panel needs (16-waypoints.md §4): the visit flags of every area entered or whose waypoint is
    // activated (they reveal the neighbouring pins and so the connection lines), and the access flags of the activated
    // waypoints' areas (their pins become travel targets).
    static List<int> PlayerQuestFlags(ClientState conn)
    {
        // ThisIsMyHideout only in a hideout and the Mine Encampment, where Niko needs it for his own options; elsewhere NPCs
        // show their "not my hideout" rows. (It does not hide Sell Items: the client offers Sell only in towns and hideouts.)
        bool inHideout = HideoutCatalog.ByArea(conn.Layout.Hash) is not null || conn.Layout.Hash == StashCatalog.DelveHub;
        // The quest flags as a snapshot, taken before the Waypoints lock (ClientState.QuestFlagsSnapshot): the two locks are
        // never held together.
        var own = conn.QuestFlagsSnapshot();
        lock (conn.Waypoints)
            return [.. WaypointCatalog.Areas.Entries.Where(e => conn.Waypoints.Contains(e.Id)).SelectMany(e => e.AccessFlags ?? [])
                .Concat(conn.Visited.Concat(conn.Waypoints).SelectMany(id => WaypointCatalog.Visits.For(id)))
                .Concat(Quests.Rows(own.Where(f => inHideout || f != Quests.ThisIsMyHideout)))
                .Distinct().Order()];
    }

    // Entering an area marks it visited (saved, added to the saved ones) before the player object carries the quest flags.
    static void RecordVisit(ClientState conn)
    {
        if (WaypointCatalog.Visits.For(conn.Layout.Id).Count == 0) return;
        lock (conn.Monsters)
        {
            if (HardcoreProgressBlocked(conn)) return;
            lock (conn.Waypoints) if (!conn.Visited.Add(conn.Layout.Id)) return;
            CharacterSaves.AddVisited(CharacterName(conn), [conn.Layout.Id]);
        }
    }

    // C→S 0x4d: a waypoint picked on the World panel. An activated waypoint changes area (the player arrives next to its
    // waypoint); with Ctrl, the instance chooser first.
    static async Task TravelByWaypoint(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, InstanceWaypoints.Travel travel)
    {
        var entry = WaypointCatalog.Areas.Find(travel.AreaHash16);
        bool activated;
        lock (conn.Waypoints) activated = entry is not null && (conn.Waypoints.Contains(entry.Id) || entry.Id == WaypointCatalog.LabyrinthPlaza);
        Console.WriteLine($"[gateway] {remote} <= 0x004d waypoint travel to 0x{travel.AreaHash16:x4} ({entry?.Name ?? "no waypoint"}), " +
            $"u32 0x{travel.U32:x}{(travel.Ctrl ? ", Ctrl" : "")}{(activated ? "" : ": not activated, ignored")}");
        if (!activated) return;
        if (travel.Ctrl) await ShowInstances(remote, stream, ciphers, conn, travel.AreaHash16, atWaypoint: true);
        else await ChangeArea(remote, stream, ciphers, conn, new PendingTransfer(travel.AreaHash16, 0, false, AtWaypoint: true), $"waypoint to {entry!.Name}");
    }

    // To the character's hideout (21-hideouts.md §3): "/hideout" (C→S 0x16 command 45) or the World panel's Hideout button
    // (C→S 0x17a). Its own instance (Auth re-enters the live one), arriving next to its waypoint.
    static async Task WarpToHideout(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string why)
    {
        if (HomeHideout(conn) is not { } hideout)
        {
            Console.WriteLine($"[gateway] {remote} {why}: no data/hideouts.json (run scripts/dev/extract-data.ps1)");
            return;
        }
        if (conn.Layout.Hash == hideout.AreaHash16) { Console.WriteLine($"[gateway] {remote} {why}: already in the {hideout.Name}"); return; }
        await ChangeArea(remote, stream, ciphers, conn, new PendingTransfer(hideout.AreaHash16, 0, false, AtWaypoint: true), $"{why}: {hideout.Name}");
    }

    static HideoutCatalog.Hideout? HomeHideout(ClientState conn) =>
        HideoutCatalog.Selected(CharacterSaves.Load(CharacterName(conn))?.SelectedHideout);

    static HideoutScene? HideoutFurniture(ClientState conn) => conn.Furniture ??=
        HideoutCatalog.ByArea(conn.Layout.Hash) is { } hideout && HideoutCatalog.Default is { } fallback
            ? HideoutEditing.Apply(HideoutScene.Build(hideout, fallback, conn.Layout.Walkable), SavedHideoutPlacements(conn)) : null;

    static async Task SelectHideout(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, (int Row, bool Guild) selected)
    {
        if (selected.Guild || conn.PendingTransfer is not null) return;
        var hideout = HideoutCatalog.Hideouts.FirstOrDefault(h => h.Row == selected.Row);
        if (hideout is null) return;
        CharacterSaves.SetSelectedHideout(CharacterName(conn), hideout.AreaId);
        conn.TalkingTo = 0;
        conn.InNpcTalk = false;
        await Send(remote, stream, ciphers, InstanceNpcs.Talk(0), "0x0084 close hideout dialogue");
        await Send(remote, stream, ciphers, InstanceEntities.PlayerHideoutsUpdate(PlayerId, hideout.Hash16, 0),
            $"0x02ba selected personal hideout: {hideout.Name}");
        Console.WriteLine($"[gateway] {CharacterName(conn)} selected hideout row {hideout.Row}: {hideout.AreaId}");
        await WarpToHideout(remote, stream, ciphers, conn, "Helena: Select Hideout");
    }

    // 0x2b2 now; the move itself (0x27) once the client acknowledges with 0x2cb.
    static async Task ChangeArea(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, PendingTransfer t, string why)
    {
        if (!conn.TryBeginAreaChange(t, out var projectiles)) return;
        await CancelPlayerTrade(conn, "left the area");
        // Remove outstanding visuals while the old world still exists, before 0x2b2. If Auth refuses the transfer,
        // their canceled tasks must not leave permanent projectile/explosion objects in the retained world.
        foreach (uint id in projectiles)
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(id), $"0x02bb remove projectile 0x{id:x} (leaving area)",
                still: () => !conn.Closed);
        await Send(remote, stream, ciphers, InstanceTransfer.ChangeArea(t.Area), $"0x02b2 change area to 0x{t.Area:x4} ({why})");
        // If the client never acknowledges, move it anyway after a moment.
        _ = Task.Delay(2000).ContinueWith(async _ => { if (conn.PendingTransfer is { } p) await Transfer(remote, stream, ciphers, conn, p); });
    }

    // The capture tour's next step (CaptureTour), after an area load (0x7d) or when /tour starts: at the tour's current
    // stop, wait for scripts/walkmaps to write it, then go to a new instance of the next (area, seed). A tour saved before
    // a restart (data/tour-left.txt) resumes at the character's first area load.
    static async Task ContinueTour(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, bool starting = false)
    {
        // Tours record with scripts/walkmaps: without it a saved tour (data/tour-left.txt) is left alone.
        if (!AreaLayouts.Recording) return;
        string character = CharacterName(conn);
        if (!starting && CaptureTour.Resume(character) is { } resumed)
        {
            Console.WriteLine($"[gateway] {remote} {resumed}");
            await conn.Commands!.Reply(resumed);
            starting = true;
        }
        if (!CaptureTour.Active(character)) return;
        if (!starting && CaptureTour.IsCurrent(character, conn.Layout.Id, conn.Seed))
        {
            CaptureTour.MarkArrived(character);
            bool ok = await CaptureTour.WaitForCapture(character, conn.Layout.Id, conn.Seed);
            Console.WriteLine($"[gateway] {remote} tour: {conn.Layout.Id} seed {conn.Seed} {(ok ? "captured" : "NOT captured (120 s; is live.ps1 -Walkmaps running?)")}");
            TourRecords.Write(conn.Layout.Id, conn.Seed, ok ? "captured" : "not captured", layout: conn.Layout);
        }
        // An area the tour did not send the player to, unless the tour waits for the client to come back.
        else if (!starting && !CaptureTour.Waiting(character)) return;
        string done = CaptureTour.Summary(character);
        if (CaptureTour.Next(character) is not var (area, hash, seed))
        {
            await conn.Commands!.Reply($"Tour finished: {done}.");
            return;
        }
        await ChangeArea(remote, stream, ciphers, conn, new PendingTransfer(hash, 0, true, AtSpawn: true) { Seed = seed }, $"tour: {area} seed {seed}");
        // A seed the client itself cannot generate ("Terrain generation failed" in Client/logs/Client.txt): the client
        // shows the exception and hangs. The stop goes to tour-failures.txt and the tour waits: nothing more is sent to
        // that client; after a restart (live.ps1) the next area load goes on.
        for (var until = DateTime.UtcNow.AddMinutes(10); DateTime.UtcNow < until && CaptureTour.IsCurrent(character, area, seed); await Task.Delay(2000))
            if (CaptureTour.Failed(character, area, seed) is { } failure)
            {
                Console.WriteLine($"[gateway] {remote} tour: {area} seed {seed} failed in the client ({failure}); waiting: restart the client with live.ps1 and log in, the tour goes on");
                return;
            }
    }
    // Auth moves the player and issues the destination ticket; 0x27 sends the client there (it reconnects to this Gateway).
    internal static async Task Transfer(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        PendingTransfer t, Func<ControlRequest, Task<ControlResponse>>? control = null)
    {
        lock (conn.Monsters)
        {
            if (!ReferenceEquals(conn.PendingTransfer, t)) return; // the acknowledgement and fallback timer can race
            conn.PendingTransfer = null;
        }
        if (!RetireRuthlessVitalsForTransfer(conn))
        {
            conn.ResumeAfterFailedTransfer();
            Console.WriteLine($"[gateway] {remote} transfer refused: Ruthless resources belong to a newer connection");
            return;
        }
        var r = await (control ?? (request => InstanceControl.CallAsync(request)))(new ControlRequest
        {
            Op = "transfer", Ticket = conn.Ticket, Area = t.Area, Instance = t.Instance, New = t.New, Seed = t.Seed,
        });
        t.Done?.Invoke(r);
        if (!r.Ok)
        {
            ResumeRuthlessVitalsAfterFailedTransfer(conn);
            conn.ResumeAfterFailedTransfer();
            Console.WriteLine($"[gateway] {remote} transfer failed: {r.Error}");
            return;
        }
        CarryCharges(conn, r.Ticket);
        CarryFortification(conn, r.Ticket);
        CarryRampage(conn, r.Ticket);
        CarryAntiquarian(conn, r.Ticket);
        CarryRage(conn, r.Ticket);
        CarryVaalSouls(conn, r.Ticket);
        conn.Retire();
        lock (conn.Monsters) LeechOf(conn).Clear(conn.Clock.Elapsed.TotalSeconds);
        conn.InvalidateIncomingHits(); // Recent enemy contacts never cross an area connection.
        if (t.AtWaypoint) WaypointArrivals[r.Ticket] = 0;
        else if (t.ArriveAt is { } spot) SpotArrivals[r.Ticket] = spot;
        else if (!t.AtSpawn) AreaLayouts.CameFrom[r.Ticket] = conn.Layout.Hash;
        string host = Environment.GetEnvironmentVariable("Game_GATEWAY_HOST") ?? "127.0.0.1";
        ushort port = ushort.TryParse(Environment.GetEnvironmentVariable("Game_GATEWAY_PORT"), out var gp) ? gp : (ushort)6112;
        await Send(remote, stream, ciphers, InstanceTransfer.ConnectInstance(host, port, r.Ticket, r.Area, conn.Keyring.Key(r.Ticket)),
            $"0x0027 connect to instance {r.Instance} of area 0x{r.Area:x4} (seed {r.Seed}) at {host}:{port}, ticket 0x{r.Ticket:x8}");
    }
}
