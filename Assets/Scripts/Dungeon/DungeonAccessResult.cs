namespace Dungeon
{
    public enum DungeonAccessFailureReason
    {
        None,
        MissingOrInvalidDungeon,
        MissingRosterOrProgression,
        NoUsablePartyCharacter,
        InsufficientLevel,
        InsufficientStamina,
    }

    public readonly struct DungeonAccessResult
    {
        public bool Allowed { get; }
        public int DungeonRequiredLevel { get; }
        public int HighestPartyLevel { get; }
        public DungeonAccessFailureReason FailureReason { get; }
        public string InsufficientStaminaCharacterId { get; }
        public int RequiredStamina { get; }
        public int CurrentStamina { get; }

        private DungeonAccessResult(
            bool allowed,
            int dungeonRequiredLevel,
            int highestPartyLevel,
            DungeonAccessFailureReason failureReason,
            string insufficientStaminaCharacterId,
            int requiredStamina,
            int currentStamina)
        {
            Allowed = allowed;
            DungeonRequiredLevel = dungeonRequiredLevel;
            HighestPartyLevel = highestPartyLevel;
            FailureReason = failureReason;
            InsufficientStaminaCharacterId = insufficientStaminaCharacterId;
            RequiredStamina = requiredStamina;
            CurrentStamina = currentStamina;
        }

        public static DungeonAccessResult Allow(int dungeonRequiredLevel, int highestPartyLevel)
        {
            return new DungeonAccessResult(true, dungeonRequiredLevel, highestPartyLevel,
                DungeonAccessFailureReason.None, null, 0, 0);
        }

        public static DungeonAccessResult Deny(DungeonAccessFailureReason reason, int dungeonRequiredLevel = 0, int highestPartyLevel = 0)
        {
            return new DungeonAccessResult(false, dungeonRequiredLevel, highestPartyLevel, reason, null, 0, 0);
        }

        public static DungeonAccessResult DenyInsufficientStamina(
            int dungeonRequiredLevel, int highestPartyLevel, string characterId,
            int requiredStamina, int currentStamina)
        {
            return new DungeonAccessResult(false, dungeonRequiredLevel, highestPartyLevel,
                DungeonAccessFailureReason.InsufficientStamina, characterId, requiredStamina, currentStamina);
        }
    }
}
