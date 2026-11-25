namespace BOW.Net
{
    /// <summary>Wire constants (TDD 8.3, 8.6, 8.10).</summary>
    public static class NetProtocol
    {
        /// <summary>Bumped on any change to the wire format. Checked in the LAN beacon and in Hello.</summary>
        public const ushort Version = 1;

        /// <summary>UDP port the LAN beacon is broadcast on.</summary>
        public const int DiscoveryPort = 47777;

        /// <summary>On a guest, the peer id of the host.</summary>
        public const int HostPeer = 0;

        /// <summary>Default game port.</summary>
        public const int GamePort = 47778;

        /// <summary>Host snapshots go out every this many simulation ticks (60 Hz / 2 = 30 Hz).</summary>
        public const int SnapshotInterval = 2;

        /// <summary>Guests render other players and balls this many ticks in the past (100 ms).</summary>
        public const int InterpolationDelayTicks = 6;

        /// <summary>Each input frame repeats this many recent inputs, so one lost packet loses nothing.</summary>
        public const int RedundantInputs = 3;

        /// <summary>Pauses each guest may call per match (the host has no limit) (TDD 8.8).</summary>
        public const int PauseBudget = 2;

        /// <summary>Seconds a guest's pause lasts before the host resumes automatically.</summary>
        public const float PauseSeconds = 60f;

        /// <summary>Player name limits (TDD 9.2).</summary>
        public const int MinNameLength = 3, MaxNameLength = 16;

        /// <summary>Most messages a session handles per poll, so a flood cannot stall a frame.</summary>
        public const int MaxEventsPerPoll = 256;
    }

    public enum MessageType : byte
    {
        None = 0,
        Hello = 1,
        Welcome = 2,
        Reject = 3,
        LobbyState = 4,
        LobbyRequest = 5,
        StartMatch = 6,
        InputFrame = 7,
        Snapshot = 8,
        GameEvents = 9,
        PauseRequest = 10,
        ResumeRequest = 11,
        Leave = 12,
        MatchEnded = 13,
        ReturnToLobby = 14,
    }

    public enum RejectReason : byte
    {
        None,
        VersionMismatch,
        LobbyFull,
        MatchInProgress,
        WrongCode,
        Kicked,
    }

    public static class RejectReasonText
    {
        public static string Describe(RejectReason reason)
        {
            switch (reason)
            {
                case RejectReason.VersionMismatch: return "Update required: you and the host have different versions.";
                case RejectReason.LobbyFull: return "That match is full.";
                case RejectReason.MatchInProgress: return "That match has already started.";
                case RejectReason.WrongCode: return "Wrong join code.";
                case RejectReason.Kicked: return "The host removed you from the match.";
                default: return "Could not join.";
            }
        }
    }
}
