using Character;

namespace Dungeon
{
    public sealed class DungeonAccessService
    {
        private readonly IOwnedCharacterLevelSource levelSource;

        public DungeonAccessService(IOwnedCharacterLevelSource levelSource)
        {
            this.levelSource = levelSource;
        }

        public DungeonAccessResult Evaluate(DungeonDefinition dungeon)
        {
            if (dungeon == null || !dungeon.IsValid)
                return DungeonAccessResult.Deny(DungeonAccessFailureReason.MissingOrInvalidDungeon);

            if (levelSource == null)
                return DungeonAccessResult.Deny(DungeonAccessFailureReason.MissingRosterOrProgression);

            int highest = levelSource.HighestOwnedCharacterLevel;

            if (highest <= 0)
                return DungeonAccessResult.Deny(DungeonAccessFailureReason.NoUsableOwnedCharacter,
                    dungeon.RequiredCharacterLevel, 0);

            int required = dungeon.RequiredCharacterLevel;

            if (highest < required)
                return DungeonAccessResult.Deny(DungeonAccessFailureReason.InsufficientLevel,
                    required, highest);

            return DungeonAccessResult.Allow(required, highest);
        }
    }
}
