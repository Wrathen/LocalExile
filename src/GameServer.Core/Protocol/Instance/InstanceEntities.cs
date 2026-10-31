using System.Buffers.Binary;
using System.Text;
using GameServer.Core.Protocol.Login;

namespace GameServer.Core.Protocol.Instance;

/// <summary>
/// Entity messages on the instance connection (Agents/docs/re/010, "Entering the area"). Message fields are big-endian like
/// every other message. The component blob inside 0x2b9 is read with plain memcpy (0x140e28cf0 and friends), so its
/// multi-byte fields are <b>little-endian</b>.
/// </summary>
public static class InstanceEntities
{
    public const ushort LocalPlayerId = 0x22, CreateObjectId = 0x2b9, AddInventoryId = 0xc0, PassiveSlotsId = 0x3d;

    /// <summary>
    /// The u32 an object type is known by: MurmurHash2 (seed 0) over the metadata path with each UTF-16 unit narrowed to
    /// a byte (0x1424748a0 → 0x140df8570). The client's Game::GameObjectTypeRegister is built from its own dat tables
    /// (0x141ec18c0; Characters.dat rows among them), so nothing has to be registered over the network.
    /// </summary>
    public static uint ObjectTypeHash(string path)
    {
        Span<byte> b = path.Length <= 256 ? stackalloc byte[path.Length] : new byte[path.Length];
        for (int i = 0; i < path.Length; i++) b[i] = (byte)path[i];
        const uint m = 0x5bd1e995;
        uint h = (uint)b.Length;
        int p = 0;
        for (; b.Length - p >= 4; p += 4)
        {
            uint k = BinaryPrimitives.ReadUInt32LittleEndian(b[p..]) * m;
            k ^= k >> 24;
            h = h * m ^ k * m;
        }
        switch (b.Length - p)
        {
            case 3: h ^= (uint)b[p + 2] << 16; goto case 2;
            case 2: h ^= (uint)b[p + 1] << 8; goto case 1;
            case 1: h = (h ^ b[p]) * m; break;
        }
        h = (h ^ h >> 13) * m;
        return h ^ h >> 15;
    }

    /// <summary>
    /// S→C 0x3d (dispatcher case 0x11, class 0x1435dcd00, write 0x14219eb40): the three **Atlas** passive tree presets.
    /// u16 +0x28, u16 +0x2a, flags (bit0 +0x2c, bit1 +0x2d = no list, bits 2/3/4 → create or update preset 0/1/2 via
    /// 0x142155bf0), then unless bit1 a vec16 of raw (little-endian) u16 node ids. Each flagged preset gets the list and
    /// the two u16 (0x141eec7e0): +0x28 → +0x468, +0x2a → +0x46c, its points (0x141eed000: "atlas passive skills
    /// allocated but only allowed"). The presets at instance+0xc2f8 (active index +0xc2f0) are read when the area UI is
    /// built (0x140785160), so all three are created, empty.
    /// </summary>
    public static byte[] PassiveSlots(ushort u28 = 0, ushort u2a = 0, byte presetMask = 0x1c,
        IReadOnlyList<ushort>? nodes = null)
    {
        var w = new LoginWriter().U16(PassiveSlotsId).U16(u28).U16(u2a).U8(presetMask).U16((ushort)(nodes?.Count ?? 0));
        foreach (ushort node in nodes ?? []) w.U8((byte)node).U8((byte)(node >> 8));   // raw little-endian u16
        return w.ToArray();
    }

    /// <summary>
    /// C→S 0x3e (7 bytes): the Atlas tree's allocate click. u8 preset index (0..2), u32 BE PassiveSkillGraphId. The
    /// client's active preset is at Atlas tree +0xc2f0 (sender 0x1403cc410).
    /// </summary>
    public static (byte Preset, uint Node) ReadAtlasAllocate(ReadOnlySpan<byte> m) =>
        (m[2], (uint)(m[3] << 24 | m[4] << 16 | m[5] << 8 | m[6]));

    public const ushort AtlasAllocateId = 0x03e, AtlasRefundId = 0x03f;
    public const int AtlasAllocateLength = 7, AtlasRefundLength = 8;

    /// <summary>C→S 0x3f (8 bytes): refund one Atlas node. u8 preset, u32 BE node, u8 flag (0 seen).</summary>
    public static (byte Preset, uint Node, byte Flag) ReadAtlasRefund(ReadOnlySpan<byte> m) =>
        (m[2], (uint)(m[3] << 24 | m[4] << 16 | m[5] << 8 | m[6]), m[7]);

    public const ushort AddBuffId = 0x1c1, RemoveBuffId = 0x1c2;

    /// <summary>
    /// S→C 0x1c1 (world group, class 0x1435ddb78, read 0x141fdf870; world handler case 0x1c1 → the object's Buffs
    /// component, 0x141e11720): object header, u16 buff instance (+0x20, what 0x1c2 removes), u16 BuffDefinitions row,
    /// u32 +0x64 (the source object), u16 +0x60 (the source skill; a flask buff's flask-bar cell), u8 flags (bit0 u8 u32
    /// u8 u16, bit1 +0x68: an f32 duration in seconds, default +infinity, so <paramref name="u68"/> takes its bits, bit2
    /// u16 stacks +0x70 (default 1), bit3 u16 ActionTypes HASH16 +0x74 then u16 skill id +0x72 (the active icon),
    /// bit4 u16 BuffVisuals override (default the row's), bit5 u8 +0x80
    /// (default 2), bit6 → +0x81, bit7 f32 +0x6c (default 1.0)), then a u16 +0x82 when the row's bool at +0xff
    /// (<c>__col44</c>) is set (<paramref name="u82"/>: the caller must give it exactly for those rows), one u32
    /// per GrantedStats value when its IsRecovery (+0x7c) is set (<paramref name="values"/>: the caller must give exactly
    /// that many, or the client misreads the message), and a u32-counted list of u32 (<paramref name="list"/>, buff +0x80: a
    /// RecordSkillUseLocations buff's first entry, which its behaviour reads unchecked). The buff's visual (BuffVisuals)
    /// plays while it lasts. u68 is the raw IEEE-754 f32 duration in seconds (not integer milliseconds);
    /// f6c is the countdown rate (default 1.0).
    /// </summary>
    public static byte[] AddBuff(uint objectId, ushort buffInstance, ushort buffRow, uint sourceId = 0, ushort sourceSkill = 0,
        IReadOnlyList<uint>? values = null, uint? u68 = null, float? f6c = null, ushort? visual = null,
        (ushort Id, ushort Action)? activeSkill = null, bool auraSource = false, byte? displayState = null, ushort? stacks = null,
        ushort? u82 = null, IReadOnlyList<uint>? list = null)
    {
        // flags bit1: u32 +0x68; bit7: f32 +0x6c (read right after bit1's field); bit4: u16 BE a BuffVisuals row in place
        // of the BuffDefinitions row's (+0x38; read after bits 2 and 3, a row past the table keeps none): a skill
        // effect's art (BuffVisualsArtVariations).
        // Bit3 associates a skill buff with the skill-bar icon: ActionTypes HASH16 first, then the server skill id.
        // SourceSkill alone identifies the caster's source and does not enable AuraFrame/AuraCircle.
        byte flags = (byte)((u68 is null ? 0 : 0x02) | (f6c is null ? 0 : 0x80) | (visual is null ? 0 : 0x10)
            | (activeSkill is null ? 0 : 0x08) | (auraSource ? 0x01 : 0) | (displayState is null ? 0 : 0x20) | (stacks is null ? 0 : 0x04));
        var w = new LoginWriter().U16(AddBuffId).U32(objectId).U32(0).U16(0).U16(buffInstance).U16(buffRow).U32(sourceId).U16(sourceSkill).U8(flags);
        // Nonzero +0x76 becomes Buff +0x30: Render selects EPKFiles2 (m_AuraEffectPack), rather than
        // EPKFiles1 (m_EffectPack). All four bit0 fields must precede the remaining optional fields.
        // The supported player auras use EPKs, not a radius-scaled MiscAnimated3 ring.
        if (auraSource) w.U8(1).U32(0).U8(0).U16(0);
        if (u68 is { } a) w.U32(a);
        if (f6c is { } f) w.U32(BitConverter.SingleToUInt32Bits(f));
        if (stacks is { } n) w.U16(n);   // bit2: the stacks (charges) count, after bit7's field and before bit3's
        if (activeSkill is { } skill) w.U16(skill.Action).U16(skill.Id);
        if (visual is { } v) w.U16(v);
        if (displayState is { } state) w.U8(state);
        if (u82 is { } row) w.U16(row);
        static byte[] Be(IReadOnlyList<uint>? u) => [.. (u ?? []).SelectMany(v => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v })];
        return w.Bytes(Be(values)).U32((uint)(list?.Count ?? 0)).Bytes(Be(list)).ToArray();
    }

    /// <summary>S→C 0x1c2 (class 0x1435dd918, read 0x141fdfdd0): object header, u16 buff instance: the buff ends.</summary>
    public static byte[] RemoveBuff(uint objectId, ushort buffInstance) =>
        new LoginWriter().U16(RemoveBuffId).U32(objectId).U32(0).U16(0).U16(buffInstance).ToArray();

    public const ushort CharacterPassivesId = 0x33, AllocatePassiveId = 0x34, MasteryEffectId = 0x35, PanelToggleId = 0x123;
    public const int MasteryEffectLength = 6;

    /// <summary>
    /// C→S 0x34 (7 bytes, live 2026-10-18): the Passive Skill Tree asks for a node. u16 high node bits, u16 the node's
    /// PassiveSkillGraphId (big-endian: 0xdfb0 = 57264 Spell Damage and Mana, next to the Witch's start), u8 expansion flag.
    /// The client waits for the tree back (S→C 0x33); without it the node is not allocated. C→S 0x123 (u8 1 / 0) is sent
    /// when the panel opens and closes.
    /// </summary>
    public static (ushort U2, ushort Node, byte U6) ReadAllocatePassive(ReadOnlySpan<byte> m) =>
        ((ushort)(m[2] << 8 | m[3]), (ushort)(m[4] << 8 | m[5]), m[6]);

    /// <summary>
    /// C→S 0x35 (6 bytes): selecting a mastery effect. u16 node (big-endian, PassiveSkillGraphId), u16 effect
    /// (big-endian, PassiveSkillMasteryEffects.dat HASH16). Captured: 003598091194 = Block Mastery 38921, effect 4500
    /// (+1% spell block per 5% attack block).
    /// </summary>
    public static (ushort Node, ushort Effect) ReadMasteryEffect(ReadOnlySpan<byte> m) =>
        ((ushort)(m[2] << 8 | m[3]), (ushort)(m[4] << 8 | m[5]));

    public const ushort RefundPassiveId = 0x36, RefundAllPassivesId = 0x37;
    public const int RefundPassiveLength = 8;

    /// <summary>
    /// C→S 0x36 (8 bytes, class 0x1435bf610, write 0x141f92b00, built in 0x1403cc410 next to 0x34): refund one node.
    /// u32 the node (big-endian; PassiveSkillGraphId, or a cluster jewel's expansion id), u8 1 for an expansion node,
    /// u8 1 when the tree was opened as a gold respec (Bestel's Respecialisation: tree state +0x17b8 == 2), 0 when it
    /// uses refund points. "Apply refunds" sends one per picked node: captured 003600001FC70001 and 003600002B780001
    /// (8135, 11128). C→S 0x37 (id only, sent from 0x1409d03bb / 0x1409da517) is "Refund All".
    /// </summary>
    public static (uint Node, bool Expansion, bool Gold) ReadRefundPassive(ReadOnlySpan<byte> m) =>
        ((uint)(m[2] << 24 | m[3] << 16 | m[4] << 8 | m[5]), m[6] != 0, m[7] != 0);

    /// <summary>
    /// S→C 0x33 (dispatcher case 0xf, class 0x1435dca60, read 0x14219e3f0): the character's passive skill tree, at
    /// instance+0xc2d8 (handler 0x141ef2710). u16 +0x88 → tree +0x270 (probably refund points), u16 +0x8a → +0x274
    /// (extra_skill_points: quests), u16 +0x8c → +0x27c (ascendancy points), u8 +0x90 → +0x26c the **level** (the client's
    /// points_from_level = (level − 1) × points per level, 0x141ef3e60), u8 +0x8f → +0x268 the class byte (Characters.dat
    /// IntegerId plus ClassNo packed by CharacterClassByte: the tree's start), u8 +0x8e → +0x269, u8 flags (bit0 +0x91, bit1 +0x92 = no
    /// lists), then unless bit1: vec16 u16 allocated nodes (PassiveSkills.dat PassiveSkillGraphId, raw little-endian),
    /// vec16 u16, vec16 u16, vec16 u32 mastery effects (node | effect &lt;&lt; 16), vec16 u32. The client drops nodes not
    /// connected to its class's start and refuses more nodes than its points allow. A mastery effect is the
    /// PassiveSkillMasteryEffects.dat HASH16 (the value in the client's passive tree data), not the data-table row.
    /// Both lists are sent sorted by node: the handler diffs them against its own sorted list (+0x180, set difference
    /// 0x141f145b0), and an unsorted list makes kept nodes look newly allocated. Each "allocated" node fires the tree's
    /// allocation event (0x141ef69e0), which takes Bestel's Respecialisation screen back to the normal tree (0x1409dd800
    /// → 0x1409de680 with 0; traced live 2026-10-19 after Apply refunds).
    /// </summary>
    public static byte[] CharacterPassives(byte level, byte classByte, IReadOnlyList<ushort>? nodes = null, ushort questPoints = 0,
        ushort refundPoints = 0, ushort ascendancyPoints = 0, IReadOnlyDictionary<ushort, ushort>? masteryEffects = null,
        IReadOnlyList<ushort>? clusterNodes = null, IReadOnlyDictionary<ushort, ushort>? tattoos = null)
    {
        var w = new LoginWriter().U16(CharacterPassivesId).U16(refundPoints).U16(questPoints).U16(ascendancyPoints)
            .U8(level).U8(classByte).U8(0).U8(0).U16(checked((ushort)(nodes?.Count ?? 0)));
        foreach (ushort n in (nodes ?? []).Order()) w.U8((byte)n).U8((byte)(n >> 8));
        // The second list stays empty; the cluster jewel nodes of the expansion cells (the client's own small ids,
        // not PassiveSkillGraphIds) go in the third, which is where the client looks for them.
        w.U16(0);
        var cluster = (clusterNodes ?? []).Order().ToList();
        w.U16(checked((ushort)cluster.Count));
        foreach (ushort n in cluster) w.U8((byte)n).U8((byte)(n >> 8));
        var effects = masteryEffects is null ? [] : masteryEffects
            .Where(kv => nodes?.Contains(kv.Key) == true).OrderBy(kv => kv.Key).ToList();
        w.U16(checked((ushort)effects.Count));
        foreach (var (node, effect) in effects) w.RawU32(node | ((uint)effect << 16));
        // The fifth list is the applied tattoos: node | PassiveSkillOverrides.HASH16 << 16. The client stores it at
        // tree +0x1f8 and looks the override up in PassiveSkillOverrides.dat for the replacement passive's name,
        // icon and stats.
        var applied = (tattoos ?? new Dictionary<ushort, ushort>()).OrderBy(kv => kv.Key).ToList();
        w.U16(checked((ushort)applied.Count));
        foreach (var (node, row) in applied) w.RawU32(node | ((uint)row << 16));
        return w.ToArray();
    }

    /// <summary>S→C 0x22 (u32, dispatcher case 0xb): the local player's entity id (world+0x980, setter 0x142186fa0).</summary>
    public static byte[] SetLocalPlayer(uint entityId) => new LoginWriter().U16(LocalPlayerId).U32(entityId).ToArray();

    /// <summary>
    /// S→C 0x2b9 (world group, class 0x1435dd8f8, write 0x141fe04d0): entity header (u32 id, u32, u16; 0x141f91f60),
    /// u32 object type hash, blob16 component data. An object whose id equals the local player id becomes the player.
    /// </summary>
    public static byte[] CreateObject(uint entityId, uint objectType, byte[] blob, uint headerU32 = 0, ushort headerU16 = 0) =>
        new LoginWriter().U16(CreateObjectId).U32(entityId).U32(headerU32).U16(headerU16).U32(objectType).Blob(blob).ToArray();

    public const ushort MonsterTypesId = 0x20;

    /// <summary>
    /// S→C 0x20 (instance group, dispatcher case 9, class 0x1435dc320, read 0x1421a4f30): monster types at a level.
    /// u16 count, u16 preload (+0x10, the first n new types are loaded at once), then count × raw u16 (little-endian)
    /// MonsterVarieties row and count × u8 level. Per pair the client registers "&lt;variety Id&gt;@&lt;level&gt;"
    /// (0x141ec2540; level 0 registers the plain Id, 0x141ec28e0). The Monster component takes its level from that
    /// suffix (1..100) and throws "Creating monster with no level" without one, so a monster's 0x2b9 type is
    /// <see cref="MonsterObject.TypePath"/>.
    /// </summary>
    public static byte[] MonsterTypes(IReadOnlyList<(ushort Row, byte Level)> types, ushort preload = ushort.MaxValue)
    {
        var w = new LoginWriter().U16(MonsterTypesId).U16((ushort)types.Count).U16(Math.Min(preload, (ushort)types.Count));
        foreach (var (row, _) in types) w.U8((byte)row).U8((byte)(row >> 8));
        foreach (var (_, level) in types) w.U8(level);
        return w.ToArray();
    }

    public const ushort StatsUpdateId = 0x1b3;

    /// <summary>
    /// S→C 0x1b3 (world group, class 0x1435dda98, read 0x141fe1350): entity header, unsigned compact count, then per stat
    /// an unsigned compact key (Stats.dat row) and a signed compact value. The world handler (0x142187480) sets each on the
    /// object's Stats component (0x1424cfaf0).
    /// </summary>
    public const ushort ComponentUpdateId = 0x2ba;

    /// <summary>
    /// S→C 0x2ba (world group, read 0x141fe05b0): world header, then blob16 = u8 component index (in the object's
    /// component order, 04 §3) + that component's vtable+0xa0 reader data (world handler 0x142187480).
    /// </summary>
    public static byte[] ComponentUpdate(uint entityId, byte componentIndex, byte[] data) =>
        new LoginWriter().U16(ComponentUpdateId).U32(entityId).U32(0).U16(0).Blob([componentIndex, .. data]).ToArray();

    /// <summary>S→C 0x2ba to an object's StateMachine (reader 0x141e3f4a0): all its shared states and timers again
    /// (<see cref="StateMachineData.Write"/>); the client runs the events of every state whose value changed.</summary>
    public static byte[] StateMachineUpdate(uint entityId, byte componentIndex, StateMachineData states)
    {
        var w = new BlobWriter();
        states.Write(w);
        return ComponentUpdate(entityId, componentIndex, w.ToArray());
    }

    /// <summary>S→C 0x2ba to an object's Transitionable (reader 0x1421c2310): u8 target state; the client steps toward
    /// it one state at a time and runs each step's on_transition events (12-quests.md §6).</summary>
    public static byte[] TransitionableUpdate(uint entityId, byte componentIndex, byte state) =>
        ComponentUpdate(entityId, componentIndex, [state]);

    /// <summary>
    /// S→C 0x2ba to an object's Animated component (reader <c>0x141e57490</c>): flags bit0 + a u32-counted UTF-16 .ao path
    /// swaps its animated object, which then plays the new one's default animation (a projectile's impact: Fireball →
    /// Metadata/Effects/Spells/fireball/explode_new2.ao, "animate": the splode particles and the End sound).
    /// </summary>
    public static byte[] AnimatedModel(uint entityId, byte animatedIndex, string ao) =>
        ComponentUpdate(entityId, animatedIndex, new BlobWriter().U8(1).WStr32(ao).ToArray());


    /// <summary>
    /// Changes the player's skills (0x2ba to the Actor, flags 0x20 → <c>0x142198800</c>): u8 records, u8 ids, the skill
    /// records (as in the 0x40 list: added, or replacing the one with the same id), the u16 ids to remove
    /// (<c>0x142198560</c>: found by id in the skill manager, its remove handler runs), then u8-counted soul seeds
    /// (ActiveSkills row, capacity) and updates (row, current, new capacity). Empty lists preserve existing budgets.
    /// Unlike the 0x40 list it removes skills, and it tells the client's UI that the skills changed
    /// (<c>0x140b14b00</c> before and after, when there is a record or an id): the character panel's skill list follows it.
    /// </summary>
    public static byte[] PlayerSkillsChange(uint playerId, IReadOnlyList<GrantedSkill> skills, IReadOnlyList<ushort> removed,
        IReadOnlyList<VaalSoulSeed>? soulSeeds = null, IReadOnlyList<VaalSoulUpdate>? soulUpdates = null)
    {
        var w = new BlobWriter().U8(0x20).U8(0).U8((byte)skills.Count).U8((byte)removed.Count);
        foreach (var s in skills) PlayerObject.WriteSkillRecord(w, s);
        foreach (var id in removed) w.U16(id);
        w.U8(checked((byte)(soulSeeds?.Count ?? 0)));
        foreach (var s in soulSeeds ?? []) w.U32(s.ActiveSkillsRow).U32(s.Capacity);
        w.U8(checked((byte)(soulUpdates?.Count ?? 0)));
        foreach (var s in soulUpdates ?? []) w.U32(s.ActiveSkillsRow).U32(s.Current).U32(s.NewCapacity);
        return ComponentUpdate(playerId, PlayerObject.ActorComponentIndex, w.ToArray());
    }

    /// <summary>Actor 0x40's cooldown trailer (0x142197d24..0x142197fd8), without replacing any skill.
    /// Existing entries are replaced, not consumed/appended: remaining and total BASE seconds, recovery rate, flags.
    /// A zero remaining budget clears the entry vector. Native skill base duration and cooldown group stay intact.</summary>
    public static byte[] PlayerSingleUseCooldowns(uint playerId, IReadOnlyList<SingleUseCooldownUpdate> cooldowns,
        IReadOnlyList<VaalSoulBudget>? souls = null)
    {
        var w = new BlobWriter().U8(0x40).U8(0).U8(0);
        PlayerObject.WriteSoulBudgets(w, souls);
        w.U32((uint)cooldowns.Count);
        foreach (var c in cooldowns)
        {
            if (!float.IsFinite(c.RemainingBaseSeconds) || c.RemainingBaseSeconds < 0
                || !float.IsFinite(c.TotalBaseSeconds) || c.TotalBaseSeconds <= 0
                || !float.IsFinite(c.RecoveryRate) || c.RecoveryRate < 0)
                throw new ArgumentOutOfRangeException(nameof(cooldowns));
            w.U16(c.SkillId).U32(c.RemainingBaseSeconds > 0 ? 1u : 0u);
            if (c.RemainingBaseSeconds > 0)
                w.F32(c.RemainingBaseSeconds).F32(c.TotalBaseSeconds).F32(c.RecoveryRate).U32(0xffffffff);
        }
        return ComponentUpdate(playerId, PlayerObject.ActorComponentIndex, w.ToArray());
    }

    /// <summary>
    /// The player's experience (0x2ba to the Player component, reader <c>0x141e7ee30</c>): u16 flags, bit0 = u32 experience,
    /// which goes through the same setter as creation (<c>0x141e7f6b0</c>): the level is derived again from it and, when
    /// it changed, the component's level-change handler (vtable +0x108) runs. The other flags stay off.
    /// </summary>
    public static byte[] PlayerExperienceUpdate(uint playerId, uint experience) =>
        ComponentUpdate(playerId, PlayerObject.PlayerComponentIndex, new BlobWriter().U16(1).U32(experience).ToArray());

    /// <summary>
    /// The player's quest flags (0x2ba to the Player component, reader 0x141e7ee30): u16 flags, bit1 (0x02) = the whole
    /// quest-flag set again (<see cref="PlayerObject.WriteFlagSet"/>, QuestFlags.dat rows). The World panel shows an area's
    /// pin only when the area's access flags (WorldAreas +0x54) are all set (0x140797750). Bit3 (0x08) would replace the
    /// second set (+0x1d0), bit9 (0x200) the 9 raw bytes (+0x1e8).
    /// </summary>
    public static byte[] PlayerQuestFlagsUpdate(uint playerId, IEnumerable<int> questFlags)
    {
        var w = new BlobWriter().U16(2);
        PlayerObject.WriteFlagSet(w, questFlags);
        return ComponentUpdate(playerId, PlayerObject.PlayerComponentIndex, w.ToArray());
    }

    /// <summary>
    /// The player's two Hideouts.dat keys (0x2ba to the Player component, reader 0x141e7ee30): u16 flags, bit4 (0x10) =
    /// u16 + u16, looked up by HASH16 like the creation blob's, then the component's vtable +0x130 (hideout changed).
    /// </summary>
    public static byte[] PlayerHideoutsUpdate(uint playerId, ushort hideout, ushort hideout2) =>
        ComponentUpdate(playerId, PlayerObject.PlayerComponentIndex, new BlobWriter().U16(0x10).U16(hideout).U16(hideout2).ToArray());

    /// <summary>The player's league quest flags (0x2ba to the Player component): u16 flags, bit3 (0x08) = the whole
    /// second flag set (+0x1d0) again, as <see cref="PlayerObject.WriteFlagSet"/>.</summary>
    public static byte[] PlayerLeagueQuestFlagsUpdate(uint playerId, IEnumerable<int> flags)
    {
        var w = new BlobWriter().U16(8);
        PlayerObject.WriteFlagSet(w, flags);
        return ComponentUpdate(playerId, PlayerObject.PlayerComponentIndex, w.ToArray());
    }

    /// <summary>
    /// The player's portrait (0x2ba to the Player component, reader 0x141e7ee30): u16 flags, bit6 (0x40) = u16 → +0x1aa,
    /// the <c>MicrotransactionCharacterPortraitVariations</c> variation (its <c>__col1</c>; 0 = the class's own). The
    /// character panel and the other in-game portraits build their art from it (0x14081e730 → 0x142426b00:
    /// Art/2DArt/UIImages/Common/PortraitVariations/…).
    /// </summary>
    public static byte[] PlayerPortraitUpdate(uint playerId, ushort portrait) =>
        ComponentUpdate(playerId, PlayerObject.PlayerComponentIndex, new BlobWriter().U16(0x40).U16(portrait).ToArray());

    public const ushort GoldId = 0x2a9;

    /// <summary>S→C 0x2a9 (class 0x14316c420, read 0x140913050): u32 BE, the character's gold. The instance dispatcher (case
    /// 0xe2, 0x14214dbe1) stores it at session +0xc40c (the inventory's and the gold respec's amount) and plays
    /// Gold_Spend.ogg when it drops.</summary>
    public static byte[] Gold(uint amount) => new LoginWriter().U16(GoldId).U32(amount).ToArray();

    /// <summary>The player's Inventories component again (0x2ba): what its model wears after an equipment change.</summary>
    public static byte[] PlayerEquipmentUpdate(uint playerId, IReadOnlyList<VisibleItem> equipped)
    {
        var w = new BlobWriter();
        PlayerObject.WriteInventories(w, equipped);
        return ComponentUpdate(playerId, PlayerObject.InventoriesComponentIndex, w.ToArray());
    }

    public static byte[] StatsUpdate(uint entityId, IReadOnlyList<(uint Stat, int Value)> stats)
    {
        var w = new LoginWriter().U16(StatsUpdateId).U32(entityId).U32(0).U16(0);
        InstanceProtocol.CompactUInt(w, (uint)stats.Count);
        foreach (var (stat, value) in stats)
        {
            InstanceProtocol.CompactUInt(w, stat);
            InstanceProtocol.CompactInt(w, value);
        }
        return w.ToArray();
    }
}

public static partial class InstanceInventories
{
    /// <summary>One Inventories.dat row the player starts with: row index (the manager's type slot), the row's InventoryType
    /// enum (first u32) and its grid size.</summary>
    public readonly record struct Slot(byte Row, byte Kind, byte Width, byte Height)
    {
        /// <summary>Holds one item, drawn as a single box (equipment, the cursor): Inventories.dat column 3, sent as flag bit 0
        /// (+0x148). Without it an equipment slot is drawn as a grid of cells.</summary>
        public bool SingleItem => Array.IndexOf(SingleItemRows, Row) >= 0;

        /// <summary>The character's own fixed slots (equipment and the flask bar): Inventories.dat column 4, sent as flag
        /// bit 1 (+0x149). With it the client checks what goes in: the slot's item classes (a wand is red over the body
        /// armour slot) and the item's attribute requirements against the attributes the Player component caches
        /// (0x141e802d0; see Program.SendPlayer for why they must arrive as a stats update).</summary>
        public bool Equipment => Array.IndexOf(EquipmentRows, Row) >= 0;

        public byte Flags => (byte)((SingleItem ? 1 : 0) | (Equipment ? 2 : 0));
    }

    /// <summary>The Inventories.dat rows whose column 4 is set (3.29.3b).</summary>
    static readonly byte[] EquipmentRows = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 54, 78, 102];

    /// <summary>The Inventories.dat rows whose column 3 is set (3.29.3b).</summary>
    static readonly byte[] SingleItemRows =
        [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 15, 16, 17, 18, 19, 20, 21, 22, 27, 28, 31, 33, 35, 37, 41, 42, 43, 44, 54, 55,
         66, 67, 71, 74, 78, 81, 82, 93, 98, 100, 102, 103, 105, 109, 111];

    public const byte CursorRow = 12;

    /// <summary>Every Inventories.dat row of 3.29.3b (117, dumped from Data/Inventories.datc64): kind = the row's first
    /// u32, width = second u32, height = byte 10 after the Id. The client looks inventories up by row from many UI places
    /// (409 call sites of 0x141d621a0 across ~50 rows), so an empty inventory is registered for every row.</summary>
    public static readonly Slot[] PlayerDefaults =
    [
        new(0, 0x00, 12, 5), new(1, 0x01, 2, 3), new(2, 0x02, 2, 4), new(3, 0x03, 2, 4), new(4, 0x04, 2, 2), new(5, 0x05, 1, 1),
        new(6, 0x06, 1, 1), new(7, 0x06, 1, 1), new(8, 0x07, 2, 2), new(9, 0x08, 2, 2), new(10, 0x09, 2, 1), new(11, 0x0a, 5, 2),
        new(12, 0x0b, 4, 4), new(13, 0x0c, 3, 2), new(14, 0x02, 2, 4), new(15, 0x03, 2, 4), new(16, 0x0f, 2, 4), new(17, 0x0f, 2, 4),
        new(18, 0x0f, 2, 4), new(19, 0x0f, 2, 4), new(20, 0x0f, 2, 4), new(21, 0x0f, 2, 4), new(22, 0x0f, 2, 4), new(23, 0x0d, 60, 1),
        new(24, 0x0e, 10, 4), new(25, 0x0c, 6, 1), new(26, 0x11, 1, 1), new(27, 0x0f, 4, 8), new(28, 0x0f, 2, 4), new(29, 0x0f, 5, 1),
        new(30, 0x10, 3, 1), new(31, 0x0f, 2, 4), new(32, 0x0f, 2, 5), new(33, 0x0f, 2, 4), new(34, 0x0f, 6, 4), new(35, 0x0f, 2, 4),
        new(36, 0x0f, 4, 1), new(37, 0x0f, 2, 4), new(38, 0x0f, 5, 1), new(39, 0x1c, 4, 1), new(40, 0x1c, 4, 1), new(41, 0x0f, 1, 1),
        new(42, 0x0f, 2, 4), new(43, 0x0f, 2, 4), new(44, 0x0f, 1, 1), new(45, 0x22, 7, 2), new(46, 0x22, 7, 2), new(47, 0x22, 7, 2),
        new(48, 0x22, 7, 2), new(49, 0x22, 7, 2), new(50, 0x22, 7, 2), new(51, 0x22, 7, 2), new(52, 0x22, 7, 2), new(53, 0x22, 7, 2),
        new(54, 0x23, 1, 1), new(55, 0x0f, 1, 1), new(56, 0x24, 107, 12), new(57, 0x25, 100, 4), new(58, 0x0c, 3, 2), new(59, 0x0c, 3, 2),
        new(60, 0x0c, 3, 2), new(61, 0x0c, 3, 2), new(62, 0x3d, 4, 5), new(63, 0x2a, 5, 5), new(64, 0x2b, 3, 1), new(65, 0x0c, 64, 5),
        new(66, 0x2c, 2, 4), new(67, 0x29, 1, 1), new(68, 0x29, 5, 5), new(69, 0x1d, 14, 9), new(70, 0x2d, 6, 5), new(71, 0x0f, 1, 1),
        new(72, 0x30, 6, 2), new(73, 0x31, 37, 10), new(74, 0x32, 1, 1), new(75, 0x0c, 5, 1), new(76, 0x34, 5, 4), new(77, 0x35, 12, 12),
        new(78, 0x36, 1, 2), new(79, 0x29, 2, 4), new(80, 0x29, 2, 4), new(81, 0x29, 1, 1), new(82, 0x37, 1, 1), new(83, 0x0f, 6, 4),
        new(84, 0x00, 4, 5), new(85, 0x38, 6, 5), new(86, 0x0c, 11, 1), new(87, 0x39, 12, 10), new(88, 0x3a, 12, 3), new(89, 0x3b, 6, 7),
        new(90, 0x3b, 6, 7), new(91, 0x3b, 6, 7), new(92, 0x0f, 8, 4), new(93, 0x0f, 4, 4), new(94, 0x0c, 3, 2), new(95, 0x3c, 17, 4),
        new(96, 0x3c, 17, 4), new(97, 0x3c, 17, 4), new(98, 0x11, 1, 2), new(99, 0x11, 1, 1), new(100, 0x11, 1, 2), new(101, 0x3f, 24, 12),
        new(102, 0x06, 1, 1), new(103, 0x0f, 1, 1), new(104, 0x0f, 120, 1), new(105, 0x0f, 2, 4), new(106, 0x0f, 8, 4), new(107, 0x3c, 17, 4),
        new(108, 0x0f, 10, 4), new(109, 0x0f, 1, 1), new(110, 0x0f, 4, 1), new(111, 0x0f, 1, 1), new(112, 0x44, 12, 79), new(113, 0x41, 12, 5),
        new(114, 0x45, 12, 40), new(115, 0x46, 3, 1), new(116, 0x46, 6, 1),
    ];

    /// <summary>
    /// S→C 0xc0 (dispatcher case 0x35, class 0x1431ab2f0, write 0x140b182a0): add an empty inventory. u8 manager
    /// (0x14216aa30: 0 = the player's, at instance+0xc890), u32 inventory id, inventory object (0x1421f4a40 / reader
    /// 0x1421f4cc0: u8 kind +0x140, u8 Inventories.dat row +0x144, u8 width, u8 height, u8 flags, compact-int item count),
    /// u8 +0x28, u8 +0x29. Flags bit 0: <see cref="Slot.SingleItem"/>, bit 1: <see cref="Slot.Equipment"/>. 0x141d625f0 files the id under its row; UI such as the flask bar looks it up by row (row 11).
    /// Each item (0x1421f5680): u32 id, u8 x, u8 y, the item (<see cref="Item.Wire"/>).
    /// </summary>
    public static byte[] AddInventory(uint inventoryId, Slot slot, byte manager = 0, IReadOnlyList<InventoryItem>? items = null)
    {
        var w = new LoginWriter().U16(InstanceEntities.AddInventoryId).U8(manager).U32(inventoryId)
            .U8(slot.Kind).U8(slot.Row).U8(slot.Width).U8(slot.Height).U8(slot.Flags);
        InstanceProtocol.CompactUInt(w, (uint)(items?.Count ?? 0));
        foreach (var it in items ?? []) w.U32(it.Id).U8(it.X).U8(it.Y).Bytes(it.Item.Wire());
        return w.U8(0).U8(0).ToArray();
    }

    /// <summary>S→C 0xc1 (dispatcher case 0x36, 0x141d62700 → 0x141d623b0, class 0x1435c0898): remove an inventory the
    /// client has: u8 manager, u32 inventory id. Used to unload stash tabs the panel no longer shows.</summary>
    public static byte[] RemoveInventory(uint inventoryId, byte manager = 0) =>
        new LoginWriter().U16(RemoveInventoryId).U8(manager).U32(inventoryId).ToArray();

    public const ushort RemoveInventoryId = 0xc1;
}

/// <summary>
/// An area transition (exit) object, `Metadata/MiscellaneousObjects/AreaTransition`: the clickable exit to another area,
/// with its minimap icon. AreaTransition.ot components: Positioned, BaseEvents (server only), Animated, AreaTransition,
/// Stats, ObjectMagicProperties, MinimapIcon, InteractionAction (+ client Render, Targetable).
/// <para>Component deserializers (vtable +0x88, found from the components' static initializers → pool → factory):
/// AreaTransition `0x141e5b870`: u8 flags (low nibble +0xb2 state, bit4 +0xb5, bit5 +0xb6, bit6 +0xb7, bit7 → extra record
/// `0x141e62df0`), then the destination `0x141e62490`, a std::variant read as u8 index + value (0 → u16, 1 → u32,
/// 2 → u16). Index 2 is a WorldAreas.HASH16 (`0x141e5be60` resolves it with `0x1422d9920`, the 0x14/0x1d area lookup).
/// MinimapIcon slot 20 `0x141e6f5d0`: u8 flags (bit0 → u16 icon override, bit3 → variant). ObjectMagicProperties slot 20
/// `0x141e77990`: u8 flags (bit1 → u16 key, bit2 → string …). InteractionAction `0x141e1e240`: u8 flags (bit2 → u8 [+ u16]).
/// Stats as the player's comp1 (`0x1421c2f40`).</para>
/// </summary>
public sealed record AreaTransitionObject(int GridX, int GridY, ushort DestinationHash16, string? Model = null, int Index = 0,
    float Rotation = 0, float? Height = null, string Type = AreaTransitionObject.MetadataPath)
{
    public const string MetadataPath = "Metadata/MiscellaneousObjects/AreaTransition";

    /// <summary>Set for a transition that leads within its own area (a map's boss arena gate): where it takes the player.
    /// Server side only; the blob names the area itself as the destination.</summary>
    public (int X, int Y)? TeleportTo { get; init; }

    /// <summary>Components that read data, in the order the client deserializes them for this type (traced live
    /// 2026-10-16: Positioned, BaseEvents (no data), Animated, AreaTransition (live vtable 0x14346de00), Stats,
    /// ObjectMagicProperties, MinimapIcon, InteractionAction, then two components the loop does not reach).</summary>
    public static readonly string[] ComponentOrder =
        ["Positioned", "Animated", "AreaTransition", "Stats", "ObjectMagicProperties", "MinimapIcon", "InteractionAction"];

    /// <summary>Whether a type with these components (BaseEvents reads nothing) can use this blob: exactly
    /// <see cref="ComponentOrder"/>. Types that add components (e.g. Transitionable) cannot: see
    /// AreaTransitionCatalog.Resolve in the Gateway.</summary>
    public static bool CanSerialize(IEnumerable<string> components) =>
        components.Where(c => c != "BaseEvents").SequenceEqual(ComponentOrder);

    public byte[] Blob()
    {
        var w = new BlobWriter();
        w.U8(0);            // object flags
        w.U8(0);            // base data entry count
        foreach (string c in ComponentOrder)
        {
            switch (c)
            {
                // the float after x, y is stored at +0x29c: the object's rotation (radians), which turns its model.
                // flags0 bit2: a fixed height, u32 +0x290 (whole steps of 7.8125; 0x7fffffff = none) and the float
                // world z +0x2b4, used instead of the terrain height (0x141e35800). Tile objects need it: their .tmd
                // record has its own z.
                case "Positioned":
                    w.I32(GridX).I32(GridY).F32(Rotation).U16(0).U8((byte)(Height is null ? 0 : 0x04)).U8(0);
                    if (Height is float z) w.I32((int)MathF.Round(z / 7.8125f)).F32(z);
                    break;
                // Animated (0x141e56a80): bit0 + a u32-counted UTF-16 .ao path (anything else throws "Filename must
                // end with .ao"): the area transition's model, the white surface highlighted on hover. For a tile object it is the
                // client's virtual "<.tdt folder>/<.tmd record name>.ao" around the record's fixed mesh.
                case "Animated": if (Model is null) w.U8(0); else w.U8(1).WStr32(Model); break;
                // state 0, variant 2 = HASH16, then a compact int (+0xa4) that is read when the type's flag
                // (type+0x78 → +0xb0 → +0x14) is set and the state is 0: true for AreaTransition (traced live).
                case "AreaTransition": w.U8(0).U8(2).U16(DestinationHash16).CompactI(Index); break;
                case "Stats": w.U8(0).U32(0).U32(0).U8(0); break;
                // base magic properties first (0x142201d70: u16 flags, rarity = low 3 bits; u8 +0x7a; sections behind
                // flag bits 0x10/0x40/0x200/0x400/0x800 and, with bit3, 0x80/0x100/0x20), then the component's u8 flags.
                case "ObjectMagicProperties": w.U16(0).U8(0).U8(0); break;
                case "MinimapIcon": w.U8(0); break;
                case "InteractionAction": w.U8(0); break;
            }
        }
        w.U8(0);            // tasks
        return w.ToArray();
    }
}

/// <summary>
/// A monster: a 0x2b9 object whose type is a MonsterVarieties row. The client's object-type register files every
/// MonsterVarieties row (0x31a bytes) under its Id with the row's BaseMonsterTypeIndex (+0x48) as the .ot, but a monster
/// also needs a level: the server registers "&lt;Id&gt;@&lt;level&gt;" with S→C 0x20 (<see cref="InstanceEntities.MonsterTypes"/>)
/// and the 0x2b9 type is <c>ObjectTypeHash(TypePath(Id, level))</c>. The components come from the .ot chain
/// (`Metadata/Monsters/Monster`: Positioned, Stats, ObjectMagicProperties, BaseEvents, Pathfinding, Buffs, Life,
/// Animated, Inventories, Actor, Monster, then whatever the type adds).
/// <para>Every component that reads is one the player or the area transition already uses; Monster itself (vtable 0x1435a6778),
/// BaseEvents and Pathfinding read nothing. Actor reads its two conditional bytes only when the current life (Life pool
/// +0x30, read just before) is below 1, so a living monster has the player's 17 bytes. Positioned's 8-byte prefix belongs
/// to types with a Sector component, which monsters lack.</para>
/// </summary>
public sealed record MonsterObject(int GridX, int GridY, uint Life, IReadOnlyList<string> Components, string? AnimatedObject = null,
    MonsterRarity Rarity = MonsterRarity.Normal, string? Name = null, StateMachineData? StateMachine = null, byte TransitionState = 1,
    ushort Team = 1, IReadOnlyList<GrantedSkill>? Skills = null, IReadOnlyList<(uint Key, int Value)>? Stats = null)
{
    /// <summary>Resolved source flags; absent metadata conservatively writes zero.</summary>
    public MonsterLifeData? LifeData { get; init; }

    /// <summary>Deserialized components of `Metadata/Monsters/Monster` in read order (scripts/re/ot_components.py).</summary>
    public static readonly string[] BaseComponents =
        ["Positioned", "Stats", "ObjectMagicProperties", "BaseEvents", "Pathfinding", "Buffs", "Life", "Animated",
         "Inventories", "Actor", "Monster"];

    /// <summary>Components a type may add after the base list that this blob can write. StateMachine (per-type state
    /// lists from the .ot) needs <see cref="StateMachineData"/>; DiesAfterTime's primary vtable 0x14359f478 has a
    /// bare `ret` slot 17, so it reads nothing.</summary>
    static readonly HashSet<string> Extras = ["Transitionable", "MinimapIcon", "Preload", "Functions", "PlayerClass", "StateMachine", "DiesAfterTime"];

    /// <summary>The registered type path of a variety at a level (1..100), after S→C 0x20 has registered it:
    /// "&lt;variety Id&gt;@&lt;level&gt;" (0x141ec2540; the Monster type parses the number after the last '@').</summary>
    public static string TypePath(string varietyId, byte level) => $"{varietyId}@{level}";

    /// <summary>Whether <see cref="Blob"/> can serialize a type with these deserialized components.</summary>
    /// StateMachine needs the type's shared states and timers (<see cref="StateMachineData"/>).
    public static bool CanSerialize(IReadOnlyList<string> components, bool haveStateMachine = false) =>
        components.Count >= BaseComponents.Length && components.Take(BaseComponents.Length).SequenceEqual(BaseComponents) &&
        components.Skip(BaseComponents.Length).All(c => Extras.Contains(c) && (c != "StateMachine" || haveStateMachine));

    public byte[] Blob()
    {
        if (!CanSerialize(Components, StateMachine is not null)) throw new ArgumentException($"unsupported components: {string.Join(", ", Components)}");
        var w = new BlobWriter();
        w.U8(0);            // object flags
        w.U8(0);            // base data entry count
        foreach (string c in Components)
        {
            switch (c)
            {
                // x, y, f32 +0x29c, u16 team (+0x1e0; the player is team 0: 0 = friendly, green outline; 1 = hostile, red),
                // flags; flags1 0x02 + (hidden 0, shown 1) as for the player.
                case "Positioned": w.I32(GridX).I32(GridY).U32(0).U16(Team).U8(0).U8(0x02).U8(0).U8(1); break;
                case "Stats": w.Stats(Stats).U32(0).U32(0).U8(0); break;
                // base magic properties (0x142201d70, Instance.MagicProperties): u16 flags (bits 0-2 rarity → +0x7c,
                // 0 = normal; bit 3 identified → +0x78, which gates the name and the explicit list), u8 +0x7a, the mod
                // lists; then the own flags. Without bit 3 the client tags the monster "Unidentified". Bit 7 (0x80)
                // adds a custom name: u32 UTF-16 unit count + units (→ +0x18); bit 8 a rare name from Words.dat keys.
                case "ObjectMagicProperties":
                    MagicProperties.Write(w, (byte)Rarity, identified: true, Mods, NameWords, Name);
                    w.U8(0);
                    break;
                case "Buffs": w.U16(0); break;
                // life, mana, energy shield pools (current, reserved, i16, u32, u32), then the flags byte.
                case "Life": Pool(w, Life); Pool(w, 0); Pool(w, 0); w.U8(LifeData?.Flags ?? 0); break;
                case "Animated":
                    if (AnimatedObject is null) w.U8(0);
                    else w.U8(1).WStr32(AnimatedObject);
                    break;
                case "Inventories": w.U8(0).U8(0); break;
                // path, u16, u8, (life >= 1: no conditional bytes), u32, u32, then slot 20's two flag bytes.
                case "Actor":
                    w.WStr32("").U16(0).U8(0).U32(0).U32(0);
                    if (Skills is { Count: > 0 }) { w.U8(0x40).U8(0); PlayerObject.WriteSkillList(w, Skills); }   // its skills (11-skills.md §3)
                    else w.U8(0).U8(0);
                    break;
                // 0x1421c24b0: u8 = state (low 7 bits, → +0x120) | bit 7 (+0x122, "transitioning"). States run 1..num_states
                // (.ot Transitionable num_states, default 2); num_states = fully transitioned (create_transitioned).
                // Emerge monsters are created in 1, buried (Gateway MonsterEmerge).
                // State 0 is invalid: the post-create slot 0x1421c26f0 runs event [(state − 1) × 0x18] and crashed in the
                // event interpreter (0x141ded7b9).
                case "Transitionable": w.U8((byte)(TransitionState & 0x7f)); break;
                case "MinimapIcon": w.U8(0); break;      // slot 20 0x141e6f5d0: u8 flags
                // 0x141e3ee40: u8 shared-state count, u8 shared-timer count (the client throws unless they match the
                // type); per define_shared_state a compact int value + u8 flags (0x02 keeps the sent value), per
                // define_shared_timer a u32 + u8. The .ot defaults are sent.
                case "StateMachine": StateMachine!.Write(w); break;
                // BaseEvents, Pathfinding, Monster, Preload, Functions, PlayerClass: no data.
            }
        }
        w.U8(0);            // tasks
        return w.ToArray();
    }

    private static void Pool(BlobWriter w, uint current) => w.U32(current).U32(0).U16(0).U32(0).U32(0);

    /// <summary>The monster's mods in ObjectMagicProperties' lists (Gateway MonsterModCatalog): the rarity's own mods
    /// and the rolled affixes, one value per stat of each Mods.dat row.</summary>
    public IReadOnlyList<ItemMod> Mods { get; init; } = [];

    /// <summary>A rare monster's name: Words.dat HASH32 keys (flags bit 8), prefix then suffix; a custom
    /// <see cref="Name"/> wins.</summary>
    public IReadOnlyList<uint> NameWords { get; init; } = [];
}

/// <summary>
/// A cosmetic pet (`Metadata/Pet/…`, a <c>Pet.dat</c> row's Id): the .ot chain <c>Metadata/Pet/Pet</c> deserializes
/// Positioned, Stats, Pathfinding, Buffs, Life, Animated, Actor, PetAi (object-types.json). Written as a monster's, with
/// the player's team, no data for Pathfinding, the type's own animated object, and PetAi (slot 20 0x141e7d410): u32 the
/// owner's object id (→ +0x44), which the client resolves to the entity the pet follows (0x141e7cfb0). About one pet type
/// in seven adds components after PetAi: StateMachine (its .ot shared states and timers, <see cref="StateMachineData"/>;
/// left out, the client read past the blob: "Buffer underflow", live 2026-10-23 with TencentGirl03), Functions and
/// BaseEvents (no data). Types with anything else are not written (<see cref="CanSerialize"/>).
/// </summary>
public sealed record PetObject(int GridX, int GridY, uint Owner, IReadOnlyList<string>? Components = null, StateMachineData? StateMachine = null)
{
    public static readonly string[] BaseComponents = ["Positioned", "Stats", "Pathfinding", "Buffs", "Life", "Animated", "Actor", "PetAi"];

    static readonly HashSet<string> Extras = ["StateMachine", "Functions", "BaseEvents"];

    /// <summary>Whether <see cref="Blob"/> can write a pet type with these deserialized components (null: the base list).</summary>
    public static bool CanSerialize(IReadOnlyList<string>? components, bool haveStateMachine) =>
        components is null || components.Count >= BaseComponents.Length && components.Take(BaseComponents.Length).SequenceEqual(BaseComponents)
            && components.Skip(BaseComponents.Length).All(c => Extras.Contains(c) && (c != "StateMachine" || haveStateMachine));

    public byte[] Blob()
    {
        if (!CanSerialize(Components, StateMachine is not null)) throw new ArgumentException($"unsupported pet components: {string.Join(", ", Components!)}");
        var w = new BlobWriter();
        w.U8(0).U8(0);                                                        // object flags, base data entries
        // Positioned: team 0 (the player's), shown, and flags1 0x08 with 1 (→ +0x1e6 bit 0x40), which the client's object
        // collision test (0x141e61540) skips: walkers, the player's own included, pass through the pet.
        w.I32(GridX).I32(GridY).U32(0).U16(0).U8(0).U8(0x0a).U8(0).U8(1).U8(1);
        w.Stats([]).U32(0).U32(0).U8(0);                                      // Stats
        w.U16(0);                                                             // Buffs
        Pool(w, 1); Pool(w, 0); Pool(w, 0); w.U8(0);                          // Life: alive
        w.U8(0);                                                              // Animated: the type's own
        w.WStr32("").U16(0).U8(0).U32(0).U32(0).U8(0).U8(0);                  // Actor: no skills
        w.U32(Owner);                                                         // PetAi
        foreach (string c in (Components ?? BaseComponents).Skip(BaseComponents.Length))
            if (c == "StateMachine") StateMachine!.Write(w);                  // Functions, BaseEvents: no data
        w.U8(0);                                                              // tasks
        return w.ToArray();
    }

    private static void Pool(BlobWriter w, uint current) => w.U32(current).U32(0).U16(0).U32(0).U32(0);
}

/// <summary>
/// A skill's projectile (a Projectiles.dat type, e.g. `Metadata/Projectiles/BurningArrow`): the object the live server
/// creates for a shot. Frida 2026-10-25 on a real Burning Arrow (player object dump of the client's CreateObject): the
/// object has three components ? Positioned, Animated, BaseEvents ? and its whole blob is 28 bytes:
/// <code>
/// 02 00                     object flags 0x02, base data 0
/// &lt;i32 x&gt;&lt;i32 y&gt;&lt;f32 rotation&gt;&lt;u16 0&gt;   Positioned
/// 04 00                     f0 0x04: a fixed height follows; f1 0: no direction floats, no extra visibility bytes
/// &lt;i32 -z/7.8125&gt;&lt;f32 z&gt;
/// 40                        Animated: the projectile's own art (Projectiles.dat AOFiles), flags 0x40
/// 00                        tasks
/// </code>
/// Both height fields are the same world z in two forms: the f32 is z itself and the i32 is -z / 7.8125. World z
/// points <b>down</b>, and z is an absolute world coordinate (the live arrow's i32 -5 / f32 +39.0625 was z = +39.0625;
/// its player stood at z = +78.125, so the arrow was 5 steps above the feet). On raised ground z is negative: the
/// hideout's player at z = -78.37 gets the torso at z = -117.1875, i.e. i32 +15 / f32 -117.1875.
/// Height takes z / 7.8125; the 0x1ba endpoint instead takes the encoded i32 marker (+15 in that example),
/// and the native move negates it to recover world z. 0x1b7 copies the encoded marker and float pair directly.
/// <see cref="AnimatedComponentIndex"/> is the index the impact's animation update (0x2ba) must use.
/// </summary>
public sealed record ProjectileObject(string TypePath, int GridX, int GridY, float Rotation = 0, ushort Team = 0,
    int? Height = null)
{
    public string? AnimatedObject { get; init; }
    /// <summary>Animated in the object's component order (Positioned, Animated, BaseEvents).</summary>
    public const byte AnimatedComponentIndex = 1;

    public byte[] Blob()
    {
        var w = new BlobWriter();
        w.U8(0x02).U8(0);                                          // object flags 0x02, base data 0 (the live projectile's)
        // Positioned: x, y, rotation, team, f0 0x04 (a fixed height follows), f1 0 (no extra visibility bytes) - exactly
        // the live server's 28-byte projectile (Frida 2026-10-25). The two visibility bytes and f1 0x02 were wrong and
        // made the arrow invisible (it never drew because the blob was 30 bytes and the components no longer lined up).
        w.I32(GridX).I32(GridY).F32(Rotation).U16(Team).U8((byte)(Height is null ? 0 : 0x04)).U8(0);
        if (Height is int h) w.I32(-h).F32(h * 7.8125f);           // h = z / 7.8125 (z points down): i32 -h, f32 z
        if (AnimatedObject is null) w.U8(0x40);                    // Animated: Projectiles.dat AOFiles
        else w.U8(1).WStr32(AnimatedObject);                        // Explicit art, the same Animated reader as effects.
        w.U8(0);                                                   // tasks
        return w.ToArray();
    }
}

/// <summary>
/// A visual effect as its own object: <c>Metadata/Effects/PermanentServerEffect</c> (Positioned, BaseEvents, Animated;
/// Render is client-only) with the given animated object, which plays its default animation where the object stands
/// until it is removed (0x2bb). The client's effect script (0x142087910) makes the same objects for MiscAnimated rows,
/// e.g. RighteousFireRing = Metadata/Effects/Spells/ri_fire/rifire.ao.
/// </summary>
public sealed record EffectObject(int GridX, int GridY, string AnimatedObject)
{
    public const string TypePath = "Metadata/Effects/PermanentServerEffect";

    /// <summary>Positioned flags0 0x80 (→ +0x1e6 bit 2): the object keeps its rotation while it glides (0x1ba); without it
    /// a glide turns it to face the move (live 2026-10-21, Righteous Fire's ring). The only flags0 bits taken here are
    /// those that read no further data (0x01, 0x80); 0x20 comes with <see cref="Scale"/>.</summary>
    public const byte FixedRotation = 0x80;

    public byte Flags0 { get; init; }

    /// <summary>The art's size; 1 sends none. Positioned flags0 0x20 reads f32 +0x2b0, +0x2a8, +0x2ac: +0x2b0 is the
    /// uniform scale (× the object type's own; "Scale" in the component's info dump 0x141e33310) and +0x2a8 / +0x2ac
    /// stretch x and y on top of it (the local-to-world transform 0x141e361b0 multiplies x by +0x2a8 × +0x2b0), so the
    /// scale goes in the first and the stretches stay 1: all three at s drew the ring at s² (live 2026-10-23).
    /// Righteous Fire's ring is scaled to the skill's radius.</summary>
    public float Scale { get; init; } = 1;

    public byte[] Blob()
    {
        var w = new BlobWriter();
        bool scaled = Scale != 1;
        w.U8(0).U8(0);                                              // object flags, base data entry count
        w.I32(GridX).I32(GridY).F32(0).U16(0).U8((byte)((Flags0 & 0x81) | (scaled ? 0x20 : 0))).U8(0x02);   // Positioned: on the terrain, shown
        if (scaled) w.F32(Scale).F32(1).F32(1);                     // uniform scale, x and y stretch
        w.U8(0).U8(1);
        w.U8(1).WStr32(AnimatedObject);                             // Animated: bit0 + the .ao path
        w.U8(0);                                                    // tasks
        return w.ToArray();
    }
}

/// <summary>
/// A room's blocking object: <c>Metadata/Terrain/Act1/Area1/Objects/Tutorial_Blocker_*</c> (The Twilight Strand's force
/// walls), components Positioned, BaseEvents, Animated, TriggerableBlockage, Transitionable (12-quests.md §6).
/// TriggerableBlockage (0x141e44290): u8 blocked, then only while blocked a u8 count and that many u8 (none here); the
/// client blocks the cells of the model's own footprint where the object stands (0x141d876e0), so the room's scale
/// matters: Positioned flags0 0x20's uniform scale (the room gives the Strand's wall 2.7; the x / y stretches stay 1, see
/// <see cref="EffectObject.Scale"/>: 2.7 in all three drew it 7.3 times as wide). TriggerableBlockage
/// has no update reader: the wall goes with the object (0x2bb).
/// </summary>
public sealed record BlockerObject(int GridX, int GridY, float Rotation, float Scale = 1)
{
    public byte[] Blob()
    {
        var w = new BlobWriter();
        w.U8(0).U8(0);                                                              // object flags, base data entry count
        bool scaled = Scale != 1;
        w.I32(GridX).I32(GridY).F32(Rotation).U16(0).U8((byte)(scaled ? 0x20 : 0)).U8(0x02);
        if (scaled) w.F32(Scale).F32(1).F32(1);                                     // uniform scale, x and y stretch
        w.U8(0).U8(1);                                                              // shown
        w.U8(0);                                                                    // Animated: the type's own
        w.U8(1).U8(0);                                                              // TriggerableBlockage: blocked
        w.U8(1);                                                                    // Transitionable: state 1
        w.U8(0);                                                                    // tasks
        return w.ToArray();
    }
}

/// <summary>A type's networked StateMachine data: the define_shared_state defaults in order and the number of
/// define_shared_timer entries (scripts/re/ot_components.py "shared_states" / "shared_timers").</summary>
public sealed record StateMachineData(IReadOnlyList<int> SharedStates, int SharedTimers, IReadOnlyList<string>? Names = null)
{
    /// <summary>States the server set (<see cref="Set"/>): sent with flags 0x02, which makes the client keep the sent
    /// value (the update reader 0x141e3f4a0 takes the type's default for a flag-0 state the client controls).</summary>
    public IReadOnlySet<int> Overridden { get; init; } = new HashSet<int>();

    /// <summary>The state's index by name, or −1.</summary>
    public int IndexOf(string name) => Names is null ? -1 : Names.ToList().IndexOf(name);

    /// <summary>A copy with this state set; unknown names change nothing.</summary>
    public StateMachineData Set(string name, int value)
    {
        int i = IndexOf(name);
        if (i < 0) return this;
        var states = SharedStates.ToArray();
        states[i] = value;
        return this with { SharedStates = states, Overridden = new HashSet<int>(Overridden) { i } };
    }

    /// <summary>The component's data, the same in the creation blob and in an update (0x2ba, reader 0x141e3f4a0):
    /// u8 state count, u8 timer count, per state a compact int value (big-endian groups as the blob reader 0x141e490e0
    /// reads them, InstanceProtocol.CompactInt) + u8 flags, per timer u32 + u8. A changed value
    /// runs the type's on_state_&lt;name&gt;_&lt;value&gt; events.</summary>
    public void Write(BlobWriter w)
    {
        w.U8((byte)SharedStates.Count).U8((byte)SharedTimers);
        for (int i = 0; i < SharedStates.Count; i++)
        {
            var lw = new LoginWriter();
            InstanceProtocol.CompactInt(lw, SharedStates[i]);
            foreach (byte b in lw.ToArray()) w.U8(b);
            w.U8((byte)(Overridden.Contains(i) ? 0x02 : 0));
        }
        for (int i = 0; i < SharedTimers; i++) w.U32(0).U8(0);
    }
}

/// <summary>A skill the server grants an Actor (ActiveSkillManager, Actor +0x598): Id is the instance's u16 (+0x40),
/// which the client uses in its skill messages; the action id (+0x42) comes from the data (GEPL row → GrantedEffect →
/// ActiveSkill → ActionTypes HASH16). Rows are GrantedEffectsPerLevel and GrantedEffectStatSetsPerLevel indices.
/// CooldownMs is the skill's cooldown, which the client takes from the record, not from the data.</summary>
public sealed record GrantedSupport(uint EffectsPerLevelRow, uint StatSetsPerLevelRow, uint Source = 0)
{
    public IReadOnlyList<uint> ExtraStatSets { get; init; } = [];
}

public sealed record GrantedSkill(ushort Id, uint EffectsPerLevelRow, uint StatSetsPerLevelRow,
    IReadOnlyList<(uint Key, int Value)>? Stats = null, uint CooldownMs = 0)
{
    /// <summary>Skill-record bit1 (0x02): retain native GEPL Vaal soul inputs, regardless of current cost or balance.</summary>
    public bool VaalResourcesEnabled { get; init; }
    /// <summary>Native support references (GEPL row and source metadata), read by 0x142196ce0.</summary>
    public IReadOnlyList<GrantedSupport> Supports { get; init; } = [];
    /// <summary>The effect's additional stat sets (GrantedEffects StatSet2, e.g. Volcanic Fissure's), in order: each one's
    /// GrantedEffectStatSetsPerLevel row and stats. The record must carry one of each per set, or the client misreads it
    /// (and crashed, live 2026-10-23).</summary>
    public IReadOnlyList<(uint Row, IReadOnlyList<(uint Key, int Value)> Stats)> ExtraStatSets { get; init; } = [];
}

/// <summary>One ordinary stored-use budget; the native duration is base time, not wall time.</summary>
public readonly record struct SingleUseCooldownUpdate(ushort SkillId, float RemainingBaseSeconds,
    float TotalBaseSeconds, float RecoveryRate);

/// <summary>ObjectMagicProperties rarity (flags bits 0-2 → +0x7c).</summary>
public enum MonsterRarity : byte { Normal = 0, Magic = 1, Rare = 2, Unique = 3 }

/// <summary>Little-endian writer for 0x2b9 component blobs.</summary>
public sealed class BlobWriter
{
    private readonly List<byte> _b = [];
    public byte[] ToArray() => [.. _b];
    public BlobWriter U8(byte v) { _b.Add(v); return this; }
    public BlobWriter U16(ushort v) { _b.Add((byte)v); _b.Add((byte)(v >> 8)); return this; }
    public BlobWriter U32(uint v) { U16((ushort)v); return U16((ushort)(v >> 16)); }
    public BlobWriter I32(int v) => U32((uint)v);
    public BlobWriter F32(float v) => U32(BitConverter.SingleToUInt32Bits(v));
    public BlobWriter Zeros(int n) { for (int i = 0; i < n; i++) _b.Add(0); return this; }
    public BlobWriter Bytes(ReadOnlySpan<byte> v) { foreach (byte x in v) _b.Add(x); return this; }
    /// <summary>u32 UTF-16 unit count, then the units (0x140e28d60).</summary>
    public BlobWriter WStr32(string s) { U32((uint)s.Length); foreach (byte x in Encoding.Unicode.GetBytes(s)) _b.Add(x); return this; }
    /// <summary>Unsigned compact int (stat keys and counts, 0x141e61010), InstanceProtocol.CompactUInt.</summary>
    public BlobWriter CompactU(uint v) { var lw = new LoginWriter(); InstanceProtocol.CompactUInt(lw, v); _b.AddRange(lw.ToArray()); return this; }
    /// <summary>Signed compact int (stat values, 0x141e490e0), InstanceProtocol.CompactInt.</summary>
    public BlobWriter CompactI(int v) { var lw = new LoginWriter(); InstanceProtocol.CompactInt(lw, v); _b.AddRange(lw.ToArray()); return this; }
    /// <summary>Compact count, then (compact key, compact value) pairs: a stats list (0x1424cfaf0 per pair).</summary>
    public BlobWriter Stats(IReadOnlyList<(uint Key, int Value)>? stats)
    {
        CompactU((uint)(stats?.Count ?? 0));
        foreach (var (k, v) in stats ?? []) CompactU(k).CompactI(v);
        return this;
    }
}

/// <summary>
/// The smallest component blob for a player object (`Metadata/Characters/{class}`), every optional part off. Components
/// are serialized in the created object's component order, which was traced live with Frida (2026-10-16): 12 components,
/// of which these read data, in this order: Positioned, comp1 (vtable 0x1435dd4e8, 0x1421c2f40, stats-like), comp3
/// (0x14359f5f8, 0x141e114d0, u16-counted entries), Life, Animated, Player (0x143466450), Inventories, comp9 (0x14346e558,
/// 0x142192a80). Pathfinding and 0x1435a0078 read nothing. Comps 10/11 (0x143468920, 0x14346c238) are still unverified.
/// </summary>
public sealed record PlayerObject(string Name, byte ClassIndex, int GridX, int GridY,
    uint Life, uint Mana, uint EnergyShield = 0, uint Experience = 0, bool Shown = true, string? AnimatedObject = null,
    IReadOnlyList<GrantedSkill>? Skills = null, IReadOnlyList<(uint Key, int Value)>? Stats = null,
    IReadOnlyList<VisibleItem>? Equipped = null, IReadOnlyCollection<int>? QuestFlags = null,
    uint ManaReserved = 0, ushort Hideout = 0, ushort Hideout2 = 0, IReadOnlyCollection<int>? LeagueQuestFlags = null,
    ushort Portrait = 0, IReadOnlyList<VaalSoulBudget>? Souls = null)
{
    /// <summary>The Player component's index in the player object's component order (04 §3).</summary>
    public const byte PlayerComponentIndex = 6;

    /// <summary>The Inventories component's index in the player object's component order (04 §3).</summary>
    public const byte InventoriesComponentIndex = 8;

    /// <summary>Inventories (0x141e1ee30): u8 n × (u8 slot, visible item 0x1424f44e0), then u8 m × u16 (skill ids the
    /// client re-binds). The same bytes update it later (0x2ba, slot 17 forwards to slot 20).</summary>
    internal static void WriteInventories(BlobWriter w, IReadOnlyList<VisibleItem>? equipped)
    {
        w.U8((byte)(equipped?.Count ?? 0));
        foreach (var e in equipped ?? []) e.Write(w);
        w.U8(0);
    }

    /// <summary>
    /// A set of flags as the Player component keeps its quest flags (+0x1b8; reader 0x141e83340, test 0x14012aae0): u16
    /// count, then per block of 64 flags in ascending order u8 block (flag &gt;&gt; 6) and u64 LE bits (bit flag &amp; 63).
    /// Quest flags are QuestFlags.dat rows. Flags above 16383 do not fit and are skipped.
    /// </summary>
    public static void WriteFlagSet(BlobWriter w, IEnumerable<int>? flags)
    {
        var blocks = new SortedDictionary<int, ulong>();
        foreach (int f in flags ?? [])
            if (f >= 0 && f < 256 * 64) blocks[f >> 6] = blocks.GetValueOrDefault(f >> 6) | 1UL << (f & 63);
        w.U16((ushort)blocks.Count);
        foreach (var (block, bits) in blocks) w.U8((byte)block).U32((uint)bits).U32((uint)(bits >> 32));
    }

    public byte[] Blob()
    {
        var w = new BlobWriter();
        w.U8(0);            // object flags (0x141ebe190: bit0 → +0x8c|0x80, bit1 → +0x8d|2)
        w.U8(0);            // base data entry count (0x141dea920)
        // Positioned (0x141e2f0b0): x, y, u32 +0x29c, u16 +0x1e0, two flag bytes. The type's +0x21 prefix (Sector) is off.
        // flags1 0x02 adds two bytes: hidden (→ +0x1e7 bit0) and shown (→ +0x1e5 bit3); both changes call the object's
        // vtable +0xb0 (visibility). Without "shown" the player entity exists but its model is not drawn.
        w.I32(GridX).I32(GridY).U32(0).U16(0).U8(0).U8(Shown ? (byte)0x02 : (byte)0);
        if (Shown) w.U8(0).U8(1);
        // comp1 = Stats (0x1421c2f40): compact-int count of (compact-int key, value) pairs, then u32 +0x28, u32 +0x2c, u8
        // +0x30. Keys are the client's stat ids (Stats.dat row + 1).
        w.Stats(Stats).U32(0).U32(0).U8(0);
        // comp3 (0x141e114d0): u16 count of entries (u16, u8, 0x141e06bb0 record).
        w.U16(0);
        // Life (0x141e6c2f0): life, mana and energy shield pools (0x141e6be70), then a flags byte (0x141e6c270).
        Pool(w, Life); Pool(w, Mana, ManaReserved); Pool(w, EnergyShield);
        w.U8(0);
        // Animated (0x141e56a80): flags. Bit0 + !bit1: u32 char count + UTF-16LE animated-object path; the client loads it
        // when it differs from the current one or none is set (+0x260 null), creating the 0x308-byte AO instance.
        if (AnimatedObject is null) w.U8(0);
        else w.U8(1).WStr32(AnimatedObject);
        // Player (0x141e7dc10).
        w.WStr32(Name)
         .U8(ClassIndex)     // Characters.dat row; bit5 and bits6-7 are the ascendancy (0x142156d80)
         .U32(Experience)    // 0x141e7f6b0 → +0x18c; the level is derived from it (+0x1ac)
         .U32(0).U16(0)
         .U16(Portrait)      // +0x1aa: the portrait variation (MicrotransactionCharacterPortraitVariations __col1, 0 = the class's)
         ;
        WriteFlagSet(w, QuestFlags);   // the quest flags (+0x1b8, 0x141e83340)
        // The league quest flags (+0x1d0, same layout; WorldAreas __col60 values − 1): bit = id − 1 of the client's built-in
        // name table (0x142fca900), e.g. UnlockedMapDevice 350 → bit 349, which the map device's Targetable requires (21-hideouts.md §1).
        WriteFlagSet(w, LeagueQuestFlags);
        w
         .Zeros(9)           // raw +0x1e8
         .U16(Hideout).U16(Hideout2)   // Hideouts.dat keys (HASH16; 0 finds no row): the character's hideout (21-hideouts.md §1)
         .U8(0)              // +0x1b1 (non-zero → "WashedUp" state)
         .U8(0).U8(0).U8(0).U8(0).U8(0)
         .U32(0)             // u32 count + u32 list (vtable +0x158)
         .U8(0);             // microtransaction counters (0x141e83470)
        WriteInventories(w, Equipped);   // Inventories (0x141e1ee30): what the model wears
        // comp9 = Actor (0x142192a80): u32-counted UTF-16 string, u16 +0x21c, u8; two more u8 (+0x230, +0x234) only
        // when a runtime value is < 1 (false here: traced live, comp9 read 15 bytes); u32 +0x26c, u32 +0x274; then its
        // vtable+0xa0 reader 0x1421932b0: two flag bytes. f0 0x40 = the skill list (0x142197b10); the rest stay off.
        w.WStr32("").U16(0).U8(0).U32(0).U32(0);
        if (Skills is { Count: > 0 } || Souls is { Count: > 0 })
        {
            w.U8(0x40).U8(0);
            WriteSkillList(w, Skills ?? [], Souls);
        }
        else w.U8(0).U8(0);
        w.U8(0);            // tasks (0x141ee00f0)
        return w.ToArray();
    }

    /// <summary>Actor slot 20, f0 0x40 (0x142197b10): u8 n × skill (0x142196ce0), u8 m × (u32 row, u32, u32), u32 k ×
    /// (u16 skill id, u32 c × 4 u32). Each skill: u16 id (+0x40), the GrantedEffectsPerLevel reference (0x1424a0dc0:
    /// u8 → +0x10, u32 GEPL row, then a GrantedEffectStatSetsPerLevel reference 0x1424a0990: u8 a [u8 if a], u32 GESSPL
    /// row; then one more stat-set reference per entry of the effect's additional stat sets, GrantedSkill.ExtraStatSets), u8
    /// flags (bit0 → u32 parent object id + u16, bit1 retains Vaal soul inputs), u8 support count ×
    /// (GEPL and stat-set references, u32), u32, u32, u32, u8, then the
    /// skill's stats (compact count × compact key, compact value) and one such stat list per additional stat set.</summary>
    /// <summary>The Actor's index in the player object's component order (04 §3).</summary>
    public const byte ActorComponentIndex = 9;

    internal static void WriteSkillList(BlobWriter w, IReadOnlyList<GrantedSkill> skills,
        IReadOnlyList<VaalSoulBudget>? souls = null)
    {
        w.U8((byte)skills.Count);
        foreach (var s in skills) WriteSkillRecord(w, s);
        WriteSoulBudgets(w, souls);
        w.U32(0);
    }

    internal static void WriteSoulBudgets(BlobWriter w, IReadOnlyList<VaalSoulBudget>? souls)
    {
        w.U8(checked((byte)(souls?.Count ?? 0)));
        foreach (var s in souls ?? []) w.U32(s.ActiveSkillsRow).U32(s.Capacity).U32(s.Current);
    }

    /// <summary>One skill record (<c>0x142196ce0</c>), as the 0x40 list and the 0x20 update carry it.</summary>
    internal static void WriteSkillRecord(BlobWriter w, GrantedSkill s)
    {
        w.U16(s.Id)
         .U8(0).U32(s.EffectsPerLevelRow)      // GEPL reference
         .U8(0).U32(s.StatSetsPerLevelRow);    // its stat set
        foreach (var (row, _) in s.ExtraStatSets) w.U8(0).U32(row);   // one more stat-set reference per additional set
        // 0x1421975f0: without bit1 a raw Vaal stored-use count makes the constructor zero its soul inputs.
        w.U8(s.VaalResourcesEnabled ? (byte)0x02 : (byte)0).U8(checked((byte)s.Supports.Count));
        foreach (var support in s.Supports)
        {
            w.U8(0).U32(support.EffectsPerLevelRow).U8(0).U32(support.StatSetsPerLevelRow);
            foreach (uint row in support.ExtraStatSets) w.U8(0).U32(row);
            w.U32(support.Source);
        }
        w
         // → skill +0xc0, +0xc4, +0xc8, +0xcc (0x141ee73d0). +0xc8 is the cooldown in ms: with it and the stat
         // virtual_total_cooldown_count the client keeps the skill's cooldown and shows it on the skill bar
         // (0x141eea6a0 creates the cooldown record only when it is not 0; live 2026-10-21).
         .U32(0).U32(0).U32(s.CooldownMs).U8(0)
         .Stats(s.Stats);                      // the skill's own stats
        foreach (var (_, stats) in s.ExtraStatSets) w.Stats(stats);   // and one stat list per additional set
    }

    /// <summary>One Life pool (0x141e6be70): u32 +0x30 current, u32 +0x10 reserved (flat), i16 +0x14 (reserved percent?),
    /// u32 +0x28, u32 +0x40. The maximum is not in the blob: the client computes it from stats (Witch level 1: life 50,
    /// mana 40). Live 2026-10-16: sending 50 in both u32 showed "Life 50/50 Reserved 50" and "Mana 50/40 Reserved 40", i.e.
    /// +0x30 is current and +0x10 a reserve clamped to the maximum.</summary>
    private static void Pool(BlobWriter w, uint current, uint reserved = 0) =>
        w.U32(current).U32(reserved).U16(0).U32(0).U32(0);
}
