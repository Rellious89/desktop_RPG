using System;
using System.Collections.Generic;
using UnityEngine;

namespace Building
{
    /// <summary>
    /// 건물 목록의 <b>순서와 구성</b>을 소유하는 에셋. <see cref="Inventory.ItemCatalog"/> /
    /// <see cref="Inventory.CurrencyCatalog"/>와 같은 역할이며, 읽는 쪽은 프로젝트를 뒤져 건물을
    /// 모으지 않고(AssetDatabase 탐색도 하지 않는다) 이 에셋 하나만 읽는다.
    ///
    /// <b>담기는 것은 활성 건물뿐이다.</b> Building.csv에서 enabled=1인 행만, display_order 오름차순 →
    /// 같으면 building_id Ordinal 오름차순으로 임포터가 넣는다 - 정렬은 목록을 채우는 임포터의 몫이지
    /// 읽는 쪽의 몫이 아니다.
    ///
    /// <b>걸러내기는 여기서 한 번만 한다.</b> 비어 있는 칸, 식별자가 없는 건물, 앞선 항목과 id가
    /// 겹치는 건물은 목록에서 제외하고 <see cref="Buildings"/>는 <b>남은 항목을 작성 순서 그대로</b>
    /// 돌려준다. 검사 결과는 캐시되며 로그도 그때 한 번만 남는다. id 비교는
    /// <see cref="StringComparer.Ordinal"/>이라 <b>적힌 그대로</b> 본다 - 공백을 떼거나 대소문자를
    /// 맞추는 정규화는 어디서도 하지 않는다. id가 겹칠 때 앞의 것을 남기는 것도 다른 카탈로그와 같은
    /// 이유다 - 나중에 실수로 복제한 항목이 먼저 작성한 건물을 밀어내지 않게 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "BuildingCatalog", menuName = "Building/Building Catalog")]
    public class BuildingCatalog : ScriptableObject
    {
        [Tooltip("건물을 나올 순서대로 넣는다. 비어 있는 칸/식별자가 없는 건물/id가 겹치는 건물은 " +
                 "자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<BuildingDefinition> buildings = new List<BuildingDefinition>();

        /// <summary>검사를 통과한 항목만 작성 순서대로 담아 둔 캐시. 조회할 때마다 새로 만들지 않는다.</summary>
        private readonly List<BuildingDefinition> validBuildings = new List<BuildingDefinition>();

        private bool built;

        /// <summary>쓸 수 있는 건물들을 <b>작성 순서 그대로</b> 돌려준다. 항목이 하나도 없으면 빈
        /// 목록이며 null이 아니다 - 비어 있는 카탈로그도 정상적인 상태로 다룬다.</summary>
        public IReadOnlyList<BuildingDefinition> Buildings
        {
            get
            {
                EnsureBuilt();
                return validBuildings;
            }
        }

        /// <summary>쓸 수 있는 건물 수.</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return validBuildings.Count;
            }
        }

        /// <summary>식별자로 건물을 찾는다. 없으면 null이다 - 목록 크기가 작아 선형 탐색으로 충분하고,
        /// 별도 사전을 두어 캐시 무효화 경로를 하나 더 만들지 않는다. <b>넘어온 문자열을 손대지 않고
        /// 그대로 비교한다</b> - 대소문자를 구분하고 앞뒤 공백도 떼지 않는다.</summary>
        public BuildingDefinition Find(string buildingId)
        {
            if (string.IsNullOrWhiteSpace(buildingId)) return null;

            EnsureBuilt();

            for (int i = 0; i < validBuildings.Count; i++)
            {
                if (string.Equals(validBuildings[i].BuildingId, buildingId, StringComparison.Ordinal))
                {
                    return validBuildings[i];
                }
            }

            return null;
        }

        /// <summary>다음 조회 때 검사를 다시 하도록 표시한다. 에디터에서 목록을 고친 뒤나 임포터가
        /// 목록을 채운 뒤에 쓴다.</summary>
        public void MarkDirty()
        {
            built = false;
        }

        private void OnEnable()
        {
            // 에셋이 로드될 때마다 한 번은 다시 검사한다.
            built = false;
        }

        private void EnsureBuilt()
        {
            if (built) return;
            built = true;

            validBuildings.Clear();
            if (buildings == null) return;

            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingDefinition building = buildings[i];

                if (building == null)
                {
                    Debug.LogWarning($"[BuildingCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                if (!building.IsValid)
                {
                    Debug.LogError($"[BuildingCatalog] '{name}': {i}번 항목('{building.name}')에 Building Id가 " +
                                   "없어 목록에서 제외합니다 - 에셋에서 식별자를 직접 지정하세요.", building);
                    continue;
                }

                if (!seenIds.Add(building.BuildingId))
                {
                    Debug.LogError($"[BuildingCatalog] '{name}': {i}번 항목('{building.name}')의 Building Id " +
                                   $"'{building.BuildingId}'가 앞선 항목과 겹쳐 목록에서 제외합니다 - " +
                                   "먼저 작성된 건물이 남습니다(대소문자는 구분합니다).", building);
                    continue;
                }

                validBuildings.Add(building);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 에디터에서 목록을 고치면 다음 조회 때 검사와 경고가 최신 내용 기준으로 한 번 다시 돈다.
            built = false;
        }
#endif
    }
}
