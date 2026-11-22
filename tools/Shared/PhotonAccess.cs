using System;
using System.Linq;
using System.Reflection;

namespace RepoQuest.Cloud
{
    /// <summary>Shared PC/IL2CPP access, with explicit failures when the game's networking API changes.</summary>
    public static class PhotonAccess
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null) ?? throw new TypeLoadException(name);

        public static object Get(object target, string name)
        {
            var type = target as Type ?? target.GetType();
            var instance = target is Type ? null : target;
            var prop = type.GetProperty(name, Flags);
            if (prop != null) return prop.GetValue(instance);
            var field = type.GetField(name, Flags) ?? throw new MissingMemberException(type.FullName, name);
            return field.GetValue(instance);
        }

        public static void Set(object target, string name, object value)
        {
            var type = target as Type ?? target.GetType();
            var instance = target is Type ? null : target;
            var prop = type.GetProperty(name, Flags);
            if (prop != null) { prop.SetValue(instance, value); return; }
            var field = type.GetField(name, Flags) ?? throw new MissingMemberException(type.FullName, name);
            field.SetValue(instance, value);
        }

        public static Type Network => Find("Photon.Pun.PhotonNetwork");
        public static object Settings => Get(Get(Network, "PhotonServerSettings"), "AppSettings");
        public static object Client => Get(Network, "NetworkingClient");
        public static bool InRoom => (bool)Get(Network, "InRoom");
        public static bool Connected => (bool)Get(Network, "IsConnected");
        public static void Disconnect() => Network.GetMethod("Disconnect", Type.EmptyTypes).Invoke(null, null);

        public static void Configure(string realtime, string voice)
        {
            Set(Settings, "AppIdRealtime", realtime);
            Set(Settings, "AppIdVoice", voice);
        }

        public static void Authenticate(string userId, string name)
        {
            var auth = Activator.CreateInstance(Find("Photon.Realtime.AuthenticationValues"));
            Set(auth, "UserId", userId);
            Set(auth, "AuthType", Enum.Parse(Find("Photon.Realtime.CustomAuthenticationType"), "None"));
            Set(Network, "AuthValues", auth);
            Set(Network, "NickName", name);
        }
    }
}
