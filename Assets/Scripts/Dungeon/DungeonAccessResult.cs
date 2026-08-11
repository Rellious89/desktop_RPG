namespace Dungeon
{
    public enum DungeonAccessFailureReason
    {
        None,
        MissingOrInvalidDungeon,
        MissingRosterOrProgression,
        NoUsableOwnedCharacter,
        InsufficientLevel,
    }

    public readonly struct DungeonAccessResult
    {
        public bool Allowed { get; }
        public int DungeonRequiredLevel { get; }
        public int HighestOwnedLevel { get; }
        public DungeonAccessFailureReason FailureReason { get; }

        private DungeonAccessResult(
            bool allowed,
            int dungeonRequiredLevel,
            int highestOwnedLevel,
            DungeonAccessFailureReason failureReason)
        {
            Allowed = allowed;
            DungeonRequiredLevel = dungeonRequiredLevel;
            HighestOwnedLevel = highestOwnedLevel;
            FailureReason = failureReason;
        }

        public static DungeonAccessResult Allow(int dungeonRequiredLevel, int highestOwnedLevel)
        {
            return new DungeonAccessResult(true, dungeonRequiredLevel, highestOwnedLevel, DungeonAccessFailureReason.None);
        }

        public static DungeonAccessResult Deny(DungeonAccessFailureReason reason, int dungeonRequiredLevel = 0, int highestOwnedLevel = 0)
        {
            return new DungeonAccessResult(false, dungeonRequiredLevel, highestOwnedLevel, reason);
        }
    }
}
