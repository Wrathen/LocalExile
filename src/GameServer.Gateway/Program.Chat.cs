using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // The global and trade channel of every character on this server (ChatChannels.cs).
    internal static readonly ChatChannels Channels = new();

    // C→S 0x134: the client joins a global or trade channel after entering an area and for /global N and /trade N, and
    // prints "Timeout while joining … chat channel" when no 0x135 comes within 5 s. The join is answered with 0x135, then
    // a chat line naming the channel when it is new for the character or was asked for by a command.
    internal static async Task JoinChatChannel(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        InstanceChat.JoinChannelRequest join)
    {
        var (channel, changed) = Channels.Join(CharacterName(conn), join.Kind, join.Channel, join.Language);
        string asked = join.Channel == InstanceChat.AnyChannel ? "any" : join.Channel.ToString();
        Console.WriteLine($"[gateway] {remote} <= 0x0134 join {join.Kind} chat channel {asked} ({(join.Automatic ? "on entering" : "command")}, language {join.Language})");
        await Send(remote, stream, ciphers, InstanceChat.ChannelJoined(join.Kind, channel, join.Language), $"0x0135 joined {join.Kind} chat channel {channel}");
        if (changed || !join.Automatic)
        {
            string text = ChatChannels.JoinedText(join.Kind, channel);
            await Send(remote, stream, ciphers, InstanceChat.ChatLine(text), $"0x0017 chat line \"{text}\"");
        }
    }

    // Every area: the channels the character joined earlier in the session (S→C 0x135), since the new instance connection
    // starts without any and the client asks only once per session.
    internal static async Task SendChatChannels(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        foreach (var (kind, channel, language) in Channels.Current(CharacterName(conn)))
            await Send(remote, stream, ciphers, InstanceChat.ChannelJoined(kind, channel, language), $"0x0135 still on {kind} chat channel {channel}");
    }

    // C→S 0x15 without a slash: the player's own line comes back as a chat line from the character, on the channel it was
    // typed in (local, '#' global, '$' trade), as it would from a server relaying it to the channel.
    internal static async Task EchoChat(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string typed)
    {
        if (ChatChannels.Echo(CharacterName(conn), typed) is { } line)
            await Send(remote, stream, ciphers, line, $"0x0017 chat line from {CharacterName(conn)}");
    }
}
