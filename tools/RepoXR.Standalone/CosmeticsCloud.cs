using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RepoQuest.Steam;
using UnityEngine;

namespace RepoXR.Standalone
{
    /// <summary>
    /// The cosmetics (MetaSave.es3: tokens, unlocks, equipped items, presets, colours) synced with Steam Cloud, so the
    /// headset and the PC game share them. On PC the Steam client syncs the file (Auto-Cloud: R.E.P.O. has no cloud code
    /// of its own) before the game starts and after it quits; here the logged-in account's backend reads and writes the
    /// same cloud file (<see cref="CloudFiles"/>).
    ///
    /// At start the game's MetaManager waits (its Start is replaced, see PlatformOverrides) until the cloud has been
    /// asked, up to a few seconds for the Steam logon: whichever side changed since the last sync wins. When both did,
    /// the main menu asks which to keep; the other is kept on the headset as a backup file. After that every save of the
    /// file goes up a few seconds later, unless the cloud changed meanwhile (a PC played): that waits for the question at
    /// the next start. A login made in the menu (no Steam at start) syncs then and reloads the cosmetics.
    ///
    /// An empty MetaSave (a fresh install) never counts as a change: it is never uploaded and never asked about.
    /// Files in persistentDataPath: cosmetics-cloud.txt (account and SHA-1 of the last synced file),
    /// MetaSave.quest-backup-*.es3 / MetaSave.steam-backup-*.es3 (what a sync replaced, the newest few).
    /// </summary>
    public sealed class CosmeticsCloud : MonoBehaviour
    {
        private const uint RepoAppId = 3241660;
        private const string CloudName = "%WinAppDataLocalLow%semiwork/Repo/MetaSave.es3";
        private const string Header = "COSMETICS";
        private static readonly Color HeaderColor = new Color(1f, 0.55f, 0f);
        private const float LogOnWait = 10f;
        private const float UploadDelay = 5f;
        private const float WatchInterval = 2f;
        private const int BackupsKept = 5;
        private const float MenuWait = 60f;     // at start, for the main menu to ask in
        private const float RetryInterval = 60f;

        private static CosmeticsCloud instance;

        private MenuButtonPopUp popUp;
        private MetaManager meta;
        private bool started;      // MetaManager has loaded the file
        private bool syncing;
        private SteamKitBackend syncedBackend; // the backend the last completed sync ran with
        private float nextSyncAttempt;
        private DateTime watchedTime;
        private string watchedSha = "";
        private float uploadAt = -1f;

        private static string LocalPath => Path.Combine(Application.persistentDataPath, "MetaSave.es3");
        private static string StatePath => Path.Combine(Application.persistentDataPath, "cosmetics-cloud.txt");

        private sealed class State
        {
            public string steamId = "";
            public string sha = "";
        }

        internal static void Install(GameObject host)
        {
            instance = host.AddComponent<CosmeticsCloud>();
        }

        private void Awake()
        {
            popUp = gameObject.AddComponent<MenuButtonPopUp>();
        }

        /// <summary>
        /// MetaManager.Start on Quest: the cloud first, then the game's own Start (load the file, debug unlocks, icons)
        /// </summary>
        public static IEnumerator StartAfterSync(MetaManager meta)
        {
            if (instance)
            {
                instance.meta = meta;
                yield return instance.SyncAtStart();
            }

            meta.Load();
            if (meta.saveReady && DebugComputerCheck.instance && !DebugComputerCheck.instance.DebugDisable &&
                DebugComputerCheck.instance.ItemDebug)
            {
                if (DebugComputerCheck.instance.CosmeticUnlockAll)
                    meta.CosmeticUnlockAll();
                if (DebugComputerCheck.instance.CosmeticReset)
                    meta.Reset();
            }
            meta.StartCoroutine(meta.GenerateIcons());

            if (instance)
            {
                instance.started = true;
                instance.Watch(true);
            }
        }

        private IEnumerator SyncAtStart()
        {
            var backend = SteamSetup.Backend;
            if (backend == null)
            {
                Log("not logged in to Steam: the headset's own cosmetics");
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + LogOnWait;
            while (!backend.IsLoggedOn && !backend.LoginRejected && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!backend.IsLoggedOn)
            {
                Log("Steam is not connected yet: the headset's own cosmetics for now");
                yield break;
            }

            yield return Sync(backend, false);
        }

        private void Update()
        {
            if (!started || syncing)
                return;

            var backend = SteamSetup.Backend;
            if (backend != null && backend != syncedBackend && backend.IsLoggedOn && InMainMenu() &&
                Time.realtimeSinceStartup >= nextSyncAttempt)
            {
                // Logged in after the start (or as another account), or the start could not finish: sync now,
                // reloading what the game shows
                nextSyncAttempt = Time.realtimeSinceStartup + RetryInterval;
                StartCoroutine(Sync(backend, true));
                return;
            }

            if (Time.frameCount % 60 == 0)
                Watch(false);
            if (uploadAt >= 0f && Time.realtimeSinceStartup >= uploadAt && backend != null && backend == syncedBackend && backend.IsLoggedOn)
            {
                uploadAt = -1f;
                StartCoroutine(UploadChange(backend));
            }
        }

        private void OnApplicationPause(bool paused)
        {
            // Taking the headset off or leaving the game: what is pending goes up now
            if (paused && uploadAt >= 0f)
                uploadAt = 0f;
        }

        /// <summary>Notices the game saving the file; <paramref name="baseline"/>: just remember it as it is</summary>
        private void Watch(bool baseline)
        {
            var time = File.Exists(LocalPath) ? File.GetLastWriteTimeUtc(LocalPath) : DateTime.MinValue;
            if (!baseline && time == watchedTime)
                return;
            watchedTime = time;
            var sha = File.Exists(LocalPath) ? Sha(File.ReadAllBytes(LocalPath)) : "";
            if (!baseline && sha != watchedSha)
                uploadAt = Time.realtimeSinceStartup + UploadDelay;
            watchedSha = sha;
        }

        /// <summary>
        /// Compares the headset's file, the cloud's and the last synced one and acts. <paramref name="reload"/>: the game
        /// has loaded the file already, so a new one is loaded into it and shown. Not completed (no answer from Steam,
        /// or no main menu to ask in at start): <see cref="Update"/> tries again later from the main menu
        /// </summary>
        private IEnumerator Sync(SteamKitBackend backend, bool reload)
        {
            syncing = true;
            try
            {
                var download = backend.Cloud.DownloadAsync(RepoAppId, CloudName);
                while (!download.IsCompleted)
                    yield return null;
                if (download.IsFaulted)
                {
                    Log($"could not read the cloud: {Reason(download)}");
                    yield break;
                }
                syncedBackend = backend;

                var cloud = download.Result;
                var account = backend.SteamId.ToString();
                var state = LoadState();
                var synced = state.steamId == account ? state.sha : "";
                var local = File.Exists(LocalPath) ? File.ReadAllBytes(LocalPath) : null;
                var localSha = local != null ? Sha(local) : "";
                var localEmpty = local == null || Summary.Of(LocalPath).IsEmpty;

                if (cloud == null)
                {
                    Log("no cosmetics in the cloud yet");
                    if (!localEmpty)
                        yield return Upload(backend, local, account);
                    yield break;
                }

                if (localSha == cloud.Sha)
                {
                    Log("the headset and the cloud have the same cosmetics");
                    SaveState(account, cloud.Sha);
                    yield break;
                }

                var localChanged = !localEmpty && localSha != synced;
                var cloudChanged = cloud.Sha != synced;
                if (!localChanged)
                {
                    Log(localEmpty ? "taking the cloud's cosmetics (none on the headset yet)" : "taking the cloud's cosmetics (changed on a PC)");
                    TakeCloud(cloud, account, local != null && !localEmpty);
                    if (reload)
                        Reload();
                    yield break;
                }

                if (!cloudChanged)
                {
                    Log("uploading the headset's cosmetics (changed here since the last sync)");
                    yield return Upload(backend, local, account);
                    yield break;
                }

                // Both changed: the player decides, in the main menu. At start the game waits for the answer (the
                // cosmetics are not loaded yet), but not for a menu that does not come (straight into a friend's game)
                var menuDeadline = Time.realtimeSinceStartup + MenuWait;
                while (!InMainMenu())
                {
                    if (!reload && Time.realtimeSinceStartup >= menuDeadline)
                    {
                        Log("both changed, and no main menu to ask in: the headset's cosmetics until then");
                        syncedBackend = null;
                        yield break;
                    }
                    yield return null;
                }
                var steamSummary = Summary.Of(cloud.Data);
                var questSummary = Summary.Of(LocalPath);
                var body = "Your cosmetics changed both on this headset and in Steam since they were last synced.\n\n" +
                           $"<b>Steam</b>: {steamSummary}, {Date(DateTimeOffset.FromUnixTimeSeconds((long)cloud.Time).UtcDateTime)}\n" +
                           $"<b>Headset</b>: {questSummary}, {Date(File.GetLastWriteTimeUtc(LocalPath))}\n\n" +
                           "Which ones do you keep? The other ones stay on the headset as a backup.";
                int answer;
                do
                {
                    var question = MenuPopup.Ask(popUp, Header, HeaderColor, body, "STEAM", "HEADSET", dismissAnswer: 3);
                    while (question.Answer == 0)
                        yield return null;
                    answer = question.Answer;
                    if (answer == 3)
                    {
                        // Closed without an answer: ask again once the menu is back
                        yield return new WaitForSecondsRealtime(0.5f);
                        while (!InMainMenu())
                            yield return null;
                    }
                } while (answer == 3);

                if (answer == 1)
                {
                    Log("conflict: the player kept Steam's cosmetics");
                    TakeCloud(cloud, account, true);
                    if (reload)
                        Reload();
                }
                else
                {
                    Log("conflict: the player kept the headset's cosmetics");
                    Backup(cloud.Data, "steam");
                    yield return Upload(backend, local, account);
                }
            }
            finally
            {
                syncing = false;
                if (started)
                    Watch(true);
            }
        }

        /// <summary>A save the game made: up to the cloud, unless a PC changed the cloud since the last sync</summary>
        private IEnumerator UploadChange(SteamKitBackend backend)
        {
            if (!File.Exists(LocalPath))
                yield break;
            var local = File.ReadAllBytes(LocalPath);
            if (Summary.Of(LocalPath).IsEmpty)
                yield break;

            syncing = true;
            try
            {
                var stat = backend.Cloud.StatAsync(RepoAppId, CloudName);
                while (!stat.IsCompleted)
                    yield return null;
                if (stat.IsFaulted)
                {
                    Log($"could not read the cloud, upload later: {Reason(stat)}");
                    uploadAt = Time.realtimeSinceStartup + 60f;
                    yield break;
                }

                var account = backend.SteamId.ToString();
                var state = LoadState();
                var cloudSha = stat.Result?.Sha ?? "";
                if (cloudSha == Sha(local))
                {
                    SaveState(account, cloudSha);
                    yield break;
                }
                if (cloudSha.Length > 0 && (state.steamId != account || cloudSha != state.sha))
                {
                    Log("the cloud's cosmetics changed on a PC meanwhile: not uploading; the next start asks which to keep");
                    yield break;
                }

                yield return Upload(backend, local, account);
            }
            finally
            {
                syncing = false;
            }
        }

        private IEnumerator Upload(SteamKitBackend backend, byte[] data, string account)
        {
            var time = (ulong)new DateTimeOffset(File.GetLastWriteTimeUtc(LocalPath)).ToUnixTimeSeconds();
            var upload = backend.Cloud.UploadAsync(RepoAppId, CloudName, data, time);
            while (!upload.IsCompleted)
                yield return null;
            if (upload.IsFaulted)
            {
                Log($"upload failed, trying again later: {Reason(upload)}");
                uploadAt = Time.realtimeSinceStartup + 60f;
                yield break;
            }
            Log($"uploaded the cosmetics ({data.Length} bytes)");
            SaveState(account, Sha(data));
        }

        /// <summary>The cloud's file becomes the headset's; the headset's goes to a backup unless it was empty</summary>
        private static void TakeCloud(CloudFile cloud, string account, bool keepLocal)
        {
            if (keepLocal && File.Exists(LocalPath))
                Backup(File.ReadAllBytes(LocalPath), "quest");
            var temp = LocalPath + ".cloud";
            File.WriteAllBytes(temp, cloud.Data);
            if (File.Exists(LocalPath))
                File.Delete(LocalPath);
            File.Move(temp, LocalPath);
            try
            {
                File.SetLastWriteTimeUtc(LocalPath, DateTimeOffset.FromUnixTimeSeconds((long)cloud.Time).UtcDateTime);
            }
            catch (Exception e)
            {
                // Only the date the next conflict question shows
                Log($"could not date the file: {e.Message}");
            }
            SaveState(account, cloud.Sha);
        }

        /// <summary>The game loaded the file already: load the new one and dress the menu's avatars (and ours) in it</summary>
        private void Reload()
        {
            if (!meta)
                return;
            meta.Load();
            foreach (var cosmetics in FindObjectsOfType<PlayerCosmetics>())
            {
                var visuals = cosmetics.playerAvatarVisuals;
                if (!visuals)
                    continue;
                if (visuals.isMenuAvatar)
                {
                    var menuAvatar = visuals.playerAvatarMenu;
                    if (menuAvatar && (menuAvatar.worldAvatar || menuAvatar.iconMakerAvatar))
                        continue;
                    cosmetics.SetupCosmetics(false, false);
                    cosmetics.SetupColors(false);
                }
                else if (visuals.playerAvatar && visuals.playerAvatar.isLocal)
                {
                    cosmetics.SetupCosmetics(true, false);
                    cosmetics.SetupColors(true);
                }
            }
            Log("cosmetics reloaded from the cloud's file");
        }

        private static bool InMainMenu() =>
            MenuManager.instance && MenuManager.instance.currentMenuPage && MenuPageMain.instance &&
            MenuManager.instance.currentMenuPage.gameObject == MenuPageMain.instance.gameObject && !SteamLoginFlow.Running;

        private static void Backup(byte[] data, string side)
        {
            var dir = Application.persistentDataPath;
            File.WriteAllBytes(Path.Combine(dir, $"MetaSave.{side}-backup-{DateTime.Now:yyyyMMdd-HHmmss}.es3"), data);
            foreach (var old in Directory.GetFiles(dir, $"MetaSave.{side}-backup-*.es3").OrderByDescending(f => f).Skip(BackupsKept))
                File.Delete(old);
        }

        private static State LoadState()
        {
            try
            {
                // Two lines: the account's SteamID, the SHA-1 of the file both sides had at the last sync
                var lines = File.Exists(StatePath) ? File.ReadAllLines(StatePath) : Array.Empty<string>();
                return lines.Length >= 2 ? new State { steamId = lines[0].Trim(), sha = lines[1].Trim() } : new State();
            }
            catch (Exception e)
            {
                Log($"unreadable {Path.GetFileName(StatePath)}: {e.Message}");
                return new State();
            }
        }

        private static void SaveState(string account, string sha) =>
            File.WriteAllLines(StatePath, new[] { account, sha });

        private static string Sha(byte[] data) => CloudFiles.Hex(CloudFiles.Sha1(data));

        private static string Date(DateTime utc) => utc.ToLocalTime().ToString("d MMM HH:mm");

        private static string Reason(Task task) => task.Exception?.GetBaseException().Message ?? "unknown";

        private static void Log(string message) => Debug.Log("[RepoXR.Standalone] Cosmetics: " + message);

        /// <summary>What a MetaSave holds, read the way MetaManager reads it</summary>
        private readonly struct Summary
        {
            private readonly int unlocks;
            private readonly int tokens;
            private readonly bool anything;

            private Summary(int unlocks, int tokens, bool anything)
            {
                this.unlocks = unlocks;
                this.tokens = tokens;
                this.anything = anything;
            }

            /// <summary>A fresh install's file: nothing unlocked, earned, equipped or coloured</summary>
            public bool IsEmpty => !anything;

            public override string ToString() => $"{unlocks} unlocked, {tokens} tokens";

            public static Summary Of(byte[] data)
            {
                var temp = Path.Combine(Application.persistentDataPath, "MetaSave.cloud-check.es3");
                File.WriteAllBytes(temp, data);
                try
                {
                    return Of(temp);
                }
                finally
                {
                    File.Delete(temp);
                }
            }

            public static Summary Of(string path)
            {
                try
                {
                    var settings = new ES3Settings(path, ES3.Location.File)
                    {
                        encryptionType = ES3.EncryptionType.AES,
                        encryptionPassword = StatsManager.instance.totallyNormalString,
                    };
                    var unlocks = List(settings, "cosmeticUnlocks");
                    var tokens = List(settings, "cosmeticTokens");
                    var anything = unlocks.Count > 0 || tokens.Count > 0 || List(settings, "cosmeticHistory").Count > 0 ||
                                   List(settings, "cosmeticEquipped").Count > 0 ||
                                   (ES3.KeyExists("colorsEquipped", settings) && ES3.Load<int[]>("colorsEquipped", settings).Any(c => c != 0));
                    return new Summary(unlocks.Count, tokens.Count, anything);
                }
                catch (Exception e)
                {
                    // The game could not load it either
                    Log($"unreadable {Path.GetFileName(path)}: {e.Message}");
                    return new Summary(0, 0, false);
                }
            }

            private static List<int> List(ES3Settings settings, string key) =>
                ES3.KeyExists(key, settings) ? ES3.Load<List<int>>(key, settings) ?? new List<int>() : new List<int>();
        }
    }
}
