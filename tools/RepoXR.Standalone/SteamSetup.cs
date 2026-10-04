using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RepoQuest.Steam;
using UnityEngine;
using UnityEngine.UI;

namespace RepoXR.Standalone
{
    /// <summary>
    /// Steam on the Quest: the shim's SteamKit2 backend when a Steam login is saved on the device, otherwise the shim's
    /// offline backend (singleplayer only). The login is made on the headset (the main menu's Steam button,
    /// <see cref="SteamLoginFlow"/>) or copied from the Mac (scripts/quest-steam-login.sh); logging in and out switch
    /// the backend while the game runs. Android only: on PC the real Facepunch.Steamworks is loaded, which has none of
    /// these types, so nothing here may run there.
    /// </summary>
    internal static class SteamSetup
    {
        private const uint RepoAppId = 3241660;

        private static SteamMenu menu;

        private static string SessionPath => Path.Combine(Application.persistentDataPath, "steam-session.json");

        /// <summary>The logged-in account's backend; null while offline (never logged in, or logged out)</summary>
        internal static SteamKitBackend Backend { get; private set; }

        /// <summary>The account name last used on this device, offered at the next login</summary>
        internal static string LastAccountName => SessionStore.Load(SessionPath).AccountName;

        /// <summary>Before the first scene: the game's SteamManager calls SteamClient.Init in its Awake</summary>
        public static void Install()
        {
            SteamKitBackend.Log = message => Debug.Log(message);
            var session = SessionStore.Load(SessionPath);
            if (session.HasLogin)
            {
                Backend = new SteamKitBackend(SessionPath);
                Steamworks.SteamBackend.Current = Backend;
                Debug.Log("[RepoXR.Standalone] Steam: logging in with the saved login");
            }
            else
            {
                Steamworks.SteamBackend.Current = Offline(session, "");
                Debug.Log("[RepoXR.Standalone] no Steam login on this device: offline, singleplayer only");
            }

            // The shim catches exceptions thrown by the game's Steam event handlers, like Facepunch's dispatcher does
            Steamworks.Dispatch.OnException = Debug.LogException;

            // Also without Steam: Join friend and the Steam button offer the login
            var host = new GameObject("RepoXR (Steam)");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            menu = host.AddComponent<SteamMenu>();

            // The main menu's "Join friend" opens the Steam overlay's friends list, which the Quest does not have
            Steamworks.SteamFriends.OnOverlayRequested += type =>
            {
                if (type == "friends")
                    menu.ShowFriends();
            };

            // Steam shows invites in its overlay/client; here the game's popup asks instead
            if (Backend != null)
                Backend.Invited += menu.ShowInvite;
        }

        /// <summary>Starts a password login with this device's ids (<see cref="SteamLoginFlow"/>)</summary>
        internal static PasswordLogin StartLogin(string accountName, string password)
        {
            var session = SessionStore.Load(SessionPath);
            session.EnsureDeviceIds();
            session.Save();
            return PasswordLogin.Start(accountName, password, session.GuardData, session.MachineId);
        }

        /// <summary>
        /// A login just made on the headset becomes this device's session, and the game switches to it at once: its
        /// backend logs on in the background, as one does at start. The previous backend (a login Steam refused) stops.
        /// </summary>
        internal static SteamKitBackend SaveLogin(PasswordLogin login)
        {
            var session = SessionStore.Load(SessionPath);
            session.SetLogin(login.AccountName, login.RefreshToken, login.SteamId, login.GuardData);
            session.Save();

            var previous = Backend;
            Backend = new SteamKitBackend(SessionPath);
            Backend.Start(Steamworks.SteamClient.AppId.Value != 0 ? Steamworks.SteamClient.AppId.Value : RepoAppId);
            Backend.Invited += menu.ShowInvite;
            Steamworks.SteamBackend.Current = Backend;
            Debug.Log("[RepoXR.Standalone] Steam: logging in with the new login");

            if (previous != null)
            {
                previous.Invited -= menu.ShowInvite;
                Task.Run(previous.Shutdown);
            }
            return Backend;
        }

        /// <summary>
        /// Back to offline: Steam revokes the login token and the session file forgets it; the account's SteamID and
        /// name stay for singleplayer, whose saves keep upgrades by SteamID
        /// </summary>
        internal static void LogOut()
        {
            var backend = Backend;
            if (backend == null)
                return;

            Backend = null;
            backend.Invited -= menu.ShowInvite;
            var signingOut = backend.LogOutAsync(); // from here on the backend no longer writes the session file

            var session = SessionStore.Load(SessionPath);
            session.ClearLogin();
            session.Save();
            Steamworks.SteamBackend.Current = Offline(session, backend.PersonaName);
            signingOut.ContinueWith(t => Debug.Log("[RepoXR.Standalone] Steam: logged out"));
        }

        private static Steamworks.OfflineSteamBackend Offline(SessionStore session, string personaName)
        {
            var offline = new Steamworks.OfflineSteamBackend();
            if (session.HasIdentity)
                offline.Id = session.SteamId;
            if (personaName.Length > 0)
                offline.Name = personaName;
            else if (session.PersonaName.Length > 0)
                offline.Name = session.PersonaName;
            return offline;
        }
    }

    /// <summary>
    /// Steam in the Quest's menus. The main menu's Join friend button shows the friends playing R.E.P.O. in a lobby
    /// one by one in the game's own two-option popup (JOIN / NEXT), JOIN being what "Join game" in the Steam friends
    /// list does; without a login it offers one. A Steam button next to Quit game shows the account and logs in or
    /// out. For development joining is also reachable from the Mac (scripts/quest-join.sh):
    ///   files/steam-join     friend name (or SteamID) -> join that friend's lobby
    ///   files/steam-friends  -> log the friends playing R.E.P.O. and their lobbies
    /// </summary>
    internal sealed class SteamMenu : MonoBehaviour
    {
        private static readonly Color HeaderColor = new Color(1f, 0.55f, 0f);
        private const string Header = "JOIN FRIEND";
        private const string InviteHeader = "INVITE";
        private const string AccountHeader = "STEAM";

        private string joinRequest;
        private string friendsRequest;
        private MenuButtonPopUp popUp;
        private bool looking;
        private bool showingAccount;

        private MenuPageMain decoratedPage;
        private MenuButton steamButton;
        private Color steamButtonColor;
        private float steamButtonUpdate;

        private void Awake()
        {
            joinRequest = Path.Combine(Application.persistentDataPath, "steam-join");
            friendsRequest = Path.Combine(Application.persistentDataPath, "steam-friends");
            popUp = gameObject.AddComponent<MenuButtonPopUp>();
        }

        private IEnumerator Start()
        {
            var wait = new WaitForSecondsRealtime(1f);
            while (true)
            {
                yield return wait;

                if (SteamSetup.Backend == null)
                    continue;

                if (File.Exists(friendsRequest))
                {
                    File.Delete(friendsRequest);
                    yield return LogFriends();
                }

                if (File.Exists(joinRequest))
                {
                    var target = File.ReadAllText(joinRequest).Trim();
                    File.Delete(joinRequest);
                    yield return JoinByName(target);
                }
            }
        }

        private void Update()
        {
            var main = MenuPageMain.instance;
            if (main && main != decoratedPage)
            {
                decoratedPage = main;
                steamButton = AddSteamButton(main);
                steamButtonUpdate = 0f;
            }

            if (steamButton && Time.unscaledTime >= steamButtonUpdate)
            {
                steamButtonUpdate = Time.unscaledTime + 0.5f;
                UpdateSteamButton();
            }
        }

        /// <summary>
        /// A copy of the main menu's Settings button, right of Quit game at the bottom of the menu's panel: it shows the
        /// Steam account and opens <see cref="ShowAccount"/>
        /// </summary>
        private MenuButton AddSteamButton(MenuPageMain main)
        {
            var source = main.transform.Find("Buttons/Menu Button - Settings");
            if (!source)
            {
                Debug.LogWarning("[RepoXR.Standalone] Steam: no Settings button in the main menu to copy");
                return null;
            }

            var copy = Instantiate(source.gameObject, source.parent);
            copy.name = "Menu Button - Steam";
            // Before their Start: the localized "Settings" would replace the label, a popup would replace the click
            foreach (var localized in copy.GetComponentsInChildren<LocalizationChangedEvent>(true))
                DestroyImmediate(localized);
            if (copy.GetComponent<MenuButtonPopUp>() is MenuButtonPopUp question)
                DestroyImmediate(question);

            var uiButton = copy.GetComponent<Button>();
            uiButton.onClick = new Button.ButtonClickedEvent(); // drops ButtonEventSettings
            uiButton.onClick.AddListener(ShowAccount);

            var button = copy.GetComponent<MenuButton>();
            var rect = (RectTransform)copy.transform;
            rect.anchoredPosition = new Vector2(190f, 0f);
            // ResizeToText (on every label change) puts the button back to where and how big it was at Awake
            button.originalButtonPosition = rect.localPosition;
            button.originalFontSize = 20f;
            button.buttonText.fontSize = 20f;
            steamButtonColor = button.colorNormal;
            AddVersionLabel(button, rect);
            return button;
        }

        /// <summary>"REPO Quest 1.0.0 by Dobutsu" above the Steam button, in line with its text: which build of the
        /// port this is (Application.version is the builder's version) and who made it. Not below: the button is on
        /// the menu page's bottom row, and the page cuts off what is under it</summary>
        private static void AddVersionLabel(MenuButton button, RectTransform buttonRect)
        {
            var labelObject = Instantiate(button.buttonText.gameObject, buttonRect.parent);
            labelObject.name = "RepoQuest Version";
            var label = labelObject.GetComponent<TMPro.TextMeshProUGUI>();
            label.text = "REPO Quest " + Application.version + " by Dobutsu";
            label.fontSize = 12f;
            label.color = new Color(0.75f, 0.75f, 0.75f, 0.8f);
            label.alignment = TMPro.TextAlignmentOptions.Left;
            label.enableWordWrapping = false; // one line, also for a long dev version
            label.raycastTarget = false;
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = buttonRect.anchorMin;
            labelRect.anchorMax = buttonRect.anchorMax;
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.sizeDelta = new Vector2(280f, 18f);
            labelRect.anchoredPosition = buttonRect.anchoredPosition + new Vector2(0f, buttonRect.sizeDelta.y + 10f);
        }

        private void UpdateSteamButton()
        {
            var backend = SteamSetup.Backend;
            var loggedIn = backend != null && !backend.LoginRejected;
            var label = !loggedIn ? "Steam: log in"
                : backend.IsLoggedOn ? "Steam: " + Shorten(backend.PersonaName.Length > 0 ? backend.PersonaName : "online", 12)
                : "Steam: offline";

            if (steamButton.buttonTextString != label)
            {
                steamButton.buttonTextString = label;
                steamButton.buttonText.text = label;
                steamButton.ResizeToText();
            }

            // Orange like the game's unplayed-tutorial button while there is no working login
            steamButton.colorNormal = loggedIn ? steamButtonColor : HeaderColor;
        }

        private static string Shorten(string text, int length) =>
            text.Length <= length ? text : text.Substring(0, length - 2) + "..";

        /// <summary>The Steam button: log in, or show the account with LOG OUT</summary>
        private void ShowAccount()
        {
            if (!showingAccount && !SteamLoginFlow.Running)
                StartCoroutine(ShowAccountRoutine());
        }

        private IEnumerator ShowAccountRoutine()
        {
            showingAccount = true;
            try
            {
                var backend = SteamSetup.Backend;
                if (backend == null)
                {
                    yield return SteamLoginFlow.Run(popUp);
                    yield break;
                }

                if (backend.LoginRejected)
                {
                    yield return SteamLoginFlow.Run(popUp, "Steam no longer accepts the saved login.");
                    yield break;
                }

                var account = MenuPopup.Ask(popUp, AccountHeader, HeaderColor,
                    backend.IsLoggedOn
                        ? $"Logged in as <b>{backend.PersonaName}</b>."
                        : $"<b>{backend.PersonaName}</b> is not connected to Steam right now.\n\nCheck the headset's internet connection.",
                    "LOG OUT", "BACK");
                while (account.Answer == 0)
                    yield return null;
                if (account.Answer != 1 || SteamSetup.Backend != backend)
                    yield break;

                SteamSetup.LogOut();
                yield return SteamLoginFlow.MenuSettled();
                MenuPopup.Message(AccountHeader, HeaderColor,
                    "Logged out of Steam.\n\nSingleplayer works as before; log in again to join friends.", "OK");
            }
            finally
            {
                showingAccount = false;
            }
        }

        public void ShowFriends()
        {
            if (!looking && !SteamLoginFlow.Running)
                StartCoroutine(ShowFriendsRoutine());
        }

        private IEnumerator ShowFriendsRoutine()
        {
            var backend = SteamSetup.Backend;
            if (backend == null)
            {
                yield return SteamLoginFlow.Run(popUp, "This headset is not logged in to Steam, so friends' games cannot be joined.");
                yield break;
            }

            looking = true;
            if (backend.IsLoggedOn)
            {
                backend.RefreshFriends();
                yield return new WaitForSecondsRealtime(2f);
            }
            looking = false;

            if (!MenuManager.instance)
                yield break;

            if (backend.LoginRejected)
            {
                yield return SteamLoginFlow.Run(popUp, "Steam no longer accepts the saved login.");
                yield break;
            }

            if (!backend.IsLoggedOn)
            {
                MenuManager.instance.PagePopUp(Header, HeaderColor,
                    "Not connected to Steam yet.\n\nCheck the headset's internet connection and try again.", "OK", true);
                yield break;
            }

            var friends = backend.FriendsInGame().Where(f => f.LobbyId != 0).ToList();
            if (friends.Count == 0)
            {
                MenuManager.instance.PagePopUp(Header, HeaderColor,
                    "None of your Steam friends is in a R.E.P.O. lobby right now.\n\nAsk a friend to host a game, then try again.",
                    "OK", true);
                yield break;
            }

            ShowFriend(friends, 0);
        }

        private void ShowFriend(List<FriendInfo> friends, int index)
        {
            var friend = friends[index];
            var more = friends.Count > 1;

            popUp.option1Event = new UnityEngine.Events.UnityEvent();
            popUp.option1Event.AddListener(() => Join(friend));
            popUp.option2Event = new UnityEngine.Events.UnityEvent();
            if (more)
                popUp.option2Event.AddListener(() => StartCoroutine(ShowNext(friends, (index + 1) % friends.Count)));

            var body = $"<b>{friend.Name}</b> is in a R.E.P.O. lobby." + (more ? $"\n\n{index + 1} of {friends.Count}" : "");
            MenuManager.instance.PagePopUpTwoOptions(popUp, Header, null, HeaderColor, body, null,
                "JOIN", null, more ? "NEXT" : "BACK", null, true);
        }

        // The popup that was just answered is still closing; open the next one once it is gone
        private IEnumerator ShowNext(List<FriendInfo> friends, int index)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            if (MenuManager.instance)
                ShowFriend(friends, index);
        }

        private static void Join(FriendInfo friend)
        {
            if (SteamSetup.Backend.JoinFriend(friend.SteamId))
                Debug.Log($"[RepoXR.Standalone] Steam: joining {friend.Name} (lobby {friend.LobbyId})");
            else
                MenuManager.instance?.PagePopUp(Header, HeaderColor, $"{friend.Name} is no longer in a lobby.", "OK", true);
        }

        /// <summary>A friend invited us: JOIN does what accepting the invite in Steam does</summary>
        public void ShowInvite(ulong friendId, ulong lobbyId)
        {
            var backend = SteamSetup.Backend;
            var name = friendId != 0 ? backend.GetPersonaName(friendId) : "";
            if (string.IsNullOrEmpty(name))
                name = "A friend";

            if (!MenuManager.instance)
            {
                Debug.LogWarning($"[RepoXR.Standalone] Steam: invite from {name} arrived with no menu to show it");
                return;
            }

            popUp.option1Event = new UnityEngine.Events.UnityEvent();
            popUp.option1Event.AddListener(() =>
            {
                if (backend.JoinInvite(friendId, lobbyId))
                    Debug.Log($"[RepoXR.Standalone] Steam: accepting the invite from {name} (lobby {lobbyId})");
                else
                    MenuManager.instance?.PagePopUp(InviteHeader, HeaderColor, $"{name} is no longer in a lobby.", "OK", true);
            });
            popUp.option2Event = new UnityEngine.Events.UnityEvent();

            MenuManager.instance.PagePopUpTwoOptions(popUp, InviteHeader, null, HeaderColor,
                $"<b>{name}</b> invites you to play R.E.P.O.", null, "JOIN", null, "LATER", null, true);
        }

        private static IEnumerator LogFriends()
        {
            SteamSetup.Backend.RefreshFriends();
            yield return new WaitForSecondsRealtime(4f);

            var friends = SteamSetup.Backend.FriendsInGame();
            Debug.Log($"[RepoXR.Standalone] Steam: {friends.Count} friends playing (logged on: {SteamSetup.Backend.IsLoggedOn})");
            foreach (var friend in friends)
                Debug.Log($"[RepoXR.Standalone] Steam:   {friend.Name} ({friend.SteamId}) lobby {(friend.LobbyId == 0 ? "-" : friend.LobbyId.ToString())}");
        }

        private static IEnumerator JoinByName(string target)
        {
            SteamSetup.Backend.RefreshFriends();
            yield return new WaitForSecondsRealtime(4f);

            var friends = SteamSetup.Backend.FriendsInGame();
            var friend = friends.FirstOrDefault(f => f.SteamId.ToString() == target) ??
                         friends.FirstOrDefault(f => f.Name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0);

            if (friend == null)
                Debug.LogWarning($"[RepoXR.Standalone] Steam: no friend matching '{target}' is playing R.E.P.O.");
            else if (!SteamSetup.Backend.JoinFriend(friend.SteamId))
                Debug.LogWarning($"[RepoXR.Standalone] Steam: {friend.Name} is not in a joinable lobby");
            else
                Debug.Log($"[RepoXR.Standalone] Steam: joining {friend.Name} (lobby {friend.LobbyId})");
        }
    }
}
