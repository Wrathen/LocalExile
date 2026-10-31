using GameServer.Core.Accounts;

partial class Program
{
    /// <summary>Left in the character's saved data from the moment of a Hardcore death until it has moved, so that a crash in between
    /// cannot bring the character back.</summary>
    internal const string HardcoreDeathMark = "hardcore.dead";

    // Called synchronously inside the lethal damage mutation, before its first network write. The player sees the death screen and
    // stays connected; the character (only: stashes stay) moves to the parent league once they have left (CommitHardcoreDeath).
    internal static void RecordHardcoreDeath(ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (!conn.Hardcore || conn.PlayerLife != 0 || conn.HardcoreDeathRecorded) return;
            string name = CharacterName(conn);
            ItemsOf(conn).SaveUnsaved();
            // Loading the save includes the pending XP total, so this saves it without consuming its retry buffer.
            CharacterSaves.ClearActiveAuras(name);
            CharacterSaves.ChangeModuleData(name, HardcoreDeathMark, _ => "1");
            conn.HardcoreDeathRecorded = true;
            conn.PendingTransfer = null;
            Console.WriteLine($"[gateway] {name}: Hardcore death in {conn.League}: moves to the parent league when the player leaves");
        }
    }

    // The connection has ended (the death screen's Exit, or any other way out).
    internal static void CommitHardcoreDeath(ClientState conn)
    {
        if (!conn.HardcoreDeathRecorded) return;
        string name = CharacterName(conn);
        var source = LeagueCatalog.Startup.ResolveSavedLeague(conn.League)
            ?? throw new InvalidOperationException("Cannot resolve the dying character's Hardcore league");
        var dead = HardcoreDeathPersistence.Commit(Saves.Db, name, source, () => ItemsOf(conn).SaveUnsaved(), expectedCharacterId: conn.CharacterId);
        CharacterSaves.ChangeModuleData(name, HardcoreDeathMark, _ => "");
        Console.WriteLine($"[gateway] {name}: Hardcore death committed: {source.Name} → {dead.League}" +
            (dead.HardcoreDeath?.PermanentlyDead == true ? " (permanently dead)" : " (carried items retained)"));
    }

    // A connection of a character that died and whose move a crash interrupted: it moves now, and this connection does not go on.
    internal static void RefuseInterruptedHardcoreDeath(ClientState conn)
    {
        if (!conn.Hardcore || CharacterSaves.ModuleData(CharacterName(conn), HardcoreDeathMark) is not { Length: > 0 }) return;
        conn.HardcoreDeathRecorded = true;
        CommitHardcoreDeath(conn);
        throw new InvalidOperationException($"{CharacterName(conn)} died in {conn.League}: moved to the parent league");
    }
}
