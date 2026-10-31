using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Modding;
using static Program;

namespace GameServer.Modules.CosmeticsAndPets;

/// <summary>Microtransaction lists, picking cosmetics, and pets.</summary>
public sealed class CosmeticsModule : IModule
{
    /// <inheritdoc/>
    public void Load(IModuleContext module)
    {
        module.OnAreaGenerated(player =>
        {
            var conn = ConnOf(player);
            return CosmeticsFeature.SpawnPets(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn);
        });
        module.OnPacket(InstanceMicrotransactions.RequestId, async p =>
        {
            var conn = ConnOf(p.Player);
            var asked = InstanceMicrotransactions.ReadRequest(p.Bytes.ToArray());
            module.Log($"{p.Player.Character}: microtransaction lists asked: {asked}");
            await CosmeticsFeature.AnswerMtxRequest(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, asked);
            return true;
        });
        module.OnPacket(InstanceMicrotransactions.SlotId, async p =>
        {
            var conn = ConnOf(p.Player);
            await CosmeticsFeature.PickCosmetic(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn, InstanceMicrotransactions.ReadSlot(p.Bytes.ToArray()));
            return true;
        });
        module.OnPacket(InstanceMicrotransactions.SkillPickId, async p =>
        {
            var conn = ConnOf(p.Player);
            await CosmeticsFeature.PickSkillCosmetic(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn, InstanceMicrotransactions.ReadSkillPick(p.Bytes.ToArray()));
            return true;
        });
    }
}

static class CosmeticsFeature
{
    // The Microtransactions panel, the store and the hideout picker ask for their lists with C→S 0x1f2 and show "Loading…"
    // until each one arrives (23-microtransactions.md). Every account owns every microtransaction; the hideout and guild
    // personal hideout list includes all exported templates; the guild list stays empty.
    internal static async Task AnswerMtxRequest(string remote, NetworkStream stream, ConnectionCiphers ciphers, InstanceMicrotransactions.Lists asked)
    {
        var owned = Mtx.Owned;
        if (asked.HasFlag(InstanceMicrotransactions.Lists.Records))
            await Send(remote, stream, ciphers, InstanceMicrotransactions.Added(owned, InstanceMicrotransactions.Records),
                $"0x01f4 microtransactions: {owned.Count} owned (the panel's records)");
        if (asked.HasFlag(InstanceMicrotransactions.Lists.StoreOwned))
            await Send(remote, stream, ciphers, InstanceMicrotransactions.Added(owned, InstanceMicrotransactions.StoreOwned),
                $"0x01f4 microtransactions: {owned.Count} owned (the store's list)");
        if (asked.HasFlag(InstanceMicrotransactions.Lists.GuildStore))
            await Send(remote, stream, ciphers, InstanceMicrotransactions.Added([], InstanceMicrotransactions.StoreSecond),
                "0x01f4 microtransactions: the guild's list, empty");
        if (asked.HasFlag(InstanceMicrotransactions.Lists.Hideouts))
            await Send(remote, stream, ciphers, InstanceMicrotransactions.Hideouts(HideoutCatalog.OwnedRows),
                $"0x01f5 owned hideouts: all {HideoutCatalog.OwnedRows.Length} templates including MTX");
        if (asked.HasFlag(InstanceMicrotransactions.Lists.GuildHideouts))
            await Send(remote, stream, ciphers, InstanceMicrotransactions.Hideouts([], guild: true), "0x01f5 guild hideouts: none");
    }

    // C→S 0x1fa: a pick in one of the Cosmetics tab's skill slots (record 0 empties it). Saved for the slot's gem type and
    // effect slot type, then the skill slots again (what the client shows worn) and the skill list, so the gem's skill
    // carries the effect's stats (skill_art_variation: the art the client draws from the skill, and Righteous Fire's
    // server-made art the next time it is cast).
    internal static async Task PickSkillCosmetic(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, (uint Key, uint RecordId) pick)
    {
        var record = pick.RecordId == 0 ? null : Mtx.Find(pick.RecordId);
        if (!conn.SkillSlots.TryGetValue(pick.Key, out var slot)
            || !SkillCosmeticPlaces(conn).Contains((slot.Inventory, slot.Socket, slot.GemHash, slot.SlotType))
            || (pick.RecordId != 0 && (record is null || ItemsFor(conn).ByHash(slot.GemHash) is not { } gem
                || !MtxEffects.FitsSkill(record.MtxTypeHash, gem, slot.SlotType))))
        {
            Console.WriteLine($"[gateway] {remote} <= 0x01fa skill cosmetic pick: key {pick.Key}, record {pick.RecordId}: stale target or incompatible record, ignored");
            return;
        }
        Console.WriteLine($"[gateway] {remote} <= 0x01fa skill cosmetic pick: key {pick.Key} (gem 0x{slot.GemHash:x8}, slot type {slot.SlotType}), " +
            $"record {pick.RecordId} (hash 0x{record?.MtxTypeHash ?? 0:x8}, stats {string.Join(", ", (record is null ? null : MtxEffects.Of(record.MtxTypeHash))?.Stats.Select(st => $"{Stats.Name(st.Stat)} {st.Value}") ?? [])})");
        CharacterSaves.SetSkillCosmetic(CharacterName(conn), slot.GemHash, slot.SlotType, record is null ? null : new CharacterSaves.SavedCosmetic(record.Id, 0));
        await SendWornCosmetics(remote, stream, ciphers, conn, "after the skill pick");
        await SendSkills(remote, stream, ciphers, conn);
        // A Righteous Fire already burning from this gem takes the new art at once: casting again while it burns does
        // nothing, so it is lit again with the new ring and buff visuals.
        await RelightRighteousFire(remote, stream, ciphers, conn, "restyled by the skill pick", slot.GemHash);
    }

    // C→S 0x1f9: a pick in the Cosmetics tab's slot flyout (record 0 empties the slot). Saved per character, then the whole
    // worn table goes back in 0x1f3, which is what the client wears. The Portal slot also sets the portals' own variation
    // (the same one /cosmetic portal sets), so the map device's portals show the picked effect.
    internal static async Task PickCosmetic(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, (byte Slot, uint RecordId, byte Option) pick)
    {
        var record = pick.RecordId == 0 ? null : Mtx.Find(pick.RecordId);
        if (pick.Slot == 0 || pick.Slot >= InstanceMicrotransactions.SlotCount || (pick.RecordId != 0 && record is null))
        {
            Console.WriteLine($"[gateway] {remote} <= 0x01f9 cosmetic pick: slot {pick.Slot}, record {pick.RecordId}: not a slot or not a record, ignored");
            return;
        }
        Console.WriteLine($"[gateway] {remote} <= 0x01f9 cosmetic pick: slot {pick.Slot}, record {pick.RecordId} (hash 0x{record?.MtxTypeHash ?? 0:x8}), option {pick.Option}");
        string character = CharacterName(conn);
        CharacterSaves.SetCosmetic(character, pick.Slot, record is null ? null : new CharacterSaves.SavedCosmetic(record.Id, pick.Option));
        conn.WornCosmetics = null;
        if (pick.Slot == InstanceMicrotransactions.PortalSlot)
        {
            ushort variation = record is not null && Cosmetics.Portals.FirstOrDefault(p => p.Hash == record.MtxTypeHash) is { } portal ? (ushort)portal.Index : (ushort)0;
            conn.CosmeticPortalVariation = variation;
            CharacterSaves.SetPortalCosmetic(character, variation);
            conn.WornCosmetics = null;
            await ResendMapPortals(remote, stream, ciphers, conn);
        }
        await SendWornCosmetics(remote, stream, ciphers, conn, "after the pick");
        // The model and the stats: skins, attachments, character effects, footprints, apparitions.
        await SendEquipment(remote, stream, ciphers, conn, ItemsOf(conn));
        if (pick.Slot is 18 or 19) await SpawnPets(remote, stream, ciphers, conn);
        if (pick.Slot == PortraitSlot)
        {
            ushort portrait = WornPortrait(conn);
            await Send(remote, stream, ciphers, InstanceEntities.PlayerPortraitUpdate(PlayerId, portrait), $"0x02ba player portrait {portrait}");
            // The character screen's list comes from Auth's account store.
            var told = await InstanceControl.CallAsync(new ControlRequest { Op = "set_portrait", Ticket = conn.Ticket, Portrait = portrait });
            if (!told.Ok) Console.WriteLine($"[gateway] {remote} set_portrait {portrait}: {told.Error}");
        }
    }

    // The worn pets (Pet slots 18 and 19): one PetObject each beside the player, owned by it (PetAi). A pick removes the
    // old ones first. RunPetFollow walks them after the player.
    internal static async Task SpawnPets(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        List<uint> old;
        lock (conn.Pets) { old = [.. conn.Pets.Keys]; conn.Pets.Clear(); }
        foreach (uint id in old)
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(id), $"0x{InstanceProtocol.RemoveObjectId:x4} remove pet 0x{id:x}");
        double now = conn.Clock.Elapsed.TotalSeconds;
        var (px, py) = conn.Walk.Position(now);
        foreach (var worn in WornCosmetics(conn).Where(w => w.Slot is 18 or 19))
        {
            if (MtxEffects.Of(worn.MtxTypeHash) is not { PetType: { Length: > 0 } type } fx) continue;
            if (!PetObject.CanSerialize(fx.PetComponents, fx.PetStates is not null))
            {
                Console.WriteLine($"[gateway] {remote} pet {type}: components {string.Join(", ", fx.PetComponents ?? [])} not supported, not spawned");
                continue;
            }
            var (x, y) = PetSpot(conn, (px, py), worn.Slot == 18 ? 1 : -1);
            uint id = conn.NextObjectId++;
            var pet = new PetObject(x, y, PlayerId, fx.PetComponents, fx.PetStates);
            await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(type), pet.Blob()),
                $"0x02b9 create pet 0x{id:x} {type} at ({x}, {y})");
            await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), $"0x01a9 pet 0x{id:x} enters wake zone");
            lock (conn.Pets) conn.Pets[id] = new SpawnedPet(conn.NewWalk(x, y, conn.Walk.Speed), worn.Slot == 18 ? 1 : -1);
        }
        if (!conn.PetFollowRunning) { conn.PetFollowRunning = true; _ = RunPetFollow(remote, stream, ciphers, conn); }
    }

    // Where a pet stands by the player: PetDistance cells to the player's side (side +1 or −1, the two Pet slots), on the
    // walk map; the player's own cell if that side is blocked.
    internal static (int X, int Y) PetSpot(ClientState conn, (int X, int Y) player, int side)
    {
        (int X, int Y) spot = (player.X + side * PetDistance, player.Y + PetDistance / 2);
        return conn.Layout.Walkable is { } w && !w(spot.X, spot.Y) ? player : spot;
    }

    // Pets follow the player: when one is farther than PetFollowRange from the player it walks (0x1ab Move, as monsters do)
    // to its spot beside the player, which keeps it near but never on top of the player. Ends with the connection.
    internal static async Task RunPetFollow(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        const ushort Move = 0x2909;
        try
        {
            while (!conn.Closed)
            {
                await Task.Delay(PlayerTickMs);
                if (conn.Paused) continue;
                double now = conn.Clock.Elapsed.TotalSeconds;
                var player = conn.Walk.Position(now);
                var orders = new List<(uint Id, (int X, int Y) From, (int X, int Y) To)>();
                lock (conn.Pets)
                    foreach (var (id, pet) in conn.Pets)
                    {
                        var here = pet.Walk.Position(now);
                        if (Dist(here, player) <= PetFollowRange) continue;
                        var to = PetSpot(conn, player, pet.Side);
                        if (pet.Target is { } last && Dist(last, to) < 3) continue;   // already on its way there
                        pet.Target = to;
                        pet.Walk.SetSpeed(Math.Max(conn.Walk.Speed, 1), now);
                        orders.Add((id, pet.Walk.Order(to.X, to.Y, now), to));
                    }
                foreach (var (id, from, to) in orders)
                    await Send(remote, stream, ciphers, InstanceProtocol.ActionStarted(id, from.X, from.Y, to.X, to.Y, Move, 0xffff, 0x0408), null);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The connection closed while sending.
        }
    }
}
