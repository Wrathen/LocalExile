using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using Progression = GameServer.Core.World.Progression;
using DamageModel = GameServer.Core.World.DamageModel;
using DamageType = GameServer.Core.World.DamageType;

partial class Program
{
    /// <summary>The most /item_tab adds of one item type at a time.</summary>
    internal const int ItemTabMax = 1000;

    /// <summary>The /item_tab tab: the one with that name (case ignored), else the only tab of a type whose name starts with
    /// it (e.g. "divination" → DivinationStash); null when none or several match.</summary>
    internal static int? ItemTabOf(AccountStash stash, string name)
    {
        var tabs = Enumerable.Range(0, stash.Count).ToList();
        var named = tabs.Where(t => string.Equals(stash.NameOf(t), name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (named.Count > 0) return named[0];
        var typed = tabs.Where(t => stash.TypeOf(t).StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList();
        return typed.Count == 1 ? typed[0] : null;
    }

    /// <summary>The chat commands' names and usages without a connection: building them only captures it in their
    /// handlers, none of which runs.</summary>
    internal static IEnumerable<(string Name, string Usage, string[] Arguments)> CommandUsages() => Commands("", null!, null!, null!).Usages;

    // /stash_place and the like: <x> <y> <degrees>.
    static (int X, int Y, float Degrees) PlaceArgs(string[] args, string usage) =>
        args.Length == 3 && int.TryParse(args[0], out int x) && int.TryParse(args[1], out int y) && float.TryParse(args[2],
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float degrees)
            ? (x, y, degrees) : throw new ArgumentException($"{usage} <x> <y> <degrees>");

    // A stationary object moved and turned in place (S→C 0x1b7 and the Positioned rotation update, as the hideout editor
    // does), with its path blocker; until the player leaves the area.
    static async Task MoveStationary(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint id,
        int x, int y, float degrees, int blockerSize, string what)
    {
        conn.SetBlocker(id, x, y, blockerSize);
        await Send(remote, stream, ciphers, InstanceProtocol.Teleport(id, x, y, 0x7fffffff), $"{what} 0x{id:x} to ({x}, {y})");
        await Send(remote, stream, ciphers, InstanceHideoutEditing.Rotation(id, degrees * MathF.PI / 180), $"{what} turned {degrees}°");
        await conn.Commands!.Reply($"{what} at ({x}, {y}), {degrees}°");
    }

    // The chat commands of one connection (ChatCommands.cs).
    static ChatCommands Commands(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        return new ChatCommands
        {
            Send = (bytes, log) => Send(remote, stream, ciphers, bytes, log),
            // S→C 0x17 chat lines in the player's chat panel, one per line of the text (an empty line as a space).
            Reply = async text =>
            {
                foreach (string line in text.Split('\n').Select(l => l.Length == 0 ? " " : l))
                    await Send(remote, stream, ciphers, InstanceChat.ChatLine(line), $"0x0017 chat line \"{line}\"");
            },
            ModCommand = (name, args) => ServerMods.RunCommand(PlayerOf(conn), name, args),
            ModUsages = () => ServerMods.Commands,
        }
        .Add("mod", "/mod (the mods in Mods/Binaries/: loaded ones with their version, kind and hooks, and why the others are not)", async _ =>
        {
            var mods = ServerMods.Mods();
            await conn.Commands!.Reply(mods.Count == 0 ? $"No mods in {ServerMods.Folder}/ (Agents/docs/dev/modding.md)."
                : string.Join('\n', mods.Select(m => m.Error is { } error ? $"{m.Id} - not loaded, {error}"
                    : $"{m.Id} {m.Version} - {m.Kind?.ToString().ToLowerInvariant()}, {m.Hooks} hook(s)").Prepend($"--- Mods in {ServerMods.Folder}/ ---")));
        })
        .Add("mod_reload", "/mod_reload [id] (reload one mod and the mods that depend on it from its folder, or every mod; their state starts over)", async args =>
        {
            string? id = args.Length > 0 ? args[0] : null;
            ServerMods.Reload(id);
            await conn.Commands!.Reply(id is null ? "Mods reloaded (/mod for the list)." : $"Mod {id} reloaded (/mod for the list).");
        })
        .Add("mod_enable", "/mod_enable <id> (load a mod, now and at every start)", args => SetModEnabled(conn, args, true))
        .Add("mod_disable", "/mod_disable <id> (unload a mod and the mods that depend on it, now and at every start)", args => SetModEnabled(conn, args, false))
        .Add("raw","/raw <hex> (send one S→C message)", async args =>
        {
            byte[] m = ChatCommands.ParseHex(args);
            await Send(remote, stream, ciphers, m, $"0x{m[0] << 8 | m[1]:x4} raw ({m.Length} bytes)");
        })
        .Add("set_speed", "/set_speed <percent> (100 = normal)", async args =>
        {
            int percent = int.Parse(args[0]);
            if (percent is < 10 or > 1000) throw new ArgumentException("10 to 1000");
            int totalPct = conn.Pools.MovementVelocityPct + percent - 100;
            conn.Walk.SetSpeed(Math.Max(1, WalkSpeed * (100 + totalPct) / 100 * PlayerActionFactor(conn)),
                conn.Clock.Elapsed.TotalSeconds);
            lock (conn.Monsters) conn.PlayerStats[StatKeys.MovementVelocityPct] = totalPct;
            await Send(remote, stream, ciphers, InstanceEntities.StatsUpdate(PlayerId, [(StatKeys.MovementVelocityPct, totalPct)]),
                $"0x01b3 stats: movement_velocity_+% (key {StatKeys.MovementVelocityPct}) = {totalPct} ({conn.Pools.MovementVelocityPct} passive + {percent - 100} dev)");
        })
        .Add("set_stat", "/set_stat <stat name or client key> <value|off> (off removes the dev replacement)", async args =>
        {
            uint stat = Stats.Key(args[0]) ?? throw new ArgumentException($"unknown stat {args[0]}");
            bool off = args[1].Equals("off", StringComparison.OrdinalIgnoreCase);
            int value = off ? 0 : int.Parse(args[1]);
            bool rawAttribute = AttributeModifiers.IsInput(Stats.Name(stat));
            string sourceName = Stats.Name(stat) ?? args[0];
            bool rebuildLife = LifeManaPoolSources.IsLifeInput(sourceName) || InherentAttributeBonuses.IsLifeInput(sourceName);
            bool rebuildMana = LifeManaPoolSources.IsManaInput(sourceName) || stat == Stats.Key(StrengthMana.PerFourStrength)
                || InherentAttributeBonuses.IsManaInput(sourceName) || stat == Stats.Key(PowerOfPurpose.RawFlag);
            bool rebuildDefences = AttributeDefenceSources.IsInput(sourceName) || InherentAttributeBonuses.IsInput(sourceName);
            bool rebuildEnergyShield = sourceName == AttributeDefenceSources.EnergyShieldPerTenStrength
                || InherentAttributeBonuses.IsEnergyShieldInput(sourceName);
            bool lifeClamped = false, manaClamped = false;
            lock (conn.Monsters)
            {
                if (off) conn.PlayerStats.Remove(stat);
                else conn.PlayerStats[stat] = value;
            }
            // Keep each supported raw consumer and source restoration when another mechanic changes this gate.
            if (AttackBaseCriticalStrike.IsInput(Stats.Name(stat) ?? args[0])
                || AccuracyRating.IsInput(Stats.Name(stat) ?? args[0])
                || ResistanceOvercap.IsInput(Stats.Name(stat) ?? args[0])
                || rebuildLife || rebuildMana || rebuildDefences || rawAttribute
                || Stats.Name(stat) == PlayerHitMitigation.ArmourAppliesToChaos)
            {
                while (true)
                {
                    CharacterStats.Pools before;
                    lock (conn.Monsters) before = conn.Pools;
                    var pools = PoolsFor(conn, before.Level);
                    lock (conn.Monsters)
                    {
                        // A concurrent level/aura rebuild wins; retry against its current inputs and level.
                        if (!ReferenceEquals(conn.Pools, before)) continue;
                        conn.Pools = pools;
                        if (rawAttribute || rebuildLife || rebuildMana)
                        {
                            uint lifeBefore = conn.PlayerLife, manaBefore = conn.PlayerMana;
                            conn.PlayerLife = Math.Min(conn.PlayerLife, conn.PlayerMaxLife);
                            conn.PlayerMana = Math.Min(conn.PlayerMana, (uint)UnreservedMana(conn));
                            lifeClamped = conn.PlayerLife != lifeBefore;
                            manaClamped = conn.PlayerMana != manaBefore;
                        }
                        break;
                    }
                }
            }
            // Action speed and its base-speed floors change the walk estimate immediately; attack/cast pacing reads the
            // same stat through PlayerActionFactor when the next action starts.
            if (Stats.Name(stat) is "action_speed_-%" or "action_speed_cannot_be_reduced_below_base"
                or "movement_speed_cannot_be_reduced_below_base")
                conn.ApplyMovementSpeed();
            // The client's requirement checks (gear and gems) read virtual_strength/dexterity/intelligence_for_requirements,
            // not the attributes themselves (13-items.md §2d), so an attribute set here sets its requirement value too.
            if (Stats.Key($"virtual_{args[0]}_for_requirements") is uint requirement && requirement != stat)
                lock (conn.Monsters)
                {
                    if (off) conn.PlayerStats.Remove(requirement);
                    else conn.PlayerStats[requirement] = value;
                }
            await SendPlayerStatSnapshot(remote, stream, ciphers, conn,
            () => PlayerStatsUpdateAtGate(conn, PlayerStatsWithCleared(conn)),
                $"0x01b3 stats: {Stats.Name(stat)} (key 0x{stat:x}) = {(off ? "the character's own" : value)}, and the attack's final stats");
            // Skills compute with their own stats (the attack and the character panel read those), so send them again.
            await SendSkills(remote, stream, ciphers, conn);
            if (rawAttribute)
            {
                // Skill refresh can restore an aura or update its reservation; use those completed caps too.
                lock (conn.Monsters)
                {
                    conn.PlayerLife = Math.Min(conn.PlayerLife, conn.PlayerMaxLife);
                    conn.PlayerMana = Math.Min(conn.PlayerMana, (uint)UnreservedMana(conn));
                }
                // A raw pool change can alter reservation while current mana stays idle. Sample resources only after
                // earlier queued sends, and never refill or resurrect a character when a maximum grows.
                await SendPlayerStatSnapshot(remote, stream, ciphers, conn,
                    () => InstanceProtocol.LifeUpdate(PlayerId, conn.PlayerLife), "raw attribute current life");
                await SendPlayerMana(remote, stream, ciphers, conn, "raw attribute mana reservation");
            }
            if (!rawAttribute && (rebuildLife || lifeClamped))
                await Send(remote, stream, ciphers, [], "0x01b1 life after raw source rebuild", atGate: () =>
                {
                    lock (conn.Monsters) return InstanceProtocol.LifeUpdate(PlayerId, conn.PlayerLife);
                });
            // Reservation can change with maximum mana even when recovery leaves current mana unchanged.
            if (!rawAttribute && (rebuildMana || manaClamped))
                await SendPlayerMana(remote, stream, ciphers, conn, "0x01b1 mana after raw source rebuild");
            if (rebuildEnergyShield)
                await SendPlayerEnergyShield(remote, stream, ciphers, conn);
        }, "stat")
        .Add("stat", "/stat <stat name or client key> (dev: current character stat value)", async args =>
        {
            if (args.Length != 1 || Stats.Key(args[0]) is not uint stat)
            {
                await conn.Commands!.Reply("Usage: /stat <stat name or client key>");
                return;
            }
            int value = CharacterPlayerStats(conn).GetValueOrDefault(stat);
            await conn.Commands!.Reply($"{args[0]} (key {stat}) = {value}");
        }, "stat")
        .Add("spawn_mob", "/spawn_mob <MonsterVarieties row, Id or name>[@level] [friendly] [normal|magic|rare|unique] [name]", async args =>
        {
            // "friendly": on the player's team (Positioned team 0); monsters are hostile (team 1) otherwise.
            bool friendly = args.Any(a => a.Equals("friendly", StringComparison.OrdinalIgnoreCase));
            args = [.. args.Where(a => !a.Equals("friendly", StringComparison.OrdinalIgnoreCase))];
            if (args.Length == 0) throw new ArgumentException("which monster");
            if (Monsters.Error is { } error) { await conn.Commands!.Reply(error); return; }
            // Words up to a rarity keyword name the monster; words after it are a custom name. Only the four names count:
            // Enum.TryParse would also accept "100" as a rarity number and send a byte the client cannot read (Buffer Underflow).
            int r = Array.FindIndex(args, a => a.ToLowerInvariant() is "normal" or "magic" or "rare" or "unique");
            var rarity = r < 0 ? MonsterRarity.Normal : Enum.Parse<MonsterRarity>(args[r], ignoreCase: true);
            string? name = r < 0 || r == args.Length - 1 ? null : string.Join(' ', args[(r + 1)..]);
            if (r == 0) throw new ArgumentException("which monster");
            string query = string.Join(' ', r < 0 ? args : args[..r]);
            byte level = 1;
            if (query.LastIndexOf('@') is int at and > 0)
            {
                level = byte.Parse(query[(at + 1)..]);
                if (level is < 1 or > 100) throw new ArgumentException("level 1 to 100");
                query = query[..at];
            }
            else if (r < 0 && args.Length > 1 && int.TryParse(args[^1], out int bare) && bare is >= 1 and <= 100)
            {
                // A bare number after the monster is the level most people mean (the syntax itself wants @).
                level = (byte)bare;
                query = query[..query.LastIndexOf(' ')];
            }
            if (Monsters.Find(query) is not { } variety) { await conn.Commands!.Reply($"No monster matches \"{query}\"."); return; }
            if (!variety.Supported)
            {
                await conn.Commands!.Reply($"{variety} is not supported yet: its type has components " +
                    $"{string.Join(", ", variety.ComponentsOrBase.Except(MonsterObject.BaseComponents))} that are not serialized.");
                return;
            }
            var (x, y) = conn.SpawnNear(conn.Walk.Position(conn.Clock.Elapsed.TotalSeconds));
            await SpawnMonster(remote, stream, ciphers, conn, variety, level, rarity, x, y, name, friendly);
        }, "monster")
        .Add("item", "/item <BaseItemTypes row, Id, name or unique> [count] [magic|rare] (into the inventory, e.g. /item Small Life Flask 2, /item Iron Ring rare, /item Mageblood)", async args =>
        {
            if (args.Length == 0) throw new ArgumentException("which item?");
            ItemRarity rolled = ItemRarity.Normal;
            if (args.Length > 1 && args[^1].ToLowerInvariant() is "magic" or "rare") { rolled = Enum.Parse<ItemRarity>(args[^1], true); args = args[..^1]; }
            int count = 1, requested = 1;
            if (args.Length > 1 && int.TryParse(args[^1], out int n)) { requested = n; count = Math.Clamp(n, 1, 60); args = args[..^1]; }
            if (ItemsFor(conn).Error is { } error) { await conn.Commands!.Reply(error); return; }
            string query = string.Join(' ', args);
            if (UniquesFor(conn).Find(query) is { } unique)
            {
                var uniqueItems = ItemsOf(conn);
                var uniqueAdded = new List<InventoryItem>();
                for (int i = 0; i < count; i++)
                {
                    if (UniquesFor(conn).Create(unique.Name, Random.Shared) is not { } createdUnique
                        || ItemsFor(conn).ByHash(createdUnique.TypeHash) is not { } uniqueType
                        || uniqueItems.FreeCell(PlayerInventories.MainInventory, uniqueType.Width, uniqueType.Height, ShapeOf) is not { } uniqueCell) break;
                    uniqueAdded.Add(uniqueItems.Put(PlayerInventories.MainInventory,
                        new InventoryItem(uniqueItems.NewItemId(), uniqueCell.X, uniqueCell.Y, createdUnique)));
                }
                if (uniqueAdded.Count == 0) { await conn.Commands!.Reply("No room in the inventory"); return; }
                await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                    uniqueItems.NextRevision(PlayerInventories.MainInventory), [], uniqueAdded),
                    $"0x00bf inventory 1: /item {unique.Name} x {uniqueAdded.Count}");
                await conn.Commands!.Reply($"Added {unique.Name}");
                return;
            }
            if (ItemsFor(conn).Find(query) is not { } type) { await conn.Commands!.Reply($"No item type \"{query}\""); return; }
            if (!type.Supported)
            {
                await conn.Commands!.Reply($"{type} has components the server cannot write yet: {string.Join(", ", type.Components.Where(c => !Item.Supported.Contains(c)))}");
                return;
            }
            var items = ItemsOf(conn);
            if (type.Stackable)
            {
                // A stackable type: [count] is how many, onto the stacks already there first, then into every free cell (at most
                // a full main inventory, 12 × 5 stacks); what doesn't fit is reported.
                int amount = Math.Clamp(requested, 1, type.MaxStack * 60);
                var stacked = items.AddStacked(PlayerInventories.MainInventory, ItemCatalog.Create(type) with { StackSize = (ushort)Math.Min(amount, ushort.MaxValue) }, ShapeOf);
                if (!stacked.Any) { await conn.Commands!.Reply("No room in the inventory"); return; }
                await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                    items.NextRevision(PlayerInventories.MainInventory), stacked.RemovedIds, stacked.AddedItems),
                    $"0x00bf inventory 1: /item {type} × {amount - stacked.Left} ({stacked.Changed.Count} stack(s) grown, {stacked.Added.Count} new)");
                if (stacked.Left > 0) await conn.Commands!.Reply($"No room for {stacked.Left} of them");
                return;
            }
            int itemLevel = Math.Clamp(conn.Pools.Level, 1, 100);
            var added = new List<InventoryItem>();
            for (int i = 0; i < count; i++)
            {
                if (items.FreeCell(PlayerInventories.MainInventory, type.Width, type.Height, ShapeOf) is not { } cell) break;
                var (sockets, links) = ItemsFor(conn).DefaultSockets(type, itemLevel);
                var created = ItemCatalog.Create(type, sockets, links) with { ItemLevel = (byte)itemLevel };
                if (rolled != ItemRarity.Normal) created = RollsFor(conn).Roll(created, rolled, Random.Shared);
                added.Add(items.Put(PlayerInventories.MainInventory, new InventoryItem(items.NewItemId(), cell.X, cell.Y, created)));
            }
            if (added.Count == 0) { await conn.Commands!.Reply("No room in the inventory"); return; }
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                items.NextRevision(PlayerInventories.MainInventory), [], added), $"0x00bf inventory 1: /item {type} × {added.Count}");
        }, "item")
        .Add("stash_place", "/stash_place <x> <y> <degrees> (dev: move and turn the stash of this area until you leave it; it opens from its new cell)", async args =>
        {
            if (conn.StashObject == 0) { await conn.Commands!.Reply("No stash in this area"); return; }
            var (x, y, degrees) = PlaceArgs(args, "/stash_place");
            conn.StashAt = (x, y);
            await MoveStationary(remote, stream, ciphers, conn, conn.StashObject, x, y, degrees, ClientState.StashBlockerSize, "stash");
        })
        .Add("guild_stash_place", "/guild_stash_place <x> <y> <degrees> (dev: move and turn the guild stash of this area until you leave it)", async args =>
        {
            if (conn.GuildStashObject == 0) { await conn.Commands!.Reply("No guild stash in this area"); return; }
            var (x, y, degrees) = PlaceArgs(args, "/guild_stash_place");
            conn.GuildStashAt = (x, y);
            await MoveStationary(remote, stream, ciphers, conn, conn.GuildStashObject, x, y, degrees, ClientState.StashBlockerSize, "guild stash");
        })
        .Add("delve_crawler", "/delve_crawler [x y degrees] (dev: create the Delve Crawler in the Azurite Mine Encampment, or move and turn the one there; a click on it opens the Subterranean Chart)", async args =>
        {
            if (conn.Layout.Hash is not (StashCatalog.DelveHub or InstanceDelve.MineArea)) { await conn.Commands!.Reply("Only in the Azurite Mine Encampment or mine"); return; }
            if (args.Length == 3 && int.TryParse(args[0], out int x) && int.TryParse(args[1], out int y) && float.TryParse(args[2],
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float degrees))
            {
                if (conn.DelveCrawlerObject == 0) await SpawnDelveCrawler(remote, stream, ciphers, conn);
                float radians = degrees * MathF.PI / 180;
                conn.DelveCrawlerAt = (x, y);
                await Send(remote, stream, ciphers, InstanceProtocol.Teleport(conn.DelveCrawlerObject, x, y, 0x7fffffff), $"Delve Crawler to ({x}, {y})");
                await Send(remote, stream, ciphers, InstanceHideoutEditing.Rotation(conn.DelveCrawlerObject, radians), $"Delve Crawler turned {degrees}°");
                foreach (uint part in conn.DelveCrawlerAttachments)
                    await Send(remote, stream, ciphers, InstanceProtocol.Teleport(part, x, y, 0x7fffffff), null);
                await BlockDelveCrawler(remote, stream, ciphers, conn, x, y, radians);
                await conn.Commands!.Reply($"Delve Crawler at ({x}, {y}), {degrees}°");
                return;
            }
            await SpawnDelveCrawler(remote, stream, ciphers, conn);
            await conn.Commands!.Reply($"Delve Crawler 0x{conn.DelveCrawlerObject:x} at {conn.DelveCrawlerAt}");
        })
        .Add("wp_place", "/wp_place [x y degrees] (dev: move and turn this area's waypoint; without arguments shows where it is)", async args =>
        {
            if (conn.Layout.Waypoint is not { } waypoint) { await conn.Commands!.Reply("This area has no waypoint"); return; }
            if (args.Length == 0)
            {
                await conn.Commands!.Reply($"Waypoint at ({waypoint.GridX}, {waypoint.GridY}), {waypoint.Rotation * 180 / MathF.PI:0.#}°");
                return;
            }
            if (args.Length != 3 || !int.TryParse(args[0], out int x) || !int.TryParse(args[1], out int y) || !float.TryParse(args[2],
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float degrees))
                throw new ArgumentException("/wp_place [x y degrees]");
            float radians = degrees * MathF.PI / 180;
            conn.MoveWaypoint(waypoint with { GridX = x, GridY = y, Rotation = radians });
            await Send(remote, stream, ciphers, InstanceProtocol.Teleport(WaypointCatalog.ObjectId, x, y, 0x7fffffff), $"waypoint to ({x}, {y})");
            await Send(remote, stream, ciphers, InstanceHideoutEditing.Rotation(WaypointCatalog.ObjectId, radians), $"waypoint turned {degrees}°");
            await conn.Commands!.Reply($"Waypoint at ({x}, {y}), {degrees}°");
        })
        .Add("wp_state", "/wp_state active <n> | bits all|none|area|<bit> | model <.ao> (dev: recreate this area's waypoint with StateMachine \"active\" n or another model, or send 0x4c with all, no, this area's or one bit, to find what lights it)", async args =>
        {
            if (conn.Layout.Waypoint is not { } waypoint) { await conn.Commands!.Reply("This area has no waypoint"); return; }
            string usage = "/wp_state active <n> | bits all|none|area|<bit> | model <.ao>";
            if (args.Length != 2) throw new ArgumentException(usage);
            if (args[0] == "bits")
            {
                int count = WaypointCatalog.Areas.Entries.Count;
                List<int> bits = args[1] switch
                {
                    "all" => [.. Enumerable.Range(0, InstanceWaypoints.BitsetLength * 8)],
                    "none" => [],
                    "area" => WaypointCatalog.Areas.BitOf(conn.Layout.Hash) is int own ? [own] : [],
                    var n when int.TryParse(n, out int bit) => [bit],
                    _ => throw new ArgumentException(usage),
                };
                await Send(remote, stream, ciphers, InstanceWaypoints.Waypoints(bits), $"0x004c waypoints: {bits.Count} bits (/wp_state, {count} waypoint areas)");
                await conn.Commands!.Reply($"0x4c sent with {bits.Count} bit(s)");
                return;
            }
            var changed = args[0] switch
            {
                "active" when int.TryParse(args[1], out int active) => waypoint with { Active = active },
                "model" => waypoint with { Model = args[1] },
                _ => throw new ArgumentException(usage),
            };
            conn.MoveWaypoint(changed);
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(WaypointCatalog.ObjectId), "0x02bb remove the waypoint (/wp_state)");
            await Send(remote, stream, ciphers, InstanceEntities.CreateObject(WaypointCatalog.ObjectId,
                InstanceEntities.ObjectTypeHash(WaypointObject.MetadataPath), changed.Blob()), $"0x02b9 create waypoint {changed} (/wp_state)");
            await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(WaypointCatalog.ObjectId), null);
            await conn.Commands!.Reply($"Waypoint recreated: active {changed.Active}, model {changed.Model}");
        })
        .Add("sulphite", "/sulphite <amount 0-65535> (dev: the character's Voltaxic Sulphite, saved and sent as the Delve data 0x228)", async args =>
        {
            if (args.Length != 1 || !ushort.TryParse(args[0], out ushort amount)) throw new ArgumentException("/sulphite <amount 0-65535>");
            CharacterSaves.SetSulphite(CharacterName(conn), amount);
            await Send(remote, stream, ciphers, PlayerDelveData(conn), $"0x0228 Delve data: {amount} sulphite (/sulphite)");
            await conn.Commands!.Reply($"Sulphite: {amount}");
        })
        .Add("item_tab", $"/item_tab <stash tab name> <count> (dev: up to count, at most {ItemTabMax}, more of every item type that has its own slot on that tab, as far as each slot holds, e.g. /item_tab Currency 1000)", async args =>
        {
            if (args.Length < 2 || !int.TryParse(args[^1], out int requested) || requested < 1)
                throw new ArgumentException("/item_tab <stash tab name> <count>, e.g. /item_tab Currency 1000");
            if (Items.Error is { } error) { await conn.Commands!.Reply(error); return; }
            int amount = Math.Min(requested, ItemTabMax);
            string name = string.Join(' ', args[..^1]);
            var stash = PersonalStashOf(conn);
            if (ItemTabOf(stash, name) is not int tab)
            {
                await conn.Commands!.Reply($"No stash tab \"{name}\"; tabs: {string.Join(", ", Enumerable.Range(0, stash.Count).Select(stash.NameOf))}");
                return;
            }
            if (stash.TabType(tab) == StashRules.Gem)
            {
                // The gem tab: one of every creatable gem variant (transfigured and Vaal too), red, green, blue then white,
                // actives before supports; one already there is not added again.
                var gems = new[] { 1, 2, 3, 4 }.SelectMany(colour => new[] { false, true }.SelectMany(support => Items.FullShopGems(colour, support)))
                    .Select(g => ItemCatalog.Create(g.Type) with { GemVariant = g.Variant.Index });
                var cells = stash.FillCells(tab, gems, i => $"{i.TypeHash:x8}:{i.GemVariant}", ShapeOf, out string gemWhy);
                if (cells is null) { await conn.Commands!.Reply($"Tab \"{stash.NameOf(tab)}\": {gemWhy}"); return; }
                if (cells.Added.Count > 0 && TabsSentOf(conn, stash).Contains(tab))
                    await Send(remote, stream, ciphers, InstanceItems.InventoryChange(stash.InventoryOf(tab), stash.NextRevision(tab), [], cells.Added),
                        $"0x00bf stash tab {tab}: /item_tab {stash.NameOf(tab)}, {cells.Added.Count} gem(s)");
                await conn.Commands!.Reply($"Tab \"{stash.NameOf(tab)}\" (GemStash): {cells.Added.Count} gem(s) added, one of each variant" +
                    (cells.Full > 0 ? $"; {cells.Full} already there" : "") + (cells.Skipped > 0 ? $"; {cells.Skipped} found no room" : ""));
                return;
            }
            Item? Create(uint hash) => Items.ByHash(hash) is { Supported: true } type ? ItemCatalog.Create(type) : null;
            if (stash.FillSlots(tab, amount, Create, ShapeOf, out string why) is not { } fill)
            {
                await conn.Commands!.Reply($"Tab \"{stash.NameOf(tab)}\": {why}");
                return;
            }
            var now = fill.Changed.Concat(fill.Added).ToList();
            if (now.Count > 0 && TabsSentOf(conn, stash).Contains(tab))
                await Send(remote, stream, ciphers, InstanceItems.InventoryChange(stash.InventoryOf(tab), stash.NextRevision(tab),
                    [.. fill.Changed.Select(i => i.Id)], now), $"0x00bf stash tab {tab}: /item_tab {stash.NameOf(tab)} {amount}, {now.Count} slot(s)");
            await conn.Commands!.Reply($"Tab \"{stash.NameOf(tab)}\" ({stash.TypeOf(tab)}): up to {amount} more of {now.Count} item type(s)" +
                (requested > amount ? $" (at most {ItemTabMax} at a time)" : "") +
                (fill.Full > 0 ? $"; {fill.Full} slot(s) already full" : "") +
                (fill.Skipped > 0 ? $"; {fill.Skipped} type(s) skipped (the server can't create them or has no cell for their slot)" : ""));
        })
        .Add("adorned", "/adorned [0-1000] (dev: The Adorned unique jewel into the inventory, corrupted, for passive-jewel testing; the normal item range is 0-100)", async args =>
        {
            int percent = args.Length > 0 && int.TryParse(args[0], out int value) ? Math.Clamp(value, 0, 1000) : 100;
            if (Items.Find("Crimson Jewel") is not { } baseType) { await conn.Commands!.Reply("The Crimson Jewel base type is missing"); return; }
            if (UniqueJewelVisuals.Of("The Adorned") is not ushort visual) { await conn.Commands!.Reply("The Adorned visual identity is missing"); return; }
            if (Items.Mods.ById("CorruptedMagicJewelModEffectUnique__1") is not { } mod) { await conn.Commands!.Reply("The Adorned modifier is missing"); return; }
            var items = ItemsOf(conn);
            if (items.FreeCell(PlayerInventories.MainInventory, 1, 1, ShapeOf) is not { } cell) { await conn.Commands!.Reply("No room in the inventory"); return; }
            var jewel = ItemCatalog.Create(baseType) with
            {
                Rarity = ItemRarity.Unique, Corrupted = true, ItemLevel = 20, VisualIdentity = visual,
                Mods = [new ItemMod(ModSection.Explicit, mod.Hash, [percent])],
            };
            var added = items.Put(PlayerInventories.MainInventory, new InventoryItem(items.NewItemId(), cell.X, cell.Y, jewel));
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                items.NextRevision(PlayerInventories.MainInventory), [], [added]), $"0x00bf inventory 1: /adorned {percent}%");
            await conn.Commands!.Reply($"The Adorned ({percent}%) added to the inventory");
        })
        .Add("unique_jewel", "/unique_jewel <name> | list (dev: a global unique jewel, max-rolled, into the inventory)", async args =>
        {
            if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                await conn.Commands!.Reply(string.Join(", ", UniqueJewelCatalogue.Names));
                return;
            }
            if (CreateUniqueJewel(string.Join(' ', args)) is not { } jewel)
            {
                await conn.Commands!.Reply($"No global unique jewel \"{string.Join(' ', args)}\"");
                return;
            }
            var items = ItemsOf(conn);
            if (items.FreeCell(PlayerInventories.MainInventory, 1, 1, ShapeOf) is not { } cell)
            {
                await conn.Commands!.Reply("No room in the inventory");
                return;
            }
            var added = items.Put(PlayerInventories.MainInventory, new InventoryItem(items.NewItemId(), cell.X, cell.Y, jewel));
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                items.NextRevision(PlayerInventories.MainInventory), [], [added]), $"0x00bf inventory 1: /unique_jewel {string.Join(' ', args)}");
            await conn.Commands!.Reply($"{string.Join(' ', args)} added to the inventory");
        })
        .Add("radius_jewel", "/radius_jewel <name> | list (dev: a radius unique jewel, max-rolled, into the inventory; pilot: the six attribute-conversion jewels)", async args =>
        {
            if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                await conn.Commands!.Reply(string.Join(", ", RadiusJewelCatalogue.Names));
                return;
            }
            if (RadiusJewelCatalogue.FindWithVariant(string.Join(' ', args)) is not { } found
                || RadiusJewelCatalogue.Create(ItemsFor(conn), found.Entry, found.Variant) is not { } jewel)
            {
                await conn.Commands!.Reply($"No radius unique jewel \"{string.Join(' ', args)}\"");
                return;
            }
            var items = ItemsOf(conn);
            if (items.FreeCell(PlayerInventories.MainInventory, 1, 1, ShapeOf) is not { } cell)
            {
                await conn.Commands!.Reply("No room in the inventory");
                return;
            }
            var added = items.Put(PlayerInventories.MainInventory, new InventoryItem(items.NewItemId(), cell.X, cell.Y, jewel));
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                items.NextRevision(PlayerInventories.MainInventory), [], [added]), $"0x00bf inventory 1: /radius_jewel {string.Join(' ', args)}");
            await conn.Commands!.Reply($"{string.Join(' ', args)} added to the inventory");
        })
        .Add("impossible_escape", "/impossible_escape <keystone> | list (dev: Impossible Escape for that keystone, into the inventory)", async args =>
        {
            if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                await conn.Commands!.Reply(string.Join(", ", ImpossibleEscapeCatalogue.Names));
                return;
            }
            string query = string.Join(' ', args);
            if (ImpossibleEscapeCatalogue.Create(ItemsFor(conn), query) is not { } jewel)
            {
                await conn.Commands!.Reply($"No Impossible Escape keystone \"{query}\"");
                return;
            }
            var items = ItemsOf(conn);
            if (items.FreeCell(PlayerInventories.MainInventory, 1, 1, ShapeOf) is not { } cell)
            {
                await conn.Commands!.Reply("No room in the inventory");
                return;
            }
            var added = items.Put(PlayerInventories.MainInventory, new InventoryItem(items.NewItemId(), cell.X, cell.Y, jewel));
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                items.NextRevision(PlayerInventories.MainInventory), [], [added]), $"0x00bf inventory 1: /impossible_escape {query}");
            bool keystoneAllocated = ImpossibleEscapeCatalogue.Find(query) is { } keystone
                && conn.Passives.Contains(keystone.GraphId);
            await conn.Commands!.Reply($"Impossible Escape ({query}) added to the inventory" +
                (keystoneAllocated ? "" : ". Allocate the chosen keystone first; the ring and bypass only apply around an allocated keystone."));
        })
        .Add("tattoo", "/tattoo <name> | list (dev: a tattoo currency item into the inventory)", async args =>
        {
            if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                await conn.Commands!.Reply(string.Join(", ", TattooCatalogue.Names));
                return;
            }
            string query = string.Join(' ', args);
            if (TattooCatalogue.Create(ItemsFor(conn), query) is not { } tattoo)
            {
                await conn.Commands!.Reply($"No tattoo \"{query}\"");
                return;
            }
            var items = ItemsOf(conn);
            if (items.FreeCell(PlayerInventories.MainInventory, 1, 1, ShapeOf) is not { } cell)
            {
                await conn.Commands!.Reply("No room in the inventory");
                return;
            }
            var added = items.Put(PlayerInventories.MainInventory, new InventoryItem(items.NewItemId(), cell.X, cell.Y, tattoo));
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                items.NextRevision(PlayerInventories.MainInventory), [], [added]), $"0x00bf inventory 1: /tattoo {query}");
            var entry = TattooCatalogue.Find(query)!;
            await conn.Commands!.Reply($"{entry.Name} added to the inventory (replaces {entry.Target}" +
                (entry.EventOnly ? "; event-only, not normally obtainable" : entry.DropDisabled ? "; drop-disabled" : "") + ")");
        })
        .Add("tattoo_apply", "/tattoo_apply <tattoo name> <node> (dev: apply a pilot tattoo to an allocated passive)", async args =>
        {
            if (args.Length < 2 || !ushort.TryParse(args[^1], out ushort node))
            {
                await conn.Commands!.Reply("Usage: /tattoo_apply <tattoo name> <node>");
                return;
            }
            string name = string.Join(' ', args.Take(args.Length - 1));
            string? error = await ApplyTattoo(remote, stream, ciphers, conn, name, node);
            await conn.Commands!.Reply(error ?? $"{name} applied to passive {node}");
        })
        .Add("journey_makanui", "/journey_makanui <node> [keystone] (dev: apply Journey Makanui, random or chosen)", async args =>
        {
            if (args.Length < 1 || !ushort.TryParse(args[0], out ushort node))
            {
                await conn.Commands!.Reply("Usage: /journey_makanui <node> [keystone name|graph id]");
                return;
            }
            JourneyMakanuiCatalogue.Entry? chosen = null;
            if (args.Length > 1)
            {
                chosen = JourneyMakanuiCatalogue.Find(string.Join(' ', args.Skip(1)));
                if (chosen is null)
                {
                    await conn.Commands!.Reply($"No keystone \"{string.Join(' ', args.Skip(1))}\"");
                    return;
                }
            }
            string? error = await ApplyTattoo(remote, stream, ciphers, conn, "Journey Tattoo of Makanui", node,
                chosenKeystone: chosen?.GraphId);
            await conn.Commands!.Reply(error ?? $"Journey Tattoo of Makanui applied to passive {node}" +
                (chosen is null ? " (random keystone)" : $" ({chosen.Name})"));
        })
        .Add("tattoo_remove", "/tattoo_remove <node> (dev: remove the tattoo from a passive)", async args =>
        {
            if (args.Length != 1 || !ushort.TryParse(args[0], out ushort node))
            {
                await conn.Commands!.Reply("Usage: /tattoo_remove <node>");
                return;
            }
            string? error = await RemoveTattoo(remote, stream, ciphers, conn, node);
            await conn.Commands!.Reply(error ?? $"Tattoo removed from passive {node}");
        })
        .Add("timeless", "/timeless <name> [seed] [conqueror] | list (dev: a Legion timeless jewel into the inventory; Elegant Hubris seeds are the displayed x20 values 2000-160000)", async args =>
        {
            if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                await conn.Commands!.Reply(string.Join("; ", TimelessJewelCatalog.All.Select(e =>
                    $"{e.Name} {e.DisplayedMinSeed}-{e.DisplayedMaxSeed}: " +
                    string.Join(", ", e.Conquerors.Select(c => c.Name)))));
                return;
            }
            var tokens = args.ToList();
            string? conquerorText = null;
            if (tokens.Count > 1)
            {
                string last = tokens[^1];
                if ((int.TryParse(last, out int index) && index is >= 1 and <= 3)
                    || TimelessJewelCatalog.All.Any(e => e.Conquerors.Any(c => c.Name.Equals(last, StringComparison.OrdinalIgnoreCase))))
                {
                    conquerorText = last;
                    tokens.RemoveAt(tokens.Count - 1);
                }
            }
            int? seed = null;
            if (tokens.Count > 1 && int.TryParse(tokens[^1], out int parsedSeed))
            {
                seed = parsedSeed;
                tokens.RemoveAt(tokens.Count - 1);
            }
            if (TimelessJewelCatalog.Find(string.Join(' ', tokens)) is not { } entry)
            {
                await conn.Commands!.Reply($"No timeless jewel \"{string.Join(' ', tokens)}\"");
                return;
            }
            if (seed is int asked && (asked < entry.DisplayedMinSeed || asked > entry.DisplayedMaxSeed
                || entry.DisplayMultiplier > 1 && asked % entry.DisplayMultiplier != 0))
            {
                await conn.Commands!.Reply($"{entry.Name} seed is {entry.DisplayedMinSeed}-{entry.DisplayedMaxSeed}" +
                    (entry.DisplayMultiplier > 1 ? $" in steps of {entry.DisplayMultiplier}" : ""));
                return;
            }
            int? conqueror = null;
            if (conquerorText is not null)
            {
                if (TimelessJewelCatalog.FindConqueror(entry, conquerorText) is not { } chosen)
                {
                    await conn.Commands!.Reply($"{entry.Name} conqueror is one of " +
                        string.Join(", ", entry.Conquerors.Select(c => c.Name)));
                    return;
                }
                conqueror = chosen.Index;
            }
            if (TimelessJewelCatalog.Create(ItemsFor(conn), entry.Name, seed, conqueror) is not { } jewel)
            {
                await conn.Commands!.Reply("The timeless jewel data is missing");
                return;
            }
            var items = ItemsOf(conn);
            if (items.FreeCell(PlayerInventories.MainInventory, 1, 1, ShapeOf) is not { } cell)
            {
                await conn.Commands!.Reply("No room in the inventory");
                return;
            }
            var added = items.Put(PlayerInventories.MainInventory, new InventoryItem(items.NewItemId(), cell.X, cell.Y, jewel));
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                items.NextRevision(PlayerInventories.MainInventory), [], [added]), $"0x00bf inventory 1: /timeless {entry.Name}");
            var spec = TimelessJewelCatalog.Read(jewel, ItemsFor(conn).Mods);
            await conn.Commands!.Reply($"{entry.Name} (seed {spec?.DisplayedSeed}, {spec?.ConquerorName}) added to the inventory");
        })
        .Add("voices", "/voices [1|3|5|7] (dev: the Voices unique Large Cluster Jewel with that many empty passives, into the inventory)", async args =>
        {
            int empty = args.Length > 0 && int.TryParse(args[0], out int value) ? value : 7;
            if (CreateVoices(empty) is not { } jewel)
            {
                await conn.Commands!.Reply("Voices needs 1, 3, 5 or 7 empty passives");
                return;
            }
            var items = ItemsOf(conn);
            if (items.FreeCell(PlayerInventories.MainInventory, 1, 1, ShapeOf) is not { } cell)
            {
                await conn.Commands!.Reply("No room in the inventory");
                return;
            }
            var added = items.Put(PlayerInventories.MainInventory, new InventoryItem(items.NewItemId(), cell.X, cell.Y, jewel));
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                items.NextRevision(PlayerInventories.MainInventory), [], [added]), $"0x00bf inventory 1: /voices {empty}");
            await conn.Commands!.Reply($"Voices ({empty} empty passive(s)) added to the inventory");
        })
        .Add("cluster_jewel", "/cluster_jewel <small|medium|large> [passives] [sockets] [theme mod] [notable mod ...] (dev: a non-unique cluster jewel with its enchantments, into the inventory; defaults 3/4/8 passives and 0/1/2 sockets)", async args =>
        {
            int size = args.Length > 0 ? args[0].ToLowerInvariant() switch
            {
                "small" => 0,
                "medium" => 1,
                "large" => 2,
                _ => -1,
            } : -1;
            if (size < 0)
            {
                await conn.Commands!.Reply("small, medium or large");
                return;
            }
            int passives = args.Length > 1 && int.TryParse(args[1], out int p) ? p : size switch { 2 => 8, 1 => 4, _ => 3 };
            int sockets = args.Length > 2 && int.TryParse(args[2], out int s) ? s : size switch { 2 => 2, 1 => 1, _ => 0 };
            if (CreateClusterJewel(size, passives, sockets, args.Length > 3 ? args[3] : null, args.Skip(4)) is not { } jewel)
            {
                await conn.Commands!.Reply("The cluster jewel data is missing");
                return;
            }
            var items = ItemsOf(conn);
            if (items.FreeCell(PlayerInventories.MainInventory, 1, 1, ShapeOf) is not { } cell)
            {
                await conn.Commands!.Reply("No room in the inventory");
                return;
            }
            var added = items.Put(PlayerInventories.MainInventory, new InventoryItem(items.NewItemId(), cell.X, cell.Y, jewel));
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                items.NextRevision(PlayerInventories.MainInventory), [], [added]), $"0x00bf inventory 1: /cluster_jewel {size} {passives} {sockets}");
            await conn.Commands!.Reply($"Cluster jewel ({args[0]}, {passives} passive(s), {sockets} socket(s)) added to the inventory");
        })
        .Add("corrupt", "/corrupt (dev: marks the held item corrupted, e.g. to make a corrupted magic jewel)", async _ =>
        {
            var items = ItemsOf(conn);
            if (items.Items(PlayerInventories.Cursor) is not [var held, ..]) { await conn.Commands!.Reply("Hold an item first"); return; }
            if (items.Change(PlayerInventories.Cursor, held.Id, it => it with { Corrupted = true }) is not { } changed) return;
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.Cursor),
                items.NextRevision(PlayerInventories.Cursor), [held.Id], [changed]), "0x00bf cursor: corrupted");
            await conn.Commands!.Reply("Corrupted");
        })
        .Add("gem", "/gem <name> [level] [quality] (a gem into the inventory at that level and quality, e.g. /gem Fireball 20 20, /gem Ice Nova of Frostbolts, /gem Vaal Fireball 5)", async args =>        {
            // The trailing numbers: the level, then the quality.
            var numbers = new List<int>();
            while (args.Length > 1 && numbers.Count < 2 && int.TryParse(args[^1], out int n)) { numbers.Insert(0, n); args = args[..^1]; }
            if (args.Length == 0) throw new ArgumentException("which gem?");
            if (ItemsFor(conn).Error is { } error) { await conn.Commands!.Reply(error); return; }
            string query = string.Join(' ', args);
            if (ItemsFor(conn).FindGem(query) is not var (type, variant) || ItemsFor(conn).Gem(type) is not { } gem) { await conn.Commands!.Reply($"No gem \"{query}\""); return; }
            if (!type.Supported)
            {
                await conn.Commands!.Reply($"{variant.Name} has components the server cannot write yet: {string.Join(", ", type.Components.Where(c => !Item.Supported.Contains(c)))}");
                return;
            }
            int level = numbers.Count > 0 ? numbers[0] : 1, quality = numbers.Count > 1 ? numbers[1] : 0;
            // Corruption (one level over the cap, quality up to 23%) is not modelled yet: the uncorrupted limits.
            if (level < 1 || level > gem.MaxLevel) { await conn.Commands!.Reply($"{variant.Name} has levels 1 to {gem.MaxLevel}"); return; }
            if (quality is < 0 or > 20) { await conn.Commands!.Reply("Quality is 0 to 20"); return; }
            var items = ItemsOf(conn);
            if (items.FreeCell(PlayerInventories.MainInventory, type.Width, type.Height, ShapeOf) is not { } cell) { await conn.Commands!.Reply("No room in the inventory"); return; }
            var created = ItemCatalog.Create(type) with { GemExperience = gem.ExperienceFor(level), GemVariant = variant.Index, Quality = (byte)quality };
            var added = items.Put(PlayerInventories.MainInventory, new InventoryItem(items.NewItemId(), cell.X, cell.Y, created));
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                items.NextRevision(PlayerInventories.MainInventory), [], [added]),
                $"0x00bf inventory 1: /gem {variant.Name} (variant {variant.Index}) level {level}, {quality}% quality, experience {created.GemExperience}");
        }, "gem")
        .Add("gem_xp", "/gem_xp <amount|near> (dev: set the experience of every gem in the inventory and the sockets; near = one below the next level's threshold)", async args =>
        {
            if (args.Length != 1) throw new ArgumentException("an amount, or near");
            bool near = args[0].Equals("near", StringComparison.OrdinalIgnoreCase);
            int amount = near ? 0 : int.Parse(args[0]);
            var items = ItemsOf(conn);
            int Set(Item gem, ItemCatalog.GemType type)
            {
                int level = ItemsFor(conn).GemLevel(gem);
                int max = Math.Min(SkillsFor(conn).MaxLevelAt(type.Variant(gem.GemVariant).Effect, conn.Pools.Level), type.MaxLevel);
                return near ? (level < max ? type.ExperienceFor(level + 1) - 1 : gem.GemExperience) : amount;
            }
            var changed = new List<(byte Row, InventoryItem Item)>();
            foreach (var held in items.Items(PlayerInventories.MainInventory))
                if (ItemsFor(conn).ByHash(held.Item.TypeHash) is { } baseType && ItemsFor(conn).Gem(baseType) is { } type)
                    if (items.Change(PlayerInventories.MainInventory, held.Id, it => it with { GemExperience = Set(it, type) }) is { } after)
                        changed.Add((PlayerInventories.MainInventory, after));
            foreach (var (row, item, _) in items.ChangeSocketedGems((_, gem) =>
                ItemsFor(conn).ByHash(gem.TypeHash) is { } baseType && ItemsFor(conn).Gem(baseType) is { } type ? gem with { GemExperience = Set(gem, type) } : null))
                changed.Add((row, item));
            if (changed.Count == 0) { await conn.Commands!.Reply("No gem to change"); return; }
            foreach (var row in changed.GroupBy(c => c.Row))
                await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(row.Key), items.NextRevision(row.Key),
                    [.. row.Select(c => c.Item.Id)], [.. row.Select(c => c.Item)]),
                    $"0x00bf inventory {row.Key}: /gem_xp {(near ? "near" : amount.ToString())} ({string.Join(", ", row.Select(c => $"{c.Item.Id:x} exp {c.Item.Item.GemExperience}"))})");
        })
        .Add("map_portal", "/map_portal [type 1-4] [created 0|1] [open after ms, -1 never] [model 0-2] (dev: one MultiplexPortal next to you, listed in the map device's 0x8f)", async args =>
        {
            if (args.Length > 3) conn.MapPortalStyle = byte.Parse(args[3]);
            byte type = args.Length > 0 ? byte.Parse(args[0]) : MapPortalObject.MapDevicePortal;
            byte created = args.Length > 1 ? byte.Parse(args[1]) : (byte)1;
            int openMs = args.Length > 2 ? int.Parse(args[2]) : -1;
            var (x, y) = conn.SpawnNear(conn.Walk.Position(conn.Clock.Elapsed.TotalSeconds));
            uint id = conn.NextObjectId++;
            await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(MapPortalObject.MetadataPath),
                new MapPortalObject(x, y, 0, type, created, Rotation: conn.PortalRotationRadians).Blob()),
                $"0x02b9 create test portal 0x{id:x} type {type}, transitionable {created}, at ({x}, {y})");
            await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), null);
            lock (conn.MapPortals) conn.MapPortals[id] = (x, y, -1);
            if (openMs >= 0) OpenMapPortalLater(remote, stream, ciphers, conn, id, openMs);
            uint deviceId;
            lock (conn.HideoutDoodads) deviceId = conn.HideoutDoodads.Keys.FirstOrDefault();
            if (deviceId != 0) SendMapPortalsLater(remote, stream, ciphers, conn, deviceId, MapDevice.OpenOf(CharacterName(conn))?.Area.Hash16 ?? conn.Layout.Hash, MapPortalListMs);
            await conn.Commands!.Reply($"Portal 0x{id:x}: type {type}, created {created}, " + (openMs >= 0 ? $"opens after {openMs} ms" : "not opened"));
        })
        .Add("cosmetic", "/cosmetic portal <name, number or part of a name> | /cosmetic list [filter] | /cosmetic off (the portal cosmetic: the shop effect replaces the base portal visual)", async args =>
        {
            if (args.Length == 0)
            {
                await conn.Commands!.Reply($"{Cosmetics.Portals.Count} portal effects. " +
                    "/cosmetic list <part of a name> lists the ones that have it, /cosmetic portal <number or name> applies one.");
                return;
            }
            string what = args[0].ToLowerInvariant();
            if (what is not ("list" or "portal" or "off")) throw new ArgumentException("list, portal or off");
            if (what == "off")
            {
                conn.CosmeticPortalVariation = 0;
                CharacterSaves.SetPortalCosmetic(CharacterName(conn), 0);
                bool back = await ResendMapPortals(remote, stream, ciphers, conn);
                await conn.Commands!.Reply(back
                    ? "Portal cosmetic off: the open portals are back to the base effect."
                    : "Portal cosmetic off: the next portal uses the base effect.");
                return;
            }
            string filter = string.Join(' ', args.Skip(1).Where(a => !a.Equals("portal", StringComparison.OrdinalIgnoreCase)));
            if (what == "list")
            {
                var shown = filter.Length == 0 ? Cosmetics.Portals : CosmeticCatalog.Matches(Cosmetics.Portals, filter);
                await conn.Commands!.Reply(shown.Count == 0
                    ? Cosmetics.Portals.Count == 0 ? "No portal effects: data/tables is missing." : $"No portal effects match \"{filter}\"."
                    : $"{shown.Count} portal effects: " + string.Join(", ", shown.Take(12).Select(c => c.ToString())) + (shown.Count > 12 ? ", ..." : ""));
                return;
            }
            if (filter.Length == 0)
            {
                // Nothing named: the first page of the list, so the numbers that pick one are right there.
                await conn.Commands!.Reply($"{Cosmetics.Portals.Count} portal effects: " + string.Join(", ", Cosmetics.Portals.Take(12)) +
                    (Cosmetics.Portals.Count > 12 ? ", ..." : "") + " — use a number, a name or a piece of one.");
                return;
            }
            var matches = CosmeticCatalog.Matches(Cosmetics.Portals, filter);
            if (matches.Count == 0) throw new ArgumentException($"no portal effect matches \"{filter}\" (try /cosmetic list {filter})");
            if (matches.Count > 1)
            {
                // Several contain what was typed: name them with the numbers that pick one, rather than guessing which was
                // meant. The next /cosmetic with one of the numbers (or a longer fragment) applies it.
                await conn.Commands!.Reply($"{matches.Count} portal effects match \"{filter}\": " +
                    string.Join(", ", matches.Take(12).Select(c => c.ToString())) + (matches.Count > 12 ? ", ..." : "") +
                    ". Add more of the name, or use one of the numbers.");
                return;
            }
            var cosmetic = matches[0];
            // A portal cosmetic is the portal's own variation (+0x26 of its 0x008f entry), not a second object: the client
            // swaps the base portal effect for the shop one.
            conn.CosmeticPortalVariation = (ushort)cosmetic.Index;
            CharacterSaves.SetPortalCosmetic(CharacterName(conn), conn.CosmeticPortalVariation);   // survives a relog
            bool applied = await ResendMapPortals(remote, stream, ciphers, conn);
            await conn.Commands!.Reply($"portal: {cosmetic.Name} (variation {cosmetic.Index})" +
                (applied ? ", applied to the open portals" : ", the next portal uses it"));
        })
        .Add("portal_rotation", "/portal_rotation <degrees> (dev: turn the map portals around the vertical axis; rebuilds the ones standing)", async args =>
        {
            if (args.Length == 0) throw new ArgumentException("degrees");
            conn.PortalRotationDegrees = float.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture);
            int rebuilt = await RebuildMapPortals(remote, stream, ciphers, conn);
            await conn.Commands!.Reply(rebuilt == 0
                ? $"Map portals will be created at {conn.PortalRotationDegrees:0.#}°."
                : $"{rebuilt} map portal(s) rebuilt at {conn.PortalRotationDegrees:0.#}°.");
        })
        .Add("item_mod", "/item_mod [implicit|explicit|enchant|scourge|hidden] <Mods row, Id or part of it> [values…] | /item_mod set [section] <mod> [values…] | /item_mod clear [section] (the held item; missing values are rolled)", async args =>
        {
            if (args.Length == 0) throw new ArgumentException("which mod?");
            ModSection? section = null;
            if (args[0].Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                args = args[1..];
                if (args.Length == 0) throw new ArgumentException("which mod?");
                ModSection? setSection = null;
                if (args.Length > 1 && ParseSection(args[0]) is { } parsed) { setSection = parsed; args = args[1..]; }
                if (ItemsFor(conn).Mods.Find(args[0]) is not { } setMod) { await conn.Commands!.Reply($"No mod \"{args[0]}\" in Mods.dat"); return; }
                if (ItemsOf(conn).Items(PlayerInventories.Cursor) is not [var held, ..]) { await conn.Commands!.Reply("Hold an item first"); return; }
                var setValues = args[1..].Select(int.Parse).ToList();
                var replacement = setMod.With(setSection ?? setMod.DefaultSection, setValues, Random.Shared);
                int existing = held.Item.Mods.Select((m, index) => (m, index))
                    .Where(p => p.m.Hash == replacement.Hash && (setSection is null || p.m.Section == setSection))
                    .Select(p => p.index).DefaultIfEmpty(-1).First();
                if (existing < 0) { await conn.Commands!.Reply($"The held item has no {setMod.Id}; use /item_mod to add it"); return; }
                var targetSection = held.Item.Mods[existing].Section;
                replacement = replacement with { Section = targetSection };
                await ChangeHeldItem(remote, stream, ciphers, conn, i =>
                {
                    var mods = i.Mods.ToList();
                    int index = mods.FindIndex(m => m.Hash == replacement.Hash && m.Section == targetSection);
                    if (index >= 0) mods[index] = replacement;
                    return i with { Mods = mods };
                }, $"set {ItemsFor(conn).Mods.Describe(replacement)}");
                return;
            }
            if (args.Length > 1 && ParseSection(args[0]) is { } s) { section = s; args = args[1..]; }
            if (args[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                var only = args.Length > 1 ? ParseSection(args[1]) : section;
                await ChangeHeldItem(remote, stream, ciphers, conn, i => i with { Mods = [.. i.Mods.Where(m => only is { } o && m.Section != o)] },
                    $"mods cleared{(only is { } o ? $" ({o})" : "")}");
                return;
            }
            if (ItemsFor(conn).Mods.Find(args[0]) is not { } mod) { await conn.Commands!.Reply($"No mod \"{args[0]}\" in Mods.dat"); return; }
            var values = args[1..].Select(int.Parse).ToList();
            var added = mod.With(section ?? mod.DefaultSection, values, Random.Shared);
            await ChangeHeldItem(remote, stream, ciphers, conn, i => i with { Mods = [.. i.Mods, added] }, $"+ {ItemsFor(conn).Mods.Describe(added)}");
        })
        .Add("roll", "/roll <magic|rare> [item level] (new affixes and name for the held item)", async args =>
        {
            if (args.Length == 0 || !Enum.TryParse<ItemRarity>(args[0], true, out var rarity) || rarity is not (ItemRarity.Magic or ItemRarity.Rare))
                throw new ArgumentException("magic or rare");
            byte? level = args.Length > 1 ? byte.Parse(args[1]) : null;
            await ChangeHeldItem(remote, stream, ciphers, conn, i => RollsFor(conn).Roll(level is byte l ? i with { ItemLevel = l } : i, rarity, Random.Shared),
                $"rolled {rarity}");
        })
        .Add("item_mods", "/item_mods (the held item's rarity and mods)", async args =>
        {
            if (ItemsOf(conn).Items(PlayerInventories.Cursor) is not [var held, ..]) { await conn.Commands!.Reply("Hold an item first"); return; }
            await conn.Commands!.Reply($"{(held.Item.NameWords.Count > 0 ? RollsFor(conn).NameOf(held.Item.NameWords) + ", " : "")}" +
                $"{ItemsFor(conn).ByHash(held.Item.TypeHash)?.Name} ilvl {held.Item.ItemLevel}, {held.Item.Rarity}" +
                $"{(held.Item.Identified ? "" : ", unidentified")}: " +
                (held.Item.Mods.Count == 0 ? "no mods" : string.Join("; ", held.Item.Mods.Select(ItemsFor(conn).Mods.Describe))));
        })
        .Add("base_defence", "/base_defence [0 to 31] (show or set the held armour's shared defence base roll)", async args =>
        {
            if (ItemsOf(conn).Items(PlayerInventories.Cursor) is not [var held, ..])
            { await conn.Commands!.Reply("Hold an armour item first"); return; }
            if (!held.Item.Components.Contains("Armour"))
            { await conn.Commands!.Reply("The held item has no Armour component"); return; }
            if (args.Length == 0)
            { await conn.Commands!.Reply($"Defence base roll: {held.Item.DefenceBaseRoll} (ordinary range 0 to 31)"); return; }
            if (args.Length != 1 || !byte.TryParse(args[0], out byte roll) || roll > Item.MaximumDefenceBaseRoll)
                throw new ArgumentException("defence base roll 0 to 31");
            await ChangeHeldItem(remote, stream, ciphers, conn, i => i with { DefenceBaseRoll = roll },
                $"defence base roll {roll} / {Item.MaximumDefenceBaseRoll}", "Armour");
        })
        .Add("rarity", "/rarity <normal|magic|rare|unique> [unid] (the held item)", async args =>
        {
            if (args.Length == 0 || !Enum.TryParse<ItemRarity>(args[0], true, out var rarity)) throw new ArgumentException("normal, magic, rare or unique");
            bool identified = !args.Skip(1).Any(a => a.StartsWith("unid", StringComparison.OrdinalIgnoreCase));
            await ChangeHeldItem(remote, stream, ciphers, conn, i => i with { Rarity = rarity, Identified = identified },
                $"{rarity}{(identified ? "" : ", unidentified")}");
        })
        .Add("sockets", "/sockets [colours] (the held item's sockets: R G B W, linked within a word, e.g. /sockets RRGBBB a 6-link, /sockets RRG B a 3-link and a single; without colours, shows them; Game_SOCKET_CAPACITY=1..6 lets any socketable base take that many, e.g. a 6-link wand)", async args =>
        {
            if (ItemsOf(conn).Items(PlayerInventories.Cursor) is not [var held, ..]) { await conn.Commands!.Reply("Hold an item first"); return; }
            if (args.Length == 0) { await conn.Commands!.Reply($"sockets: {SocketLayout.Describe(held.Item)}"); return; }
            if (ItemsFor(conn).ByHash(held.Item.TypeHash) is not { } type) { await conn.Commands!.Reply("Unknown item type"); return; }
            if (SocketLayout.Parse(args, out string? error) is not var (colours, links)) throw new ArgumentException(error);
            int capacity = SocketLayout.CapacityOverride;
            if (SocketLayout.Apply(held.Item, type, colours, links, out error, capacity) is not { } result) { await conn.Commands!.Reply(error!); return; }
            await ChangeHeldItem(remote, stream, ciphers, conn, i => SocketLayout.Apply(i, type, colours, links, out _, capacity) ?? i,
                $"sockets: {SocketLayout.Describe(result)}", "Sockets");
        })
        .Add("set_level", "/set_level <1 to 100> (saved to the account; the level's experience and stats are sent at once)", async args =>
        {
            if (args.Length == 0 || !int.TryParse(args[0], out int level) || level is < 1 or > 100) throw new ArgumentException("level 1 to 100");
            await SetLevel(remote, stream, ciphers, conn, level);
            var r = await SynchronizeAccountLevel(conn);
            if (!r.Ok) await conn.Commands!.Reply($"Level saved locally; account update failed: {r.Error}");
        })
        .Add("add_xp", "/add_xp <amount> (experience as from a kill: levels up, saved)", async args =>
        {
            if (args.Length == 0 || !uint.TryParse(args[0], out uint xp) || xp == 0) throw new ArgumentException("an amount of experience");
            await GainExperience(remote, stream, ciphers, conn, xp, "/add_xp");
        })
        .Add("passives", "/passives (passive skill points and the allocated passives)", async _ =>
        {
            var (points, refundPoints, nodes, _) = PassiveState(conn);
            int unspent = points + PassivesFor(conn).PointsGranted(nodes) + AscendancyOrdinaryPoints(conn) - nodes.Count;
            await conn.Commands!.Reply($"Level {conn.Pools.Level}, {conn.Experience} experience: {points} passive skill point(s), {unspent} unspent, {refundPoints} refund point(s).\n" +
                string.Join("\n", nodes.Select(n => $"{n} {PassivesFor(conn).Find(n)?.Name}").DefaultIfEmpty("No passives allocated.")));
        })
        .Add("ascendancy", "/ascendancy [normal|phrecia] [name] (list or choose; Phrecia play is gated pending client compatibility)", async args =>
        {
            byte baseClass = (byte)Skills.ClassFor(conn.ClassRow).IntegerId;
            var choices = Ascendancies.Choices(baseClass).ToArray();
            if (choices.Length == 0) throw new ArgumentException("ascendancy data is missing; run scripts/dev/extract-data.ps1");
            if (args.Length == 0)
            {
                var current = AscendancyRules.Validate(baseClass, conn.MainAscendancy);
                int spent = AscendancyRules.SpentPoints(baseClass, conn.MainAscendancy);
                await conn.Commands!.Reply($"Main ascendancy - {current?.Name ?? "none"}. Ascendancy points {spent}/{conn.MainAscendancy.EarnedPoints}.\n" +
                    string.Join("\n", choices.Select(c => $"/ascendancy {c.Identity.Family.ToString().ToLowerInvariant()} {c.Name}{(c.Identity.Family == AscendancyFamily.Phrecia ? " (play gated)" : "")}")));
                return;
            }
            bool hasFamily = Enum.TryParse<AscendancyFamily>(args[0], true, out var family) && Enum.IsDefined(family);
            string name = string.Join(' ', hasFamily ? args[1..] : args);
            var matching = choices.Where(c => (!hasFamily || c.Identity.Family == family) &&
                (c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || c.Identity.Id.Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (matching.Length != 1) throw new ArgumentException("choose one name from /ascendancy, including its normal/phrecia family when needed");
            var chosen = matching[0];
            AscendancyClientCompatibility.RequirePlayable(chosen.Identity);
            if (conn.MainAscendancy.Selection == chosen.Identity)
            {
                await conn.Commands!.Reply($"{chosen.Name} is already your main ascendancy.");
                return;
            }
            var next = Saves.Atomically(() =>
            {
                var saved = CharacterSaves.Load(CharacterName(conn));
                return CharacterSaves.ChangeMainAscendancy(CharacterName(conn), latest =>
                {
                    var candidate = AscendancyRules.Select(baseClass, latest, chosen.Identity);
                    ValidateOrdinaryPassives(conn, saved?.Passives ?? [], candidate);
                    return candidate;
                });
            });
            lock (conn.Monsters)
            {
                conn.MainAscendancy = next;
                conn.SavedPassiveProgress = new(next, conn.Passives, conn.MasteryEffects, conn.RefundPoints);
            }
            await conn.Commands!.Reply($"Main ascendancy selected - {chosen.Name}. Reloading the area to show its tree.");
            await ChangeArea(remote, stream, ciphers, conn, new PendingTransfer(conn.Layout.Hash, 0, false), $"ascendancy {chosen.Name}");
        })
        .Add("ascendancy_points", "/ascendancy_points <normal|cruel|merciless|eternal|all> (dev: grant each Labyrinth's two ascendancy points once)", async args =>
        {
            if (args.Length != 1) throw new ArgumentException("a Labyrinth tier or all");
            string tier = args[0].ToLowerInvariant();
            byte baseClass = (byte)Skills.ClassFor(conn.ClassRow).IntegerId;
            var next = CharacterSaves.ChangeMainAscendancy(CharacterName(conn), latest =>
            {
                foreach (string award in tier == "all" ? global::AscendancyRules.AwardKeys : [tier])
                    latest = AscendancyRules.Award(baseClass, latest, award);
                return latest;
            });
            lock (conn.Monsters)
            {
                conn.MainAscendancy = next;
                conn.SavedPassiveProgress = new(next, conn.Passives, conn.MasteryEffects, conn.RefundPoints);
            }
            await SendPassives(remote, stream, ciphers, conn);
            await conn.Commands!.Reply($"Ascendancy points earned - {next.EarnedPoints}; each tier can be earned once.");
        })
        .Add("reset_ascendancy", "/reset_ascendancy (refund all ascendancy nodes; five refund points per node/effect, choice shells are free)", async _ =>
        {
            if (conn.MainAscendancy.Allocated.Count == 0) { await conn.Commands!.Reply("No ascendancy passives allocated."); return; }
            var error = await ChangePassives(remote, stream, ciphers, conn, conn.MainAscendancy.Allocated.ToArray(), false, "/reset_ascendancy", refundPoints: true);
            await conn.Commands!.Reply(error ?? "Ascendancy passives refunded; you can choose another main ascendancy.");
        })
        .Add("gold", "/gold <amount> (dev: add gold, or take it with a negative amount; the account's, saved)", async args =>
        {
            if (args.Length == 0 || !long.TryParse(args[0], out long delta)) throw new ArgumentException("an amount of gold (negative takes gold)");
            uint gold = PersonalStashOf(conn).AddGold(delta);
            await Send(remote, stream, ciphers, InstanceEntities.Gold(gold), $"0x02a9 gold: {gold} (/gold {delta})");
            await conn.Commands!.Reply($"Gold: {gold}");
        })
        .Add("add_passive_points", "/add_passive_points <n> (dev: add n extra passive skill points, saved)", async args =>
        {
            if (args.Length == 0 || !int.TryParse(args[0], out int add) || add <= 0) throw new ArgumentException("a positive number");
            ushort total;
            lock (conn.Monsters)
            {
                total = Saves.Atomically(() =>
                {
                    ushort current = CharacterSaves.Load(CharacterName(conn))?.ExtraPassivePoints ?? 0;
                    ushort next = (ushort)Math.Min(ushort.MaxValue, (long)current + add);
                    CharacterSaves.SetExtraPassivePoints(CharacterName(conn), next);
                    return next;
                });
                conn.ExtraPassivePoints = total;
            }
            await SendPassives(remote, stream, ciphers, conn);
            await conn.Commands!.Reply($"{add} extra passive point(s) added; {total} extra total.");
        })
        .Add("add_refund_points", "/add_refund_points <n> (dev: add n refund points, saved)", async args =>
        {
            if (args.Length == 0 || !int.TryParse(args[0], out int add) || add <= 0) throw new ArgumentException("a positive number");
            ushort total = Saves.Atomically(() =>
            {
                ushort next = (ushort)Math.Min(ushort.MaxValue, (CharacterSaves.Load(CharacterName(conn))?.RefundPoints ?? 0) + add);
                CharacterSaves.SetRefundPoints(CharacterName(conn), next);
                return next;
            });
            lock (conn.Monsters)
            {
                conn.RefundPoints = total;
                conn.SavedPassiveProgress = new(conn.MainAscendancy, conn.Passives, conn.MasteryEffects, total);
            }
            await SendPassives(remote, stream, ciphers, conn);
            await conn.Commands!.Reply($"{add} refund point(s) added; {total} total.");
        })
        .Add("add_passive", "/add_passive <PassiveSkillGraphId> [...] (dev: allocate a connected set of passive nodes)", async args =>
        {
            if (args.Length == 0 || args.Any(a => !ushort.TryParse(a, out _))) throw new ArgumentException("PassiveSkillGraphId numbers");
            ushort[] nodes = [.. args.Select(ushort.Parse)];
            string? error = await ChangePassives(remote, stream, ciphers, conn, nodes, allocate: true, "/add_passive");
            await conn.Commands!.Reply(error ?? $"Passives {string.Join(", ", nodes)} allocated.");
        })
        .Add("reset_passives", "/reset_passives (refunds every passive, saved)", async _ =>
        {
            var (_, _, nodes, _) = PassiveState(conn);
            await ChangePassives(remote, stream, ciphers, conn, nodes, allocate: false, "/reset_passives");
            await conn.Commands!.Reply($"{nodes.Count} passive(s) refunded.");
        })
        .Add("resistances", "/resistances (your uncapped and capped fire/cold/lightning/chaos resistance)", async _ =>
        {
            var p = conn.Pools;
            int penalty = ResistancePenalty(conn);
            int fire = p.FireResistance + penalty, cold = p.ColdResistance + penalty;
            int lightning = p.LightningResistance + penalty, chaos = p.ChaosResistance + penalty;
            await conn.Commands!.Reply(
                $"Kitava penalty {penalty}%. " +
                $"Fire {fire}% (passives {p.FireResistance}%, max {p.FireMaxResistance}%, capped {DamageModel.Capped(fire, p.FireMaxResistance)}%), " +
                $"Cold {cold}% (passives {p.ColdResistance}%, max {p.ColdMaxResistance}%, capped {DamageModel.Capped(cold, p.ColdMaxResistance)}%), " +
                $"Lightning {lightning}% (passives {p.LightningResistance}%, max {p.LightningMaxResistance}%, capped {DamageModel.Capped(lightning, p.LightningMaxResistance)}%), " +
                $"Chaos {chaos}% (passives {p.ChaosResistance}%, max {p.ChaosMaxResistance}%, capped {DamageModel.Capped(chaos, p.ChaosMaxResistance)}%).");
        })
        .Add("hit", "/hit <fire|cold|lightning|chaos|physical> <damage> (dev: incoming enemy test contact through resistances or armour)", async args =>
        {
            if (args.Length < 2) throw new ArgumentException("a damage type and an amount");
            if (ParseDamageType(args[0]) is not { } type) throw new ArgumentException("fire, cold, lightning, chaos or physical");
            if (!int.TryParse(args[1], out int amount) || amount <= 0) throw new ArgumentException("a positive amount");
            if (!TryDevEnemyHit(conn, type, amount, false, false, false, out var hit,
                ranged: args.Length > 2 && args[2] == "ranged"))
            {
                await conn.Commands!.Reply(conn.PlayerLife == 0 ? "You are already dead." : "Enemy test hit rejected while paused or leaving.");
                return;
            }
            int dealt = hit.Damage; string mitigation = hit.Mitigation; uint life = hit.Life;
            await SendPlayerEnergyShield(remote, stream, ciphers, conn);
            if (life == 0)
                await Send(remote, stream, ciphers, InstanceEntities.StatsUpdate(PlayerId, [(StatKeys.HideMiniLifeBar, 1)]),
                    "0x01b3 player hide_mini_life_bar = 1 (dev hit)");
            await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(PlayerId, life),
                $"0x01b1 player dev {type} hit for {dealt} ({amount} before {mitigation}), life {life}/{conn.PlayerMaxLife}");
            if (life == 0)
                await PlayerDied(remote, stream, ciphers, conn, $"0x0055 death screen ({conn.League ?? "?"} league, dev hit)");
            await conn.Commands!.Reply($"{type}: {amount} → {dealt} after {mitigation}; life {life}/{conn.PlayerMaxLife}.");
        })
        .Add("suppression", "/suppression (Spell Suppression chance, roll and damage reduction)", async _ =>
        {
            var (chance, effect, lucky, unlucky) = Suppression(conn);
            double effective = DamageModel.EffectiveSuppressionChance(chance, lucky, unlucky);
            await conn.Commands!.Reply($"Spell Suppression: {chance}% chance (cap {DamageModel.SpellSuppressionChanceCap}%, effective {effective:0.#}%" +
                $"{(lucky ? ", lucky" : "")}{(unlucky ? ", unlucky" : "")}), {effect}% less damage on a suppressed spell hit.");
        })
        .Add("suppression_luck", "/suppression_luck <lucky|unlucky|off> (dev: force a lucky/unlucky suppression roll)", async args =>
        {
            string mode = args.Length == 0 ? "" : args[0].ToLowerInvariant();
            bool lucky = mode == "lucky", unlucky = mode == "unlucky";
            if (mode is not ("lucky" or "unlucky" or "off" or "")) throw new ArgumentException("lucky, unlucky or off");
            lock (conn.Monsters)
            {
                conn.ForceSpellSuppressionLucky = lucky;
                conn.ForceSpellSuppressionUnlucky = unlucky;
                conn.Pools = PoolsFor(conn, conn.Pools.Level);
            }
            await SendCharacterStats(remote, stream, ciphers, conn, $"suppression luck {mode}");
            await conn.Commands!.Reply($"Spell Suppression roll: {(lucky ? "lucky" : unlucky ? "unlucky" : "normal")}.");
        })
        .Add("accuracy", "/accuracy [monster level] (accuracy rating and attack hit chance; default: your level)", async args =>
        {
            int level = conn.Pools.Level;
            if (args.Length > 0)
            {
                if (!int.TryParse(args[0], out int requested) || requested < 1 || requested > Progression.MaxLevel)
                    throw new ArgumentException($"monster level 1 to {Progression.MaxLevel}");
                level = requested;
            }
            int evasion = Monsters.BaseEvasion(level) ?? 0;
            int evasiveEvasion = Monsters.BaseEvasiveEvasion(level) ?? 0;
            int chance = DamageModel.AccuracyHitChance(evasion, conn.Pools.Accuracy);
            int evasiveChance = DamageModel.AccuracyHitChance(evasiveEvasion, conn.Pools.Accuracy);
            var withMastery = conn.Pools.AccuracyMoreVsUniquePct != 0 || conn.Pools.AccuracyMoreInCloseRangePct != 0
                ? $" Targets gain {conn.Pools.AccuracyMoreVsUniquePct:0.#}% more accuracy against unique enemies and " +
                  $"{conn.Pools.AccuracyMoreInCloseRangePct:0.#}% more within the 20-unit close range, applied per attack roll."
                : "";
            await conn.Commands!.Reply($"Accuracy {conn.Pools.Accuracy} ({conn.Pools.AccuracyPct}% increased): " +
                $"{chance}% chance to hit a level {level} monster (evasion {evasion}), " +
                $"{evasiveChance}% against evasive monsters (evasion {evasiveEvasion}). Spells always hit." + withMastery);
        })
        .Add("defences", "/defences [monster level] (armour, evasion and avoidance; default: your level)", async args =>
        {
            int level = conn.Pools.Level;
            if (args.Length > 0)
            {
                if (!int.TryParse(args[0], out int requested) || requested < 1 || requested > Progression.MaxLevel)
                    throw new ArgumentException($"monster level 1 to {Progression.MaxLevel}");
                level = requested;
            }
            int monsterAccuracy = Monsters.BaseAccuracy(level) ?? 0;
            int monsterDamage = Monsters.BaseDamage(level) ?? 0;
            int hitChance = DamageModel.AccuracyHitChance(conn.Pools.Evasion, monsterAccuracy);
            int reduction = DamageModel.ArmourReduction(conn.Pools.Armour, monsterDamage);
            await conn.Commands!.Reply($"Armour {conn.Pools.Armour} ({conn.Pools.ArmourPct}% increased): " +
                $"{reduction}% physical reduction against a level {level} monster hit ({monsterDamage}), " +
                $"evasion {conn.Pools.Evasion} ({conn.Pools.EvasionPct}% increased): {100 - hitChance}% chance to evade " +
                $"(monster accuracy {monsterAccuracy}, {hitChance}% chance to be hit).");
        })
        .Add("auras", "/auras (active auras, their reservation and the character's efficiency)", async _ =>
        {
            List<AuraCatalog.Aura> auras;
            int reserved;
            int maxMana;
            int efficiency;
            int auraEffect;
            lock (conn.Monsters)
            {
                auras = [.. conn.Auras.Values];
                reserved = AuraCatalog.ReservedMana(conn.Pools, auras);
                maxMana = (int)conn.Pools.Mana;
                efficiency = conn.Pools.ManaReservationEfficiencyPct;
                auraEffect = conn.Pools.AuraEffectPct;
            }
            string list = auras.Count == 0 ? "none" : string.Join(", ", auras.Select(a =>
                $"{a.Name} ({a.ManaFlat} flat, {a.ManaPercent / 100.0:0.##}%)"));
            await conn.Commands!.Reply($"Auras: {list}. Mana {conn.PlayerMana}/{maxMana}, {reserved} reserved " +
                $"({maxMana - reserved} unreserved); reservation efficiency {efficiency}%, aura effect {auraEffect}%.");
        })
        .Add("spellhit", "/spellhit <fire|cold|lightning|chaos|physical> <damage> (dev: spell hit through resistance and Spell Suppression)", async args =>
        {
            if (args.Length < 2) throw new ArgumentException("a damage type and an amount");
            if (ParseDamageType(args[0]) is not { } type) throw new ArgumentException("fire, cold, lightning, chaos or physical");
            if (!int.TryParse(args[1], out int amount) || amount <= 0) throw new ArgumentException("a positive amount");
            if (!TryDevEnemyHit(conn, type, amount, false, true, true, out var hit))
            {
                await conn.Commands!.Reply(conn.PlayerLife == 0 ? "You are already dead." : "Enemy test hit rejected while paused or leaving.");
                return;
            }
            int dealt = hit.Damage; string mitigation = hit.Mitigation; uint life = hit.Life;
            bool suppressed = hit.Suppressed;
            var (chance, effect, lucky, unlucky) = hit.Suppression;
            await SendPlayerEnergyShield(remote, stream, ciphers, conn);
            if (life == 0)
                await Send(remote, stream, ciphers, InstanceEntities.StatsUpdate(PlayerId, [(StatKeys.HideMiniLifeBar, 1)]),
                    "0x01b3 player hide_mini_life_bar = 1 (dev spell hit)");
            await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(PlayerId, life),
                $"0x01b1 player dev spell {type} hit for {dealt} ({amount} before {mitigation}, suppression {(suppressed ? "yes" : "no")}), life {life}/{conn.PlayerMaxLife}");
            if (life == 0)
                await PlayerDied(remote, stream, ciphers, conn, $"0x0055 death screen ({conn.League ?? "?"} league, dev spell hit)");
            double effective = DamageModel.EffectiveSuppressionChance(chance, lucky, unlucky);
            await conn.Commands!.Reply($"{type} spell hit: {amount} → {dealt} after {mitigation}; " +
                $"suppression {(suppressed ? "rolled" : "not rolled")} ({chance}% chance, effective {effective:0.#}%" +
                $"{(lucky ? ", lucky" : "")}{(unlucky ? ", unlucky" : "")}); life {life}/{conn.PlayerMaxLife}.");
        })
        .Add("charges", ChargesUsage, args => ChargesCommand(conn, args))
        .Add("crit", "/crit (attack and spell critical strike chance and multiplier)", async _ =>
        {
            var p = conn.Pools;
            await conn.Commands!.Reply(
                $"Attack crit {p.AttackCriticalStrikeChance / 100.0:0.##}% ({p.CriticalStrikeChancePct}% increased), " +
                $"multiplier {AttackCriticalMultiplier(conn, CharacterPlayerStats(conn))}%. Spell crit reference (5% base) {p.SpellCriticalStrikeChance / 100.0:0.##}% " +
                $"({p.SpellCriticalStrikeChancePct}% increased), multiplier {p.SpellCriticalStrikeMultiplier}%.");
        })
        .Add("block", "/block (attack and spell block chance and caps)", async _ =>
        {
            var p = conn.Pools;
            await conn.Commands!.Reply($"Block {p.BlockChance}% (max {p.BlockChanceMax}%, uncapped {p.BlockChanceUncapped}%, effective {DamageModel.EffectiveBlockChance(p.BlockChance, p.AttackBlockLucky, p.AttackBlockUnlucky):0.#}%), " +
                $"spell block {p.SpellBlockChance}% (max {p.SpellBlockChanceMax}%, uncapped {p.SpellBlockChanceUncapped}%, effective {DamageModel.EffectiveBlockChance(p.SpellBlockChance, p.SpellBlockLucky, p.SpellBlockUnlucky):0.#}%)" +
                $"{(p.AttackBlockLucky ? "; attack lucky" : "")}{(p.SpellBlockLucky ? "; spell lucky" : "")}{(p.AttackBlockUnlucky ? "; attack unlucky" : "")}{(p.SpellBlockUnlucky ? "; spell unlucky" : "")}.");
        })
        .Add("block_luck", "/block_luck <attack|spell|both|off> (dev: force lucky block)", async args =>
        {
            string mode = args.Length == 0 ? "" : args[0].ToLowerInvariant();
            bool attack = mode is "attack" or "both", spell = mode is "spell" or "both";
            if (mode is not ("attack" or "spell" or "both" or "off" or "")) throw new ArgumentException("attack, spell, both or off");
            lock (conn.Monsters)
            {
                conn.ForceAttackBlockLucky = attack;
                conn.ForceSpellBlockLucky = spell;
                conn.Pools = PoolsFor(conn, conn.Pools.Level);
            }
            await SendCharacterStats(remote, stream, ciphers, conn, $"lucky block {mode}");
            await conn.Commands!.Reply($"Lucky block: attack {(attack ? "on" : "off")}, spell {(spell ? "on" : "off")}.");
        })
        .Add("blockhit", "/blockhit <attack|spell> <fire|cold|lightning|chaos|physical> <damage> (dev: hit through block and resistance)", async args =>
        {
            if (args.Length < 3) throw new ArgumentException("attack|spell, a damage type and an amount");
            bool spell = args[0].Equals("spell", StringComparison.OrdinalIgnoreCase);
            if (!spell && !args[0].Equals("attack", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("attack or spell");
            if (ParseDamageType(args[1]) is not { } type) throw new ArgumentException("fire, cold, lightning, chaos or physical");
            if (!int.TryParse(args[2], out int amount) || amount <= 0) throw new ArgumentException("a positive amount");
            if (!TryDevEnemyHit(conn, type, amount, true, spell, false, out var hit))
            {
                await conn.Commands!.Reply(conn.PlayerLife == 0 ? "You are already dead." : "Enemy test hit rejected while paused or leaving.");
                return;
            }
            int dealt = hit.Damage; string mitigation = hit.Mitigation; uint life = hit.Life;
            uint mana = hit.Mana, manaBefore = hit.ManaBefore;
            bool blocked = hit.Blocked; int chance = hit.BlockChance;
            await SendPlayerEnergyShield(remote, stream, ciphers, conn);
            if (life == 0)
                await Send(remote, stream, ciphers, InstanceEntities.StatsUpdate(PlayerId, [(StatKeys.HideMiniLifeBar, 1)]),
                    "0x01b3 player hide_mini_life_bar = 1 (dev block hit)");
            await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(PlayerId, life),
                $"0x01b1 player dev {(spell ? "spell" : "attack")} hit for {dealt} ({(blocked ? "blocked" : mitigation)}), life {life}/{conn.PlayerMaxLife}");
            if (mana != manaBefore)
                await SendPlayerMana(remote, stream, ciphers, conn,
                    $"0x01b1 player mana {mana} ({(blocked ? $"+{conn.Pools.ManaGainedOnBlock} on block" : "no block gain")})");
            if (life == 0)
                await PlayerDied(remote, stream, ciphers, conn, $"0x0055 death screen ({conn.League ?? "?"} league, dev block hit)");
            await conn.Commands!.Reply($"{(spell ? "Spell" : "Attack")} hit: {amount} → {dealt}; {(blocked ? "blocked" : "not blocked")} ({chance}% chance); life {life}/{conn.PlayerMaxLife}.");
        })
        .Add("god", "/god (toggles: 1000% increased attack and cast speed, 100% of life and 10 000 mana regenerated per second, 200% increased area of effect, +10 000 life, +1000 attributes)", async _ =>
        {
            bool on = !conn.God;
            await SetGod(remote, stream, ciphers, conn, on);
            // A burning Righteous Fire takes its radius when lit: light it again with the new area.
            await RelightRighteousFire(remote, stream, ciphers, conn, "/god");
            await conn.Commands!.Reply(on ? "God mode on." : "God mode off.");
        })
        .Add("set_aoe", "/set_aoe <percent> | off (dev: the character's increased area of effect, base_skill_area_of_effect_+%, in place of what passives, items and /god give)", async args =>
        {
            if (args.Length == 0) throw new ArgumentException("percent or off");
            if (Stats.Key("base_skill_area_of_effect_+%") is not uint stat) throw new ArgumentException("no area of effect stat in the data");
            bool off = args[0].Equals("off", StringComparison.OrdinalIgnoreCase);
            int value = off ? 0 : int.Parse(args[0]);
            lock (conn.Monsters) { if (off) conn.PlayerStats.Remove(stat); else conn.PlayerStats[stat] = value; }
            await SendPlayerStatSnapshot(remote, stream, ciphers, conn,
            () => PlayerStatsUpdateAtGate(conn, PlayerStatsWithCleared(conn)),
                $"0x01b3 stats: base_skill_area_of_effect_+% = {(off ? "the character's own" : value)}");
            await SendSkills(remote, stream, ciphers, conn);
            bool relit = await RelightRighteousFire(remote, stream, ciphers, conn, "/set_aoe");
            await conn.Commands!.Reply((off ? "Increased area of effect: the character's own" : $"Increased area of effect: {value}%")
                + (relit ? ", Righteous Fire relit" : ""));
        })
        .Add("heal", "/heal (full life; also revives: 0x1b1 from 0 runs the client's revive)", async _ =>
        {
            lock (conn.Monsters) { (conn.PlayerLife, conn.PlayerMana) = (conn.PlayerMaxLife, (uint)UnreservedMana(conn)); conn.EnergyShield.SetMaximum(conn.Pools.EnergyShield, true); }
            await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(PlayerId, conn.PlayerMaxLife),
                $"0x01b1 player healed to {conn.PlayerMaxLife}");
            await SendPlayerMana(remote, stream, ciphers, conn, $"0x01b1 player mana {conn.PlayerMana}");
            await Send(remote, stream, ciphers, InstanceEntities.StatsUpdate(PlayerId, [(StatKeys.HideMiniLifeBar, 0)]),
                "0x01b3 player hide_mini_life_bar = 0");
        })
        .Add("death_screen", "/death_screen [hex flags] (show the death screen; default: this league's; 2 hides it)", async args =>
        {
            byte flags = args.Length > 0 ? Convert.ToByte(args[0].Replace("0x", ""), 16) : DeathScreenFlags(conn);
            await Send(remote, stream, ciphers, InstanceProtocol.DeathScreen(flags), $"0x0055 death screen flags 0x{flags:x2}");
        })
        .Add("transition", "/transition [pos [here | x y | +dx +dy] | rot <degrees> | z <z | none> | model <path | none> | index <n>] " +
            "(dev: show or change the first area transition; it is re-created)", async args =>
        {
            var (px, py) = conn.Walk.Position(conn.Clock.Elapsed.TotalSeconds);
            if (conn.Layout.AreaTransitions.Length == 0) { await conn.Commands!.Reply("This area has no area transition."); return; }
            var (id, at) = conn.Layout.AreaTransitions[0];
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string? what = args.Length > 0 ? args[0] : null;
            string[] rest = args.Skip(1).ToArray();
            AreaTransitionObject? changed = what switch
            {
                "pos" when rest.Length > 0 => rest[0] == "here" ? at with { GridX = px, GridY = py }
                    : rest[0].StartsWith('+') || rest[0].StartsWith('-')
                        ? at with { GridX = at.GridX + int.Parse(rest[0]), GridY = at.GridY + int.Parse(rest[1]) }
                        : at with { GridX = int.Parse(rest[0]), GridY = int.Parse(rest[1]) },
                "rot" => at with { Rotation = float.Parse(rest[0], inv) * MathF.PI / 180f },
                "z" => at with { Height = rest[0] == "none" ? null : float.Parse(rest[0], inv) },
                "model" => at with { Model = rest[0] == "none" ? null : string.Join(' ', rest) },
                "index" => at with { Index = int.Parse(rest[0]) },
                _ => null,
            };
            if (changed is not null)
            {
                conn.ReplaceAreaTransition(id, changed);
                at = changed;
                await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(id), $"0x02bb remove area transition 0x{id:x}");
                await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(at.Type), at.Blob()),
                    $"0x02b9 create area transition {at}");
                await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), $"0x01a9 area transition 0x{id:x} enters wake zone");
            }
            await conn.Commands!.Reply($"Area transition 0x{id:x} at ({at.GridX}, {at.GridY}), {at.Rotation * 180 / MathF.PI:0.#} degrees, " +
                $"height {at.Height?.ToString(inv) ?? "terrain"}, model {at.Model ?? "none"}; you at ({px}, {py}).");
        })
        .Add("quest_flag", "/quest_flag [<QuestFlags Id> [off]] (dev: list, set or clear quest flags, e.g. /quest_flag a1q1-KilledHillock)", async args =>
        {
            if (args.Length < 1) { await conn.Commands!.Reply(string.Join(", ", conn.QuestFlags.DefaultIfEmpty("no quest flags"))); return; }
            bool off = args.Length > 1 && args[1].Equals("off", StringComparison.OrdinalIgnoreCase);
            await SetQuestFlags(remote, stream, ciphers, conn, [args[0]], on: !off);
            await conn.Commands!.Reply($"{args[0]} {(off ? "cleared" : "set")}");
        }, "quest_flag")
        .Add("delve_chart", "/delve_chart <u32> <u16> [<u16> <u16> <u64> <u8> <u32> ...] (dev: what the next Delve chart (S→C 0x240) carries, to work out its fields)", async args =>
        {
            try
            {
                var n = args.Select(a => a.StartsWith("0x") ? Convert.ToUInt64(a[2..], 16) : ulong.Parse(a)).ToArray();
                var cells = new List<InstanceDelve.Cell>();
                for (int i = 2; i + 4 < n.Length; i += 5) cells.Add(new((ushort)n[i], (ushort)n[i + 1], n[i + 2], (byte)n[i + 3], (uint)n[i + 4]));
                conn.DelveChartProbe = (n.Length > 0 ? (uint)n[0] : 0, n.Length > 1 ? (ushort)n[1] : (ushort)0, cells);
                await conn.Commands!.Reply($"Next Delve chart: {conn.DelveChartProbe.A}, {conn.DelveChartProbe.B}, {cells.Count} cell(s). Open it from Niko.");
            }
            catch (FormatException) { await conn.Commands!.Reply("Numbers only (decimal or 0x…)."); }
        })
        .Add("atlas", "/atlas [layout] (dev: show or choose the fallback layout generic maps open)", async args =>
        {
            var atlas = MapDevices.Value.Atlas;
            if (atlas is null || atlas.Layouts.Count == 0) { await conn.Commands!.Reply("No Atlas layouts: extract AtlasNode.json and WorldAreas.json."); return; }
            if (args.Length == 0)
            {
                var current = atlas.Selected(CharacterName(conn));
                await conn.Commands!.Reply($"Atlas layout: {current?.Name} ({current?.Id}); {atlas.Layouts.Count} layouts available.");
                return;
            }
            string query = string.Join(' ', args);
            if (atlas.Find(query) is not { } layout) { await conn.Commands!.Reply($"No Atlas layout \"{query}\"."); return; }
            if (CharacterName(conn) is { } character)
            {
                atlas.Select(character, layout);
                CharacterSaves.SetAtlasLayout(character, layout.Id);
            }
            await conn.Commands!.Reply($"Atlas layout set to {layout.Name} ({layout.Id}, tier {layout.Tier}).");
        })
        .Add("atlas_tree", "/atlas_tree (dev: open the Atlas passive tree; S→C 0x7b kind 0x11)", async _ =>
        {
            await Send(remote, stream, ciphers, InstanceWaypoints.OpenUi(InstanceWaypoints.AtlasUi),
                "0x007b open the Atlas passive tree");
        })
        .Add("atlas_points", "/atlas_points [<n>] (dev: show or set the Atlas passive point budget, saved)", async args =>
        {
            if (args.Length == 0)
            {
                await conn.Commands!.Reply($"Atlas passive points: {conn.AtlasPassivePoints}; " +
                    $"preset allocations: {string.Join(", ", conn.AtlasPassives.Select((p, i) => $"{i}={p.Count}"))}.");
                return;
            }
            if (!int.TryParse(args[0], out int points) || points is < 0 or > ushort.MaxValue)
                throw new ArgumentException("a number from 0 to 65535");
            conn.AtlasPassivePoints = (ushort)points;
            if (CharacterName(conn) is { } character)
                CharacterSaves.SetAtlasPassives(character, conn.AtlasPassives, conn.AtlasPassivePoints);
            await SendAtlasSlots(remote, stream, ciphers, conn, "/atlas_points");
            await conn.Commands!.Reply($"Atlas passive points: {points}.");
        })
        .Add("goto", "/goto <area id or name> [seed] (dev: change area as if through an area transition, e.g. /goto 1_1_3; with a seed, a new instance at that seed, e.g. /goto 1_1_5 1384217569)", async args =>
        {
            // A last number is the seed: a new instance at it, as the capture tour's stops (towns keep seed 1).
            uint seed = args.Length > 1 && uint.TryParse(args[^1], out var s) && s != 0 ? s : 0;
            string target = string.Join(' ', seed != 0 ? args[..^1] : args);
            if (AreaTransitionCatalog.HashOf(target) is not { } hash) { await conn.Commands!.Reply($"Unknown area \"{target}\" (needs data/area-transitions.json)."); return; }
            await ChangeArea(remote, stream, ciphers, conn, new PendingTransfer(hash, 0, seed != 0) { Seed = seed },
                seed != 0 ? $"/goto {target} seed {seed}" : $"/goto {target}");
        }, "area")
        .Add("tour", "/tour <list file> | stop | status (dev, live.ps1 -Walkmaps: visit every \"<area id> <seed>\" of the file in a new instance and record it; levelgen tourlist writes one)", async args =>
        {
            string character = CharacterName(conn);
            string what = args.Length > 0 ? args[0] : "status";
            if (what.Equals("stop", StringComparison.OrdinalIgnoreCase)) { await conn.Commands!.Reply(CaptureTour.Stop(character)); return; }
            if (what.Equals("status", StringComparison.OrdinalIgnoreCase)) { await conn.Commands!.Reply(CaptureTour.Status(character)); return; }
            string path = string.Join(' ', args);
            if (!AreaLayouts.Recording) { await conn.Commands!.Reply("A tour needs the walk-map recorder: start the servers with live.ps1 -Walkmaps."); return; }
            if (!File.Exists(path)) { await conn.Commands!.Reply($"No file {path} (relative to the Gateway's directory, the repository root)."); return; }
            await conn.Commands!.Reply(CaptureTour.Start(character, path));
            // Not awaited: a step waits for the client to arrive or to fail.
            _ = ContinueTour(remote, stream, ciphers, conn, starting: true);
        })
        .Add("league_flag", "/league_flag [<id> [off]] (dev: list, set or clear league quest flags by the client's id, e.g. /league_flag 350)", async args =>
        {
            if (args.Length < 1 || !int.TryParse(args[0], out int flag))
            {
                await Send(remote, stream, ciphers, InstanceChat.ChatLine($"League quest flags: {string.Join(", ", conn.LeagueQuestFlags.Order())}"), "0x0017 chat line: league quest flags");
                return;
            }
            bool off = args.Length > 1 && args[1].Equals("off", StringComparison.OrdinalIgnoreCase);
            if (off) conn.LeagueQuestFlags.Remove(flag); else conn.LeagueQuestFlags.Add(flag);
            await Send(remote, stream, ciphers, InstanceEntities.PlayerLeagueQuestFlagsUpdate(PlayerId, Quests.League.Bits(conn.LeagueQuestFlags)),
                $"0x02ba player league quest flags: {string.Join(", ", conn.LeagueQuestFlags.Order())}");
            await Send(remote, stream, ciphers, InstanceChat.ChatLine($"League quest flag {flag} {(off ? "cleared" : "set")}."), $"0x0017 chat line: league quest flag {flag}");
        })
        .Add("hideout_keys", "/hideout_keys <a> <b> (dev: the Player component's two Hideouts.dat keys, HASH16 hex or a hideout area id)", async args =>
        {
            ushort Key(string s) => HideoutCatalog.Hideouts.FirstOrDefault(h => h.AreaId.Equals(s, StringComparison.OrdinalIgnoreCase)) is { } h ? h.Hash16
                : ushort.TryParse(s.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out var k) ? k : (ushort)0;
            ushort a = args.Length > 0 ? Key(args[0]) : (ushort)0, b = args.Length > 1 ? Key(args[1]) : (ushort)0;
            await Send(remote, stream, ciphers, InstanceEntities.PlayerHideoutsUpdate(PlayerId, a, b), $"0x02ba player hideout keys 0x{a:x4} 0x{b:x4}");
            await Send(remote, stream, ciphers, InstanceChat.ChatLine($"Hideout keys 0x{a:x4} 0x{b:x4}."), "0x0017 chat line: hideout keys");
        })
        .Add("waypoints", "/waypoints [all | reset] (list the activated waypoints; dev: activate all and visit every pinned area, or keep only the towns')", async args =>
        {
            var entries = WaypointCatalog.Areas.Entries;
            if (entries.Count == 0) { await conn.Commands!.Reply("No waypoints (needs data/tables/WorldAreas.json)."); return; }
            string what = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (what is "all" or "reset")
            {
                string[] all;
                string[] visited;
                lock (conn.Waypoints)
                {
                    conn.Waypoints.Clear();
                    conn.Waypoints.UnionWith(entries.Where(e => what == "all" || e.IsTown).Select(e => e.Id));
                    all = [.. conn.Waypoints];
                    // "all" also visits every area with a World panel pin, so every pin and line shows.
                    if (what == "all") conn.Visited.UnionWith(WaypointCatalog.Visits.PinnedAreas);
                    else { conn.Visited.Clear(); conn.Visited.Add(conn.Layout.Id); }
                    visited = [.. conn.Visited];
                }
                CharacterSaves.SetWaypoints(CharacterName(conn), all);
                CharacterSaves.SetVisited(CharacterName(conn), visited);
                await SendWaypointList(remote, stream, ciphers, conn, $"/waypoints {what}");
            }
            string list;
            lock (conn.Waypoints) list = string.Join(", ", entries.Where(e => conn.Waypoints.Contains(e.Id)).Select(e => e.Name));
            await conn.Commands!.Reply(list.Length == 0 ? "No activated waypoints." : $"Activated waypoints: {list}.");
        })
        .Add("npc", "/npc <NPCs row, Id or name> [rotation°] (spawns an NPC next to you)", async args =>
        {
            if (args.Length == 0) throw new ArgumentException("which NPC");
            if (Npcs.Value.Error is { } error) { await conn.Commands!.Reply(error); return; }
            float? deg = args.Length > 1 && float.TryParse(args[^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d) ? d : null;
            string query = string.Join(' ', deg is null ? args : args[..^1]);
            if (Npcs.Value.Find(query) is not { } npc) { await conn.Commands!.Reply($"No NPC {query}."); return; }
            if (!npc.Supported) { await conn.Commands!.Reply($"{npc} has components the server cannot write: {string.Join(", ", npc.Components ?? [])}."); return; }
            var (x, y) = NextToPlayer(conn);
            await SpawnNpc(remote, stream, ciphers, conn, npc, x, y, (deg ?? 0) * MathF.PI / 180);
        }, "npc")
        // Turns an NPC: it is created again with the new facing (0x2bb, then 0x2b9).
        .Add("npc_place", "/npc_place <name> <x> <y> <degrees> (dev: move and turn an NPC here)", async args =>
        {
            if (args.Length < 4 || !int.TryParse(args[^3], out int x) || !int.TryParse(args[^2], out int y)) throw new ArgumentException("/npc_place <name> <x> <y> <degrees>");
            float deg = float.Parse(args[^1], System.Globalization.CultureInfo.InvariantCulture);
            string name = string.Join(' ', args[..^3]);
            KeyValuePair<uint, SpawnedNpc> found;
            lock (conn.Npcs) found = conn.Npcs.FirstOrDefault(n => n.Value.Npc.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (found.Value is not { } npc) { await conn.Commands!.Reply($"No NPC {name} here."); return; }
            lock (conn.Npcs) conn.Npcs.Remove(found.Key);
            conn.RemoveBlocker(found.Key);
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(found.Key), $"0x02bb remove npc 0x{found.Key:x} {npc.Npc.Name}");
            await SpawnNpc(remote, stream, ciphers, conn, npc.Npc, x, y, deg * MathF.PI / 180);
            await conn.Commands!.Reply($"{npc.Npc.Name} at ({x}, {y}), {deg}°");
        })
        .Add("npc_face", "/npc_face <name> <degrees> (turn an NPC here)", async args =>
        {
            if (args.Length < 2) throw new ArgumentException("name and degrees");
            float deg = float.Parse(args[^1], System.Globalization.CultureInfo.InvariantCulture);
            string name = string.Join(' ', args[..^1]);
            KeyValuePair<uint, SpawnedNpc> found;
            lock (conn.Npcs) found = conn.Npcs.FirstOrDefault(n => n.Value.Npc.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (found.Value is not { } npc) { await conn.Commands!.Reply($"No NPC {name} here."); return; }
            lock (conn.Npcs) conn.Npcs.Remove(found.Key);
            conn.RemoveBlocker(found.Key);
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(found.Key), $"0x02bb remove npc 0x{found.Key:x} {npc.Npc.Name}");
            float rad = deg * MathF.PI / 180;
            await SpawnNpc(remote, stream, ciphers, conn, npc.Npc, npc.X, npc.Y, rad);
            await conn.Commands!.Reply($"{npc.Npc.Name} at ({npc.X}, {npc.Y}) faces {deg}° ({rad:0.###} rad).");
        })
        // Probe: S→C 0x7b (open a UI) with any kind and its four u32s (16-waypoints.md §3; kind 0 = World panel).
        .Add("ui", "/ui <kind hex> [a] [b] [c] [d] (S→C 0x7b open a UI)", async args =>
        {
            if (args.Length == 0) throw new ArgumentException("which kind");
            byte kind = Convert.ToByte(args[0].Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);
            uint U(int i) => args.Length > i ? Convert.ToUInt32(args[i], args[i].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10) : 0;
            await Send(remote, stream, ciphers, InstanceWaypoints.OpenUi(kind, U(1), U(2), U(3), U(4)), $"0x007b open UI kind 0x{kind:x2} ({U(1)}, {U(2)}, {U(3)}, {U(4)})");
        })
        .Add("npcs", "/npcs (list NPCs here)", async _ =>
        {
            string list;
            lock (conn.Npcs) list = string.Join(", ", conn.Npcs.Select(n => $"0x{n.Key:x} {n.Value.Npc.Name} ({n.Value.X}, {n.Value.Y})"));
            await conn.Commands!.Reply(list.Length == 0 ? "No NPCs." : list);
        })
        .Add("pos", "/pos (your cell)", async _ =>
        {
            var (x, y) = conn.Walk.Position(conn.Clock.Elapsed.TotalSeconds);
            Console.WriteLine($"[gateway] {remote} /pos ({x}, {y}) in {conn.Layout.Id}");
            await conn.Commands!.Reply($"({x}, {y}) in {conn.Layout.Id}");
        })
        .Add("game_data", "/game_data reload | reference | mod <mod> <stat> <min> <max> | clear_mod <mod> [stat] | limit <key> <value> | clear_limit <key> | setting <key> <value> | clear_setting <key> | unique_weight <name> <weight> [ruthless] | clear_unique_weight <name> | unique_weights [filter] | settings | list (dev: SQLite game-data overrides; no restart)", async args =>
        {
            if (args.Length == 0)
            {
                await conn.Commands!.Reply("Usage: /game_data reload | reference | mod <mod> <stat> <min> <max> | clear_mod <mod> [stat] | " +
                    "limit <key> <value> | clear_limit <key> | setting <key> <value> | clear_setting <key> | " +
                    "unique_weight <name> <weight> [ruthless] | clear_unique_weight <name> | unique_weights [filter] | settings | list");
                return;
            }
            if (GameData.Store is not { } store)
            {
                await conn.Commands!.Reply("The game-data store is not open");
                return;
            }
            string action = args[0].ToLowerInvariant();
            try
            {
                switch (action)
                {
                    case "reload":
                        GameData.Reload("command");
                        await conn.Commands!.Reply($"Game data reloaded: revision {GameData.Current.Revision}");
                        return;
                    case "reference":
                        bool seeded = GameDataReference.Seed(store, force: true);
                        bool weightsSeeded = GameDataWeights.Seed(store, force: true);
                        GameDataWeights.Reload(store);
                        var (stats, mods, modStats) = store.ReferenceCounts();
                        var (weights, weightOverrides) = store.UniqueWeightCounts();
                        await conn.Commands!.Reply(seeded
                            ? $"Reference tables rebuilt: {stats} stat(s), {mods} mod(s), {modStats} default mod stat row(s); " +
                              (weightsSeeded ? $"{weights} unique weight(s)" : "unique weights need VillageUniqueDisenchantValues.json")
                            : "Reference tables need the extracted game tables (scripts/dev/extract-data.ps1)");
                        if (weightOverrides > 0) await conn.Commands!.Reply($"  {weightOverrides} unique weight override(s) kept");
                        return;
                    case "unique_weight":
                        {
                            if (args.Length < 3)
                            {
                                await conn.Commands!.Reply("Usage: /game_data unique_weight <name> <weight 0..1> [ruthless 0..1]");
                                return;
                            }
                            static bool TryWeight(string text, out double value) =>
                                double.TryParse(text, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out value);
                            int valueCount = 0;
                            double normal, ruthless;
                            if (args.Length >= 4 && TryWeight(args[^1], out double last) && TryWeight(args[^2], out double second))
                            {
                                normal = second;
                                ruthless = last;
                                valueCount = 2;
                            }
                            else if (TryWeight(args[^1], out double one))
                            {
                                normal = ruthless = one;
                                valueCount = 1;
                            }
                            else
                            {
                                await conn.Commands!.Reply("Unique weight must be a number 0..1");
                                return;
                            }
                            string name = string.Join(' ', args[1..^valueCount]);
                            if (name.Length == 0 || !double.IsFinite(normal) || !double.IsFinite(ruthless)
                                || normal is < 0 or > 1 || ruthless is < 0 or > 1)
                            {
                                await conn.Commands!.Reply("Usage: /game_data unique_weight <name> <weight 0..1> [ruthless 0..1]");
                                return;
                            }
                            var all = store.LoadUniqueWeights();
                            var match = all.FirstOrDefault(row => row.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                                ?? all.FirstOrDefault(row => row.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
                            if (match is null)
                            {
                                await conn.Commands!.Reply($"No unique weight \"{name}\"");
                                return;
                            }
                            store.SetUniqueWeight(match.Name, normal, ruthless);
                            GameData.Reload("unique weight");
                            await conn.Commands!.Reply($"{match.Name}: {normal} normal, {ruthless} Ruthless");
                            return;
                        }
                    case "clear_unique_weight":
                        {
                            if (args.Length < 2)
                            {
                                await conn.Commands!.Reply("Usage: /game_data clear_unique_weight <name>");
                                return;
                            }
                            string name = string.Join(' ', args[1..]);
                            store.ClearUniqueWeight(name);
                            GameData.Reload("clear unique weight");
                            await conn.Commands!.Reply($"Cleared the unique weight override for {name}");
                            return;
                        }
                    case "unique_weights":
                        {
                            var all = store.LoadUniqueWeights();
                            var overrides = store.LoadUniqueWeightOverrides()
                                .ToDictionary(row => row.Name, row => row, StringComparer.OrdinalIgnoreCase);
                            string filter = args.Length > 1 ? string.Join(' ', args[1..]) : "";
                            var matches = all.Where(row => filter.Length == 0
                                || row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                            await conn.Commands!.Reply($"{matches.Count} unique weight(s)" +
                                (filter.Length > 0 ? $" matching \"{filter}\"" : "") +
                                (overrides.Count > 0 ? $", {overrides.Count} override(s)" : ""));
                            foreach (var row in matches.Take(40))
                            {
                                var effective = overrides.GetValueOrDefault(row.Name) ?? row;
                                await conn.Commands!.Reply($"  {row.Name}: {effective.Weight} normal, " +
                                    $"{effective.RuthlessWeight} Ruthless" + (overrides.ContainsKey(row.Name) ? " (override)" : ""));
                            }
                            if (matches.Count > 40) await conn.Commands!.Reply("  ... use a filter to narrow the list");
                            return;
                        }
                    case "mod":
                        {
                            if (args.Length != 5 || !int.TryParse(args[2], out int stat) || !int.TryParse(args[3], out int min)
                                || !int.TryParse(args[4], out int max))
                            {
                                await conn.Commands!.Reply("Usage: /game_data mod <mod> <stat> <min> <max>");
                                return;
                            }
                            if (ItemsFor(conn).Mods.Find(args[1]) is not { } mod)
                            {
                                await conn.Commands!.Reply($"No mod \"{args[1]}\"");
                                return;
                            }
                            if (stat < 0 || stat >= mod.Stats.Count)
                            {
                                await conn.Commands!.Reply($"{mod.Id} has stats 0..{mod.Stats.Count - 1}");
                                return;
                            }
                            if (min > max)
                            {
                                await conn.Commands!.Reply("minimum is above maximum");
                                return;
                            }
                            store.SetModRange(mod.Id, stat, min, max);
                            GameData.Reload("mod range");
                            var range = mod.RangeAt(stat);
                            await conn.Commands!.Reply($"{mod.Id} stat {stat} ({mod.Stats[stat].Id}) range {range.Min}..{range.Max}; " +
                                "new rolls use it, existing items keep their values");
                            return;
                        }
                    case "clear_mod":
                        {
                            if (args.Length is < 2 or > 3 || (args.Length == 3 && !int.TryParse(args[2], out _)))
                            {
                                await conn.Commands!.Reply("Usage: /game_data clear_mod <mod> [stat]");
                                return;
                            }
                            string modId = ItemsFor(conn).Mods.Find(args[1])?.Id ?? args[1];
                            store.ClearModRange(modId, args.Length == 3 ? int.Parse(args[2]) : null);
                            GameData.Reload("clear mod range");
                            await conn.Commands!.Reply($"Cleared the range override for {modId}" +
                                (args.Length == 3 ? $" stat {args[2]}" : ""));
                            return;
                        }
                    case "limit":
                        {
                            if (args.Length != 3 || !int.TryParse(args[2], out int value) || value < 0)
                            {
                                await conn.Commands!.Reply("Usage: /game_data limit <key> <value> (0 = unlimited)");
                                return;
                            }
                            store.SetLimit(args[1], value);
                            GameData.Reload("limit");
                            await conn.Commands!.Reply($"Limit {args[1]} = {value}" + (value == 0 ? " (unlimited)" : ""));
                            return;
                        }
                    case "clear_limit":
                        if (args.Length != 2)
                        {
                            await conn.Commands!.Reply("Usage: /game_data clear_limit <key>");
                            return;
                        }
                        store.ClearLimit(args[1]);
                        GameData.Reload("clear limit");
                        await conn.Commands!.Reply($"Cleared the limit override for {args[1]}");
                        return;
                    case "setting":
                        {
                            if (args.Length == 2)
                            {
                                if (!SystemSettings.Seeds.ContainsKey(args[1]))
                                {
                                    await conn.Commands!.Reply($"unknown setting \"{args[1]}\"");
                                    return;
                                }
                                await conn.Commands!.Reply($"{args[1]} = {SystemSettings.Default.Get(args[1])}");
                                return;
                            }
                            if (args.Length != 3 || !double.TryParse(args[2], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double setting))
                            {
                                await conn.Commands!.Reply("Usage: /game_data setting <key> [value]");
                                return;
                            }
                            if (!SystemSettings.Validate(args[1], setting, out string? settingError))
                            {
                                await conn.Commands!.Reply(settingError!);
                                return;
                            }
                            store.SetSetting(args[1], setting);
                            GameData.Reload("setting");
                            await conn.Commands!.Reply($"{args[1]} = {SystemSettings.Default.Get(args[1])}");
                            return;
                        }
                    case "clear_setting":
                        if (args.Length != 2)
                        {
                            await conn.Commands!.Reply("Usage: /game_data clear_setting <key>");
                            return;
                        }
                        store.DeleteSetting(args[1]);
                        GameData.Reload("clear setting");
                        await conn.Commands!.Reply($"Cleared {args[1]}; it falls back to {SystemSettings.Seeds.GetValueOrDefault(args[1])}");
                        return;
                    case "settings":
                        foreach (var (key, value) in SystemSettings.Default.Values.OrderBy(kv => kv.Key))
                            await conn.Commands!.Reply($"  {key} = {value}");
                        return;
                    case "list":
                        await conn.Commands!.Reply($"Game data revision {GameData.Current.Revision}: " +
                            $"{GameData.Current.ModRanges.Count} mod range override(s), {GameData.Current.Limits.Count} limit override(s), " +
                            $"{SystemSettings.Default.Values.Count} system setting(s); " + GameDataCatalog.Summary);
                        var reference = store.ReferenceCounts();
                        await conn.Commands!.Reply($"  reference: {reference.Stats} stat(s), {reference.Mods} mod(s), " +
                            $"{reference.ModStats} default mod stat row(s)");
                        var uniqueWeights = store.UniqueWeightCounts();
                        await conn.Commands!.Reply($"  unique weights: {uniqueWeights.Weights} row(s), " +
                            $"{uniqueWeights.Overrides} override(s)");
                        foreach (var (key, range) in GameData.Current.ModRanges.OrderBy(kv => kv.Key.ModId).ThenBy(kv => kv.Key.Index))
                            await conn.Commands!.Reply($"  mod {key.ModId}[{key.Index}] = {range.Min}..{range.Max}");
                        foreach (var (key, value) in GameData.Current.Limits.OrderBy(kv => kv.Key))
                            await conn.Commands!.Reply($"  limit {key} = {value}" + (value == 0 ? " (unlimited)" : ""));
                        return;
                    default:
                        await conn.Commands!.Reply("Unknown /game_data action; use reload, reference, mod, clear_mod, limit, clear_limit, setting, clear_setting, unique_weight, clear_unique_weight, unique_weights, settings or list");
                        return;
                }
            }
            catch (Exception e) when (e is IOException or Microsoft.Data.Sqlite.SqliteException or ArgumentException)
            {
                await conn.Commands!.Reply($"Game data change failed: {e.Message}");
            }
        })
        .Add("lastpackets", "/lastpackets [n] (dev: the connection's last n messages, both directions, to the Gateway log; default 64)", async args =>
        {
            int n = args.Length > 0 ? Math.Clamp(int.Parse(args[0]), 1, PacketHistory.Capacity) : 64;
            conn.Packets.Print(remote, "/lastpackets", n);
            await conn.Commands!.Reply($"The last {Math.Min(n, conn.Packets.Count)} of {conn.Packets.Count} packet(s) are in logs\\live-gateway.log.");
        })
        .Add("tp", "/tp <x> <y> | <+dx> <+dy> | <id> | <name> [n] | list [filter] (dev: teleport to a cell, a monster, NPC, transition, waypoint or captured object, e.g. /tp RhoaChest 2)",
            args => DevTeleport(remote, stream, ciphers, conn, args))
        .Add("mobs", "/mobs (list spawned monsters)", async _ =>
        {
            // Copied under the lock: the AI and the emerge watch change both lists.
            List<string> listed;
            lock (conn.Monsters)
            {
                var buried = MonsterEmerge.Buried(conn);
                var waiting = new HashSet<SpawnedMonster>(MonsterEmerge.Unrisen(conn), ReferenceEqualityComparer.Instance);
                double now = conn.Clock.Elapsed.TotalSeconds;
                listed = [.. conn.Monsters.Concat(buried).Select(m =>
                    $"0x{m.Key:x} {m.Value.Label} {m.Value.Walk.Position(now)}" +
                    (waiting.Contains(m.Value) ? buried.ContainsKey(m.Key) ? " (buried)" : " (lurking)" : m.Value.Aggro ? " (chasing)" : ""))];
            }
            await conn.Commands!.Reply(listed.Count == 0 ? "No monsters." : string.Join(", ", listed));
        })
        .Add("despawn", "/despawn [all | <id>] (default: the last one spawned)", async args =>
        {
            List<uint> ids;
            lock (conn.Monsters)
                ids = args.Length == 0 ? [.. conn.Monsters.Keys.Concat(MonsterEmerge.Buried(conn).Keys).OrderBy(k => k).TakeLast(1)]
                    : args[0].Equals("all", StringComparison.OrdinalIgnoreCase) ? [.. conn.Monsters.Keys, .. MonsterEmerge.Buried(conn).Keys]
                    : [Convert.ToUInt32(args[0], args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10)];
            foreach (uint id in ids)
            {
                bool removed;
                lock (conn.Monsters) removed = conn.Monsters.Remove(id) || MonsterEmerge.Buried(conn).Remove(id);
                if (!removed) { await conn.Commands!.Reply($"No monster 0x{id:x}."); continue; }
                await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(id), $"0x02bb remove monster 0x{id:x}");
            }
        })
        // Movement probes: walk (0x1ba, Positioned move with a speed) or teleport (0x1b7) a monster to a few cells from the player.
        .Add("mob_move", "/mob_move [id] [speed] (0x1ba toward you; default the last monster, speed 20)", async args =>
        {
            if (MonsterArg(conn, args, 0) is not { } id) { await conn.Commands!.Reply("No such monster."); return; }
            string? sp = args.FirstOrDefault(a => !a.StartsWith("0x", StringComparison.OrdinalIgnoreCase));
            float speed = sp is null ? 20f : float.Parse(sp, System.Globalization.CultureInfo.InvariantCulture);
            var (x, y) = NextToPlayer(conn);
            await Send(remote, stream, ciphers, InstanceProtocol.MoveTo(id, x, y, speed), $"0x01ba move monster 0x{id:x} to ({x}, {y}) speed {speed}");
            lock (conn.Monsters)
            {
                conn.Monsters[id].Walk = conn.NewMonsterWalk(id, x, y, conn.Monsters[id].Walk.Speed,
                    LivingCollision.Size(conn.Monsters[id]), conn.Monsters[id].Rarity == MonsterRarity.Unique);
                conn.Monsters[id].CollisionPosition = (x, y);
            }
        })
        // Action probe: start the monster's Move action (0x1ab) toward the player, like the echo of a player's click. The skill
        // id is the player's Move (0x2909) unless given (hex); sequence 0xffff (the monster is not client-predicted).
        .Add("mob_walk", "/mob_walk [id] [skill hex] (0x1ab Move toward you)", async args =>
        {
            if (MonsterArg(conn, args, 0) is not { } id) { await conn.Commands!.Reply("No such monster."); return; }
            string? sk = args.FirstOrDefault(a => !a.StartsWith("0x", StringComparison.OrdinalIgnoreCase));
            ushort skill = sk is null ? (ushort)0x2909 : Convert.ToUInt16(sk, 16);
            var (x, y) = NextToPlayer(conn);
            (int X, int Y) from;
            lock (conn.Monsters) from = conn.Monsters[id].Walk.Order(x, y, conn.Clock.Elapsed.TotalSeconds);
            await Send(remote, stream, ciphers, InstanceProtocol.ActionStarted(id, from.X, from.Y, x, y, skill, 0xffff, 0x0408),
                $"0x01ab monster 0x{id:x} action skill 0x{skill:x4} from {from} to ({x}, {y})");
        })
        // Animation probe: play animation index `id` on the nearest living monster (0x1b2 kind 0). The Animated handler
        // bounds-checks the index against the actor's animation count, so an out-of-range value is ignored.
        .Add("anim", "/anim <index> [speed] (0x1b2 kind 0: play an animation on the nearest monster)", async args =>
        {
            if (args.Length < 1 || !uint.TryParse(args[0], out uint index)) throw new ArgumentException("an animation index");
            float speed = args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 1f;
            var here = conn.Walk.Position(conn.Clock.Elapsed.TotalSeconds);
            uint? pick;
            lock (conn.Monsters)
                pick = conn.Monsters.Where(kv => kv.Value.Life > 0)
                    .OrderBy(kv => Dist(here, kv.Value.Walk.Position(conn.Clock.Elapsed.TotalSeconds)))
                    .Select(kv => (uint?)kv.Key).FirstOrDefault();
            if (pick is not { } id) { await conn.Commands!.Reply("No living monster nearby."); return; }
            await Send(remote, stream, ciphers, InstanceProtocol.AnimationUpdate(id, 0, index, speed),
                $"0x01b2 monster 0x{id:x} play animation {index} at speed {speed}");
            await conn.Commands!.Reply($"0x1b2 anim {index} (speed {speed}) -> monster 0x{id:x}");
        })
        .Add("mob_mods", "/mob_mods [id] [mod …|pool] (a monster's mods; with mods: create it again with those affixes, e.g. /mob_mods Quick of_Flames)",
            args => MonsterModsCommand(remote, stream, ciphers, conn, args))
        .Add("mob_tp", "/mob_tp [id] (0x1b7 next to you; default the last monster)", async args =>
        {
            if (MonsterArg(conn, args, 0) is not { } id) { await conn.Commands!.Reply("No such monster."); return; }
            var (x, y) = NextToPlayer(conn);
            await Send(remote, stream, ciphers, InstanceProtocol.Teleport(id, x, y), $"0x01b7 teleport monster 0x{id:x} to ({x}, {y})");
            lock (conn.Monsters)
            {
                conn.Monsters[id].Walk = conn.NewMonsterWalk(id, x, y, conn.Monsters[id].Walk.Speed,
                    LivingCollision.Size(conn.Monsters[id]), conn.Monsters[id].Rarity == MonsterRarity.Unique);
                conn.Monsters[id].CollisionPosition = (x, y);
            }
        })
        .Add("ai", "/ai [on|off] (monsters chase you when you come near; emerge monsters rise either way)", async args =>
        {
            if (args.Length > 0) conn.AiEnabled = !args[0].Equals("off", StringComparison.OrdinalIgnoreCase);
            await conn.Commands!.Reply($"Monster AI is {(conn.AiEnabled ? "on" : "off")}.");
        });
    }
}
