using RepoQuest.Cloud;
using Xunit;

public sealed class HostedInvitationTests
{
    private static readonly Dictionary<string, string> Metadata = new()
    {
        ["HasPassword"] = "0", ["RoomName"] = "waiting-room", ["Region"] = "eu", ["BuildName"] = "0.5.0",
    };

    [Fact]
    public void StartingGameStopsExportUntilWaitingRoomOpensAgain()
    {
        Photon.Pun.PhotonNetwork.InRoom = true;
        Photon.Pun.PhotonNetwork.IsMasterClient = true;
        var room = Photon.Pun.PhotonNetwork.CurrentRoom = new TestPhotonRoom();
        Assert.Equal("waiting-room", CloudRoomRouting.HostedInvitation(10, 20, key => Metadata[key]).Room);
        // The native lobby Start action closes the Photon room before changing scenes.
        room.IsOpen = false;
        Assert.Throws<InvalidOperationException>(() => CloudRoomRouting.HostedInvitation(10, 20,
            _ => throw new Exception("A started room must not export metadata")));
        room.IsOpen = true;
        Assert.Equal("waiting-room", CloudRoomRouting.HostedInvitation(10, 20, key => Metadata[key]).Room);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void DisconnectedPlayerOrGuestCannotExportHostInvitation(bool inRoom, bool master)
    {
        Photon.Pun.PhotonNetwork.InRoom = inRoom;
        Photon.Pun.PhotonNetwork.IsMasterClient = master;
        Assert.Throws<InvalidOperationException>(() => CloudRoomRouting.HostedInvitation(10, 20, key => Metadata[key]));
    }

    public sealed class TestPhotonRoom
    {
        public bool IsOpen = true;
        public string Name = "waiting-room";
        public byte MaxPlayers = 6;
    }

    public sealed class TestPhotonClient { public string AppVersion = "0.5.0"; }
    public sealed class TestPhotonSettings
    {
        public string AppIdRealtime = Guid.NewGuid().ToString("D");
        public string AppIdVoice = Guid.NewGuid().ToString("D");
    }
    public sealed class TestPhotonServerSettings { public TestPhotonSettings AppSettings = new(); }
}

namespace Photon.Pun
{
    // PhotonAccess uses the same reflection path as the PC/Android runtime.
    public static class PhotonNetwork
    {
        public static bool InRoom = true;
        public static bool IsMasterClient = true;
        public static HostedInvitationTests.TestPhotonRoom CurrentRoom = new();
        public static HostedInvitationTests.TestPhotonClient NetworkingClient = new();
        public static HostedInvitationTests.TestPhotonServerSettings PhotonServerSettings = new();
    }
}
