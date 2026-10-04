using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace RepoQuest.Steam
{
    /// <summary>
    /// Signs in to Steam with account name and password, as the Steam client's login window does, and gets the refresh
    /// token <see cref="SteamKitBackend"/> logs on with. Steam Guard: approving the sign-in in the Steam app, a code
    /// from the app, or a code sent by email. The password goes to Steam only, encrypted with Steam's key for the
    /// account, and is not kept.
    ///
    /// Talks to Steam's authentication service with plain unified messages instead of SteamKit 2.5's
    /// SteamAuthentication, which builds its calls from compiled expression trees and invokes generic methods through
    /// reflection, neither of which is safe under IL2CPP.
    ///
    /// Runs on its own connection and threads; the game polls <see cref="State"/> from its main thread.
    /// </summary>
    public sealed class PasswordLogin : IDisposable
    {
        public enum Step
        {
            Connecting,
            CheckingPassword,
            /// <summary>Waiting for the player to approve the sign-in (Steam app, or the link in an email)</summary>
            WaitingForApproval,
            /// <summary>Waiting for <see cref="SubmitCode"/>; <see cref="CodeRejected"/> if the last one was wrong</summary>
            WaitingForCode,
            CheckingCode,
            Succeeded,
            Failed,
        }

        public enum CodeKind
        {
            None,
            /// <summary>The Steam Guard code the Steam app shows</summary>
            App,
            /// <summary>A code Steam emails to the account's address</summary>
            Email,
        }

        public const string DeviceName = "RepoXR Quest";

        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(15);

        private readonly SteamClient client;
        private readonly CallbackManager callbacks;
        private readonly SteamUnifiedMessages unified;
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> connected =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object codeLock = new object();

        // A Step; written by the login's thread, and once by the game's (PreferCode, compare-and-swap)
        private int state = (int)Step.Connecting;
        private volatile bool running = true;
        private volatile bool codeRejected;
        private string pendingCode;
        private Thread pump;
        private ulong clientId;
        private byte[] requestId;
        private ulong steamId;

        private PasswordLogin(string machineId)
        {
            // IL2CPP cannot emit code: protobuf-net must use reflection only (also set by SteamKitBackend, which may
            // not exist yet when the first login happens)
            ProtoBuf.Meta.RuntimeTypeModel.Default.AutoCompile = false;

            client = new SteamClient(SteamKitBackend.CreateConfiguration(machineId));
            unified = client.GetHandler<SteamUnifiedMessages>();
            callbacks = new CallbackManager(client);
            callbacks.Subscribe<SteamClient.ConnectedCallback>(_ => connected.TrySetResult(true));
            callbacks.Subscribe<SteamClient.DisconnectedCallback>(_ => connected.TrySetResult(false));
        }

        public Step State
        {
            get => (Step)Volatile.Read(ref state);
            private set => Volatile.Write(ref state, (int)value);
        }

        /// <summary>The code the account can sign in with instead of approving it (<see cref="CodeKind.None"/>: none)</summary>
        public CodeKind Code { get; private set; }

        /// <summary>Where an emailed code went, as Steam names it (the address's domain)</summary>
        public string EmailDomain { get; private set; } = "";

        /// <summary>Steam asks for approval by email (a link) rather than in the Steam app</summary>
        public bool ApprovalByEmail { get; private set; }

        public bool CodeRejected => codeRejected;

        public EResult FailureResult { get; private set; } = EResult.OK;
        public string FailureMessage { get; private set; } = "";

        // The login, once State is Succeeded
        public string AccountName { get; private set; } = "";
        public string RefreshToken { get; private set; } = "";
        public ulong SteamId => steamId;
        /// <summary>Lets the next login on this device skip the emailed code; empty if Steam sent none</summary>
        public string GuardData { get; private set; } = "";

        /// <summary>
        /// Starts signing in on a thread of its own. <paramref name="guardData"/>: what an earlier login returned as
        /// <see cref="GuardData"/>, or empty. <paramref name="machineId"/>: the device id of the session store.
        /// </summary>
        public static PasswordLogin Start(string accountName, string password, string guardData, string machineId)
        {
            var login = new PasswordLogin(machineId);
            Task.Run(() => login.RunAsync(accountName.Trim(), password, guardData));
            return login;
        }

        /// <summary>A Steam Guard code the player typed (App or Email, whichever <see cref="Code"/> says)</summary>
        public void SubmitCode(string code)
        {
            // Before the code can be sent: a rejection of this code must not be overwritten
            codeRejected = false;
            lock (codeLock)
                pendingCode = code.Trim().ToUpperInvariant();
        }

        /// <summary>The player chose to type the app's code instead of approving the sign-in in the app</summary>
        public void PreferCode()
        {
            // Unless the approval has just come through
            if (Code == CodeKind.App)
                Interlocked.CompareExchange(ref state, (int)Step.WaitingForCode, (int)Step.WaitingForApproval);
        }

        public void Dispose()
        {
            cancel.Cancel();
        }

        private async Task RunAsync(string accountName, string password, string guardData)
        {
            try
            {
                pump = new Thread(Pump) { IsBackground = true, Name = "SteamKit login" };
                pump.Start();
                client.Connect();
                if (await Task.WhenAny(connected.Task, Task.Delay(ConnectTimeout, cancel.Token)).ConfigureAwait(false) != connected.Task ||
                    !connected.Task.Result)
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    throw new SteamCallException("connect", EResult.NoConnection);
                }

                State = Step.CheckingPassword;
                var key = await CallAsync<CAuthentication_GetPasswordRSAPublicKey_Request, CAuthentication_GetPasswordRSAPublicKey_Response>(
                    "Authentication.GetPasswordRSAPublicKey#1",
                    new CAuthentication_GetPasswordRSAPublicKey_Request { account_name = accountName }).ConfigureAwait(false);

                var request = new CAuthentication_BeginAuthSessionViaCredentials_Request
                {
                    account_name = accountName,
                    encrypted_password = Encrypt(password, key),
                    encryption_timestamp = key.timestamp,
                    persistence = ESessionPersistence.k_ESessionPersistence_Persistent,
                    website_id = "Client",
                    device_details = new CAuthentication_DeviceDetails
                    {
                        device_friendly_name = DeviceName,
                        // A token for the Steam client, which is what CM logons accept
                        platform_type = EAuthTokenPlatformType.k_EAuthTokenPlatformType_SteamClient,
                        // SteamKit's own detection (Utils.GetOSType is internal); SteamUser.LogOn reports the same
                        os_type = (int)new AuthSessionDetails().ClientOSType,
                    },
                };
                if (!string.IsNullOrEmpty(guardData))
                    request.guard_data = guardData;
                password = null;

                var session = await CallAsync<CAuthentication_BeginAuthSessionViaCredentials_Request, CAuthentication_BeginAuthSessionViaCredentials_Response>(
                    "Authentication.BeginAuthSessionViaCredentials#1", request).ConfigureAwait(false);
                clientId = session.client_id;
                requestId = session.request_id;
                steamId = session.steamid;
                var interval = TimeSpan.FromSeconds(Math.Min(Math.Max(session.interval, 1f), 5f));

                SteamKitBackend.Log("[Steam] password accepted; Steam Guard: " +
                                    string.Join(", ", session.allowed_confirmations.Select(c => c.confirmation_type)));
                if (!ChooseConfirmation(session))
                {
                    Fail(EResult.Fail, "This account's Steam Guard settings are not supported here.");
                    return;
                }

                // Until the sign-in is approved or a code is accepted; polling also notices an approval in the Steam
                // app after the player chose to type a code
                while (true)
                {
                    cancel.Token.ThrowIfCancellationRequested();

                    if (TakeCode() is string code)
                        await SendCodeAsync(code).ConfigureAwait(false);

                    // An emailed code is the only way through; nothing to poll for until it is sent
                    if (State == Step.WaitingForCode && Code == CodeKind.Email)
                    {
                        await Task.Delay(200, cancel.Token).ConfigureAwait(false);
                        continue;
                    }

                    var poll = await CallAsync<CAuthentication_PollAuthSessionStatus_Request, CAuthentication_PollAuthSessionStatus_Response>(
                        "Authentication.PollAuthSessionStatus#1",
                        new CAuthentication_PollAuthSessionStatus_Request { client_id = clientId, request_id = requestId }).ConfigureAwait(false);
                    if (poll.new_client_id != 0)
                        clientId = poll.new_client_id;

                    if (!string.IsNullOrEmpty(poll.refresh_token))
                    {
                        AccountName = string.IsNullOrEmpty(poll.account_name) ? accountName : poll.account_name;
                        RefreshToken = poll.refresh_token;
                        GuardData = poll.new_guard_data ?? "";
                        if (steamId == 0)
                            steamId = SessionStore.SteamIdFromToken(RefreshToken);
                        State = Step.Succeeded;
                        SteamKitBackend.Log($"[Steam] signed in: {steamId}");
                        return;
                    }

                    // Wait out the polling interval, but send a typed code right away
                    for (var waited = TimeSpan.Zero; waited < interval && !HasCode(); waited += TimeSpan.FromMilliseconds(100))
                        await Task.Delay(100, cancel.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                Fail(EResult.Cancelled, "Login cancelled.");
            }
            catch (SteamCallException e)
            {
                SteamKitBackend.Log($"[Steam] login failed: {e.Method} -> {e.Result}");
                Fail(e.Result, Describe(e.Result));
            }
            catch (Exception e)
            {
                SteamKitBackend.Log($"[Steam] login failed: {e}");
                Fail(EResult.Fail, "The login failed: " + e.GetBaseException().Message);
            }
            finally
            {
                running = false;
                if (client.IsConnected)
                    client.Disconnect();
            }
        }

        /// <summary>
        /// What Steam Guard wants, in the order the Steam client prefers: nothing, approval in the Steam app (with its
        /// code as the alternative), the app's code, an emailed code, a link in an email. False if none of these.
        /// </summary>
        private bool ChooseConfirmation(CAuthentication_BeginAuthSessionViaCredentials_Response session)
        {
            var types = session.allowed_confirmations.Select(c => c.confirmation_type).ToList();
            var email = session.allowed_confirmations.FirstOrDefault(c => c.confirmation_type == EAuthSessionGuardType.k_EAuthSessionGuardType_EmailCode);
            EmailDomain = email?.associated_message ?? "";

            if (types.Contains(EAuthSessionGuardType.k_EAuthSessionGuardType_None))
            {
                // Nothing to confirm: the first poll has the token
            }
            else if (types.Contains(EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceConfirmation))
            {
                Code = types.Contains(EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceCode) ? CodeKind.App : CodeKind.None;
                State = Step.WaitingForApproval;
            }
            else if (types.Contains(EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceCode))
            {
                Code = CodeKind.App;
                State = Step.WaitingForCode;
            }
            else if (types.Contains(EAuthSessionGuardType.k_EAuthSessionGuardType_EmailCode))
            {
                Code = CodeKind.Email;
                State = Step.WaitingForCode;
            }
            else if (types.Contains(EAuthSessionGuardType.k_EAuthSessionGuardType_EmailConfirmation))
            {
                ApprovalByEmail = true;
                State = Step.WaitingForApproval;
            }
            else
            {
                return false;
            }

            return true;
        }

        private async Task SendCodeAsync(string code)
        {
            State = Step.CheckingCode;
            var job = unified.SendMessage("Authentication.UpdateAuthSessionWithSteamGuardCode#1",
                new CAuthentication_UpdateAuthSessionWithSteamGuardCode_Request
                {
                    client_id = clientId,
                    steamid = steamId,
                    code = code,
                    code_type = Code == CodeKind.Email
                        ? EAuthSessionGuardType.k_EAuthSessionGuardType_EmailCode
                        : EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceCode,
                });
            var response = await WithTimeout(job.ToTask()).ConfigureAwait(false);

            switch (response.Result)
            {
                case EResult.OK:
                case EResult.DuplicateRequest: // the same code again: already accepted
                    return;
                case EResult.TwoFactorCodeMismatch: // app code
                case EResult.InvalidLoginAuthCode: // emailed code
                case EResult.Expired:
                    codeRejected = true;
                    State = Step.WaitingForCode;
                    return;
                default:
                    throw new SteamCallException("UpdateAuthSessionWithSteamGuardCode", response.Result);
            }
        }

        private async Task<TResponse> CallAsync<TRequest, TResponse>(string method, TRequest request)
            where TRequest : ProtoBuf.IExtensible, new()
            where TResponse : ProtoBuf.IExtensible, new()
        {
            var response = await WithTimeout(unified.SendMessage(method, request).ToTask()).ConfigureAwait(false);
            if (response.Result != EResult.OK)
                throw new SteamCallException(method, response.Result);
            return response.GetDeserializedResponse<TResponse>();
        }

        private async Task<T> WithTimeout<T>(Task<T> task)
        {
            if (await Task.WhenAny(task, Task.Delay(CallTimeout, cancel.Token)).ConfigureAwait(false) != task)
            {
                cancel.Token.ThrowIfCancellationRequested();
                throw new SteamCallException("request", EResult.Timeout);
            }

            try
            {
                return await task.ConfigureAwait(false);
            }
            catch (AsyncJobFailedException)
            {
                // SteamKit fails pending requests when the connection drops
                throw new SteamCallException("request", EResult.NoConnection);
            }
            catch (TaskCanceledException)
            {
                // SteamKit's own request timeout
                throw new SteamCallException("request", EResult.Timeout);
            }
        }

        private static string Encrypt(string password, CAuthentication_GetPasswordRSAPublicKey_Response key)
        {
            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = FromHex(key.publickey_mod),
                    Exponent = FromHex(key.publickey_exp),
                });
                return Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(password), RSAEncryptionPadding.Pkcs1));
            }
        }

        private static byte[] FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        private string TakeCode()
        {
            lock (codeLock)
            {
                var code = pendingCode;
                pendingCode = null;
                return code;
            }
        }

        private bool HasCode()
        {
            lock (codeLock)
                return pendingCode != null;
        }

        private void Fail(EResult result, string message)
        {
            FailureResult = result;
            FailureMessage = message;
            State = Step.Failed;
        }

        /// <summary>For the player; Steam answers InvalidPassword for unknown accounts too</summary>
        private static string Describe(EResult result)
        {
            switch (result)
            {
                case EResult.InvalidPassword:
                case EResult.InvalidName:
                case EResult.InvalidParam:
                    return "Wrong account name or password.\n\nUse the name you sign in to Steam with, not your profile name.";
                case EResult.RateLimitExceeded:
                case EResult.AccountLoginDeniedThrottle:
                    return "Too many login attempts.\n\nWait a while, then try again.";
                case EResult.NoConnection:
                case EResult.Timeout:
                case EResult.ServiceUnavailable:
                case EResult.TryAnotherCM:
                    return "Could not reach Steam.\n\nCheck the headset's internet connection and try again.";
                case EResult.FileNotFound:
                case EResult.Expired:
                case EResult.AccessDenied:
                    return "The sign-in was declined or took too long.\n\nTry again.";
                default:
                    return $"Steam refused the login ({result}).";
            }
        }

        private void Pump()
        {
            while (running)
            {
                try
                {
                    callbacks.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
                }
                catch (Exception e)
                {
                    SteamKitBackend.Log($"[Steam] login callback failed: {e}");
                }
            }
        }

        private sealed class SteamCallException : Exception
        {
            public SteamCallException(string method, EResult result)
                : base($"{method}: {result}")
            {
                Method = method;
                Result = result;
            }

            public string Method { get; }
            public EResult Result { get; }
        }
    }
}
