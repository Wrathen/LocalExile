using GameServer.Core.Protocol.Instance;
using GameServer.Modding;

namespace GameServer.Modules.NpcDialogue;

/// <summary>What every NPC's dialogue options do: their text, quest rewards, hideout selection, respec and identifying.</summary>
public sealed class NpcDialogueModule : IModule
{
    /// <inheritdoc/>
    public void Load(IModuleContext module) => module.OnNpcOption(async talk =>
    {
        var world = module.World(talk.Player);
        switch (talk.Category)
        {
            case "SelectHideout":
                await world.NpcClose("0x0084 close NPC dialogue for hideout selection");
                await talk.Player.Send(InstanceWaypoints.OpenUi(InstanceNpcs.HideoutSelectionUi, talk.OpensGuildHideouts ? 1u : 0u),
                    $"0x007b open hideout selection ({(talk.OpensGuildHideouts ? "guild" : "character")})");
                return true;
            case "QuestReward" when talk.RewardOffer is { } offer && await world.NpcOfferReward(offer):
                return true;
            case "Gossip" or "Quests" or "QuestReward" when talk.HasText:
                // 0x84 with the row's index: the client opens the dialogue window on that NPCTalk row (its text and audio).
                await world.NpcSay(talk);
                return true;
            // Bestel's Respecialisation (script: SendOpenScreen( CharacterPassiveTreeScreen, 1 )): the character's passive tree,
            // S→C 0x7b kind 0x5f with 1. Its refunds come back as C→S 0x36 (per node) and 0x37 (Refund All), paid in gold.
            case "RespecPassivePointsForGold":
                world.NpcEndTalk();
                await talk.Player.Send(InstanceWaypoints.OpenUi(InstanceNpcs.PassiveTreeUi, 1), "0x007b open the passive tree (respec)");
                return true;
            // Nessa's Identify Items (script: triggerer.IdentifyItems(), then the IdentifyAll sound): every unidentified item
            // in the main inventory, free. The sound effect is not sent (its message is not mapped).
            case "IdentifyItems":
                await world.NpcIdentifyItems();
                return true;
            default:
                return false;
        }
    });
}
