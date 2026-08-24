using System;
using System.Collections.Generic;

namespace Common
{
    /// <summary>
    /// 어떤 경로로 만들어졌든 저장 문서를 <b>호출부가 null을 따지지 않아도 되는 모양</b>으로 맞춘다.
    /// 새 게임, 그대로 불러온 문서, 변환을 거친 문서 모두 마지막에 여기를 지난다 - 경로마다 따로
    /// 보정하면 언젠가 한 경로만 빠지고, 그 경로로만 터지는 NullReference가 남는다.
    ///
    /// <b>정규화는 값을 바꾸지 않는다.</b> 없는 것을 채우고(빈 목록, 최소 슬롯), 있을 수 없는 것을
    /// 치울 뿐이다(목록 안의 null). 값의 의미를 바꾸는 일은 마이그레이션 단계의 몫이며, 그래야 어떤
    /// 버전에서 무엇이 바뀌었는지가 <see cref="SaveMigrationRunner"/> 한 곳에만 남는다.
    ///
    /// 여러 번 불러도 결과가 같다(멱등) - 불러오기 도중 여러 번 지나도 안전하다.
    /// </summary>
    public static class SaveDataNormalizer
    {
        /// <summary>
        /// 문서를 제자리에서 정규화하고 그 문서를 돌려준다. <paramref name="data"/>가 null이면 기본값
        /// 문서를 새로 만들어 준다 - 반환값은 <b>항상</b> null이 아니다.
        /// </summary>
        public static SaveData Normalize(SaveData data)
        {
            if (data == null) data = new SaveData();

            // 캐릭터와 아이템은 목록 안의 항목을 id로 찾으므로, null 항목은 아무것도 가리키지 않는
            // 쓰레기다. 지워도 나머지 항목의 상대 순서는 그대로라 아이템의 획득 순서가 흐트러지지 않는다.
            data.characters = CompactCharacters(data.characters);
            foreach (CharacterSaveState state in data.characters)
            {
                if (double.IsNaN(state.currentCorruption) || double.IsInfinity(state.currentCorruption)
                    || state.currentCorruption < 0d) state.currentCorruption = 0d;
            }
            data.partyCharacterIds = CompactPartyCharacterIds(data.partyCharacterIds, data.characters);
            data.items = CompactItems(data.items);

            // 건설 기록도 buildingId로 찾으므로 null 항목은 아무것도 가리키지 않는 쓰레기다. 지우는
            // 것은 <b>null 항목뿐</b>이다 - 모르는 buildingId(표에서 잠시 빠진 건물)도, 같은 id가 두 줄
            // 있는 손상된 파일도 그대로 둔다. 여기서 걸러 내면 다시 들어온 건물을 또 짓게 되고, 순서를
            // 바꾸면 "먼저 시작한 순서"라는 목록의 뜻이 흔들린다.
            data.buildingConstructions = CompactBuildingConstructions(data.buildingConstructions);

            // 모집 주기도 null 목록과 null 항목만 정리한다. 모르는 Access Id, 중복 키, 손상된 시각은
            // 원본 그대로 남겨 두어 서비스가 Unreadable로 판정하고 이후 데이터 복구 가능성을 보존한다.
            data.recruitmentCycles = CompactRecruitmentCycles(data.recruitmentCycles);

            // 회복 슬롯만 규칙이 다르다. 여기서는 <b>목록의 인덱스가 곧 슬롯 번호</b>라서 null을 지우면
            // 뒤 슬롯들이 앞으로 밀려 남의 진행이 다른 슬롯으로 옮겨간다. 그래서 지우지 않고 빈 슬롯으로
            // 갈아 끼우며, 그 처리와 최소 개수 채우기는 SaveData가 이미 갖고 있는 규칙을 그대로 쓴다.
            SaveData.EnsureRecoverySlots(data);
            SaveData.EnsurePurificationSlots(data);
            var seenPurificationCharacters = new HashSet<string>(StringComparer.Ordinal);
            foreach (PurificationSlotSaveState slot in data.purificationSlots)
            {
                if (string.IsNullOrEmpty(slot.characterId))
                {
                    slot.Clear();
                    continue;
                }

                if (slot.progressTicks < 0) slot.progressTicks = 0;
                if (!seenPurificationCharacters.Add(slot.characterId)) slot.Clear();
            }

            // 저장 일련번호는 커지기만 하는 값이다. 음수는 우리가 쓸 수 없는 값이므로 "모름"인 0으로 되돌린다.
            // 시각 문자열은 손대지 않는다 - 읽을 수 없는 값이라도 지우는 것보다 남겨 두는 쪽이 낫고,
            // 이 값으로 무엇을 판단하는 코드는 어차피 파싱 실패를 "모름"으로 다뤄야 한다.
            if (data.saveRevision < 0) data.saveRevision = 0;

            return data;
        }

        private static List<CharacterSaveState> CompactCharacters(List<CharacterSaveState> source)
        {
            if (source == null) return new List<CharacterSaveState>();

            for (int i = source.Count - 1; i >= 0; i--)
            {
                if (source[i] == null) source.RemoveAt(i);
            }

            return source;
        }

        private static List<string> CompactPartyCharacterIds(
            List<string> source, List<CharacterSaveState> characters)
        {
            if (source == null) return new List<string>();

            var owned = new HashSet<string>(StringComparer.Ordinal);
            if (characters != null)
            {
                foreach (CharacterSaveState state in characters)
                {
                    if (state != null && !string.IsNullOrEmpty(state.characterId)) owned.Add(state.characterId);
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Count; i++)
            {
                string id = source[i];
                if (string.IsNullOrEmpty(id) || !owned.Contains(id) || !seen.Add(id)) source[i] = string.Empty;
            }
            return source;
        }

        private static List<InventoryItemState> CompactItems(List<InventoryItemState> source)
        {
            if (source == null) return new List<InventoryItemState>();

            for (int i = source.Count - 1; i >= 0; i--)
            {
                if (source[i] == null) source.RemoveAt(i);
            }

            return source;
        }

        private static List<BuildingConstructionSaveState> CompactBuildingConstructions(
            List<BuildingConstructionSaveState> source)
        {
            if (source == null) return new List<BuildingConstructionSaveState>();

            for (int i = source.Count - 1; i >= 0; i--)
            {
                if (source[i] == null) source.RemoveAt(i);
            }

            return source;
        }

        private static List<RecruitmentCycleSaveState> CompactRecruitmentCycles(
            List<RecruitmentCycleSaveState> source)
        {
            if (source == null) return new List<RecruitmentCycleSaveState>();

            for (int i = source.Count - 1; i >= 0; i--)
            {
                if (source[i] == null) source.RemoveAt(i);
            }

            return source;
        }
    }
}
