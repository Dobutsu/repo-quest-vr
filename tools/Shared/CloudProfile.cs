using System;
using System.Globalization;

namespace RepoXR.Standalone
{
    [Serializable]
    public sealed class CloudProfile
    {
        private const ulong LocalBase = 0x0240000100000000UL;
        public string userId;
        public string gameId;

        public static string GenerateId(string active = null, string selected = null)
        {
            while (true)
            {
                var account = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
                if (account == 0) continue;
                var id = (LocalBase | account).ToString(CultureInfo.InvariantCulture);
                if (id != active && id != selected) return id;
            }
        }

        public static bool IsLocalId(string value) => ulong.TryParse(value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var id) && (id & 0xFFFFFFFF00000000UL) == LocalBase &&
            (id & uint.MaxValue) != 0 && value == id.ToString(CultureInfo.InvariantCulture);

        // An explicit selection can repair a damaged profile. Legacy identities keep their original range.
        public static CloudProfile Resolve(CloudProfile previous, string selected)
        {
            if (!string.IsNullOrEmpty(selected))
            {
                if (!IsLocalId(selected)) throw new FormatException("Invalid Cloud ID. Generate a new ID in Cloud settings.");
                return new CloudProfile { userId = selected, gameId = selected };
            }
            if (previous == null || previous.userId != previous.gameId ||
                !ulong.TryParse(previous.gameId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
                throw new FormatException("Invalid cloud-profile.json. Restore its backup to keep your player identity.");
            return previous;
        }
    }
}
