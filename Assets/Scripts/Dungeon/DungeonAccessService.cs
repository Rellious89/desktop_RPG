using Character;
using Recovery;

namespace Dungeon
{
    public sealed class DungeonAccessService
    {
        private readonly IPartyCharacterLevelSource levelSource;

        public DungeonAccessService(IPartyCharacterLevelSource levelSource)
        {
            this.levelSource = levelSource;
        }

        public DungeonAccessResult Evaluate(DungeonDefinition dungeon)
        {
            if (dungeon == null || !dungeon.IsValid)
                return DungeonAccessResult.Deny(DungeonAccessFailureReason.MissingOrInvalidDungeon);

            if (levelSource == null)
                return DungeonAccessResult.Deny(DungeonAccessFailureReason.MissingRosterOrProgression);

            int highest = levelSource.HighestPartyCharacterLevel;

            if (highest <= 0)
                return DungeonAccessResult.Deny(DungeonAccessFailureReason.NoUsablePartyCharacter,
                    dungeon.RequiredCharacterLevel, 0);

            int required = dungeon.RequiredCharacterLevel;

            if (highest < required)
                return DungeonAccessResult.Deny(DungeonAccessFailureReason.InsufficientLevel,
                    required, highest);

            // 순수 레벨 소스(기존 테스트/외부 사용)는 레벨 정책만 제공한다. 실제 게임 로스터에서는
            // 모닥불과 동일하게 회복소를 제외한 출전 인원 전원의 행동력을 검사한다.
            if (levelSource is CharacterRoster roster)
            {
                int eligibleCount = 0;
                var entries = roster.Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    CharacterDefinition character = entries[i] != null ? entries[i].definition : null;
                    if (character == null || RecoveryService.IsCharacterInRecovery(character)) continue;

                    int requiredStamina = DungeonCombatRules.RequiredStamina(roster.GetMaxStamina(character));
                    if (requiredStamina <= 0) continue;

                    eligibleCount++;
                    int currentStamina = roster.GetStamina(character);
                    if (currentStamina < requiredStamina)
                        return DungeonAccessResult.DenyInsufficientStamina(
                            required, highest, character.CharacterId, requiredStamina, currentStamina);
                }

                if (eligibleCount == 0)
                    return DungeonAccessResult.Deny(DungeonAccessFailureReason.NoUsablePartyCharacter,
                        required, highest);
            }

            return DungeonAccessResult.Allow(required, highest);
        }
    }
}
