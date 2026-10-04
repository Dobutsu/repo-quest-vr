using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Internal;

namespace RepoQuest.Steam
{
    /// <summary>
    /// Auth session tickets (ISteamUser::GetAuthSessionTicket) for SteamKit2 2.5, which predates SteamKit's own
    /// SteamAuthTicket handler (3.x). Ported from SteamKit 3.4.0 (LGPL-2.1, SteamKit2/Steam/Handlers/SteamAuthTicket)
    /// without .NET 8-only APIs. A ticket is a game connect token (sent by Steam at logon) plus a session header,
    /// registered with Steam through ClientAuthList, followed by the app ownership ticket. Photon validates it with
    /// Steam (REPO's Photon app uses Steam custom authentication).
    /// </summary>
    internal sealed class AuthTicketHandler : ClientMsgHandler
    {
        private const uint TicketTypeAuthSession = 2;

        private readonly Queue<byte[]> connectTokens = new Queue<byte[]>();
        private readonly Dictionary<uint, List<CMsgAuthTicket>> ticketsByGame = new Dictionary<uint, List<CMsgAuthTicket>>();
        private readonly Dictionary<ulong, TaskCompletionSource<CMsgClientAuthListAck>> pendingAcks =
            new Dictionary<ulong, TaskCompletionSource<CMsgClientAuthListAck>>();
        private readonly object sync = new object();
        private static int sequence;

        public int TokensAvailable
        {
            get
            {
                lock (sync)
                    return connectTokens.Count;
            }
        }

        /// <summary>Builds and registers a ticket; returns it with the CRC Steam knows it by (for cancelling)</summary>
        public async Task<(byte[] Ticket, uint Crc)> CreateAsync(uint appId, byte[] ownershipTicket, TimeSpan ackTimeout)
        {
            byte[] token;
            lock (sync)
            {
                if (connectTokens.Count == 0)
                    throw new InvalidOperationException("Steam has not sent any game connect tokens (not logged on yet?)");
                token = connectTokens.Dequeue();
            }

            var authTicket = BuildAuthTicket(token);
            var crc = Crc32.Compute(authTicket);

            lock (sync)
            {
                if (!ticketsByGame.TryGetValue(appId, out var tickets))
                    ticketsByGame[appId] = tickets = new List<CMsgAuthTicket>();
                tickets.Add(new CMsgAuthTicket { gameid = appId, ticket = authTicket, ticket_crc = crc });
            }

            var ack = SendTickets();
            var finished = await Task.WhenAny(ack, Task.Delay(ackTimeout)).ConfigureAwait(false);
            if (finished != ack)
                throw new TimeoutException("Steam did not acknowledge the auth ticket");
            if (!ack.Result.ticket_crc.Contains(crc))
                throw new InvalidOperationException("Steam did not accept the auth ticket");

            return (Combine(authTicket, ownershipTicket), crc);
        }

        public void Cancel(uint appId, uint crc)
        {
            lock (sync)
            {
                if (ticketsByGame.TryGetValue(appId, out var tickets))
                    tickets.RemoveAll(t => t.ticket_crc == crc);
            }

            SendTickets();
        }

        public override void HandleMsg(IPacketMsg packetMsg)
        {
            switch (packetMsg.MsgType)
            {
                case EMsg.ClientGameConnectTokens:
                {
                    var body = new ClientMsgProtobuf<CMsgClientGameConnectTokens>(packetMsg).Body;
                    lock (sync)
                    {
                        foreach (var token in body.tokens)
                            connectTokens.Enqueue(token);
                        while (connectTokens.Count > body.max_tokens_to_keep)
                            connectTokens.Dequeue();
                    }
                    break;
                }
                case EMsg.ClientAuthListAck:
                {
                    var ack = new ClientMsgProtobuf<CMsgClientAuthListAck>(packetMsg);
                    TaskCompletionSource<CMsgClientAuthListAck> pending;
                    lock (sync)
                    {
                        if (pendingAcks.TryGetValue(ack.TargetJobID, out pending))
                            pendingAcks.Remove(ack.TargetJobID);
                    }
                    pending?.TrySetResult(ack.Body);
                    break;
                }
                case EMsg.ClientLogOff:
                    lock (sync)
                        connectTokens.Clear();
                    break;
            }
        }

        /// <summary>Tokens and registered tickets belong to a logon; a new logon brings new tokens</summary>
        public void Reset()
        {
            lock (sync)
            {
                connectTokens.Clear();
                ticketsByGame.Clear();
                foreach (var pending in pendingAcks.Values)
                    pending.TrySetCanceled();
                pendingAcks.Clear();
            }
        }

        private Task<CMsgClientAuthListAck> SendTickets()
        {
            var auth = new ClientMsgProtobuf<CMsgClientAuthList>(EMsg.ClientAuthList);
            var done = new TaskCompletionSource<CMsgClientAuthListAck>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (sync)
            {
                auth.Body.tokens_left = (uint)connectTokens.Count;
                auth.Body.app_ids.AddRange(ticketsByGame.Keys);
                auth.Body.tickets.AddRange(ticketsByGame.Values.SelectMany(t => t));
                auth.SourceJobID = Client.GetNextJobID();
                pendingAcks[auth.SourceJobID] = done;
            }

            Client.Send(auth);
            return done.Task;
        }

        private static byte[] BuildAuthTicket(byte[] gameConnectToken)
        {
            const int sessionSize =
                4 + // unknown, always 1
                4 + // ticket type (2 = auth session)
                4 + // public IPv4, optional
                4 + // private IPv4, optional
                4 + // timestamp
                4;  // sequence

            var random = new byte[8];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(random);

            using (var stream = new MemoryStream(gameConnectToken.Length + 4 + 4 + sessionSize))
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(gameConnectToken.Length);
                    writer.Write(gameConnectToken);
                    writer.Write(sessionSize);
                    writer.Write(1);
                    writer.Write(TicketTypeAuthSession);
                    writer.Write(random);
                    writer.Write((uint)Stopwatch.GetTimestamp());
                    writer.Write((uint)Interlocked.Increment(ref sequence));
                }

                return stream.ToArray();
            }
        }

        private static byte[] Combine(byte[] authTicket, byte[] ownershipTicket)
        {
            var result = new byte[authTicket.Length + 4 + ownershipTicket.Length];
            Buffer.BlockCopy(authTicket, 0, result, 0, authTicket.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(ownershipTicket.Length), 0, result, authTicket.Length, 4);
            Buffer.BlockCopy(ownershipTicket, 0, result, authTicket.Length + 4, ownershipTicket.Length);
            return result;
        }
    }

    /// <summary>CRC-32 (IEEE 802.3, as System.IO.Hashing.Crc32), which Steam uses to identify registered tickets</summary>
    internal static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var c = i;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }
    }
}
