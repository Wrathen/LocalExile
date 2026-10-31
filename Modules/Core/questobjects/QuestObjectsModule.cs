using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using GameServer.Modding;
using static Program;
using static QuestObjectsFeature;

namespace GameServer.Modules.M_questobjects;

/// <summary>The objects of the campaign's quests: the opening tutorial, its gems, and the Glyph quest.</summary>
public sealed class QuestObjectsModule : IModule, IDisposable
{
    readonly List<IDisposable> _events = [];
    void On<T>(GameEvent<T> ev, T run) where T : Delegate => _events.Add(ev.Add("questobjects", run));

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var e in _events) e.Dispose();
    }

    /// <inheritdoc/>
    public void Load(IModuleContext module)
    {
        module.OnAreaGenerated(async player =>
        {
            var conn = ConnOf(player); var (remote, stream, ciphers) = conn.Wire;
            await SpawnQuestRoomObjects(remote, stream, ciphers, conn);
            await SpawnOpeningTutorial(remote, stream, ciphers, conn);
            await SpawnGlyphQuest(remote, stream, ciphers, conn);
        });
        On(GameEvents.Kill, kill =>
        {
            if (kill.Conn.Layout.Id != TutorialObjects.Strand || kill.VarietyId != TutorialObjects.Zombie
                || CharacterSaves.Load(CharacterName(kill.Conn))?.Tutorial is not { ZombieKilled: false }) return;
            CharacterSaves.ChangeTutorial(CharacterName(kill.Conn), t => t with { ZombieKilled = true });
            var (remote, stream, ciphers) = kill.Conn.Wire;
            kill.Later(() => DropTutorialGem(remote, stream, ciphers, kill.Conn, false, kill.At.X, kill.At.Y));
        });
        On(GameEvents.NpcTalk, async (conn, id, npc) =>
        {
            if (npc.Npc.Id != TutorialObjects.Exile) return false;
            var (remote, stream, ciphers) = conn.Wire;
            await TalkToWoundedExile(remote, stream, ciphers, conn, id, npc);
            return true;
        });
        On(GameEvents.ObjectUse, async (conn, use, requestedAt) =>
        {
            var (remote, stream, ciphers) = conn.Wire;
            if (conn.TutorialChestId == use.TargetId && conn.TutorialChest is { } tutorialChest)
            {
                await OpenTutorialChest(remote, stream, ciphers, conn, use, tutorialChest, requestedAt);
                return true;
            }
            GlyphQuest.Nest? nest;
            lock (conn.GlyphNests) nest = conn.GlyphNests.GetValueOrDefault(use.TargetId);
            if (nest is not null)
            {
                await OpenRhoaNest(remote, stream, ciphers, conn, use, nest);
                return true;
            }
            if (conn.GlyphWallId != 0 && use.TargetId == conn.GlyphWallId)
            {
                await UseGlyphWall(remote, stream, ciphers, conn, use);
                return true;
            }
            return false;
        });
    }
}
