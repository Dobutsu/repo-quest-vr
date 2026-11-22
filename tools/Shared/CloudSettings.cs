using System;
using RepoQuest.Cloud;

namespace RepoXR.Standalone
{
    [Serializable]
    public sealed class CloudSettings
    {
        private const int CurrentSchemaVersion = 2;

        public bool enabled = true;
        public int modeSchemaVersion;
        // Read this field from older builds to distinguish user-entered IDs from bundled defaults.
        public bool useCustomAppIds;
        public string realtimeAppId = "";
        public string voiceAppId = "";
        public string playerName = "Quest Player";
        public string cloudPlayerId = "";
        public string websiteUrl = "";

        // Editing a draft must not change live settings until the complete file is safely written.
        public CloudSettings Copy() => (CloudSettings)MemberwiseClone();

        // The marker is independent of the release version: a later manual Steam choice must survive updates.
        public bool Migrate()
        {
            if (modeSchemaVersion >= CurrentSchemaVersion) return false;
            if (modeSchemaVersion < 1) enabled = true;
            if (!useCustomAppIds)
            {
                realtimeAppId = "";
                voiceAppId = "";
            }
            modeSchemaVersion = CurrentSchemaVersion;
            return true;
        }

        public bool HasPhotonAppIds => CloudAppIds.IsValid(realtimeAppId) && CloudAppIds.IsValid(voiceAppId);

        public CloudSettings PrepareSave()
        {
            var draft = Copy();
            draft.modeSchemaVersion = CurrentSchemaVersion;
            if (!draft.enabled) return draft;
            if (!string.IsNullOrEmpty(draft.cloudPlayerId) && !CloudProfile.IsLocalId(draft.cloudPlayerId))
                throw new FormatException("Invalid Cloud ID. Generate a new ID in Cloud settings.");
            if (!draft.HasPhotonAppIds)
                throw new FormatException("Enter valid Realtime and Voice App IDs from your Photon dashboard.");
            if (string.IsNullOrWhiteSpace(draft.playerName) || draft.playerName.Length > 32 ||
                draft.playerName.IndexOfAny(new[] { '<', '>', '\n', '\r' }) >= 0)
                throw new FormatException("Use a player name of 1-32 characters without markup or line breaks.");
            draft.realtimeAppId = Guid.Parse(draft.realtimeAppId).ToString("D");
            draft.voiceAppId = Guid.Parse(draft.voiceAppId).ToString("D");
            draft.websiteUrl = CloudWebsite.Normalize(draft.websiteUrl);
            return draft;
        }

        public bool MatchesRuntime(CloudSettings other) => other != null && enabled == other.enabled &&
            (!enabled || (string.Equals(realtimeAppId, other.realtimeAppId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(voiceAppId, other.voiceAppId, StringComparison.OrdinalIgnoreCase) &&
            playerName == other.playerName && websiteUrl == other.websiteUrl &&
            (cloudPlayerId ?? "") == (other.cloudPlayerId ?? "")));
    }
}
