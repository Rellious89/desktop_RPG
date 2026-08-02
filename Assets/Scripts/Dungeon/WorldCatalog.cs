using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// 월드 목록의 <b>순서와 구성</b>을 소유하는 에셋. <see cref="DungeonCatalog"/>와 같은 역할이며,
    /// 읽는 쪽은 씬이나 프로젝트를 뒤져 월드를 모으지 않고(AssetDatabase 탐색도 하지 않는다) 이 에셋
    /// 하나만 읽는다 - 목록에 무엇이 어떤 순서로 있는지는 데이터에서 정하는 값이지 실행 중에 발견하는
    /// 값이 아니기 때문이다.
    ///
    /// <b>걸러내기는 여기서 한 번만 한다.</b> 비어 있는 칸, 식별자가 없는 월드, 앞선 항목과 id가 겹치는
    /// 월드는 목록에서 제외하고 <see cref="Worlds"/>는 <b>남은 항목을 작성 순서 그대로</b> 돌려준다.
    /// <see cref="WorldDefinition.DisplayOrder"/>로 다시 정렬하지 않는다 - 목록의 순서는 이 에셋의
    /// 작성 순서 하나로만 정해지고, DisplayOrder는 그 순서를 만들어 넣는 쪽(임포터)이 쓰는 값이다.
    ///
    /// 검사 결과는 캐시되며 로그도 그때 한 번만 남는다. id가 겹칠 때 <b>앞의 것을 남기는</b> 것도
    /// <see cref="DungeonCatalog"/>와 같은 이유다 - 나중에 실수로 복제한 항목이 먼저 작성한 월드를
    /// 밀어내지 않게 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "WorldCatalog", menuName = "Dungeon/World Catalog")]
    public class WorldCatalog : ScriptableObject
    {
        [Tooltip("월드를 나올 순서대로 넣는다. 비어 있는 칸/식별자가 없는 월드/id가 겹치는 월드는 " +
                 "자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<WorldDefinition> worlds = new List<WorldDefinition>();

        /// <summary>검사를 통과한 항목만 작성 순서대로 담아 둔 캐시. 조회할 때마다 새로 만들지 않는다.</summary>
        private readonly List<WorldDefinition> validWorlds = new List<WorldDefinition>();

        private bool built;

        /// <summary>쓸 수 있는 월드들을 <b>작성 순서 그대로</b> 돌려준다. 항목이 하나도 없으면 빈
        /// 목록이며 null이 아니다 - 비어 있는 카탈로그도 정상적인 상태로 다룬다.</summary>
        public IReadOnlyList<WorldDefinition> Worlds
        {
            get
            {
                EnsureBuilt();
                return validWorlds;
            }
        }

        /// <summary>쓸 수 있는 월드 수.</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return validWorlds.Count;
            }
        }

        /// <summary>식별자로 월드를 찾는다. 없으면 null이다 - 목록 크기가 작아 선형 탐색으로 충분하고,
        /// 별도 사전을 두어 캐시 무효화 경로를 하나 더 만들지 않는다.</summary>
        public WorldDefinition Find(string worldId)
        {
            if (string.IsNullOrWhiteSpace(worldId)) return null;

            EnsureBuilt();
            string trimmed = worldId.Trim();

            for (int i = 0; i < validWorlds.Count; i++)
            {
                if (validWorlds[i].WorldId == trimmed) return validWorlds[i];
            }

            return null;
        }

        /// <summary>다음 조회 때 검사를 다시 하도록 표시한다. 에디터에서 목록을 고친 뒤나 임포터가
        /// 목록을 채운 뒤에 쓴다 - 여기서 바로 다시 만들지 않는 것은 한 번의 편집으로 검사가 여러 번
        /// 도는 것을 피하기 위함이다.</summary>
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

            validWorlds.Clear();
            if (worlds == null) return;

            var seenIds = new HashSet<string>();

            for (int i = 0; i < worlds.Count; i++)
            {
                WorldDefinition world = worlds[i];

                if (world == null)
                {
                    Debug.LogWarning($"[WorldCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                if (!world.IsValid)
                {
                    Debug.LogError($"[WorldCatalog] '{name}': {i}번 항목('{world.name}')에 World Id가 " +
                                   "없어 목록에서 제외합니다 - 에셋에서 식별자를 직접 지정하세요.", world);
                    continue;
                }

                if (!seenIds.Add(world.WorldId))
                {
                    Debug.LogError($"[WorldCatalog] '{name}': {i}번 항목('{world.name}')의 World Id " +
                                   $"'{world.WorldId}'가 앞선 항목과 겹쳐 목록에서 제외합니다 - " +
                                   "먼저 작성된 월드가 남습니다.", world);
                    continue;
                }

                validWorlds.Add(world);
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
