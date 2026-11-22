using System.Runtime.Loader;
using HarmonyBaker;
using HarmonyLib;
using Mono.Cecil;
using RepoQuest.Cloud;
using Steamworks;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class CloudTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-id")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void RejectsMissingOrInvalidAppIds(string value) => Assert.False(CloudAppIds.IsValid(value));

    private static CloudJoinCode Invitation() => new()
    {
        Region = "eu", Room = "room / комната", Build = "v0.5.0", AppVersion = "v0.5.0_2.52",
        RealtimeAppId = "12345678-1234-1234-1234-123456789abc", VoiceAppId = "22345678-1234-1234-1234-123456789abc",
        LobbyId = 12345, OwnerId = 76561198000000001, Capacity = 6,
    };

    [Fact]
    public void InvitationRoundTripsRoutingAndVersionWithoutCredentials()
    {
        var original = Invitation();
        var code = original.Encode();
        Assert.DoesNotContain("=", code);
        var decoded = CloudJoinCode.Decode("  " + code + "\n");
        Assert.Equal(original.Room, decoded.Room);
        Assert.Equal(original.AppVersion, decoded.AppVersion);
        Assert.Equal(original.LobbyId, decoded.LobbyId);
        Assert.Equal(original.OwnerId, decoded.OwnerId);
        Assert.Equal(original.Capacity, decoded.Capacity);
        decoded.CheckApps(original.RealtimeAppId.ToUpperInvariant(), original.VoiceAppId);
        Assert.Throws<InvalidOperationException>(() => decoded.CheckApps(Guid.NewGuid().ToString(), original.VoiceAppId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("RQ2.YQ")]
    [InlineData("RQ1.????")]
    [InlineData("RQ1.YQ")]
    public void RejectsInvalidInvitations(string code) => Assert.ThrowsAny<Exception>(() => CloudJoinCode.Decode(code));

    [Fact]
    public void RejectsOversizedAndInjectedMetadata()
    {
        Assert.Throws<FormatException>(() => CloudJoinCode.Decode("RQ1." + new string('a', 1025)));
        var invitation = Invitation();
        invitation.Room = "room\nother";
        Assert.Throws<FormatException>(() => invitation.Encode());
        invitation = Invitation(); invitation.RealtimeAppId = Guid.Empty.ToString();
        Assert.Throws<FormatException>(() => invitation.Encode());
        invitation = Invitation(); invitation.Capacity = 0;
        Assert.Throws<FormatException>(() => invitation.Encode());
        invitation = Invitation(); invitation.Region = "eu/../../";
        Assert.Throws<FormatException>(() => invitation.Encode());
    }

    [Fact]
    public async Task LocalMetadataFlowsThroughRealShimJoinEventWithoutSteam()
    {
        var invitation = Invitation();
        var backend = new CloudSteamBackend(42, "Quest Two");
        SteamBackend.Current = backend;
        Assert.Null(await SteamMatchmaking.JoinLobbyAsync(invitation.LobbyId));
        backend.Prepare(invitation);
        var entered = 0;
        void OnEntered(Steamworks.Data.Lobby lobby)
        {
            entered++;
            Assert.Equal(invitation.Room, lobby.GetData("RoomName"));
            Assert.Equal(invitation.Build, lobby.GetData("BuildName"));
            Assert.Equal("eu", lobby.GetData("Region"));
            Assert.Equal("0", lobby.GetData("HasPassword"));
            Assert.Equal(invitation.OwnerId, lobby.Owner.Id.Value);
            Assert.False(lobby.IsOwnedBy(42));
            Assert.Equal(6, lobby.MaxMembers);
        }
        SteamMatchmaking.OnLobbyEntered += OnEntered;
        var callbackErrors = new List<Exception>();
        Dispatch.OnException = callbackErrors.Add;
        try
        {
            Assert.Null(await SteamMatchmaking.JoinLobbyAsync(999));
            var lobby = await SteamMatchmaking.JoinLobbyAsync(invitation.LobbyId);
            Assert.NotNull(lobby);
            Assert.Empty(callbackErrors);
            Assert.Equal(1, entered);
            Assert.Equal(2, lobby.Value.MemberCount);
            Assert.Equal("Quest Two", SteamClient.Name);
            Assert.Empty(backend.GetAuthSessionTicket(out var handle));
            Assert.Equal(0u, handle);
            lobby.Value.Leave();
            Assert.Equal(0, lobby.Value.MemberCount);
            backend.Clear();
            Assert.Null(await SteamMatchmaking.JoinLobbyAsync(invitation.LobbyId));
            Assert.Equal("", lobby.Value.GetData("RoomName"));
        }
        finally
        {
            SteamMatchmaking.OnLobbyEntered -= OnEntered;
            Dispatch.OnException = null;
            SteamBackend.Current = new OfflineSteamBackend();
        }
    }

    [Fact]
    public async Task HostLobbyUsesShimEventsAndKeepsMetadataUntilLeave()
    {
        var backend = new CloudSteamBackend(42, "Headset Host");
        SteamBackend.Current = backend;
        var events = new List<string>();
        var errors = new List<Exception>();
        void Created(Result result, Steamworks.Data.Lobby lobby)
        {
            Assert.Equal(Result.OK, result);
            Assert.True(lobby.IsOwnedBy(42));
            Assert.True(lobby.SetData("RoomName", "headset-room"));
            Assert.True(lobby.SetData("Region", "eu"));
            Assert.True(lobby.SetData("HasPassword", "0"));
            events.Add("created");
        }
        void Entered(Steamworks.Data.Lobby lobby)
        {
            Assert.Equal("headset-room", lobby.GetData("RoomName"));
            Assert.Equal(6, lobby.MaxMembers);
            events.Add("entered");
        }
        Dispatch.OnException = errors.Add;
        SteamMatchmaking.OnLobbyCreated += Created;
        SteamMatchmaking.OnLobbyEntered += Entered;
        try
        {
            var lobby = (await SteamMatchmaking.CreateLobbyAsync(6)).Value;
            Assert.Empty(errors);
            Assert.Equal(new[] { "created", "entered" }, events);
            Assert.True(backend.IsHosting);
            Assert.NotEqual(0UL, backend.HostedLobbyId);
            Assert.True(lobby.SetPublic());
            Assert.True(lobby.SetJoinable(true));
            Assert.Equal(1, lobby.MemberCount);
            lobby.MaxMembers = 8;
            Assert.Equal(8, lobby.MaxMembers);
            Assert.False(backend.SetLobbyData(999, "RoomName", "wrong"));
            var previous = backend.HostedLobbyId;
            lobby.Leave();
            Assert.False(backend.IsHosting);
            Assert.Empty(lobby.GetData("RoomName"));
            Assert.Equal(0, lobby.MemberCount);
            Assert.Equal(0UL, lobby.Owner.Id.Value);
            Assert.NotEqual(previous, await backend.CreateLobby(6));
            Assert.Empty(backend.GetLobbyData(backend.HostedLobbyId, "RoomName"));
            backend.Prepare(Invitation());
            Assert.False(backend.IsHosting);
            Assert.False(backend.SetLobbyData(Invitation().LobbyId, "RoomName", "overwrite"));
        }
        finally
        {
            SteamMatchmaking.OnLobbyCreated -= Created;
            SteamMatchmaking.OnLobbyEntered -= Entered;
            Dispatch.OnException = null;
            SteamBackend.Current = new OfflineSteamBackend();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    public async Task HostRejectsInvalidCapacity(int capacity)
    {
        var backend = new CloudSteamBackend(42, "Host");
        Assert.Null(await backend.CreateLobby(capacity));
        Assert.False(backend.IsHosting);
    }

    [Fact]
    public void GuestRoutingRejectsVersionAndRoomMismatch()
    {
        var invitation = Invitation();
        CloudRoomRouting.ValidateGuestJoin(invitation, invitation.AppVersion, invitation.Room);
        Assert.Throws<InvalidOperationException>(() => CloudRoomRouting.ValidateGuestJoin(invitation, "different", invitation.Room));
        Assert.Throws<InvalidOperationException>(() => CloudRoomRouting.ValidateGuestJoin(invitation, invitation.AppVersion, "different"));
    }

    [Fact]
    public void GuestRoutingOnlyInvokesJoinExistingRoom()
    {
        var client = new FakeRoomClient();
        var parameters = new FakeRoomParameters();
        Assert.True(CloudRoomRouting.JoinExisting(client, parameters));
        Assert.Same(parameters, client.LastJoin);
        Assert.Equal(0, client.CreatedRooms);
    }

    [Fact]
    public void PublishedHostIdentityRejectsMigrationDuringInitialConnection()
    {
        var invitation = Invitation();
        CloudRoomRouting.ValidateHostIdentity(invitation, invitation.OwnerId.ToString());
        CloudRoomRouting.ValidateHostIdentity(invitation, null); // Legacy PC host.
        Assert.Throws<InvalidOperationException>(() => CloudRoomRouting.ValidateHostIdentity(invitation, "42"));
        var parameters = new FakeRoomParameters();
        CloudRoomRouting.PublishHostIdentity(parameters);
        Assert.True(parameters.RoomOptions.PublishUserId);
    }

    public sealed class FakeRoomParameters { public FakeRoomOptions RoomOptions = new(); }
    public sealed class FakeRoomOptions { public bool PublishUserId; }
    public sealed class FakeRoomClient
    {
        public FakeRoomParameters LastJoin;
        public int CreatedRooms;
        public bool OpJoinRoom(FakeRoomParameters value) { LastJoin = value; return true; }
        public bool OpJoinOrCreateRoom(FakeRoomParameters value) { CreatedRooms++; return true; }
    }

    [Fact]
    public void AdditionalPatchesComposeIntoOneBakedMethodAndPublishTheirAssembly()
    {
        BakingPatcher.Install();
        var assembly = typeof(CloudTests).Assembly;
        var harmony = new Harmony("test.cloud-bake");
        var selection = new PatchSelection(assembly);
        selection.ApplyAdditionalPatches(harmony, assembly);
        Assert.Empty(selection.Failed);
        Assert.Contains(typeof(FirstPatch).FullName!, selection.Applied);
        Assert.Contains(typeof(SecondPatch).FullName!, selection.Applied);
        var patcher = Assert.Single(BakingPatcher.Instances, p => p.Original.Name == nameof(GameTarget.Compute));
        Assert.True(patcher.Completed);
        var calls = patcher.Latest!.Definition.Body.Instructions.Where(i => i.Operand is MethodReference)
            .Select(i => ((MethodReference)i.Operand).FullName).ToList();
        Assert.Contains(calls, c => c.Contains("FirstPatch::Prefix"));
        Assert.Contains(calls, c => c.Contains("SecondPatch::Postfix"));

        var directory = Path.GetDirectoryName(assembly.Location)!;
        var output = Path.Combine(Path.GetTempPath(), "repoquest-cloud-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var index = new AssemblyIndex(new[] { directory, Path.GetDirectoryName(typeof(object).Assembly.Location)! });
            var baker = new AssemblyBaker(index, directory, output, null, "android", new Dictionary<string, string>());
            // This is an additional assembly, not RepoXR: its runtime patcher must not be stubbed as RepoXR's.
            var results = baker.Write(new[] { patcher }, typeof(GameTarget).Assembly.GetName().Name!, new NativeHooks(Array.Empty<BakingPatcher>()),
                new HashSet<string> { assembly.GetName().Name! });
            Assert.All(results, r => Assert.True(r.Errors.Count == 0, string.Join("\n", r.Errors)));
            Assert.All(results, r => Assert.Empty(r.MissingNetstandardTypes));
            Assert.Single(results, r => r.Name == assembly.GetName().Name);
            var path = Path.Combine(output, Path.GetFileName(assembly.Location));
            using var baked = AssemblyDefinition.ReadAssembly(path);
            Assert.True(baked.MainModule.GetType(typeof(FirstPatch).FullName).IsPublic);
            // Execute the emitted IL in an isolated context, proving both patches survived serialization.
            var context = new AssemblyLoadContext("cloud-bake-test", isCollectible: true);
            try
            {
                context.Resolving += (_, name) => AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
                using var patches = new MemoryStream(File.ReadAllBytes(path));
                context.LoadFromStream(patches);
                using var stream = new MemoryStream(File.ReadAllBytes(Path.Combine(output, "CloudBakeFixture.dll")));
                var loaded = context.LoadFromStream(stream);
                var actual = loaded.GetType(nameof(GameTarget))!.GetMethod(nameof(GameTarget.Compute))!.Invoke(null, new object[] { 5 });
                Assert.Equal(18, actual); // (5 + 1) * 3
            }
            finally { context.Unload(); }
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

}

[HarmonyPatch(typeof(GameTarget), nameof(GameTarget.Compute))]
internal static class FirstPatch
{
    public static void Prefix(ref int value) => value++;
}

[HarmonyPatch(typeof(GameTarget), nameof(GameTarget.Compute))]
internal static class SecondPatch
{
    public static void Postfix(ref int __result) => __result *= 3;
}
