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
    /// With the 3.5.0 fix (SteamKit issue #1567): since 2025-08 Steam does not echo the JobID on repeated auth lists, so
    /// the ack is matched by the ticket CRCs it lists, and a ticket without an ack is still returned (Photon validates it
    /// with Steam anyway). Before that the second and later tickets of a logon timed out.
    /// </summary>
    internal sealed class AuthTicketHandler : ClientMsgHandler
    {
        private const uint TicketTypeAuthSession = 2;

        private readonly Queue<byte[]> connectTokens = new Queue<byte[]>();
        private readonly Dictionary<uint, List<CMsgAuthTicket>> ticketsByGame = new Dictionary<uint, List<CMsgAuthTicket>>();
        private readonly Dictionary<uint, TaskCompletionSource<bool>> ackWaiters = new Dictionary<uint, TaskCompletionSource<bool>>();
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

        /// <summary>
        /// Builds and registers a ticket; returns it with the CRC Steam knows it by (for cancelling) and whether Steam
        /// acknowledged it within <paramref name="ackTimeout"/> (an unacknowledged ticket is returned all the same)
        /// </summary>
        public async Task<(byte[] Ticket, uint Crc, bool Acknowledged)> CreateAsync(uint appId, byte[] ownershipTicket, TimeSpan ackTimeout)
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

            // The waiter goes in before the auth list is sent, so the ack cannot arrive first
            var ack = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (sync)
            {
                if (!ticketsByGame.TryGetValue(appId, out var tickets))
                    ticketsByGame[appId] = tickets = new List<CMsgAuthTicket>();
                tickets.Add(new CMsgAuthTicket { gameid = appId, ticket = authTicket, ticket_crc = crc });
                ackWaiters[crc] = ack;
            }

            bool acknowledged;
            try
            {
                SendTickets();
                var finished = await Task.WhenAny(ack.Task, Task.Delay(ackTimeout)).ConfigureAwait(false);
                acknowledged = finished == ack.Task && ack.Task.Status == TaskStatus.RanToCompletion;
            }
            finally
            {
                lock (sync)
                {
                    if (ackWaiters.TryGetValue(crc, out var waiter) && waiter == ack)
                        ackWaiters.Remove(crc);
                }
            }

            if (ack.Task.IsCanceled)
                throw new OperationCanceledException("logged off while waiting for the auth ticket's ack");

            return (Combine(authTicket, ownershipTicket), crc, acknowledged);
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
                    // Matched by the CRCs Steam lists as active, not by JobID (see the class summary)
                    var ack = new ClientMsgProtobuf<CMsgClientAuthListAck>(packetMsg).Body;
                    var acknowledged = new List<TaskCompletionSource<bool>>();
                    lock (sync)
                    {
                        foreach (var crc in ack.ticket_crc)
                            if (ackWaiters.TryGetValue(crc, out var waiter))
                                acknowledged.Add(waiter);
                    }
                    foreach (var waiter in acknowledged)
                        waiter.TrySetResult(true);
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
                foreach (var waiter in ackWaiters.Values)
                    waiter.TrySetCanceled();
                ackWaiters.Clear();
            }
        }

        private void SendTickets()
        {
            var auth = new ClientMsgProtobuf<CMsgClientAuthList>(EMsg.ClientAuthList);

            lock (sync)
            {
                auth.Body.tokens_left = (uint)connectTokens.Count;
                auth.Body.app_ids.AddRange(ticketsByGame.Keys);
                auth.Body.tickets.AddRange(ticketsByGame.Values.SelectMany(t => t));
                auth.SourceJobID = Client.GetNextJobID();
            }

            Client.Send(auth);
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
