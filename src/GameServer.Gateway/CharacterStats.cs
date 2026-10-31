using System.Text.Json;
using GameServer.Core.World;

/// <summary>
/// A character's level-based pools and attribute bonuses, from its class, level and allocated passives: what the server
/// sends as final stats (the client derives none, 11-skills.md §4). Path of Exile's base rules, before items and buffs,
/// with the per-level values of Metadata/Characters/Character.ot (life_per_level 12, mana_per_level 6,
/// accuracy_rating_per_level 2, base_evasion_rating 15 and no evasion per level):
/// life = (Characters.dat BaseMaxLife (38) + 12 per level + 1 per 2 strength + flat life) × (1 + increased life);
/// mana likewise with BaseMaxMana (34), 6 per level and intelligence; accuracy = (2 per level + 2 per dexterity + flat)
/// × (1 + increased accuracy / 100), with the Accuracy Mastery's per-level and per-dexterity replacements;
/// armour = flat armour × (1 + increased armour / 100); evasion = (15 + flat evasion) raised by 1% per 5 dexterity and
/// increased evasion; 1% increased maximum energy shield per 10 intelligence and 1% increased melee physical damage per 5
/// strength. Without these stats the client falls back to the base for level 1 (the Witch's 50 life and 40 mana).
/// </summary>
static class CharacterStats
{
    public sealed record Pools(int Level, uint Life, uint Mana, int Accuracy, uint Experience,
        int Armour = 0, int ArmourPct = 0, int Evasion = BaseEvasion, int EvasionPct = 0,
        int EnergyShieldPct = 0, int MeleePhysicalDamagePct = 0,
        int Strength = 0, int Dexterity = 0, int Intelligence = 0,
        int FireResistance = BaseResistance, int ColdResistance = BaseResistance,
        int LightningResistance = BaseResistance, int ChaosResistance = BaseResistance,
        int FireMaxResistance = DamageModel.DefaultMaxResistance, int ColdMaxResistance = DamageModel.DefaultMaxResistance,
        int LightningMaxResistance = DamageModel.DefaultMaxResistance, int ChaosMaxResistance = DamageModel.DefaultMaxResistance,
        int SpellSuppressionChance = 0, int SpellSuppressionEffect = DamageModel.SpellSuppressionEffect,
        bool SpellSuppressionLucky = false, bool SpellSuppressionUnlucky = false,
        int SpellSuppressionWhileFullLife = 0, int SpellSuppressionWhileMoving = 0, int SpellSuppressionIfHitRecently = 0,
        int MovementVelocityPct = 0, int BlockChance = 0, int SpellBlockChance = 0,
        int BlockChanceMax = DamageModel.DefaultMaxBlockChance, int SpellBlockChanceMax = DamageModel.DefaultMaxSpellBlockChance,
        int BlockChanceBase = 0, int BlockChanceUncapped = 0, int SpellBlockChanceUncapped = 0,
        int SpellBlockPer5AttackBlock = 0, int LifeGainedOnBlock = 0, int ManaGainedOnBlock = 0,
        bool VersatileCombatant = false, bool AttackBlockLucky = false, bool SpellBlockLucky = false,
        bool AttackBlockUnlucky = false, bool SpellBlockUnlucky = false,
        int AttackSpeedPct = 0,
        long CriticalStrikeChancePct = 0, long SpellCriticalStrikeChancePct = 0,
        int CriticalStrikeMultiplier = DamageModel.BaseCriticalStrikeMultiplier,
        int SpellCriticalStrikeMultiplier = DamageModel.BaseCriticalStrikeMultiplier,
        int AttackCriticalStrikeChance = 0,
        int SpellCriticalStrikeChance = DamageModel.BaseSpellCriticalStrikeChancePerMyriad,
        long AccuracyPct = 0,
        // The Accuracy Mastery's target-conditional "more" effects, kept out of Accuracy: they only apply to a hit's
        // own target (TargetAccuracy), so the panel must not show them as the character's accuracy.
        int AccuracyMoreVsUniquePct = 0, int AccuracyMoreInCloseRangePct = 0,
        int ManaReservationEfficiencyPct = 0, int LifeReservationEfficiencyPct = 0,
        int AuraEffectPct = 0, int AuraAreaPct = 0, uint EnergyShield = 0,
        int ColdOvercapEvasionPct = 0, int LightningOvercapCritPct = 0,
        int SpellSuppressionWhileFullEnergyShield = 0, int? SpellSuppressionChanceUncapped = null,
        bool ArmourAppliesToChaosDamage = false, uint Ward = 0,
        // The rating while the player moves, when a "while moving" increase exists (null: the same as Evasion).
        int? EvasionWhileMoving = null)
    {
        // Identifies the transient source snapshot used by this immutable pool calculation, including with-clones.
        internal long FortificationRevision { get; init; }
        // Calculated zero attributes are valid; legacy partial pool records may still need class defaults.
        internal bool AttributesResolved { get; init; }
        // Player stats that monster swings, player ticks, regeneration steps and sweeps read for every character: resolved
        // once per pool build (BuildPoolsFor), never a full stat build on those paths. 0/false: the character has none.
        internal int AttackerSlowPct { get; init; }
        internal int EnemyRegenerationOnHitPct { get; init; }
        internal int VsBlindedCriticalPct { get; init; }
        internal bool WhileMovingStats { get; init; }
        // Real pool builds retain the signed base before the one final floor; legacy partial pools use their rating.
        internal long AccuracyBase { get; init; } = Accuracy;
    }

    /// <summary>What allocated passives and equipped base items add to the pools: attributes, flat and increased life,
    /// mana, armour and evasion, increased energy shield and melee physical damage, the base resistances, attack speed's
    /// increased and more multipliers, critical strike chance and multiplier, and the Accuracy Mastery's conversions.</summary>
    public sealed record Bonus(int Strength = 0, int Dexterity = 0, int Intelligence = 0, int Life = 0, int LifePct = 0,
        int Mana = 0, int ManaPct = 0, decimal Armour = 0, int ArmourPct = 0, decimal Evasion = 0, int EvasionPct = 0,
        int SharedDefencesPct = 0, bool IronReflexes = false, bool ChaosInoculation = false,
        int EnergyShieldPct = 0, int MeleePhysicalDamagePct = 0,
        int FireResistance = 0, int ColdResistance = 0, int LightningResistance = 0, int ChaosResistance = 0,
        int FireMaxResistance = 0, int ColdMaxResistance = 0, int LightningMaxResistance = 0, int ChaosMaxResistance = 0,
        int SpellSuppressionChance = 0, int SpellSuppressionEffect = 0,
        bool SpellSuppressionLucky = false, bool SpellSuppressionUnlucky = false,
        int SpellSuppressionWhileFullLife = 0, int SpellSuppressionWhileMoving = 0, int SpellSuppressionIfHitRecently = 0,
        int MovementVelocityPct = 0, int BlockChance = 0, int SpellBlockChance = 0,
        int BlockChanceMax = 0, int SpellBlockChanceMax = 0,
        int SpellBlockPer5AttackBlock = 0, int LifeGainedOnBlock = 0, int ManaGainedOnBlock = 0,
        int BlockChanceInc = 0, int BlockChanceMore = 0,
        bool VersatileCombatant = false, bool AttackBlockLucky = false, bool SpellBlockLucky = false,
        bool AttackBlockUnlucky = false, bool SpellBlockUnlucky = false,
        int AttackSpeedPct = 0, int AttackSpeedMorePct = 0,
        long CriticalStrikeChancePct = 0, long SpellCriticalStrikeChancePct = 0,
        int CriticalStrikeMultiplier = 0, int SpellCriticalStrikeMultiplier = 0,
        long Accuracy = 0, long AccuracyPct = 0, int AccuracyPerLevel = 0,
        bool DexterityAccuracyTriple = false, bool AccuracyEqualsIntelligence = false,
        int AccuracyMoreVsUniquePct = 0, int AccuracyMoreInCloseRangePct = 0,
        int ManaReservationEfficiencyPct = 0, int LifeReservationEfficiencyPct = 0,
        int AuraEffectPct = 0, int AuraAreaPct = 0, decimal ArmourMorePct = 0, decimal EvasionMorePct = 0, int EnergyShield = 0,
        // The overcapped-resistance flags' derived increases (ResistanceOvercap), computed once in PoolsFor from the
        // final raw resistances and maxima: +1% per 1% of cold overcap on evasion, lightning overcap on crit chance.
        int ColdOvercapEvasionPct = 0, int LightningOvercapCritPct = 0,
        int ManaPerFourStrength = 0,
        // These local shield values are already included in Armour/Evasion/EnergyShield. Keep their identity so
        // shield-only increases can add to the global increase bucket without scaling other flat sources.
        decimal ShieldArmour = 0, decimal ShieldEvasion = 0, int ShieldEnergyShield = 0,
        int ShieldArmourPct = 0, int ShieldEvasionPct = 0, int ShieldEnergyShieldPct = 0,
        int SharedShieldDefencesPct = 0, bool NoShieldDefences = false,
        // Body values are likewise local subsets of the flat totals, before slot-specific global operators.
        decimal BodyArmour = 0, decimal BodyEvasion = 0, int BodyEnergyShield = 0, int BodyEnergyShieldPct = 0,
        bool DoubleBodyArmour = false, bool NoBodyArmour = false,
        int SpellSuppressionWhileFullEnergyShield = 0,
        bool ArmourAppliesToChaosDamage = false, AttributeModifiers Attributes = default,
        int StrengthPct = 0, int DexterityPct = 0, int IntelligencePct = 0, int AllAttributesPct = 0,
        int LifePerLevel = 0, int ManaPerLevel = 0,
        int LifePerTenDexterity = 0, int LifePerTenIntelligence = 0,
        int LifePerFourDexterity = 0, int LifePerTwoIntelligence = 0,
        int EnergyShieldPctPerTenStrength = 0, int EvasionPctPerTenIntelligence = 0,
        bool NoLifeFromStrength = false, bool NoManaFromIntelligence = false,
        bool NoStrengthBonuses = false, bool NoDexterityBonuses = false, bool NoIntelligenceBonuses = false,
        bool MentalConditioning = false, decimal Ward = 0, int WardPct = 0, int EvasionWhileMovingPct = 0)
    {
        public static readonly Bonus None = new();
    }

    /// <summary>Character.ot base_evasion_rating.</summary>
    public const int BaseEvasion = 15;

    /// <summary>Path of Exile's resistance before the quest penalties: 0% to all four types at character level 1.
    /// Act 5 and Act 10 Kitava each take another 30% away; see <see cref="ResistancePenalty"/> in the Gateway.</summary>
    public const int BaseResistance = 0;

    /// <summary>Strength, dexterity and intelligence from attribute stats: the single ones, all attributes and the
    /// pairs, each in its base_ and additional_ form, then the percentage modifiers (individual and all-attributes)
    /// applied to the summed flat value.</summary>
    public static (int Strength, int Dexterity, int Intelligence) Attributes(Func<string, int> get)
        => AttributeModifiers.From(get).Flat;

    static int Percent(int value, int percent) =>
        percent == 0 ? value : (int)Math.Floor(value * (100 + percent) / 100.0);

    /// <summary>
    /// Resolve ordinary attributes from the whole class/flat bucket and additive typed/all increases. Pools and
    /// equipment qualification use this same calculation. The half-up projection is pinned PoB Common.lua round;
    /// the final native integer cannot independently establish the official server's fractional rounding.
    /// Attribute conversions, more multipliers and conditional replacements are separate operators.
    /// </summary>
    public static (int Str, int Dex, int Int) ResolveAttributes((int Str, int Dex, int Int) baseAttributes,
        (int Str, int Dex, int Int) flat, (int Str, int Dex, int Int) increases, int allIncrease = 0)
    {
        return new AttributeModifiers(flat.Str, flat.Dex, flat.Int,
            (long)increases.Str + allIncrease, (long)increases.Dex + allIncrease,
            (long)increases.Int + allIncrease).Apply(baseAttributes.Str, baseAttributes.Dex, baseAttributes.Int);
    }

    /// <summary>Spell Suppression chance with the conditional passives whose condition holds now (on full life, moving,
    /// hit an enemy recently, on full energy shield), bounded to 0..100% after every active source is added.</summary>
    public static int SuppressionChance(Pools p, bool fullLife, bool moving, bool hitRecently,
        bool fullEnergyShield = false) =>
        (int)Math.Clamp((long)(p.SpellSuppressionChanceUncapped ?? p.SpellSuppressionChance)
            + (fullLife ? p.SpellSuppressionWhileFullLife : 0)
            + (moving ? p.SpellSuppressionWhileMoving : 0)
            + (hitRecently ? p.SpellSuppressionIfHitRecently : 0)
            + (fullEnergyShield ? p.SpellSuppressionWhileFullEnergyShield : 0),
            0L, DamageModel.SpellSuppressionChanceCap);

    public static Pools For(SkillCatalog.ClassInfo c, int level, Bonus? bonus = null, string folder = "data")
    {
        var b = bonus ?? Bonus.None;
        level = Math.Clamp(level, 1, Progression.MaxLevel);
        var attributes = b.Attributes with
        {
            StrengthPct = b.Attributes.StrengthPct + b.StrengthPct + (long)b.AllAttributesPct,
            DexterityPct = b.Attributes.DexterityPct + b.DexterityPct + (long)b.AllAttributesPct,
            IntelligencePct = b.Attributes.IntelligencePct + b.IntelligencePct + (long)b.AllAttributesPct,
        };
        var (str, dex, intel) = attributes.Apply((long)c.Strength + b.Strength,
            (long)c.Dexterity + b.Dexterity, (long)c.Intelligence + b.Intelligence);
        // These prohibitions remove only inherent grants. Final attributes still supply item group terms and
        // requirements; narrow and broad flags suppress their shared contribution once.
        int strengthLife = b.NoLifeFromStrength || b.NoStrengthBonuses ? 0 : str / 2;
        int intelligenceMana = b.NoManaFromIntelligence || b.NoIntelligenceBonuses ? 0 : intel / 2;
        // Complete attribute groups and signed per-level additions join the flat bucket after final attributes.
        // Preserve the existing floor projection; pinned PoB's final pool round is a separate parity question.
        // Equipment ES-to-Life conversion can supply a large flat value. Keep the signed panel stat representable.
        decimal flatLife = c.BaseMaxLife + 12m * level + strengthLife + b.Life
            + LifeManaPoolSources.AdditionalLife(level, dex, intel, b);
        long flatMana = c.BaseMaxMana + 6L * level + intelligenceMana + b.Mana
            + LifeManaPoolSources.AdditionalMana(level, b);
        uint life = b.ChaosInoculation ? 1 : (uint)Math.Clamp(decimal.Floor(flatLife
            * (100m + b.LifePct) / 100m), 1m, int.MaxValue);
        uint mana = b.ManaPerFourStrength == 0
            ? (uint)Math.Clamp(decimal.Floor(flatMana
                * (100m + b.ManaPct) / 100m), 0m, int.MaxValue)
            : StrengthMana.Maximum(flatMana, str, b.ManaPerFourStrength, b.ManaPct);
        // PoB CalcOffence: accuracy = floor(base * (1 + increased / 100)) with no more multiplier modelled. The
        // Accuracy Mastery can replace the 2 per level and 2 per dexterity, and add Accuracy equal to Intelligence.
        long accuracyPerLevel = 2L + b.AccuracyPerLevel;
        int accuracyPerDex = b.NoDexterityBonuses ? 0 : b.DexterityAccuracyTriple ? 3 : 2;
        long accuracyBase = accuracyPerLevel * level + (long)accuracyPerDex * dex + b.Accuracy
            + (b.AccuracyEqualsIntelligence ? intel : 0);
        int accuracy = AccuracyRating.Calculate(accuracyBase, b.AccuracyPct);
        // PoB CalcDefence: armour and evasion are flat defences raised by their increased modifiers. Evasion keeps the
        // character's 15 base and gains 1% per 5 dexterity; armour has no base outside items and passives. The
        // overcapped cold resistance's increase is one of the evasion increases (it applies before the conversion, so
        // Iron Reflexes' converted evasion keeps it, as PoB's converted branch reads the same Evasion INC bucket).
        int armourPct = b.ArmourPct;
        // Native slot calculations add slot-specific increases to the applicable global bucket. Sum fractional
        // contributions before rounding the final rating (AR 0x142732ee7, EV 0x14272a59b, ES 0x14252c6e7).
        decimal Increased(decimal total, decimal shield, decimal body, long increase, long shieldIncrease,
            long bodyIncrease = 0, bool bodyAsArmour = false)
        {
            // Keep each known local source even if the aggregate was saturated while merging equipment.
            decimal fromShield = Math.Max(0m, shield), fromBody = Math.Max(0m, body);
            decimal bodyFactor = bodyAsArmour ? b.NoBodyArmour ? 0m : b.DoubleBodyArmour ? 2m : 1m : 1m;
            return Math.Max(0m, total - fromShield - fromBody) * Math.Max(0m, 100m + increase) / 100m
                + (b.NoShieldDefences ? 0m : fromShield * Math.Max(0m, 100m + increase + shieldIncrease) / 100m)
                + fromBody * Math.Max(0m, 100m + increase + bodyIncrease) / 100m * bodyFactor;
        }
        decimal More(decimal value, decimal increase)
        {
            decimal factor = Math.Max(0m, 100m + increase) / 100m;
            return factor > 1m && value > decimal.MaxValue / factor ? decimal.MaxValue : value * factor;
        }
        int Rating(decimal value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0m, int.MaxValue);
        decimal armourValue = More(Increased(b.Armour, b.ShieldArmour, b.BodyArmour, armourPct,
            (long)b.ShieldArmourPct + b.SharedShieldDefencesPct, bodyAsArmour: true), b.ArmourMorePct);
        // These item coefficients use complete groups of final production attributes. They join ordinary INC,
        // before slot increases/MORE; Iron Reflexes retains this item term while excluding inherent Dexterity.
        long ordinaryEvasionPct = (long)b.EvasionPct + b.ColdOvercapEvasionPct
            + (long)b.EvasionPctPerTenIntelligence * (intel / 10);
        long totalEvasionPct = (b.NoDexterityBonuses ? 0L : dex / 5L) + ordinaryEvasionPct;
        int evasionPct = (int)Math.Clamp(totalEvasionPct, int.MinValue, int.MaxValue);
        decimal evasionBase = BaseEvasion + b.Evasion;
        decimal evasionValue = More(Increased(evasionBase, b.ShieldEvasion, b.BodyEvasion, totalEvasionPct,
            (long)b.ShieldEvasionPct + b.SharedShieldDefencesPct), b.EvasionMorePct);
        // "Increased evasion while moving" joins the same increased bucket while the condition holds (PoB Condition Moving).
        // shortcut: Iron Reflexes' converted armour does not get it; upgrade when armour reads a moving condition.
        int? evasionWhileMoving = b.EvasionWhileMovingPct == 0 || b.IronReflexes ? null
            : Rating(More(Increased(evasionBase, b.ShieldEvasion, b.BodyEvasion, totalEvasionPct + b.EvasionWhileMovingPct,
                (long)b.ShieldEvasionPct + b.SharedShieldDefencesPct), b.EvasionMorePct));
        if (b.IronReflexes)
        {
            // Iron Reflexes converts all evasion rating to armour and removes dexterity's evasion bonus. The converted
            // evasion keeps the armour, evasion and shared defence increases (each once), but not dexterity.
            long convertedPct = armourPct + ordinaryEvasionPct - b.SharedDefencesPct;
            // Native 0x142731a7b also doubles converted body EV, or suppresses it with the no-body-AR flag.
            decimal converted = More(More(Increased(evasionBase, b.ShieldEvasion, b.BodyEvasion, convertedPct,
                (long)b.ShieldArmourPct + b.ShieldEvasionPct + b.SharedShieldDefencesPct, bodyAsArmour: true),
                b.EvasionMorePct), b.ArmourMorePct);
            armourValue = armourValue > decimal.MaxValue - converted ? decimal.MaxValue : armourValue + converted;
            evasionPct = (int)Math.Clamp(ordinaryEvasionPct, int.MinValue, int.MaxValue);
            evasionValue = 0;
        }
        int armour = Rating(armourValue), evasion = Rating(evasionValue);
        if (b.MentalConditioning)
        {
            long rawFlatManaTwice = 2 * (c.BaseMaxMana + 6L * level + b.Mana
                + LifeManaPoolSources.AdditionalMana(level, b) + StrengthMana.Flat(str, b.ManaPerFourStrength))
                + (b.NoManaFromIntelligence || b.NoIntelligenceBonuses ? 0 : intel);
            (mana, armour) = PowerOfPurpose.Pools(rawFlatManaTwice, b, intel);
        }
        // Native ClientStrings IntelligenceDescription: 10 Intelligence grants 1% increased ES.
        // Pinned PoB CalcPerform applies complete groups after resolving final attributes.
        long totalEnergyShieldPct = (b.NoIntelligenceBonuses ? 0L : intel / 10L) + b.EnergyShieldPct
            + (long)b.EnergyShieldPctPerTenStrength * (str / 10);
        int energyShieldPct = (int)Math.Clamp(totalEnergyShieldPct, int.MinValue, int.MaxValue);
        uint energyShield = (uint)Rating(Increased(b.EnergyShield, b.ShieldEnergyShield, b.BodyEnergyShield,
            totalEnergyShieldPct, (long)b.ShieldEnergyShieldPct + b.SharedShieldDefencesPct, b.BodyEnergyShieldPct));
        int Max(int bonus) => Math.Min(DamageModel.MaxResistCap, DamageModel.DefaultMaxResistance + bonus);
        int maxBlock = Math.Min(DamageModel.BlockChanceCap, DamageModel.DefaultMaxBlockChance + b.BlockChanceMax);
        int maxSpellBlock = Math.Min(DamageModel.BlockChanceCap, DamageModel.DefaultMaxSpellBlockChance + b.SpellBlockChanceMax);
        // Versatile Combatant: -10% to both maxima. The overcap spell block is applied after the weapon/shield base
        // in PoolsFor (the keystone reads the final uncapped attack chance).
        if (b.VersatileCombatant)
        {
            maxBlock = Math.Max(0, maxBlock - 10);
            maxSpellBlock = Math.Max(0, maxSpellBlock - 10);
        }
        // PoB CalcOffence: rate = base rate * (1 + increased / 100) * more. The client takes one integer
        // main_hand_attack_speed_+%, so send the equivalent increased value (93 for 75% increased and 10% more).
        int attackSpeedPct = (int)Math.Round(
            ((1 + b.AttackSpeedPct / 100.0) * (1 + b.AttackSpeedMorePct / 100.0) - 1) * 100,
            MidpointRounding.AwayFromZero);
        attackSpeedPct = Math.Max(-99, attackSpeedPct);
        return new(level, life, mana, accuracy, ExperienceFor(level, folder),
            Armour: armour, ArmourPct: armourPct, Evasion: evasion, EvasionPct: evasionPct,
            EnergyShieldPct: energyShieldPct, EnergyShield: energyShield,
            MeleePhysicalDamagePct: (int)Math.Clamp((b.NoStrengthBonuses ? 0L : str / 5L)
                + b.MeleePhysicalDamagePct, int.MinValue, int.MaxValue),
            Strength: str, Dexterity: dex, Intelligence: intel,
            FireResistance: BaseResistance + b.FireResistance, ColdResistance: BaseResistance + b.ColdResistance,
            LightningResistance: BaseResistance + b.LightningResistance, ChaosResistance: BaseResistance + b.ChaosResistance,
            FireMaxResistance: Max(b.FireMaxResistance), ColdMaxResistance: Max(b.ColdMaxResistance),
            LightningMaxResistance: Max(b.LightningMaxResistance), ChaosMaxResistance: Max(b.ChaosMaxResistance),
            SpellSuppressionChance: Math.Clamp(b.SpellSuppressionChance, 0, DamageModel.SpellSuppressionChanceCap),
            SpellSuppressionChanceUncapped: b.SpellSuppressionChance,
            SpellSuppressionEffect: Math.Max(0, DamageModel.SpellSuppressionEffect + b.SpellSuppressionEffect),
            SpellSuppressionLucky: b.SpellSuppressionLucky, SpellSuppressionUnlucky: b.SpellSuppressionUnlucky,
            SpellSuppressionWhileFullLife: b.SpellSuppressionWhileFullLife,
            SpellSuppressionWhileMoving: b.SpellSuppressionWhileMoving,
            SpellSuppressionIfHitRecently: b.SpellSuppressionIfHitRecently,
            SpellSuppressionWhileFullEnergyShield: b.SpellSuppressionWhileFullEnergyShield,
            MovementVelocityPct: b.MovementVelocityPct,
            BlockChance: b.BlockChance, SpellBlockChance: b.SpellBlockChance,
            BlockChanceMax: maxBlock, SpellBlockChanceMax: maxSpellBlock, BlockChanceUncapped: b.BlockChance,
            SpellBlockChanceUncapped: b.SpellBlockChance,
            SpellBlockPer5AttackBlock: b.SpellBlockPer5AttackBlock, LifeGainedOnBlock: b.LifeGainedOnBlock,
            ManaGainedOnBlock: b.ManaGainedOnBlock, VersatileCombatant: b.VersatileCombatant,
            AttackBlockLucky: b.AttackBlockLucky, SpellBlockLucky: b.SpellBlockLucky,
            AttackBlockUnlucky: b.AttackBlockUnlucky, SpellBlockUnlucky: b.SpellBlockUnlucky, AttackSpeedPct: attackSpeedPct,
            CriticalStrikeChancePct: b.CriticalStrikeChancePct + b.LightningOvercapCritPct,
            SpellCriticalStrikeChancePct: b.SpellCriticalStrikeChancePct + b.LightningOvercapCritPct,
            CriticalStrikeMultiplier: DamageModel.BaseCriticalStrikeMultiplier + b.CriticalStrikeMultiplier,
            SpellCriticalStrikeMultiplier: DamageModel.BaseCriticalStrikeMultiplier + b.SpellCriticalStrikeMultiplier,
            SpellCriticalStrikeChance: DamageModel.CriticalStrikeChancePerMyriad(
                DamageModel.BaseSpellCriticalStrikeChancePerMyriad,
                b.SpellCriticalStrikeChancePct + b.LightningOvercapCritPct),
            AccuracyPct: b.AccuracyPct,
            AccuracyMoreVsUniquePct: b.AccuracyMoreVsUniquePct,
            AccuracyMoreInCloseRangePct: b.AccuracyMoreInCloseRangePct,
            ManaReservationEfficiencyPct: b.ManaReservationEfficiencyPct,
            LifeReservationEfficiencyPct: b.LifeReservationEfficiencyPct,
            AuraEffectPct: b.AuraEffectPct, AuraAreaPct: b.AuraAreaPct,
            ColdOvercapEvasionPct: b.ColdOvercapEvasionPct, LightningOvercapCritPct: b.LightningOvercapCritPct,
            ArmourAppliesToChaosDamage: b.ArmourAppliesToChaosDamage, Ward: WardState.Max(b.Ward, b.WardPct),
            EvasionWhileMoving: evasionWhileMoving)
            { AttributesResolved = true, AccuracyBase = accuracyBase };
    }

    private static readonly Lazy<uint[]> Experience = new(() => LoadExperience("data"));

    /// <summary>Every level's starting experience (index = level − 1), ExperienceLevels.dat.</summary>
    public static IReadOnlyList<uint> LevelStarts => Experience.Value;

    /// <summary>The experience at the start of a level (ExperienceLevels.dat; level 37: 12 361 842). The client derives
    /// the level from the Player component's experience (04 §4).</summary>
    public static uint ExperienceFor(int level, string folder = "data")
    {
        var table = folder == "data" ? Experience.Value : LoadExperience(folder);
        return level >= 1 && level <= table.Length ? table[level - 1] : 0;
    }

    private static uint[] LoadExperience(string folder)
    {
        string path = Path.Combine(folder, "tables", "ExperienceLevels.json");
        if (!File.Exists(path)) return [0];
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        return [.. d.RootElement.EnumerateArray().OrderBy(r => r.GetProperty("Level").GetInt32()).Select(r => (uint)r.GetProperty("Experience").GetInt64())];
    }
}
