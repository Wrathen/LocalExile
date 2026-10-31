using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    internal static async Task RegisterStationaryCollision(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, uint id, string type, int x, int y,
        float rotation, string? editorKey = null, string? model = null)
    {
        var (size, hull) = StationaryCollision.Resolve(type, model);
        if (size >= 0) conn.SetBlocker(id, x, y, size, hull);
        else conn.RemoveBlocker(id);
        if (StationaryCollision.For(type).BlockingLeagueFlag == "HaveGuild"
            && conn.LeagueQuestFlags.Add(Quests.League.HaveGuild))
            await Send(remote, stream, ciphers, InstanceEntities.PlayerLeagueQuestFlagsUpdate(PlayerId,
                Quests.League.Bits(conn.LeagueQuestFlags)), "guild storage access enables native collision");
        Console.WriteLine($"[gateway] stationary collision 0x{id:x} {type} at ({x},{y}): size {size}, hull {hull?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "none"}");
        if (editorKey is not null) conn.HideoutEditor.Register(id, editorKey, x, y, rotation, size, hull);
    }
}
