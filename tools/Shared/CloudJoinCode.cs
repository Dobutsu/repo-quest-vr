using System;
using System.Globalization;
using System.Text;

namespace RepoQuest.Cloud
{
    /// <summary>Versioned room invitation. Contains routing data, never Steam credentials.</summary>
    public sealed class CloudJoinCode
    {
        public string Region;
        public string Room;
        public string Build;
        public string AppVersion;
        public string RealtimeAppId;
        public string VoiceAppId;
        public ulong LobbyId;
        public ulong OwnerId;
        public int Capacity;

        public string Encode()
        {
            Validate();
            var text = string.Join("\n", Region, Room, Build, AppVersion,
                Guid.Parse(RealtimeAppId).ToString("D"), Guid.Parse(VoiceAppId).ToString("D"),
                LobbyId.ToString(CultureInfo.InvariantCulture), OwnerId.ToString(CultureInfo.InvariantCulture),
                Capacity.ToString(CultureInfo.InvariantCulture));
            var code = "RQ1." + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (code.Length > 1024) throw new FormatException("Room data is too long for a Quest invitation.");
            return code;
        }

        public static CloudJoinCode Decode(string text)
        {
            text = (text ?? "").Trim();
            if (!text.StartsWith("RQ1.", StringComparison.Ordinal) || text.Length > 1024)
                throw new FormatException("Expected an RQ1 room code from the host.");
            var payload = text.Substring(4).Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            var parts = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(payload)).Split('\n');
            if (parts.Length != 9)
                throw new FormatException("The room code is incomplete or uses another version.");
            var code = new CloudJoinCode
            {
                Region = parts[0], Room = parts[1], Build = parts[2], AppVersion = parts[3],
                RealtimeAppId = parts[4], VoiceAppId = parts[5],
                LobbyId = ulong.Parse(parts[6], CultureInfo.InvariantCulture),
                OwnerId = ulong.Parse(parts[7], CultureInfo.InvariantCulture),
                Capacity = int.Parse(parts[8], CultureInfo.InvariantCulture),
            };
            code.Validate();
            return code;
        }

        private void Validate()
        {
            Check(Region, 16); Check(Room, 128); Check(Build, 64); Check(AppVersion, 80);
            foreach (var c in Region)
                if (c < 'a' || c > 'z') throw new FormatException("Invalid Photon region.");
            if (!Guid.TryParse(RealtimeAppId, out var realtime) || realtime == Guid.Empty ||
                !Guid.TryParse(VoiceAppId, out var voice) || voice == Guid.Empty)
                throw new FormatException("The host has not configured valid Photon App IDs.");
            if (LobbyId == 0 || OwnerId == 0 || Capacity < 2 || Capacity > 255)
                throw new FormatException("Invalid host or room capacity.");
        }

        private static void Check(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > max)
                throw new FormatException("Missing or oversized room data.");
            foreach (var c in value)
                if (char.IsControl(c)) throw new FormatException("Invalid character in room data.");
        }

        public void CheckApps(string realtime, string voice)
        {
            if (!Guid.TryParse(realtime, out var r) || !Guid.TryParse(voice, out var v) ||
                r != Guid.Parse(RealtimeAppId) || v != Guid.Parse(VoiceAppId))
                throw new InvalidOperationException("Host and guest Photon App IDs differ. Open Cloud co-op settings.");
        }
    }
}
