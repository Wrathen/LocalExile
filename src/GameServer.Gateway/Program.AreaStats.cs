partial class Program
{
    // The player's final stats for a skill's area (SkillArea): the panel total skill_area_of_effect_+% already holds the
    // base_skill_area_of_effect_+% of passives, items, /god and /set_aoe (PanelStats), so the base form is left out
    // here and only a skill's own base (gem quality) goes in when the skill's stats are laid over. SkillArea.Increased
    // sums both forms; with the player's base still in, every area skill counted it twice.
    internal static Dictionary<uint, int> AreaPlayerStats(ClientState conn) =>
        SkillArea.WithoutPlayerBase(FinalPlayerStats(conn).ToDictionary(kv => kv.Item1, kv => kv.Item2), Stats.Key(SkillArea.BaseIncreased));
}
