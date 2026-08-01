using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// 던전 목록의 <b>순서와 구성</b>을 소유하는 에셋. 패널은 씬을 뒤져 던전을 모으지 않고
    /// (FindObjectsOfType 같은 탐색은 쓰지 않는다) 이 에셋 하나만 읽는다 - 목록에 무엇이 어떤 순서로
    /// 나오는지는 에디터에서 정하는 값이지 실행 중에 발견하는 값이 아니기 때문이다.
    ///
    /// <b>걸러내기는 여기서 한 번만 한다.</b> 비어 있는 칸, 식별자가 없는 던전, 앞선 항목과 id가 겹치는
    /// 던전은 목록에서 제외하고 <see cref="Dungeons"/>는 <b>남은 항목을 작성 순서 그대로</b> 돌려준다.
    /// 검사 결과는 캐시되며 로그도 그때 한 번만 남는다 - 목록을 그릴 때마다(하물며 매 프레임) 같은
    /// 경고가 반복되지 않게 하기 위함이다. 에셋이 다시 로드되거나 에디터에서 값이 바뀌면 다음 조회 때
    /// 한 번 더 검사한다.
    ///
    /// id가 겹칠 때 <b>앞의 것을 남기는</b> 것은 의도적이다 - 목록 순서가 곧 표시 순서이므로, 나중에
    /// 실수로 복제한 항목이 먼저 작성한 던전을 밀어내지 않게 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "DungeonCatalog", menuName = "Dungeon/Dungeon Catalog")]
    public class DungeonCatalog : ScriptableObject
    {
        [Tooltip("표시할 던전을 나올 순서대로 넣는다. 비어 있는 칸/식별자가 없는 던전/id가 겹치는 " +
                 "던전은 자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<DungeonDefinition> dungeons = new List<DungeonDefinition>();

        /// <summary>검사를 통과한 항목만 작성 순서대로 담아 둔 캐시. 조회할 때마다 새로 만들지 않는다.</summary>
        private readonly List<DungeonDefinition> validDungeons = new List<DungeonDefinition>();

        private bool built;

        /// <summary>목록에 올릴 수 있는 던전들을 <b>작성 순서 그대로</b> 돌려준다. 항목이 하나도 없으면
        /// 빈 목록이며 null이 아니다 - 비어 있는 카탈로그도 정상적인 상태로 다룬다(패널이 상세를 감춘다).</summary>
        public IReadOnlyList<DungeonDefinition> Dungeons
        {
            get
            {
                EnsureBuilt();
                return validDungeons;
            }
        }

        /// <summary>표시 가능한 던전 수.</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return validDungeons.Count;
            }
        }

        /// <summary>다음 조회 때 검사를 다시 하도록 표시한다. 에디터에서 목록을 고친 뒤나, 테스트에서
        /// 내용을 바꾼 뒤에 쓴다 - 여기서 바로 다시 만들지 않는 것은 한 번의 편집으로 검사가 여러 번
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

            validDungeons.Clear();
            if (dungeons == null) return;

            var seenIds = new HashSet<string>();

            for (int i = 0; i < dungeons.Count; i++)
            {
                DungeonDefinition dungeon = dungeons[i];

                if (dungeon == null)
                {
                    Debug.LogWarning($"[DungeonCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                if (!dungeon.IsValid)
                {
                    Debug.LogError($"[DungeonCatalog] '{name}': {i}번 항목('{dungeon.name}')에 Dungeon Id가 " +
                                   "없어 목록에서 제외합니다 - 에셋에서 식별자를 직접 지정하세요.", dungeon);
                    continue;
                }

                if (!seenIds.Add(dungeon.DungeonId))
                {
                    Debug.LogError($"[DungeonCatalog] '{name}': {i}번 항목('{dungeon.name}')의 Dungeon Id " +
                                   $"'{dungeon.DungeonId}'가 앞선 항목과 겹쳐 목록에서 제외합니다 - " +
                                   "먼저 작성된 던전이 남습니다.", dungeon);
                    continue;
                }

                validDungeons.Add(dungeon);
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
