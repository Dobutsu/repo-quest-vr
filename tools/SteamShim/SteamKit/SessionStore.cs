using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RepoQuest.Steam
{
    /// <summary>
    /// The saved Steam login: account name and refresh token (from the headset's login page, or a QR login copied from
    /// the Mac), plus what the game needs before the connection is up (SteamID, persona name) and a per-device LoginID,
    /// so this session never kicks the PC's Steam. After a logout the token is gone but the identity stays: singleplayer
    /// saves keep upgrades by SteamID, and the account name is offered at the next login.
    /// Flat JSON, compatible with work/steam-session.json written by tools/SteamPhotonProbe (no System.Text.Json in Unity).
    /// </summary>
    public sealed class SessionStore
    {
        public string AccountName { get; private set; } = "";
        public string RefreshToken { get; private set; } = "";
        public string PersonaName { get; set; } = "";
        public ulong SteamId { get; private set; }
        public uint LoginId { get; private set; }
        public string MachineId { get; private set; } = "";
        /// <summary>From a password login with an emailed Steam Guard code: lets the next one skip the code</summary>
        public string GuardData { get; private set; } = "";

        public string Path { get; }

        public bool HasLogin => AccountName.Length > 0 && RefreshToken.Length > 0;

        /// <summary>An account was logged in on this device (maybe logged out since)</summary>
        public bool HasIdentity => SteamId != 0;

        private SessionStore(string path)
        {
            Path = path;
        }

        public static SessionStore Load(string path)
        {
            var store = new SessionStore(path);
            if (!File.Exists(path))
                return store;

            var fields = ReadFlatJson(File.ReadAllText(path));
            store.AccountName = fields.TryGetValue("AccountName", out var account) ? account : "";
            store.RefreshToken = fields.TryGetValue("RefreshToken", out var token) ? token : "";
            store.PersonaName = fields.TryGetValue("PersonaName", out var persona) ? persona : "";
            store.SteamId = fields.TryGetValue("SteamId", out var id) && ulong.TryParse(id, out var steamId)
                ? steamId
                : SteamIdFromToken(store.RefreshToken);
            store.LoginId = fields.TryGetValue("LoginId", out var login) && uint.TryParse(login, out var loginId) ? loginId : 0;
            store.MachineId = fields.TryGetValue("MachineId", out var machine) ? machine : "";
            store.GuardData = fields.TryGetValue("GuardData", out var guard) ? guard : "";
            return store;
        }

        /// <summary>A new login on this device; the device's LoginID and machine id stay</summary>
        public void SetLogin(string accountName, string refreshToken, ulong steamId, string guardData)
        {
            var sameAccount = steamId == SteamId;
            AccountName = accountName;
            RefreshToken = refreshToken;
            SteamId = steamId != 0 ? steamId : SteamIdFromToken(refreshToken);
            if (!string.IsNullOrEmpty(guardData))
                GuardData = guardData;
            if (!sameAccount)
                PersonaName = ""; // the new account's name arrives with its logon
        }

        /// <summary>Logged out: the token goes, the account's identity stays (see the class summary)</summary>
        public void ClearLogin()
        {
            RefreshToken = "";
        }

        /// <summary>
        /// Gives this device its own LoginID (Steam kicks sessions that share one) and machine id (reported at logon)
        /// the first time
        /// </summary>
        public void EnsureDeviceIds()
        {
            if (LoginId == 0)
            {
                var bytes = new byte[4];
                using (var rng = RandomNumberGenerator.Create())
                    rng.GetBytes(bytes);
                LoginId = BitConverter.ToUInt32(bytes, 0) | 1;
            }

            if (MachineId.Length == 0)
                MachineId = Guid.NewGuid().ToString("N");
        }

        public void Save()
        {
            var json = new StringBuilder("{");
            Append(json, "AccountName", AccountName, first: true);
            Append(json, "RefreshToken", RefreshToken);
            Append(json, "PersonaName", PersonaName);
            Append(json, "SteamId", SteamId.ToString());
            Append(json, "LoginId", LoginId.ToString());
            Append(json, "MachineId", MachineId);
            if (GuardData.Length > 0)
                Append(json, "GuardData", GuardData);
            json.Append('}');

            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(Path, json.ToString());
        }

        /// <summary>A refresh token is a JWT whose subject is the SteamID64</summary>
        public static ulong SteamIdFromToken(string token)
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
                return 0;

            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload += new string('=', (4 - payload.Length % 4) % 4);
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                return ReadFlatJson(json).TryGetValue("sub", out var sub) && ulong.TryParse(sub, out var id) ? id : 0;
            }
            catch (FormatException)
            {
                return 0;
            }
        }

        private static void Append(StringBuilder json, string key, string value, bool first = false)
        {
            if (!first)
                json.Append(',');
            json.Append('"').Append(key).Append("\":\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    default:
                        if (c < 0x20)
                            json.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            json.Append(c);
                        break;
                }
            }
            json.Append('"');
        }

        // Top-level string and number members of a flat JSON object; enough for the session file and a JWT payload
        private static Dictionary<string, string> ReadFlatJson(string json)
        {
            var result = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(json, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*(?:\"((?:[^\"\\\\]|\\\\.)*)\"|(-?[0-9]+))"))
                result[Unescape(m.Groups[1].Value)] = m.Groups[2].Success ? Unescape(m.Groups[2].Value) : m.Groups[3].Value;
            return result;
        }

        private static string Unescape(string value) =>
            Regex.Replace(value, "\\\\(u[0-9a-fA-F]{4}|.)", m =>
            {
                var e = m.Groups[1].Value;
                switch (e[0])
                {
                    case 'u': return ((char)Convert.ToInt32(e.Substring(1), 16)).ToString();
                    case 'n': return "\n";
                    case 't': return "\t";
                    case 'r': return "\r";
                    case 'b': return "\b";
                    case 'f': return "\f";
                    default: return e;
                }
            });
    }
}
