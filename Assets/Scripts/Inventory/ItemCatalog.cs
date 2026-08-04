using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// 아이템 목록의 <b>순서와 구성</b>을 소유하는 에셋. <see cref="Dungeon.WorldCatalog"/> /
    /// <see cref="Dungeon.MonsterCatalog"/> / <see cref="Dungeon.DungeonCatalog"/>와 같은 역할이며,
    /// 읽는 쪽은 프로젝트를 뒤져 아이템을 모으지 않고(AssetDatabase 탐색도 하지 않는다) 이 에셋
    /// 하나만 읽는다.
    ///
    /// <b>이 목록이 "지금 갖고 있는 아이템"은 아니다.</b> 보유 수량은 저장 데이터(SaveData.items)가
    /// 소유하고, 여기는 "이 게임에 어떤 아이템이 있는가"를 모아 두는 자리다.
    ///
    /// <b>걸러내기는 여기서 한 번만 한다.</b> 비어 있는 칸, 식별자가 없는 아이템, 앞선 항목과 id가
    /// 겹치는 아이템은 목록에서 제외하고 <see cref="Items"/>는 <b>남은 항목을 작성 순서 그대로</b>
    /// 돌려준다. 검사 결과는 캐시되며 로그도 그때 한 번만 남는다. id가 겹칠 때 앞의 것을 남기는 것도
    /// 다른 카탈로그와 같은 이유다 - 나중에 실수로 복제한 항목이 먼저 작성한 아이템을 밀어내지 않게 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemCatalog", menuName = "Inventory/Item Catalog")]
    public class ItemCatalog : ScriptableObject
    {
        [Tooltip("아이템을 나올 순서대로 넣는다. 비어 있는 칸/식별자가 없는 아이템/id가 겹치는 아이템은 " +
                 "자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<ItemDefinition> items = new List<ItemDefinition>();

        /// <summary>검사를 통과한 항목만 작성 순서대로 담아 둔 캐시. 조회할 때마다 새로 만들지 않는다.</summary>
        private readonly List<ItemDefinition> validItems = new List<ItemDefinition>();

        private bool built;

        /// <summary>쓸 수 있는 아이템들을 <b>작성 순서 그대로</b> 돌려준다. 항목이 하나도 없으면 빈
        /// 목록이며 null이 아니다 - 비어 있는 카탈로그도 정상적인 상태로 다룬다.</summary>
        public IReadOnlyList<ItemDefinition> Items
        {
            get
            {
                EnsureBuilt();
                return validItems;
            }
        }

        /// <summary>쓸 수 있는 아이템 수.</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return validItems.Count;
            }
        }

        /// <summary>식별자로 아이템을 찾는다. 없으면 null이다 - 목록 크기가 작아 선형 탐색으로 충분하고,
        /// 별도 사전을 두어 캐시 무효화 경로를 하나 더 만들지 않는다.</summary>
        public ItemDefinition Find(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return null;

            EnsureBuilt();
            string trimmed = itemId.Trim();

            for (int i = 0; i < validItems.Count; i++)
            {
                if (validItems[i].ItemId == trimmed) return validItems[i];
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

            validItems.Clear();
            if (items == null) return;

            var seenIds = new HashSet<string>();

            for (int i = 0; i < items.Count; i++)
            {
                ItemDefinition item = items[i];

                if (item == null)
                {
                    Debug.LogWarning($"[ItemCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                if (!item.IsValid)
                {
                    Debug.LogError($"[ItemCatalog] '{name}': {i}번 항목('{item.name}')에 Item Id가 " +
                                   "없어 목록에서 제외합니다 - 에셋에서 식별자를 직접 지정하세요.", item);
                    continue;
                }

                if (!seenIds.Add(item.ItemId))
                {
                    Debug.LogError($"[ItemCatalog] '{name}': {i}번 항목('{item.name}')의 Item Id " +
                                   $"'{item.ItemId}'가 앞선 항목과 겹쳐 목록에서 제외합니다 - " +
                                   "먼저 작성된 아이템이 남습니다.", item);
                    continue;
                }

                validItems.Add(item);
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
