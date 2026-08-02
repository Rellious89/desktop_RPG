using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// 몬스터 목록의 <b>순서와 구성</b>을 소유하는 에셋. <see cref="DungeonCatalog"/> /
    /// <see cref="WorldCatalog"/>와 같은 역할이며, 읽는 쪽은 프로젝트를 뒤져 몬스터를 모으지 않고
    /// 이 에셋 하나만 읽는다.
    ///
    /// <b>이 목록이 던전의 등장 몬스터를 정하지는 않는다.</b> 어떤 던전에 무엇이 나오는지는
    /// <see cref="DungeonDefinition.Monsters"/>가 소유하고, 여기는 "프로젝트에 어떤 몬스터가 있는가"를
    /// 모아 두는 자리다 - 그래서 이 카탈로그를 지나지 않는 몬스터도 던전에 직접 연결될 수 있다.
    ///
    /// <b>걸러내기는 여기서 한 번만 한다.</b> 비어 있는 칸, 식별자가 없는 몬스터, 앞선 항목과 id가
    /// 겹치는 몬스터는 목록에서 제외하고 <see cref="Monsters"/>는 <b>남은 항목을 작성 순서 그대로</b>
    /// 돌려준다. 검사 결과는 캐시되며 로그도 그때 한 번만 남는다.
    /// </summary>
    [CreateAssetMenu(fileName = "MonsterCatalog", menuName = "Dungeon/Monster Catalog")]
    public class MonsterCatalog : ScriptableObject
    {
        [Tooltip("몬스터를 나올 순서대로 넣는다. 비어 있는 칸/식별자가 없는 몬스터/id가 겹치는 몬스터는 " +
                 "자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<MonsterDefinition> monsters = new List<MonsterDefinition>();

        /// <summary>검사를 통과한 항목만 작성 순서대로 담아 둔 캐시. 조회할 때마다 새로 만들지 않는다.</summary>
        private readonly List<MonsterDefinition> validMonsters = new List<MonsterDefinition>();

        private bool built;

        /// <summary>쓸 수 있는 몬스터들을 <b>작성 순서 그대로</b> 돌려준다. 항목이 하나도 없으면 빈
        /// 목록이며 null이 아니다.</summary>
        public IReadOnlyList<MonsterDefinition> Monsters
        {
            get
            {
                EnsureBuilt();
                return validMonsters;
            }
        }

        /// <summary>쓸 수 있는 몬스터 수.</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return validMonsters.Count;
            }
        }

        /// <summary>식별자로 몬스터를 찾는다. 없으면 null이다 - 목록 크기가 작아 선형 탐색으로 충분하고,
        /// 별도 사전을 두어 캐시 무효화 경로를 하나 더 만들지 않는다.</summary>
        public MonsterDefinition Find(string monsterId)
        {
            if (string.IsNullOrWhiteSpace(monsterId)) return null;

            EnsureBuilt();
            string trimmed = monsterId.Trim();

            for (int i = 0; i < validMonsters.Count; i++)
            {
                if (validMonsters[i].MonsterId == trimmed) return validMonsters[i];
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

            validMonsters.Clear();
            if (monsters == null) return;

            var seenIds = new HashSet<string>();

            for (int i = 0; i < monsters.Count; i++)
            {
                MonsterDefinition monster = monsters[i];

                if (monster == null)
                {
                    Debug.LogWarning($"[MonsterCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                if (!monster.IsValid)
                {
                    Debug.LogError($"[MonsterCatalog] '{name}': {i}번 항목('{monster.name}')에 Monster Id가 " +
                                   "없어 목록에서 제외합니다 - 에셋에서 식별자를 직접 지정하세요.", monster);
                    continue;
                }

                if (!seenIds.Add(monster.MonsterId))
                {
                    Debug.LogError($"[MonsterCatalog] '{name}': {i}번 항목('{monster.name}')의 Monster Id " +
                                   $"'{monster.MonsterId}'가 앞선 항목과 겹쳐 목록에서 제외합니다 - " +
                                   "먼저 작성된 몬스터가 남습니다.", monster);
                    continue;
                }

                validMonsters.Add(monster);
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
