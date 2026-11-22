using System;
using System.Collections;
using System.Collections.Generic;
using RepoQuest.Steam;
using UnityEngine;

namespace RepoXR.Standalone
{
    /// <summary>
    /// Logging in to Steam on the headset, in the game's menu: account name and password on a text page with RepoXR's
    /// VR keyboard, then Steam Guard (approve the sign-in in the Steam app, or type the app's code or an emailed one),
    /// then the game switches to the account without a restart (<see cref="SteamSetup.SaveLogin"/>).
    /// </summary>
    internal static class SteamLoginFlow
    {
        private const string Header = "STEAM LOGIN";
        private static readonly Color HeaderColor = new Color(1f, 0.55f, 0f);
        private static readonly Color ErrorColor = new Color(1f, 0.25f, 0.25f);

        private enum Screen
        {
            None,
            Progress,
            Approval,
            Code,
        }

        public static bool Running { get; private set; }

        /// <summary>Opens on top of the current menu page; <paramref name="reason"/> says why a login is needed</summary>
        public static IEnumerator Run(MenuButtonPopUp host, string reason = null)
        {
            if (Running)
                yield break;

            Running = true;
            // Steps and what they wait for run here rather than as Unity's nested coroutines, so that an exception
            // ends the login with Running cleared instead of leaving the Steam button dead until a restart
            var stack = new Stack<IEnumerator>();
            stack.Push(Steps(host, reason));
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool more;
                try
                {
                    more = top.MoveNext();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    break;
                }

                if (!more)
                    stack.Pop();
                else if (top.Current is IEnumerator nested)
                    stack.Push(nested); // also WaitForSecondsRealtime, whose MoveNext says whether to keep waiting
                else
                    yield return top.Current;
            }

            Running = false;
        }

        private static IEnumerator Steps(MenuButtonPopUp host, string reason)
        {
            var intro = MenuPopup.Ask(host, Header, HeaderColor,
                (reason != null ? RepoXR.UI.QuestText.Literal(reason) + "\n\n" : "") + RepoXR.UI.QuestText.Get("steam.intro"),
                "LOG IN", "BACK");
            while (intro.Answer == 0)
                yield return null;
            Debug.Log($"[RepoXR.Standalone] Steam login: popup answer {intro.Answer}");
            if (intro.Answer != 1)
                yield break;
            yield return MenuSettled();

            // Account name and password on one page: BACK from the password returns to the name
            var account = SteamSetup.LastAccountName;
            string password = null;
            var page = MenuTextPage.Open();
            if (page == null)
                yield break;
            while (password == null)
            {
                page.Show("STEAM ACCOUNT", "NEXT", "BACK", account);
                yield return WaitForAnswer(page);
                if (page.Answer != 1)
                {
                    Debug.Log($"[RepoXR.Standalone] Steam login: account page answer {page.Answer}, open {page.IsOpen}");
                    page.Close();
                    yield break;
                }

                account = page.Text.Trim();
                page.Show("PASSWORD", "LOG IN", "BACK", "", masked: true);
                yield return WaitForAnswer(page);
                if (page.Answer == 1)
                    password = page.Text;
                else if (!page.IsOpen)
                    yield break;
            }

            page.Close();
            var login = SteamSetup.StartLogin(account, password);
            password = null;
            yield return MenuSettled();

            yield return Confirm(host, login);
        }

        /// <summary>From the password to a logged-on account: Steam Guard, then the switch</summary>
        private static IEnumerator Confirm(MenuButtonPopUp host, PasswordLogin login)
        {
            var screen = Screen.None;
            MenuPopup popup = null;
            MenuTextPage codePage = null;
            var codeSent = false;
            var cancelled = false;

            while (!cancelled)
            {
                var state = login.State;
                if (state == PasswordLogin.Step.Succeeded || state == PasswordLogin.Step.Failed)
                    break;

                // What the login is waiting for decides the screen; a code being checked stays on the code page
                var wanted = state == PasswordLogin.Step.WaitingForApproval ? Screen.Approval
                    : state == PasswordLogin.Step.WaitingForCode ? Screen.Code
                    : state == PasswordLogin.Step.CheckingCode && screen == Screen.Code ? Screen.Code
                    : Screen.Progress;

                if (wanted != screen)
                {
                    popup?.Close();
                    codePage?.Close();
                    popup = null;
                    codePage = null;
                    if (screen != Screen.None)
                        yield return MenuSettled();
                    if (!MenuManager.instance || !MenuManager.instance.currentMenuPage)
                    {
                        cancelled = true;
                        break;
                    }

                    screen = wanted;
                    switch (screen)
                    {
                        case Screen.Progress:
                            popup = MenuPopup.Message(Header, HeaderColor, "Logging in to Steam...", "CANCEL");
                            break;
                        case Screen.Approval:
                            popup = login.ApprovalByEmail
                                ? MenuPopup.Message(Header, HeaderColor,
                                    "Steam emailed you a link to confirm this sign-in.\n\nOpen it, and the headset logs in.", "CANCEL")
                                : login.Code == PasswordLogin.CodeKind.App
                                    ? MenuPopup.Ask(host, Header, HeaderColor,
                                        "Approve the sign-in in the <b>Steam app</b> on your phone.\n\nOr enter the code the app shows.",
                                        "ENTER CODE", "CANCEL")
                                    : MenuPopup.Message(Header, HeaderColor,
                                        "Approve the sign-in in the <b>Steam app</b> on your phone.", "CANCEL");
                            break;
                        case Screen.Code:
                            codePage = MenuTextPage.Open();
                            codePage?.Show(CodeTitle(login), "CONFIRM", "CANCEL", "", upperOnly: true, maxLength: 5);
                            codeSent = false;
                            break;
                    }

                    if (screen == Screen.Code && codePage == null)
                        cancelled = true;
                    continue;
                }

                if (screen == Screen.Code)
                {
                    if (codeSent && state == PasswordLogin.Step.WaitingForCode && login.CodeRejected)
                    {
                        codePage.Warn("WRONG CODE");
                        codeSent = false;
                    }
                    else if (!codeSent && codePage.Answer == 1)
                    {
                        login.SubmitCode(codePage.Text);
                        codePage.SetTitle("CHECKING...");
                        codeSent = true;
                    }
                    else if (codePage.Answer == 2 || !codePage.IsOpen)
                        cancelled = true;
                }
                else if (popup != null && popup.Answer != 0)
                {
                    // ENTER CODE on the approval popup; any other answer is CANCEL
                    if (screen == Screen.Approval && popup.Answer == 1 && login.Code == PasswordLogin.CodeKind.App &&
                        !login.ApprovalByEmail)
                    {
                        login.PreferCode();
                        popup = null;
                        screen = Screen.None;
                        yield return MenuSettled();
                    }
                    else
                        cancelled = true;
                }

                yield return null;
            }

            // The "Logging in" popup stays for the last step; anything else makes way
            var progress = screen == Screen.Progress && popup != null && popup.IsOpen ? popup : null;
            if (progress == null)
                popup?.Close();
            codePage?.Close();
            if (cancelled || login.State == PasswordLogin.Step.Failed)
            {
                progress?.Close();
                login.Dispose();
                if (cancelled || login.FailureResult == SteamKit2.EResult.Cancelled)
                    yield break;

                yield return MenuSettled();
                MenuPopup.Message(Header, ErrorColor, login.FailureMessage, "OK");
                yield break;
            }

            // Signed in: the headset's session becomes this account, and the game switches to it
            var backend = SteamSetup.SaveLogin(login);
            login.Dispose();
            if (progress == null)
            {
                yield return MenuSettled();
                progress = MenuPopup.Message(Header, HeaderColor, "Logging in to Steam...", "OK");
            }
            else
                progress.SetButton("OK"); // nothing left to cancel
            var until = Time.realtimeSinceStartup + 30f;
            while (!(backend.IsLoggedOn && backend.PersonaKnown) && !backend.LoginRejected && Time.realtimeSinceStartup < until)
                yield return null;

            progress.Close();
            yield return MenuSettled();
            MenuPopup result;
            if (backend.IsLoggedOn)
                result = MenuPopup.Message(Header, HeaderColor,
                    string.Format(RepoXR.UI.QuestText.Get("steam.loggedIn"), backend.PersonaName), "OK");
            else if (backend.LoginRejected)
                result = MenuPopup.Message(Header, ErrorColor, "Steam accepted the password, but refused the logon.\n\nTry again later.", "OK");
            else
                result = MenuPopup.Message(Header, HeaderColor,
                    "Steam accepted the login, but the connection is slow.\n\nThe headset keeps trying in the background.", "OK");
            while (result.IsOpen && result.Answer == 0) yield return null;
            yield return MenuSettled();
        }

        private static string CodeTitle(PasswordLogin login) =>
            login.Code == PasswordLogin.CodeKind.Email ? "EMAIL CODE" : "STEAM GUARD CODE";

        private static IEnumerator WaitForAnswer(MenuTextPage page)
        {
            while (page.IsOpen && page.Answer == 0)
                yield return null;
        }

        /// <summary>
        /// The page or popup that was just closed is still animating away: wait until the page under it is back, so
        /// the next popup opens on top of the right page (as SteamMenu does between friends)
        /// </summary>
        internal static IEnumerator MenuSettled()
        {
            yield return new WaitForSecondsRealtime(0.25f);
            var until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until &&
                   !(MenuManager.instance && MenuManager.instance.currentMenuPage &&
                     MenuManager.instance.currentMenuPage.currentPageState == MenuPage.PageState.Active))
                yield return null;
        }
    }
}
