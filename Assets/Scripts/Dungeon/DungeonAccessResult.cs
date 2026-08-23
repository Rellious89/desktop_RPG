namespace Dungeon
{
    public enum DungeonAccessFailureReason
    {
        None,
        MissingOrInvalidDungeon,
        MissingRosterOrProgression,
        NoUsablePartyCharacter,
        InsufficientLevel,
    }

    public readonly struct DungeonAccessResult
    {
        public bool Allowed { get; }
        public int DungeonRequiredLevel { get; }
        public int HighestPartyLevel { get; }
        public DungeonAccessFailureReason FailureReason { get; }

        private DungeonAccessResult(
            bool allowed,
            int dungeonRequiredLevel,
            int highestPartyLevel,
            DungeonAccessFailureReason failureReason)
        {
            Allowed = allowed;
            DungeonRequiredLevel = dungeonRequiredLevel;
            HighestPartyLevel = highestPartyLevel;
            FailureReason = failureReason;
        }

        public static DungeonAccessResult Allow(int dungeonRequiredLevel, int highestPartyLevel)
        {
            return new DungeonAccessResult(true, dungeonRequiredLevel, highestPartyLevel, DungeonAccessFailureReason.None);
        }

        public static DungeonAccessResult Deny(DungeonAccessFailureReason reason, int dungeonRequiredLevel = 0, int highestPartyLevel = 0)
        {
            return new DungeonAccessResult(false, dungeonRequiredLevel, highestPartyLevel, reason);
        }
    }
}
