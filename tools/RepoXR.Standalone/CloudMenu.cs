using System;
using System.Collections;
using System.IO;
using RepoQuest.Cloud;
using UnityEngine;
using UnityEngine.UI;
using RepoXR.UI;

namespace RepoXR.Standalone
{
    public sealed class CloudMenu : MonoBehaviour
    {
        private static readonly Color Accent = new Color(0.25f, 0.85f, 1f);
        private MenuPageMain decorated;
        private MenuButtonPopUp popup;
        private bool busy;
        private bool joining;
        private bool joined;
        private int hostActor;
        private float deadline;
        private float nextPoll;
        private string failure;
        private MenuPopup progress;
        private string hostedCode = "";
        private string shownLobby = "";
        private MenuPopup invitationPopup;
        private string CodePath => Path.Combine(Application.persistentDataPath, "quest-room.txt");

        private void Awake()
        {
            popup = gameObject.AddComponent<MenuButtonPopUp>();
            ClearHostedCode();
        }

        private void Update()
        {
            var main = MenuPageMain.instance;
            if (main && main != decorated)
            {
                decorated = main;
                AddButton(main);
            }
            if (CloudCoop.MigrationNotice && main && !busy && !SteamLoginFlow.Running) Open();
            if (failure != null && main && !busy && MenuManager.instance && MenuManager.instance.currentMenuPage)
            {
                var message = failure;
                failure = null;
                StartCoroutine(FailureRoutine(message));
            }
            try
            {
                // Close an invitation immediately when the lobby starts, even between metadata polls.
                if ((hostedCode.Length != 0 || invitationPopup != null) && !CanShareInvitation) ClearHostedCode();
                if (Time.unscaledTime < nextPoll) return;
                nextPoll = Time.unscaledTime + 0.5f;
                if (joining) PollConnection();
                if (hosting)
                {
                    if (PhotonAccess.InRoom) { hosting = false; progress?.Close(); progress = null; }
                    else if (progress?.Answer == 1 || Time.unscaledTime >= hostDeadline) Fail(T("net.createFailed"));
                }
                if (!joining && CloudCoop.IsGuest && !PhotonAccess.Connected && main && SemiFunc.IsMainMenu() &&
                    !UnityEngine.Object.FindFirstObjectByType<NetworkConnect>()) CloudCoop.GuestReturnedToMenu();
                UpdateHostedCode();
            }
            catch (Exception error) { Fail(error.GetBaseException().Message); }
        }

        // RunIsLobby is the in-game truck. Only the pre-game LobbyMenu accepts new players.
        private bool CanShareInvitation => CloudCoop.IsHost && RunManager.instance && SemiFunc.RunIsLobbyMenu() &&
            PhotonAccess.InRoom && (bool)PhotonAccess.Get(PhotonAccess.Network, "IsMasterClient") &&
            (bool)PhotonAccess.Get(PhotonAccess.Get(PhotonAccess.Network, "CurrentRoom"), "IsOpen");

        private void UpdateHostedCode()
        {
            if (!CanShareInvitation)
            {
                ClearHostedCode();
                return;
            }
            try
            {
                var backend = CloudCoop.Backend;
                var invitation = CloudRoomRouting.HostedInvitation(backend.HostedLobbyId, backend.SteamId,
                    key => backend.GetLobbyData(backend.HostedLobbyId, key));
                var code = invitation.Encode();
                if (code != hostedCode) { File.WriteAllText(CodePath, code); hostedCode = code; }
                if (MenuPageLobby.instance && shownLobby != invitation.Room)
                {
                    shownLobby = invitation.Room;
                    ShowInvitation();
                }
            }
            catch (InvalidOperationException) { ClearHostedCode(); } // Metadata is not ready, or the lobby has a password.
        }

        public void ShowInvitation()
        {
            if (!CanShareInvitation) { ClearHostedCode(); return; }
            invitationPopup?.Close();
            invitationGeneration++;
            if (hostedCode.Length == 0)
                invitationPopup = MenuPopup.Message(T("net.hostTitle"), Accent, T("cloud.hostNotReady"), T("ui.close"));
            else StartCoroutine(InvitationRoutine(invitationGeneration, hostedCode));
        }

        private int invitationGeneration;
        private bool CurrentInvitation(int generation, string code) =>
            generation == invitationGeneration && hostedCode == code && CanShareInvitation;

        private IEnumerator InvitationRoutine(int generation, string code)
        {
            var copied = false;
            var browserError = false;
            while (CurrentInvitation(generation, code))
            {
                var hasWebsite = HasWebsite;
                var labels = hasWebsite
                    ? new[] { T("net.copyCode"), T("net.openWebsite"), T("ui.close") }
                    : new[] { T("net.copyCode"), T("ui.close") };
                invitationPopup = Choose("net.hostTitle", T("net.hostCode", code) +
                    (hasWebsite ? "\n\n" + T("net.publishHint") : "") + (copied ? "\n\n" + T("net.copied") : "") +
                    (browserError ? "\n\n" + T("net.browserError") : ""),
                    labels);
                while (CurrentInvitation(generation, code) && invitationPopup.Answer == 0) yield return null;
                if (!CurrentInvitation(generation, code)) yield break;
                var action = invitationPopup.Answer;
                yield return SteamLoginFlow.MenuSettled();
                if (!CurrentInvitation(generation, code)) yield break;
                if (action == labels.Length) yield break;
                if (action == 1)
                {
                    try { GUIUtility.systemCopyBuffer = code; copied = true; }
                    catch (Exception error) { Debug.LogWarning(error); copied = false; }
                }
                else browserError = !TryOpenWebsite();
                while (!Application.isFocused && CurrentInvitation(generation, code)) yield return null;
            }
        }

        private void ClearHostedCode()
        {
            invitationGeneration++;
            hostedCode = "";
            shownLobby = "";
            invitationPopup?.Close();
            invitationPopup = null;
            if (File.Exists(CodePath)) File.Delete(CodePath);
        }

        private void OnDestroy() => ClearHostedCode();

        private void AddButton(MenuPageMain main)
        {
            var source = main.transform.Find("LeftTransform/Buttons/Menu Button - Settings") ??
                         main.transform.Find("Buttons/Menu Button - Settings");
            if (!source) { Debug.LogWarning("[CloudCoop] no Settings button to clone"); return; }
            var button = MenuActions.Add(source.GetComponent<MenuButton>(), QuestText.Get("net.title"), Vector2.zero, Open);
            button.gameObject.name = "Menu Button - Cloud Co-op";
            button.originalFontSize = 20f;
            var layout = button.gameObject.AddComponent<NetworkButtonLayout>();
            layout.Source = source.GetComponent<MenuButton>();
            QuestText.Bind(button.buttonText, "net.title");
        }

        public void Open()
        {
            if (!busy && !joining && !hosting && !SteamLoginFlow.Running) StartCoroutine(OpenRoutine());
        }

        private static string T(string key, params object[] values) => string.Format(QuestText.Get(key), values);

        private MenuPopup Choose(string header, string body, params string[] labels) =>
            MenuPopup.Options(popup, T(header), Accent, body, labels);

        private IEnumerator OpenRoutine(bool create = false)
        {
            busy = true;
            try
            {
                if (CloudCoop.MigrationNotice)
                {
                    CloudCoop.MigrationNotice = false;
                    var notice = Choose("net.title", T("net.migrated"), T("ui.continue"), T("net.change"));
                    notice.DismissAs(1);
                    while (notice.Answer == 0) yield return null;
                    var switchMode = notice.Answer == 2;
                    yield return SteamLoginFlow.MenuSettled();
                    if (switchMode) yield return ChangeRoutine();
                }
                if (create && !CloudCoop.RestartRequired && CloudCoop.StartupError == null)
                { yield return CreateRoutine(); yield break; }
                while (MenuPageMain.instance && !joining && !hosting)
                {
                    if (CloudCoop.RestartRequired)
                    {
                        var pending = Choose("net.restartTitle", T("net.pending", CloudCoop.Enabled ? "Cloud" : "Steam",
                            CloudCoop.Settings.enabled ? "Cloud" : "Steam"), T("net.quit"), T("net.editChoice"), T("ui.back"));
                        while (pending.Answer == 0) yield return null;
                        var action = pending.Answer;
                        yield return SteamLoginFlow.MenuSettled();
                        if (action == 1) { Application.Quit(); yield break; }
                        if (action == 2) { yield return ChangeRoutine(); continue; }
                        yield break;
                    }
                    if (CloudCoop.StartupError != null)
                    {
                        var repair = Choose("net.settingsError", T(CloudCoop.Settings.HasPhotonAppIds ? "net.repair" : "net.configureApps"),
                            T("net.cloudSettings"), T("net.change"), T("ui.back"));
                        while (repair.Answer == 0) yield return null;
                        var action = repair.Answer;
                        yield return SteamLoginFlow.MenuSettled();
                        if (action == 1) yield return ConfigureRoutine();
                        else if (action == 2) yield return ChangeRoutine();
                        else yield break;
                        continue;
                    }
                    var cloud = CloudCoop.Enabled;
                    var text = cloud ? T("net.cloudHub", CloudCoop.ActiveSettings.playerName) :
                        T("net.steamHub", SteamSetup.NetworkReady ? T("net.account", SteamSetup.NetworkName) : T("net.noAccount"));
                    var menu = Choose("net.title", text, T("net.connect"), T("net.create"),
                        T(cloud ? "net.cloudSettings" : "net.steamAccount"), T("net.change"), T("ui.back"));
                    while (menu.Answer == 0) yield return null;
                    var choice = menu.Answer;
                    yield return SteamLoginFlow.MenuSettled();
                    if (choice == 1)
                    {
                        if (cloud) yield return JoinRoutine();
                        else { yield return SteamSetup.NetworkJoin(popup); yield break; }
                    }
                    else if (choice == 2) { yield return CreateRoutine(); yield break; }
                    else if (choice == 3)
                    {
                        if (cloud) yield return ConfigureRoutine();
                        else { SteamSetup.ShowNetworkAccount(); yield break; }
                    }
                    else if (choice == 4) yield return ChangeRoutine();
                    else yield break;
                }
            }
            finally { busy = false; }
        }

        public void OpenHost()
        {
            if (!busy && !joining && !hosting && !SteamLoginFlow.Running) StartCoroutine(OpenRoutine(true));
        }

        private IEnumerator ChangeRoutine()
        {
            var mode = Choose("net.changeTitle", T("net.chooseMode", CloudCoop.Enabled ? "Cloud" : "Steam"), "Cloud", "Steam", T("ui.back"));
            while (mode.Answer == 0) yield return null;
            var choice = mode.Answer;
            yield return SteamLoginFlow.MenuSettled();
            if (choice == 1) { yield return ConfigureRoutine(true); yield break; }
            if (choice != 2 || (!CloudCoop.Enabled && !CloudCoop.RestartRequired)) yield break;
            var confirm = Choose("net.steamConfirmTitle", T("net.steamConfirm", CloudCoop.Enabled ? "Cloud" : "Steam"),
                T("net.enableSteam"), T("ui.back"));
            while (confirm.Answer == 0) yield return null;
            var save = confirm.Answer == 1;
            yield return SteamLoginFlow.MenuSettled();
            if (!save) yield break;
            var draft = CloudCoop.Settings.Copy();
            draft.enabled = false;
            yield return SaveDraftRoutine(draft);
        }

        private bool draftSaved;
        private IEnumerator SaveDraftRoutine(CloudSettings draft)
        {
            draftSaved = false;
            while (true)
            {
                string error = null;
                try { CloudCoop.SaveSettings(draft); }
                catch (Exception ex) { error = ex.Message; Debug.LogWarning("[CloudCoop] save: " + ex); }
                if (error == null) break;
                var failed = Choose("net.saveErrorTitle", T("net.saveError"), T("ui.retry"), T("net.returnSettings"));
                while (failed.Answer == 0) yield return null;
                var retry = failed.Answer == 1;
                yield return SteamLoginFlow.MenuSettled();
                if (!retry) yield break;
            }
            draftSaved = true;
            if (!CloudCoop.RestartRequired)
            {
                var unchanged = MenuPopup.Message(T("net.settingsSaved"), Accent, T("net.noRestart"), T("ui.continue"));
                while (unchanged.IsOpen && unchanged.Answer == 0) yield return null;
                yield return SteamLoginFlow.MenuSettled();
                yield break;
            }
            var changed = draft.enabled != CloudCoop.Enabled;
            var saved = Choose(changed ? "net.modeChanged" : "net.settingsSaved",
                changed ? T("net.modeSaved", draft.enabled ? "Cloud" : "Steam") : T("net.cloudSaved"),
                T("net.quit"), T("ui.later"));
            while (saved.Answer == 0) yield return null;
            var quit = saved.Answer == 1;
            yield return SteamLoginFlow.MenuSettled();
            if (quit) Application.Quit();
        }

        private IEnumerator ConfigureRoutine(bool fromSelector = false)
        {
            var draft = CloudCoop.Settings.Copy();
            draft.enabled = true;
            Func<string> selectedId = () => !string.IsNullOrEmpty(draft.cloudPlayerId) ? draft.cloudPlayerId : CloudCoop.Profile?.gameId;
            var originalId = selectedId();
            var values = new[] { draft.realtimeAppId, draft.voiceAppId, draft.playerName, draft.websiteUrl };
            var titles = new[] { "net.stepRealtime", "net.stepVoice", "net.stepName", "net.stepWebsite" };
            var hints = new[] { "net.hintRealtime", "net.hintVoice", "net.hintName", "net.hintWebsite" };
            var step = 0;
            while (true)
            {
                while (step < 4)
                {
                    var page = MenuTextPage.Open();
                    if (!page) yield break;
                    page.Show(T(titles[step]), T("ui.next"), T("ui.back"), values[step], maxLength: step < 2 ? 36 : step == 2 ? 32 : CloudWebsite.MaxLength,
                        defaultValue: step == 3 ? "" : null, allowPaste: step != 2, defaultLabel: T("net.clearWebsite"),
                        hint: step == 2 ? T("net.nameAndId", T(hints[step]), selectedId() ?? T("net.idOnFirstLaunch")) : T(hints[step]),
                        allowCancel: true, auxiliaryLabel: step == 2 ? T("net.generateId") : null,
                        auxiliary: () =>
                        {
                            draft.cloudPlayerId = CloudProfile.GenerateId(CloudCoop.Profile?.gameId, selectedId());
                            page.SetHint(T("net.nameAndId", T("net.hintName"), selectedId()));
                        });
                    var action = 0;
                    while (true)
                    {
                        while (page.IsOpen && page.Answer == 0) yield return null;
                        action = page.IsOpen ? page.Answer : 3;
                        values[step] = page.Text.Trim();
                        if (action != 1) break;
                        var error = ValidateStep(step, values[step]);
                        if (error == null) break;
                        page.Reject(T(error));
                    }
                    page.Close();
                    yield return SteamLoginFlow.MenuSettled();
                    if (action == 3) yield break;
                    if (action == 2)
                    {
                        if (--step < 0) { if (fromSelector) yield return ChangeRoutine(); yield break; }
                    }
                    else step++;
                }
                draft.realtimeAppId = values[0]; draft.voiceAppId = values[1];
                draft.playerName = values[2]; draft.websiteUrl = values[3];
                draft.useCustomAppIds = true;
                var review = Choose("net.reviewTitle", T("net.review", values[0], values[1], values[2], values[3], selectedId() ?? T("net.idOnFirstLaunch")),
                    T("ui.save"), T("ui.back"), T("ui.cancel"));
                while (review.Answer == 0) yield return null;
                var choice = review.Answer;
                yield return SteamLoginFlow.MenuSettled();
                if (choice == 3) yield break;
                if (choice == 2) { step = 3; continue; }
                if (selectedId() != originalId)
                {
                    var confirm = Choose("net.idChangeTitle", T("net.idChangeWarning"), T("net.saveNewId"), T("ui.back"));
                    confirm.DismissAs(2);
                    while (confirm.Answer == 0) yield return null;
                    var save = confirm.Answer == 1;
                    yield return SteamLoginFlow.MenuSettled();
                    if (!save) continue;
                }
                yield return SaveDraftRoutine(draft);
                if (draftSaved) yield break;
                step = 3;
            }
        }

        private static string ValidateStep(int step, string value)
        {
            if (step < 2) return CloudAppIds.IsValid(value) ? null : step == 0 ? "net.invalidRealtime" : "net.invalidVoice";
            if (step == 2) return string.IsNullOrWhiteSpace(value) || value.Length > 32 || value.IndexOfAny(new[] { '<', '>', '\n', '\r' }) >= 0 ? "cloud.invalidName" : null;
            try { CloudWebsite.Normalize(value); return null; }
            catch (FormatException) { return "cloud.invalidUrl"; }
        }

        private static bool HasWebsite => !string.IsNullOrEmpty(CloudCoop.ActiveSettings?.websiteUrl);

        private bool TryOpenWebsite()
        {
            if (!HasWebsite) return false;
            try { Application.OpenURL(CloudCoop.ActiveSettings.websiteUrl); return true; }
            catch (Exception error)
            {
                Debug.LogWarning("[CloudCoop] Could not open browser: " + error.Message);
                return false;
            }
        }

        private IEnumerator JoinRoutine()
        {
            var page = MenuTextPage.Open();
            if (!page) yield break;
            page.Show(T("net.joinTitle"), T("net.connect"), T("ui.back"), "", maxLength: 1024, allowPaste: true,
                hint: T(HasWebsite ? "net.joinHint" : "net.joinHintNoWebsite"),
                auxiliaryLabel: HasWebsite ? T("net.openWebsite") : null,
                auxiliary: () => { if (!TryOpenWebsite()) page.Reject(T("net.browserError")); });
            string code;
            while (true)
            {
                while (page.IsOpen && page.Answer == 0) yield return null;
                if (!page.IsOpen || page.Answer != 1) { page.Close(); yield break; }
                code = page.Text;
                string error = null;
                try
                {
                    var invite = CloudJoinCode.Decode(code);
                    invite.CheckApps(CloudCoop.ActiveSettings.realtimeAppId, CloudCoop.ActiveSettings.voiceAppId);
                }
                catch (InvalidOperationException) { error = "net.appMismatch"; }
                catch (Exception) { error = "net.badCode"; }
                if (error == null) break;
                if (error == "net.badCode") { page.Reject(T(error)); continue; }
                page.Close();
                yield return SteamLoginFlow.MenuSettled();
                var mismatch = Choose("net.joinTitle", T(error), T("net.cloudSettings"), T("ui.back"));
                while (mismatch.Answer == 0) yield return null;
                var configure = mismatch.Answer == 1;
                yield return SteamLoginFlow.MenuSettled();
                if (configure) yield return ConfigureRoutine();
                yield break;
            }
            page.Close();
            yield return SteamLoginFlow.MenuSettled();
            lastJoinCode = code;
            StartJoin(code);
        }

        private IEnumerator CreateRoutine()
        {
            if (!CloudCoop.Enabled)
            {
                yield return SteamSetup.EnsureNetworkLogin(popup);
                if (!SteamSetup.NetworkReady) yield break;
            }
            else
            {
                var question = Choose("net.createTitle", T("net.createHint"), T("ui.continue"), T("ui.back"));
                while (question.Answer == 0) yield return null;
                var create = question.Answer == 1;
                yield return SteamLoginFlow.MenuSettled();
                if (!create) yield break;
            }
            AllowNativeHost = true;
            try { MenuPageMain.instance.ButtonEventHostGame(); }
            finally { AllowNativeHost = false; }
        }

        public bool AllowNativeHost { get; private set; }
        private bool hosting;
        private float hostDeadline;
        private string lastJoinCode;

        public void HostingStarted()
        {
            if (!CloudCoop.Enabled || CloudCoop.IsGuest) return;
            hosting = true;
            hostDeadline = Time.unscaledTime + 45f;
            progress = MenuPopup.Message(T("net.creatingTitle"), Accent, T("net.creating"), T("ui.cancel"), explicitOnly: true);
        }

        private IEnumerator FailureRoutine(string message)
        {
            busy = true;
            try
            {
                var settings = message == T("net.appMismatch");
                var canRetry = !settings && lastJoinCode != null && message != T("net.versionMismatch") && message != T("net.hostLeft");
                var dialog = Choose("net.joinTitle", message, T(settings ? "net.cloudSettings" : canRetry ? "ui.retry" : "net.enterOther"), T("net.toMenu"));
                while (dialog.Answer == 0) yield return null;
                var action = dialog.Answer;
                yield return SteamLoginFlow.MenuSettled();
                if (action == 1)
                {
                    if (settings) yield return ConfigureRoutine();
                    else if (canRetry) StartJoin(lastJoinCode);
                    else yield return JoinRoutine();
                }
            }
            finally { busy = false; }
        }

        /// <summary>ADB guests can provide any host's RQ1 invitation through files/cloud-join.</summary>
        private IEnumerator Start()
        {
            var request = Path.Combine(Application.persistentDataPath, "cloud-join");
            while (true)
            {
                yield return new WaitForSecondsRealtime(1);
                if (!CloudCoop.Enabled || busy || joining || !MenuPageMain.instance || !File.Exists(request)) continue;
                string code;
                try { code = File.ReadAllText(request); File.Delete(request); }
                catch (Exception error) { Debug.LogWarning("[CloudCoop] " + error.Message); continue; }
                StartJoin(code);
            }
        }

        private void StartJoin(string code)
        {
            try
            {
                joining = true;
                joined = false;
                deadline = Time.unscaledTime + 45f;
                progress = MenuPopup.Message(T("net.joinTitle"), Accent, T("net.connecting"), QuestText.Get("ui.cancel"), explicitOnly: true);
                CloudCoop.Join(code);
            }
            catch (Exception error) { Fail(error.GetBaseException().Message); }
        }

        private void PollConnection()
        {
            if (PhotonAccess.InRoom)
            {
                var master = PhotonAccess.Get(PhotonAccess.Network, "MasterClient");
                var actor = Convert.ToInt32(PhotonAccess.Get(master, "ActorNumber"));
                CloudRoomRouting.ValidateHostIdentity(CloudCoop.Invitation, (string)PhotonAccess.Get(master, "UserId"));
                // The original host must still be the master, including while the scene is loading.
                if (!joined)
                {
                    if ((bool)PhotonAccess.Get(PhotonAccess.Network, "IsMasterClient")) { Fail(T("net.hostLeft")); return; }
                    joined = true;
                    hostActor = actor;
                    progress?.Close();
                    progress = null;
                    Debug.Log("[CloudCoop] joined Photon room; waiting for the game's scene handoff");
                }
                else if (actor != hostActor) Fail(T("net.hostLeft"));
                return;
            }
            if (joined) { Fail(T("net.disconnected")); return; }
            // Scene handoff may destroy the popup while Photon is still connecting. Only its actual button cancels.
            if (progress != null && progress.Answer == 1) { Fail(QuestText.Get("cloud.cancelled")); return; }
            if (Time.unscaledTime >= deadline) Fail(T("net.timeout"));
        }

        public void Disconnected(string reason)
        {
            // JoinSteamLobby and NetworkConnect intentionally disconnect before starting the next connection.
            // An initial client-initiated handoff must not clear the invitation or cancel the pending join.
            if (reason == "ApplicationQuit" || (!joined && reason == "DisconnectByClientLogic")) return;
            Debug.LogWarning("[CloudCoop] disconnected: " + reason);
            if (joining || hosting) Fail(T("net.disconnected"));
        }

        public void ConnectionFailed(string message) => Fail(message);

        public void RoomFailed(short code)
        {
            Debug.LogWarning("[CloudCoop] room operation failed: " + code);
            if (code == 32746) { Fail(T("net.duplicateId")); return; }
            if (hosting) { Fail(T("net.createFailed")); return; }
            var key = code == 32765 ? "net.roomFull" : code == 32758 || code == 32764 ? "net.roomClosed" : "net.joinFailed";
            Fail(T(key));
        }

        private void Fail(string message)
        {
            var wasJoining = joining;
            var wasHosting = hosting;
            hosting = false;
            joining = false;
            joined = false;
            progress?.Close();
            progress = null;
            failure = message == "Host and guest game/PUN versions differ. Use matching builds." ? T("net.versionMismatch") :
                message == "The host has left. The session has ended." ? T("net.hostLeft") : QuestText.Literal(message);
            Debug.LogWarning("[CloudCoop] " + message);
            try { if ((wasJoining || wasHosting) && PhotonAccess.Connected) PhotonAccess.Disconnect(); }
            catch (Exception error) { Debug.LogException(error); }
            if (wasJoining || wasHosting)
            {
                CloudCoop.ClearInvitation();
                // A client-initiated disconnect alone does not return the game to its menus.
                if (NetworkManager.instance) NetworkManager.instance.TriggerLeavePhotonRoomForced();
                else if (RunManager.instance)
                    RunManager.instance.StartCoroutine(RunManager.instance.LeaveToMainMenu());
            }
        }
    }
}
