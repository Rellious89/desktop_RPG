using System;
using System.Collections.Generic;
using Common;

namespace Character
{
    /// <summary>
    /// "이 게임에 어떤 캐릭터가 있는가"(<see cref="CharacterCatalog"/>)와 "이 플레이어가 무엇을
    /// 가지고 있는가"(<see cref="SaveData.characters"/>)를 <b>겹쳐 보는</b> 자리.
    ///
    /// 두 목록의 관계는 v2에서 이미 정해져 있다 - 저장 목록의 항목 하나가 곧 그 characterId의
    /// <b>보유</b>이며 동시에 그 캐릭터의 레벨/행동력이다. 이 클래스는 그 규칙을 <b>읽기만</b> 하는
    /// 얇은 층이고, 규칙을 새로 만들지 않는다.
    ///
    /// <b>저장소를 모른다.</b> 파일 경로도 persistentDataPath도 <see cref="SaveSystem"/>도 보지 않고,
    /// 생성될 때 받은 문서 하나만 다룬다 - 그래야 시험이 디스크 없이 전부 돌아가고, 씬 없이도 규칙을
    /// 확인할 수 있다. 저장은 이 클래스의 일이 아니다(부르는 쪽이 정한다).
    ///
    /// <b>읽는 동작은 문서를 절대 바꾸지 않는다.</b> 목록을 훑든 <see cref="IsOwned(string)"/>를 묻든
    /// <see cref="TryGetState(string, out CharacterSaveState)"/>로 상태를 얻든, 저장 목록의 개수도
    /// 내용도 달라지지 않는다. 항목을 <b>더하는</b> 경로는 <see cref="InitializeNewGame"/> 하나뿐이며,
    /// 항목을 지우는 경로는 아예 없다.
    ///
    /// 세 가지 어긋남을 어떻게 다루는지가 이 클래스의 전부다.
    /// <list type="bullet">
    ///   <item><b>카탈로그에 있고 저장에 없다</b> → 미보유. 목록에 나오지 않는다.</item>
    ///   <item><b>저장에 있고 카탈로그에 없다</b> → 값은 <b>그대로 둔다</b>(지우지 않는다). 다만 지금
    ///         빌드에는 그 정의가 없으므로 보유 목록에는 나오지 않는다.</item>
    ///   <item><b>카탈로그에서 빠진 비활성 캐릭터</b> → 생성 카탈로그가 애초에 활성 행만 담으므로
    ///         여기서 따로 거를 것이 없다.</item>
    /// </list>
    /// </summary>
    public sealed class OwnedCharacterCollection
    {
        private static readonly IReadOnlyList<CharacterDefinition> Empty = new CharacterDefinition[0];

        private readonly CharacterCatalog catalog;
        private readonly SaveData document;

        /// <param name="catalog">활성 캐릭터 전체. null이면 "이 게임에 캐릭터가 없다"로 다룬다.</param>
        /// <param name="document">보유와 진행을 담은 저장 문서. null이면 "아무것도 가지지 않았다"로
        /// 다룬다 - 어느 쪽도 예외로 만들지 않는 이유는, 씬 구성이 덜 된 상태에서 이 클래스가 터지는
        /// 것보다 "아무도 없다"가 훨씬 다루기 쉬운 답이기 때문이다.</param>
        public OwnedCharacterCollection(CharacterCatalog catalog, SaveData document)
        {
            this.catalog = catalog;
            this.document = document;
        }

        /// <summary>이 클래스가 보고 있는 저장 문서. 부르는 쪽이 저장 시점을 정할 때 쓴다.</summary>
        public SaveData Document => document;

        /// <summary>카탈로그가 담고 있는 <b>활성 캐릭터 전체</b>. 보유 여부와 무관하다.</summary>
        public IReadOnlyList<CharacterDefinition> AllCharacters =>
            catalog != null ? catalog.Characters : Empty;

        /// <summary>
        /// 지금 보유한 캐릭터를 <b>카탈로그 순서 그대로</b>. 저장 목록의 순서가 아니라 카탈로그의
        /// 순서인 이유는 화면에 보이는 차례가 표의 display_order여야 하기 때문이며, 카탈로그를 훑기
        /// 때문에 저장 목록에 같은 id가 두 번 있어도 <b>한 번만</b> 나온다.
        ///
        /// <b>호출할 때마다 새 목록을 만들어 돌려준다.</b> 재사용 버퍼를 돌려주면 그 다음 호출이
        /// <b>이미 남에게 건넨 목록</b>을 비우고 다시 채우게 되어, 호출부가 들고 있던 결과가 등 뒤에서
        /// 바뀐다 - 목록을 한 번 받아 두고 나중에 훑는 코드(UI 갱신 등)가 그때 다른 답을 보게 된다.
        /// 여섯 남짓한 목록이라 매번 만드는 비용보다 그 사고를 막는 쪽이 훨씬 싸다.
        /// </summary>
        public IReadOnlyList<CharacterDefinition> OwnedCharacters
        {
            get
            {
                var snapshot = new List<CharacterDefinition>();

                IReadOnlyList<CharacterDefinition> all = AllCharacters;
                for (int i = 0; i < all.Count; i++)
                {
                    CharacterDefinition definition = all[i];
                    if (definition == null) continue;
                    if (IsOwned(definition.CharacterId)) snapshot.Add(definition);
                }

                return snapshot;
            }
        }

        /// <summary>보유한 캐릭터 수. <see cref="OwnedCharacters"/>와 같은 판정이다.</summary>
        public int OwnedCount
        {
            get
            {
                int count = 0;
                IReadOnlyList<CharacterDefinition> all = AllCharacters;

                for (int i = 0; i < all.Count; i++)
                {
                    CharacterDefinition definition = all[i];
                    if (definition != null && IsOwned(definition.CharacterId)) count++;
                }

                return count;
            }
        }

        /// <summary>카탈로그에서 id로 정의를 찾는다(Ordinal 완전 일치). 보유 여부는 보지 않는다.</summary>
        public CharacterDefinition Find(string characterId)
        {
            return catalog != null ? catalog.Find(characterId) : null;
        }

        /// <summary>
        /// 이 id를 보유했는가. 비교는 <b>Ordinal 완전 일치</b>다 - 'Barbarian'과 'barbarian'은 서로
        /// 다른 캐릭터이며, 어느 쪽도 다른 쪽을 보유한 것으로 만들지 않는다.
        ///
        /// <b>카탈로그를 보지 않는다.</b> 저장 문서에만 있는 id도 여기서는 true다 - "가지고 있다"는
        /// 사실과 "지금 빌드에서 쓸 수 있다"는 사실은 다르고, 전자를 저장 문서 하나로만 판정해야
        /// 카탈로그가 바뀌었다고 보유가 사라지지 않는다. 쓸 수 있는지는
        /// <see cref="OwnedCharacters"/>가 카탈로그와 겹쳐서 답한다.
        /// </summary>
        public bool IsOwned(string characterId)
        {
            return FindState(characterId) != null;
        }

        /// <summary>정의로 묻는 같은 판정. 정의가 들고 있는 <b>CharacterId</b>로만 본다 -
        /// 에셋 참조가 같은지는 보지 않으므로, 같은 id를 가진 수동 에셋으로 물어도 답이 같다.</summary>
        public bool IsOwned(CharacterDefinition definition)
        {
            return definition != null && IsOwned(definition.CharacterId);
        }

        /// <summary>
        /// 보유 중이면 그 캐릭터의 저장 상태를 <b>있는 그대로</b> 돌려준다(사본이 아니다 - 부르는 쪽이
        /// 행동력을 고쳐야 한다). <b>없으면 만들지 않고 false</b>다: 상태를 만드는 것은 곧 캐릭터를
        /// 지급하는 것이라, 조회하다가 얻어걸리는 일이 있어서는 안 된다.
        ///
        /// 저장 목록에 같은 id가 두 번 있으면 <b>먼저 나온 항목</b>을 돌려준다(다른 목록들과 같은 규칙).
        /// </summary>
        public bool TryGetState(string characterId, out CharacterSaveState state)
        {
            state = FindState(characterId);
            return state != null;
        }

        public bool TryGetState(CharacterDefinition definition, out CharacterSaveState state)
        {
            if (definition == null)
            {
                state = null;
                return false;
            }

            return TryGetState(definition.CharacterId, out state);
        }

        /// <summary>
        /// <b>새 게임을 시작할 때 딱 한 번</b> 부르는 초기 지급. 카탈로그에서
        /// <see cref="CharacterDefinition.InitiallyOwned"/>가 켜진 정의만, 아직 없는 것만 목록 끝에
        /// 더한다(레벨 1, 행동력 -1 = "아직 초기화되지 않음").
        ///
        /// <b>부르는 쪽이 지켜야 할 조건이 있다.</b> 저장 상태가
        /// <see cref="SaveLoadStatus.NewGame"/>일 때만 불러야 한다 - 불러온 문서(Loaded/Migrated)나
        /// 진행이 막힌 문서(CorruptFallback/FutureVersionBlocked/MigrationFailed)에 이것을 적용하면
        /// <b>플레이어가 스스로 비운 목록이 다시 채워지거나</b>, 읽지 못한 문서 위에 지급이 얹힌다.
        /// 특히 v2에서 목록이 비어 있는 것은 정상적인 상태이므로 "비었으니 채운다"는 판단을 여기서
        /// 하지 않는다 - 판단의 근거는 언제나 불러오기 상태 하나다.
        ///
        /// <b>여러 번 불러도 결과가 같다.</b> 이미 있는 id는 건드리지 않으므로 두 번째 호출은 아무것도
        /// 하지 않는다. 기존 항목, null 항목, 중복 항목, 카탈로그에 없는 id는 <b>하나도 손대지 않는다</b>.
        /// </summary>
        /// <returns>실제로 더해진 항목 수.</returns>
        public int InitializeNewGame()
        {
            if (document == null) return 0;

            IReadOnlyList<CharacterDefinition> all = AllCharacters;
            int added = 0;

            for (int i = 0; i < all.Count; i++)
            {
                CharacterDefinition definition = all[i];
                if (definition == null) continue;
                if (!definition.InitiallyOwned) continue;

                string id = definition.CharacterId;
                if (string.IsNullOrEmpty(id)) continue;

                // 이미 있으면 그대로 둔다 - 진행 중인 값을 초기값으로 덮어쓰지 않는다.
                if (FindState(id) != null) continue;

                if (document.characters == null) document.characters = new List<CharacterSaveState>();

                document.characters.Add(new CharacterSaveState
                {
                    characterId = id,
                    level = 1,
                    currentStamina = -1,
                });

                added++;
            }

            return added;
        }

        /// <summary>저장 목록에서 그 id의 항목을 찾는다. <b>만들지 않는다.</b> null 항목과 id가 없는
        /// 항목은 아무것도 가리키지 않으므로 건너뛴다(지우지는 않는다).</summary>
        private CharacterSaveState FindState(string characterId)
        {
            if (document == null || document.characters == null) return null;
            if (string.IsNullOrEmpty(characterId)) return null;

            List<CharacterSaveState> states = document.characters;
            for (int i = 0; i < states.Count; i++)
            {
                CharacterSaveState state = states[i];
                if (state == null) continue;
                if (string.Equals(state.characterId, characterId, StringComparison.Ordinal)) return state;
            }

            return null;
        }
    }
}
