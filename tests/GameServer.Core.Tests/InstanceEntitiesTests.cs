using GameServer.Core.Protocol.Instance;

namespace GameServer.Core.Tests;

public class InstanceEntitiesTests
{
    [Theory]
    // Reference values from an independent MurmurHash2 implementation (seed 0).
    [InlineData("Metadata/Characters/Int/Int", 0x9c41993eu)]
    [InlineData("Metadata/Characters/Str/Str", 0xd7aa9065u)]
    [InlineData("", 0u)]
    public void Object_type_hash_is_murmur2_of_the_narrowed_path(string path, uint expected)
    {
        Assert.Equal(expected, InstanceEntities.ObjectTypeHash(path));
    }

    [Fact]
    public void Local_player_id_is_a_u32_message()
    {
        Assert.Equal("002200001000", Convert.ToHexString(InstanceEntities.SetLocalPlayer(0x1000)));
    }

    [Fact]
    public void Gold_is_a_big_endian_u32_message()
    {
        Assert.Equal("02A90001E240", Convert.ToHexString(InstanceEntities.Gold(123456)));
    }

    [Fact]
    public void Create_object_has_entity_header_type_and_blob16()
    {
        byte[] m = InstanceEntities.CreateObject(0x1000, 0x9c41993e, [0xaa, 0xbb]);
        Assert.Equal("02B9" + "00001000" + "00000000" + "0000" + "9C41993E" + "0002" + "AABB", Convert.ToHexString(m));
    }

    [Fact]
    public void Minimal_player_blob_layout()
    {
        byte[] b = new PlayerObject("Ab", ClassIndex: 1, GridX: 0x73, GridY: 0x45, Life: 50, Mana: 40).Blob();
        string hex = Convert.ToHexString(b);
        string pool(uint v) => Convert.ToHexString(BitConverter.GetBytes(v)) + "00000000" + "0000" + "00000000" + "00000000"; // current, reserved 0
        string expected =
            "00" + "00" +                                                     // flags, base data
            "73000000" + "45000000" + "00000000" + "0000" + "0002" + "0001" + // Positioned, flags1 0x02: not hidden, shown
            "00" + "00000000" + "00000000" + "00" +                           // comp1
            "0000" +                                                          // comp3
            pool(50) + pool(40) + pool(0) + "00" +                            // Life
            "00" +                                                            // Animated
            "02000000" + "41006200" + "01" + "00000000" + "00000000" + "0000" + "0000" + "0000" + "0000" +
            "000000000000000000" + "0000" + "0000" + "00" + "0000000000" + "00000000" + "00" + // Player
            "0000" +                                                          // Inventories
            "00000000" + "0000" + "00" + "00000000" + "00000000" + "0000" +    // comp9
            "00";                                                             // tasks
        Assert.Equal(expected, hex);
    }

    [Fact]
    public void Player_blob_carries_reserved_mana()
    {
        byte[] b = new PlayerObject("Ab", ClassIndex: 1, GridX: 0x73, GridY: 0x45, Life: 50, Mana: 40,
            ManaReserved: 17).Blob();
        string hex = Convert.ToHexString(b);
        int mana = hex.IndexOf("28000000", StringComparison.Ordinal);   // current mana 40, little-endian
        Assert.True(mana >= 0);
        Assert.Equal("11000000", hex.Substring(mana + 8, 8));           // then the reserved amount, 17
    }

    [Fact]
    public void Player_blob_with_stats_and_a_granted_skill()
    {
        // Live 2026-10-17: the default attack (PlayerMelee, GEPL 4474, GESSPL 4430) with the Witch's unarmed stats.
        (uint, int)[] stats = [(0x142, 833), (0x117, 2)];
        byte[] b = new PlayerObject("Ab", ClassIndex: 1, GridX: 0x73, GridY: 0x45, Life: 50, Mana: 40,
            Skills: [new GrantedSkill(0x0102, 4474, 4430, stats)], Stats: stats).Blob();
        string hex = Convert.ToHexString(b);
        const string statList = "02" + "8142" + "8341" + "8117" + "02";       // compact: count, key 0x142 = 833, key 0x117 = 2
        Assert.StartsWith("0000" + "73000000" + "45000000" + "00000000" + "0000" + "0002" + "0001" + // flags, base, Positioned
            statList + "00000000" + "00000000" + "00", hex);                  // comp1 = Stats
        string actor =
            "00000000" + "0000" + "00" + "00000000" + "00000000" +           // Actor main body
            "4000" +                                                          // slot 20: f0 0x40 = skill list
            "01" + "0201" + "00" + "7A110000" + "00" + "4E110000" +          // 1 skill: id 0x102, GEPL ref (u8, row 4474, stat set u8, row 4430)
            "00" + "00" + "00000000" + "00000000" + "00000000" + "00" + statList + // flags, supports, 3 × u32, u8, stats
            "00" + "00000000";                                                // no (row, u32, u32) entries, no u32-counted list
        Assert.EndsWith(actor + "00", hex);                                   // then tasks
    }

    [Fact]
    public void Minimal_monster_blob_layout()
    {
        byte[] b = new MonsterObject(0x73, 0x45, Life: 100, MonsterObject.BaseComponents).Blob();
        string pool(uint v) => Convert.ToHexString(BitConverter.GetBytes(v)) + "00000000" + "0000" + "00000000" + "00000000";
        string expected =
            "00" + "00" +                                                     // flags, base data
            "73000000" + "45000000" + "00000000" + "0100" + "0002" + "0001" + // Positioned (no Sector prefix), team 1, shown
            "00" + "00000000" + "00000000" + "00" +                           // Stats
            "0800" + "00" + "00" +                                            // ObjectMagicProperties: identified, normal; flags
            "0000" +                                                          // Buffs (BaseEvents, Pathfinding: none)
            pool(100) + pool(0) + pool(0) + "00" +                            // Life
            "00" +                                                            // Animated
            "0000" +                                                          // Inventories
            "00000000" + "0000" + "00" + "00000000" + "00000000" + "0000" +    // Actor (life >= 1: no conditional u8s)
            "00";                                                             // Monster reads nothing; tasks
        Assert.Equal(expected, Convert.ToHexString(b));
        Assert.Equal(112, b.Length);
    }

    [Fact]
    public void Life_update_layout()
    {
        // header, u32 current, u32 reserved, u8 pool (0 = life), u16, u8 — all big-endian.
        Assert.Equal("01B1" + "00003000" + "00000000" + "0000" + "0000004D" + "00000000" + "00" + "0000" + "00",
            Convert.ToHexString(InstanceProtocol.LifeUpdate(0x3000, 77)));
    }

    [Fact]
    public void Animation_update_layout()
    {
        // header, u8 kind (0 = play), u32 animation index, u32 float bits (speed 1.0).
        Assert.Equal("01B2" + "00003000" + "00000000" + "0000" + "00" + "00000005" + "3F800000",
            Convert.ToHexString(InstanceProtocol.AnimationUpdate(0x3000, 0, 5, 1f)));
    }

    [Fact]
    public void Action_started_on_an_object_carries_its_id_after_the_flags()
    {
        string hex = Convert.ToHexString(InstanceProtocol.ActionStarted(0x1000, 1, 2, 3, 4, 0x4000, 7, 0x0408, targetObjectId: 0x3000));
        Assert.StartsWith("01AB" + "00001000" + "00000000" + "0000" + "0488" + "00003000" + "01020304" + "4000" + "0007", hex);
        Assert.DoesNotContain("00003000", Convert.ToHexString(InstanceProtocol.ActionStarted(0x1000, 1, 2, 3, 4, 0x4000, 7, 0x0488))[14..]);
    }

    [Fact]
    public void Remove_object_is_a_header_only_message()
    {
        Assert.Equal("02BB" + "00003001" + "00000000" + "0000", Convert.ToHexString(InstanceProtocol.RemoveObject(0x3001)));
    }

    [Fact]
    public void Monster_types_message_and_type_path()
    {
        // u16 count, u16 preload (big-endian), rows as raw little-endian u16, then one level byte each.
        Assert.Equal("0020" + "0002" + "0002" + "8704" + "0100" + "0D" + "01",
            Convert.ToHexString(InstanceEntities.MonsterTypes([(1159, 13), (1, 1)])));
        Assert.Equal("0020" + "0001" + "0000" + "8704" + "0D",
            Convert.ToHexString(InstanceEntities.MonsterTypes([(1159, 13)], preload: 0)));
        Assert.Equal("Metadata/Monsters/Zombies/ZombieBite@13", MonsterObject.TypePath("Metadata/Monsters/Zombies/ZombieBite", 13));
    }

    [Fact]
    public void Monster_blob_writes_known_extras_and_the_animated_object()
    {
        string[] withExtras = [.. MonsterObject.BaseComponents, "Transitionable", "Preload", "DiesAfterTime"];
        byte[] plain = new MonsterObject(1, 2, 100, MonsterObject.BaseComponents).Blob();
        byte[] extras = new MonsterObject(1, 2, 100, withExtras, TransitionState: 4).Blob();
        Assert.Equal(plain.Length + 1, extras.Length);
        Assert.Equal(4, extras[^2]);   // Transitionable: fully transitioned (state = num_states), then tasks
        byte[] withAo = new MonsterObject(1, 2, 100, MonsterObject.BaseComponents, "A.ao").Blob();
        Assert.Equal(plain.Length + 4 + 2 * 4, withAo.Length);
    }

    [Fact]
    public void Monster_rarity_and_name_go_in_object_magic_properties()
    {
        string plain = Convert.ToHexString(new MonsterObject(1, 2, 100, MonsterObject.BaseComponents).Blob());
        string rare = Convert.ToHexString(new MonsterObject(1, 2, 100, MonsterObject.BaseComponents, Rarity: MonsterRarity.Rare, Name: "Ab").Blob());
        // Positioned (18) + Stats (10) after flags/base (2): ObjectMagicProperties starts at byte 30.
        Assert.Equal("0800" + "00" + "00", plain.Substring(60, 8));
        Assert.Equal("8A00" + "00" + "02000000" + "41006200" + "00", rare.Substring(60, 24));
        Assert.Equal(plain.Length + 2 * (4 + 4), rare.Length);
    }

    [Fact]
    public void Monster_state_machine_writes_shared_states_and_timers()
    {
        string[] list = [.. MonsterObject.BaseComponents, "StateMachine"];
        Assert.False(MonsterObject.CanSerialize(list));
        Assert.True(MonsterObject.CanSerialize(list, haveStateMachine: true));
        byte[] plain = new MonsterObject(1, 2, 100, MonsterObject.BaseComponents).Blob();
        byte[] b = new MonsterObject(1, 2, 100, list, StateMachine: new([0, 2, 5000], 1)).Blob();
        // counts 03 01; states: compact 0, 2, 5000 (0x80 | 0x13, 0x88) each + u8 flags 0; one timer u32 + u8; then tasks.
        Assert.Equal("0301" + "0000" + "0200" + "938800" + "0000000000" + "00",
            Convert.ToHexString(b.AsSpan(plain.Length - 1)));
    }

    [Fact]
    public void A_set_state_is_sent_with_flag_2_and_updates_carry_every_state()
    {
        // ZombieBoss.ot (Hillock): boss_life_bar, disable_life_bar, global_first_aggro, aggro.
        var states = new StateMachineData([0, 0, 0, 0], 0, ["boss_life_bar", "disable_life_bar", "global_first_aggro", "aggro"]);
        var shown = states.Set("boss_life_bar", 1).Set("aggro", 1).Set("no_such_state", 1);
        Assert.Equal([1, 0, 0, 1], shown.SharedStates);
        Assert.Equal([0, 0, 0, 0], states.SharedStates);
        // 0x2ba, object 0x3000, u32 0, u16 0, blob16 (length 11): component 11, counts 04 00, states value + flags.
        Assert.Equal("02BA" + "00003000" + "00000000" + "0000" + "000B" + "0B" + "0400" + "0102" + "0000" + "0000" + "0102",
            Convert.ToHexString(InstanceEntities.StateMachineUpdate(0x3000, 11, shown)));
    }

    [Fact]
    public void Action_ended_is_the_object_header()
    {
        Assert.Equal("01AC" + "00003000" + "00000000" + "0000", Convert.ToHexString(InstanceProtocol.ActionEnded(0x3000)));
    }

    [Fact]
    public void Transitionable_update_is_the_target_state()
    {
        Assert.Equal("02BA" + "00003000" + "00000000" + "0000" + "0002" + "0C" + "02",
            Convert.ToHexString(InstanceEntities.TransitionableUpdate(0x3000, 12, 2)));
    }

    [Fact]
    public void A_scaled_blocker_sends_its_scale_and_starts_blocked()
    {
        // The Twilight Strand's force wall: room scale 2.7 → Positioned flags0 0x20 and three f32.
        byte[] plain = new BlockerObject(1403, 679, 5.2948f).Blob();
        byte[] scaled = new BlockerObject(1403, 679, 5.2948f, 2.7f).Blob();
        Assert.Equal(plain.Length + 12, scaled.Length);
        Assert.Equal(0x20, scaled[2 + 14]);
        Assert.Equal(2.7f, BitConverter.ToSingle(scaled, 2 + 16));
        // Only the uniform scale: the x / y stretches multiply it, so they stay 1.
        Assert.Equal(1f, BitConverter.ToSingle(scaled, 2 + 20));
        Assert.Equal(1f, BitConverter.ToSingle(scaled, 2 + 24));
        // shown (0 1), Animated 0, TriggerableBlockage blocked with no list (1 0), Transitionable 1, tasks 0.
        Assert.Equal("0001" + "00" + "0100" + "01" + "00", Convert.ToHexString(scaled.AsSpan(scaled.Length - 7)));
    }

    [Fact]
    public void Monster_blob_refuses_components_it_cannot_write()
    {
        Assert.False(MonsterObject.CanSerialize([.. MonsterObject.BaseComponents, "StateMachine"]));
        Assert.False(MonsterObject.CanSerialize(["Positioned", "Animated"]));
        Assert.False(MonsterObject.CanSerialize([]));
        Assert.Throws<ArgumentException>(() => new MonsterObject(0, 0, 1, [.. MonsterObject.BaseComponents, "ControlZone"]).Blob());
    }

    [Fact]
    public void Add_inventory_layout()
    {
        var flask = InstanceInventories.PlayerDefaults[11];
        Assert.Equal((11, 0x0a, 5, 2), (flask.Row, flask.Kind, flask.Width, flask.Height));
        Assert.Equal("00C0" + "00" + "0000000C" + "0A" + "0B" + "05" + "02" + "02" + "00" + "00" + "00",
            Convert.ToHexString(InstanceInventories.AddInventory(12, flask)));
    }

    [Fact]
    public void Passive_slots_create_all_three()
    {
        Assert.Equal("003D" + "0000" + "0000" + "1C" + "0000", Convert.ToHexString(InstanceEntities.PassiveSlots()));
    }

    [Fact]
    public void Allocate_passive_reads_the_node()
    {
        // Captured live 2026-10-18 (Witch, level 2): Spell Damage and Mana.
        byte[] m = Convert.FromHexString("00340000DFB000");
        Assert.Equal(7, InstanceProtocol.KnownClientLength(InstanceEntities.AllocatePassiveId));
        Assert.Equal(((ushort)0, (ushort)57264, (byte)0), InstanceEntities.ReadAllocatePassive(m));
    }

    [Fact]
    public void Refund_passive_reads_the_node_and_the_gold_flag()
    {
        // Captured live 2026-10-19: Bestel's Respecialisation, "Apply refunds" with two picked nodes.
        Assert.Equal(8, InstanceProtocol.KnownClientLength(InstanceEntities.RefundPassiveId));
        Assert.Equal(((uint)8135, false, true), InstanceEntities.ReadRefundPassive(Convert.FromHexString("003600001FC70001")));
        Assert.Equal(((uint)11128, false, true), InstanceEntities.ReadRefundPassive(Convert.FromHexString("003600002B780001")));
        // "Refund All" is the id alone.
        Assert.Equal(2, InstanceProtocol.KnownClientLength(InstanceEntities.RefundAllPassivesId));
    }

    [Fact]
    public void Effect_object_blob()
    {
        // flags, base data count, Positioned (x, y, rotation, team, f0, f1 0x02, 0, 1), Animated bit0 + u32-counted UTF-16 path, tasks.
        byte[] b = new EffectObject(10, 20, "a.ao").Blob();
        Assert.Equal("0000" + "0A000000" + "14000000" + "00000000" + "0000" + "00" + "02" + "00" + "01" + "01" + "04000000" + "61002E0061006F00" + "00",
            Convert.ToHexString(b));
    }

    [Fact]
    public void Buff_messages_layout()
    {
        // header (u32 id, u32, u16), u16 instance, u16 BuffDefinitions row, u32 source, u16 skill, u8 flags, u32 list count.
        Assert.Equal("01C1" + "00001000" + "00000000" + "0000" + "0001" + "000C" + "00001000" + "0042" + "00" + "00000000",
            Convert.ToHexString(InstanceEntities.AddBuff(0x1000, 1, 12, 0x1000, 0x42)));
        Assert.Equal("01C2" + "00001000" + "00000000" + "0000" + "0001", Convert.ToHexString(InstanceEntities.RemoveBuff(0x1000, 1)));
    }

    [Fact]
    public void A_recovery_buff_carries_its_stat_values_before_the_list()
    {
        // flask_effect_life (row 5, IsRecovery, one GrantedStats entry): 1400 life per minute, then the empty list.
        Assert.Equal("01C1" + "00001000" + "00000000" + "0000" + "0002" + "0005" + "00001000" + "0000" + "00" + "00000578" + "00000000",
            Convert.ToHexString(InstanceEntities.AddBuff(0x1000, 2, 5, 0x1000, values: [1400])));
        // Flags bit1 (u32 +0x68) and bit7 (f32 +0x6c) come right after the flags, before the stat values.
        Assert.Equal("01C1" + "00001000" + "00000000" + "0000" + "0002" + "0005" + "00001000" + "0000" + "82" + "00000BB8" + "40400000" + "00000578" + "00000000",
            Convert.ToHexString(InstanceEntities.AddBuff(0x1000, 2, 5, 0x1000, values: [1400], u68: 3000, f6c: 3f)));
    }

    [Fact]
    public void A_buff_visual_override_is_flags_bit4_after_the_f32()
    {
        // righteous_fire with Divine's art: flags 0x90, f32 +0x6c, then u16 BE BuffVisuals row 401 (righteous_fire_white).
        Assert.Equal("01C1" + "00001000" + "00000000" + "0000" + "0001" + "000C" + "00001000" + "0042" + "90" + "3F800000" + "0191" + "00000000",
            Convert.ToHexString(InstanceEntities.AddBuff(0x1000, 1, 12, 0x1000, 0x42, f6c: 1f, visual: 401)));
    }

    [Fact]
    public void A_pet_does_not_block_walkers()
    {
        // Positioned: x, y, rotation, u16, flags0 0, flags1 0x0a, the shown pair (0, 1), then 1 for flags1 0x08.
        byte[] blob = new PetObject(10, 20, 0x1000).Blob();
        Assert.Equal("0000" + "0A000000" + "14000000" + "00000000" + "0000" + "00" + "0A" + "00" + "01" + "01", Convert.ToHexString(blob[..21]));
    }

    [Fact]
    public void An_effect_object_with_a_fixed_rotation_sets_positioned_flags0_0x80()
    {
        // object flags, entry count, x, y, f32 rotation, u16, flags0, flags1 (0x02: shown)
        byte[] fixedRing = new EffectObject(10, 20, "Metadata/Effects/Spells/ri_fire/rifire.ao") { Flags0 = EffectObject.FixedRotation }.Blob();
        byte[] plain = new EffectObject(10, 20, "Metadata/Effects/Spells/ri_fire/rifire.ao").Blob();
        Assert.Equal(0x80, fixedRing[16]);
        Assert.Equal(0x00, plain[16]);
        Assert.Equal(0x02, fixedRing[17]);
        Assert.Equal(plain.Length, fixedRing.Length);
    }

    [Fact]
    public void A_scaled_effect_object_sends_positioned_flags0_0x20_and_a_uniform_scale()
    {
        // Righteous Fire's ring scaled to its radius: flags0 0x80 | 0x20, then after flags1 the uniform scale (+0x2b0)
        // and the x / y stretches (+0x2a8, +0x2ac) at 1: the client multiplies them, so three 0.5s drew it at 0.25.
        var ring = new EffectObject(10, 20, "a.ao") { Flags0 = EffectObject.FixedRotation, Scale = 0.5f };
        byte[] scaled = ring.Blob(), plain = (ring with { Scale = 1 }).Blob();
        Assert.Equal(0xa0, scaled[16]);
        Assert.Equal(0x02, scaled[17]);
        Assert.Equal("0000003F" + "0000803F" + "0000803F", Convert.ToHexString(scaled[18..30]));
        Assert.Equal(plain.Length + 12, scaled.Length);
        Assert.Equal(plain[18..], scaled[30..]);
    }

    [Fact]
    public void Character_passives_layout()
    {
        // u16 refund, u16 quest, u16 ascendancy points (big-endian), u8 level, u8 class, u8, u8 flags, vec16 nodes (count
        // big-endian, ids little-endian, sorted: the client diffs it against its own sorted list), then four empty lists.
        Assert.Equal("0033" + "0000" + "0000" + "0000" + "05" + "03" + "00" + "00" + "0002" + "3412" + "6776" + "0000" + "0000" + "0000" + "0000",
            Convert.ToHexString(InstanceEntities.CharacterPassives(5, 3, [0x7667, 0x1234])));
    }

    [Fact]
    public void Use_item_reads_the_inventory_and_item_big_endian()
    {
        Assert.Equal(10, InstanceProtocol.KnownClientLength(InstanceItems.UseItemId));
        var first = InstanceItems.ReadUseItem(Convert.FromHexString("00580000000100000041"));
        Assert.Equal(1u, first.InventoryId);
        Assert.Equal(0x41u, first.ItemId);
        var second = InstanceItems.ReadUseItem(Convert.FromHexString("00580000000100000042"));
        Assert.Equal(1u, second.InventoryId);
        Assert.Equal(0x42u, second.ItemId);
    }

    [Fact]
    public void Character_passives_carry_refund_quest_and_ascendancy_points()
    {
        // Refund points come first, then extra_skill_points and ascendancy points.
        Assert.Equal("0033" + "0002" + "0001" + "0003" + "05" + "03" + "00" + "00" + "0002" + "3412" + "6776" + "0000" + "0000" + "0000" + "0000",
            Convert.ToHexString(InstanceEntities.CharacterPassives(5, 3, [0x7667, 0x1234],
                questPoints: 1, refundPoints: 2, ascendancyPoints: 3)));
    }

    [Fact]
    public void Area_transition_blob_layout_and_type()
    {
        // Traced live 2026-10-16: the client consumed all 41 bytes (Positioned 16, Animated 1, AreaTransition 5, Stats 10,
        // ObjectMagicProperties 4, MinimapIcon 1, InteractionAction 1, + flags, base data, tasks).
        byte[] b = new AreaTransitionObject(GridX: 384, GridY: 217, DestinationHash16: 0x7ad2).Blob();
        Assert.Equal(41, b.Length);
        Assert.Equal(
            "00" + "00" +                                                    // flags, base data
            "80010000" + "D9000000" + "00000000" + "0000" + "0000" +         // Positioned (384, 217)
            "00" +                                                           // Animated
            "00" + "02" + "D27A" + "00" +                                    // AreaTransition: state 0, variant 2 = HASH16 0x7ad2, compact int
            "00" + "00000000" + "00000000" + "00" +                          // Stats
            "0000" + "00" + "00" +                                           // ObjectMagicProperties
            "00" + "00" +                                                    // MinimapIcon, InteractionAction
            "00",                                                            // tasks
            Convert.ToHexString(b));
        Assert.Equal(InstanceEntities.ObjectTypeHash("Metadata/MiscellaneousObjects/AreaTransition"),
            InstanceEntities.ObjectTypeHash(AreaTransitionObject.MetadataPath));
    }

    [Fact]
    public void Area_transition_with_a_tile_object_model_rotation_and_height()
    {
        // Live 2026-10-17: the Coast exit with the tile's arch (.tmd record beachencampment_v01_01_area_transition2):
        // Positioned rotation float, flags0 bit2 = fixed height (u32 steps of 7.8125, float z), Animated bit0 + .ao path.
        const string model = "Metadata/Terrain/Beach/Town_v2/beachencampment_v01_01_area_transition2.ao";
        byte[] b = new AreaTransitionObject(GridX: 388, GridY: 230, DestinationHash16: 0x7ad2, Model: model,
            Rotation: MathF.PI / 2, Height: -351.04f).Blob();
        string hex = Convert.ToHexString(b);
        Assert.StartsWith(
            "00" + "00" +
            "84010000" + "E6000000" + "DB0FC93F" + "0000" + "04" + "00" +   // Positioned (388, 230), 90°, flags0 0x04
            "D3FFFFFF" + "1F85AFC3" +                                        // height -45 steps, z -351.04
            "01" + "49000000" + Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes(model)) +
            "00" + "02" + "D27A" + "00", hex);                               // AreaTransition as before
        Assert.Equal(41 + 8 + 4 + model.Length * 2, b.Length);
    }

    [Fact]
    public void Area_transition_blob_fits_only_types_with_its_exact_components()
    {
        // AreaTransition / AreaTransitionMapMarker (ot_components.py); WaterCaveAreaTransition adds Transitionable, which
        // the client does not read for it (live 2026-10-18), so the Gateway creates it as a plain AreaTransition.
        string[] plain = ["Positioned", "BaseEvents", "Animated", "AreaTransition", "Stats", "ObjectMagicProperties", "MinimapIcon", "InteractionAction"];
        Assert.True(AreaTransitionObject.CanSerialize(plain));
        Assert.False(AreaTransitionObject.CanSerialize([.. plain, "Transitionable"]));
        Assert.False(AreaTransitionObject.CanSerialize(plain.Take(7)));
        var marker = new AreaTransitionObject(114, 307, 0xf00a, Type: "Metadata/MiscellaneousObjects/AreaTransitionMapMarker");
        Assert.Equal(new AreaTransitionObject(114, 307, 0xf00a).Blob(), marker.Blob());
    }

    [Fact]
    public void Player_skills_remove_is_a_0x20_update_with_only_ids()
    {
        byte[] m = InstanceEntities.PlayerSkillsChange(0x1000, [], [0x0103, 0x0104]);
        // index 9 = Actor, flags 0x20, no records, two ids (u16 LE), no 0x188 entries, no triples
        string body = "09" + "2000" + "00" + "02" + "0301" + "0401" + "00" + "00";
        Assert.Equal("02BA" + "00001000" + "00000000" + "0000" + (body.Length / 2).ToString("X4") + body, Convert.ToHexString(m));
    }

    [Fact]
    public void Player_skills_change_carries_records_like_the_list_then_the_removed_ids()
    {
        byte[] m = InstanceEntities.PlayerSkillsChange(0x1000, [new GrantedSkill(0x0102, 4474, 4430)], [0x0104]);
        // flags 0x20, one record (the 0x40 list's record layout), one id to remove, then the two empty lists
        string body = "09" + "2000" + "01" + "01" + "0201" + "00" + "7A110000" + "00" + "4E110000" + "00" + "00" +
            "00000000" + "00000000" + "00000000" + "00" + "00" + "0401" + "00" + "00";
        Assert.Equal("02BA" + "00001000" + "00000000" + "0000" + (body.Length / 2).ToString("X4") + body, Convert.ToHexString(m));
    }

    [Fact]
    public void A_skill_records_cooldown_is_its_third_u32()
    {
        // Haste: 1200 ms → skill +0xc8, which the client's cooldown reads.
        byte[] m = InstanceEntities.PlayerSkillsChange(0x1000, [new GrantedSkill(0x0105, 16886, 16733, CooldownMs: 1200)], []);
        Assert.Contains("00" + "00" + "00000000" + "00000000" + "B0040000" + "00" + "00", Convert.ToHexString(m));
    }

    [Fact]
    public void Monster_blob_with_a_skill_and_stats()
    {
        // Live 2026-10-17: Hungry Corpse with Melee (GEPL 4394) in its Actor and its attack stats: 175 bytes, 0 left.
        (uint, int)[] stats = [(0x142, 2400)];
        byte[] b = new MonsterObject(0x73, 0x45, Life: 15, MonsterObject.BaseComponents,
            Skills: [new GrantedSkill(0x0102, 4394, 4320, [.. stats, (0x4f32, 1)])], Stats: stats).Blob();
        string hex = Convert.ToHexString(b);
        Assert.Contains("0001" + "01" + "8142" + "8960" + "00000000" + "00000000" + "00", hex);   // Stats after Positioned
        Assert.EndsWith("4000" + "01" + "0201" + "00" + "2A110000" + "00" + "E0100000" + "00" + "00" +
            "00000000" + "00000000" + "00000000" + "00" + "02" + "8142" + "8960" + "C04F32" + "01" + "00" + "00000000" + "00", hex);
    }
}
