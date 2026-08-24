using System;
using System.Collections.Generic;
using Common;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 보유 캐릭터 목록과 "지금 전투 중인 캐릭터"를 소유하는 <b>단일 관리자</b>. 캐릭터를 실제로
    /// 바꾸는 방법은 이 컴포넌트의 <see cref="TrySwitchTo"/> 하나뿐이고, 캐릭터 교체 패널
    /// (CharacterSwapPanel)이 그 유일한 호출부다 - 활성화 주체가 둘 이상이면 두 캐릭터가 동시에
    /// 켜지거나 아무도 켜지지 않는 상태가 생긴다.
    ///
    /// 교체 방식은 <b>런타임 액터 하나</b>다: 씬에 캐릭터마다 GameObject를 배치해 켜고 끄는 대신,
    /// <see cref="CharacterRuntimeActor"/> 하나에 그 캐릭터의 모션 프로필을 적용한다. 전투/충전/
    /// 발사체/오버레이/이동/색 정리는 액터가 소유하므로(각 컴포넌트의 OnDisable 재사용), 공격
    /// 도중에 교체해도 이전 캐릭터의 입력이나 자세가 남지 않는다. 이 컴포넌트는 "누가 지금
    /// 전투 중인가"만 소유하고, 화면에 어떻게 그릴지는 전혀 알지 않는다.
    ///
    /// 데이터 원천은 두 곳으로 명확히 나뉜다.
    ///   - <see cref="CharacterDefinition"/> 에셋: 캐릭터가 무엇인지(이름/초상화/최대 행동력/프로필)
    ///   - SaveData.characters: 캐릭터가 지금 어떤 상태인지(레벨/현재 행동력)
    /// 목록의 근거는 정의 에셋 하나뿐이다 - 씬에 캐릭터 오브젝트가 있는지는 더 이상 보지 않는다.
    ///
    /// <b>행동력 소비 규칙: 몬스터 처치 1회당 1.</b> 근거는 <see cref="Target.AnyTargetDefeated"/>
    /// (처치가 확정된 순간 정확히 한 번 발생하는 기존 이벤트) 하나뿐이다 - 키 입력, 공격 시작,
    /// 타격 판정, 데미지 적용, 애니메이션 종료로는 절대 줄지 않는다. 공격 템포가 빠른 캐릭터와 느린
    /// 캐릭터가 같은 몬스터 하나를 잡는 데 같은 비용을 쓰게 하기 위한 초기 규칙이다.
    ///
    /// 행동력이 0이면 <see cref="CurrentCharacterCanAct"/>가 false가 되고, PlayerCharacterAnimator가
    /// 그 값을 보고 <b>새 공격 세션을 시작하지 않는다</b>(이미 재생 중인 공격은 끊지 않는다).
    /// 자동 교체는 하지 않는다 - 캐릭터는 Idle 상태로 대기하고, 교체는 사용자가 패널에서 한다.
    ///
    /// <b>행동력 회복은 회복소(Recovery.RecoveryService)가 소유한다.</b> 예전에 있던 "전체 충전"
    /// 테스트 경로는 제거했다 - 회복은 재화를 내고 시간을 기다리는 정식 규칙 하나뿐이며, 그 경로를
    /// 우회해 값을 최대치로 만드는 공개 API를 남겨 두지 않는다.
    ///
    /// 회복 중(Recovering/RecoveryComplete)인 캐릭터는 <see cref="GetSwapBlockReason"/>이
    /// <see cref="SwapBlockReason.InRecovery"/>로 교체를 막고, <see cref="SetStamina"/>도 값 변경을
    /// 거부한다 - 회복소가 계산한 행동력을 바깥에서 덮어쓰지 못하게 하기 위함이다. 회복소 자신은
    /// 전용 경로인 <see cref="ApplyRecoveryStamina"/>를 쓴다.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterRoster : MonoBehaviour, IPartyCharacterLevelSource
    {
        /// <summary>교체가 막히는 이유. UI가 사용자에게 무엇을 보여줄지 결정하는 근거다 -
        /// "선택했는데 아무 반응 없이 실패"하는 경로를 만들지 않기 위해 항상 이유를 함께 돌려준다.</summary>
        public enum SwapBlockReason
        {
            None,
            /// <summary>지금 전투 중인 캐릭터를 다시 고른 경우.</summary>
            AlreadyCurrent,
            /// <summary>행동력이 0이라 투입할 수 없는 경우.</summary>
            NoStamina,
            /// <summary>로스터에 없는 캐릭터이거나, 지금 구성으로는 투입할 수 없는 상태
            /// (Runtime Actor 미연결 등 설정 문제로 적용 자체가 실패하는 경우 포함).</summary>
            NotAvailable,
            /// <summary>회복소 슬롯에 들어가 있다(회복 중이거나, 회복이 끝나 합류를 기다리는 중).
            /// 행동력이 남아 있어도 교체할 수 없다 - 슬롯에서 합류시켜야 다시 쓸 수 있다.</summary>
            InRecovery,
        }

        /// <summary>로스터 한 칸. 예전에는 정의와 "그 캐릭터의 씬 GameObject"를 짝지어 들고 있었지만,
        /// 캐릭터를 그리는 주체가 런타임 액터 하나로 합쳐지면서 씬 오브젝트 칸이 사라졌다. 클래스 이름과
        /// 바깥 리스트 필드 이름(<c>entries</c>)을 그대로 둔 이유는, 씬에 이미 직렬화돼 있는 항목의
        /// <see cref="definition"/> 연결을 Unity가 그대로 읽어 오게 하기 위함이다 - 없어진 칸은
        /// 사용자가 다음에 씬을 저장할 때 조용히 떨어져 나간다.</summary>
        [Serializable]
        public class Entry
        {
            [Tooltip("이 캐릭터의 정의 에셋.")]
            public CharacterDefinition definition;
        }

        [Header("Roster")]
        [Tooltip("이 게임의 활성 캐릭터 전체(생성 카탈로그). 이것이 연결되면 로스터는 " +
                 "'저장 출전 파티 순서 ∩ 카탈로그 ∩ 보유'로 목록을 만들고 아래 Entries는 보지 않는다.")]
        [SerializeField] private CharacterCatalog catalog;

        [Tooltip("<b>과도기 폴백</b>. Character Catalog가 연결되지 않은 씬에서만 쓰인다 - " +
                 "카탈로그가 있으면 이 목록은 한 항목도 읽지 않는다. 여기 적힌 캐릭터는 " +
                 "보유로 취급되지 않으며 저장 데이터에 항목을 만들지도 않는다.")]
        [SerializeField] private List<Entry> entries = new List<Entry>();

        [Header("Runtime Actor")]
        [Tooltip("보유 캐릭터 전원을 연기하는 씬의 런타임 액터 오브젝트(CharacterRuntimeActor). " +
                 "교체는 이 액터 하나에 프로필을 적용하는 방식이라 캐릭터별 오브젝트는 필요 없다. " +
                 "반드시 에디터에서 직접 연결한다 - 코드가 자동으로 찾거나 만들지 않으며, 비어 있으면 " +
                 "오류를 남기고 아무도 투입하지 않는다.")]
        [SerializeField] private CharacterRuntimeActor runtimeActor;

        [Tooltip("앱 시작 시 전투에 투입할 캐릭터. 비워두면 목록의 첫 번째를 쓴다.")]
        [SerializeField] private CharacterDefinition defaultCharacter;

        [Header("Stamina")]
        [Tooltip("몬스터를 한 번 처치할 때 현재 캐릭터가 소비하는 행동력. 타격 수나 공격 횟수가 아니라 " +
                 "'처치 확정 1회'가 단위다.")]
        [Min(0)]
        [SerializeField] private int staminaCostPerDefeat = 1;

        [Header("Debug (개발용 - 정식 UI에 노출하지 않는다)")]
        [Tooltip("켜면 시작할 때 모든 캐릭터의 현재 행동력을 아래 값으로 덮어쓰고 저장한다. " +
                 "반복 테스트용이며, 켜 둔 채로 두면 실제 저장 데이터가 매 실행 덮어써진다.")]
        [SerializeField] private bool overrideStaminaOnStart;

        [Min(0)]
        [SerializeField] private int debugStartStamina = 3;

        /// <summary>씬에 하나만 둔다. 패널/버튼이 정적으로 접근한다(ToastManager 등과 같은 패턴).</summary>
        public static CharacterRoster Instance { get; private set; }

        /// <summary>전투 중인 캐릭터가 실제로 바뀐 직후 발생. 인자는 새 캐릭터다.</summary>
        public static event Action<CharacterDefinition> CurrentCharacterChanged;

        /// <summary>한 캐릭터의 상태(레벨/행동력)가 바뀔 때 발생 - 리스트 전체가 아니라 그 항목만
        /// 갱신하면 되도록 어떤 캐릭터인지 함께 보낸다.</summary>
        public static event Action<CharacterDefinition> CharacterStateChanged;

        // 검증을 통과해 실제로 쓸 수 있는 항목만 남긴 목록. entries를 직접 순회하지 않는 이유는
        // 비어 있는 슬롯이 UI 인덱스나 순환 순서에 끼어들지 않게 하기 위함이다.
        private readonly List<Entry> usableEntries = new List<Entry>();

        private CharacterDefinition current;

        /// <summary>카탈로그와 저장 문서를 겹쳐 보는 자리. 카탈로그가 없으면 null이며, 그때 로스터는
        /// 과도기 폴백(직렬화된 Entries)으로 동작한다.</summary>
        private OwnedCharacterCollection owned;

        /// <summary>이번 실행이 카탈로그 기반인지. 두 모드의 차이가 코드 여러 곳에 흩어지지 않도록
        /// 판정을 한 곳에 둔다.</summary>
        private bool UsesCatalog => catalog != null;

        // 같은 처치 이벤트가 두 번 들어와도 행동력이 두 번 깎이지 않게 막는 최소 방어(판정 규칙은
        // DefeatEventFilter 참고). 보상 지급 쪽은 자기 필터를 따로 들고 있다 - 하나를 공유하면
        // 먼저 처리한 쪽이 다른 쪽의 이벤트를 삼켜서 둘이 서로에게 영향을 준다.
        private readonly DefeatEventFilter defeatFilter = new DefeatEventFilter();

        public IReadOnlyList<Entry> Entries => usableEntries;

        /// <summary>출전 여부와 무관한 전체 보유 캐릭터. 회복소처럼 보유 상태를 다루는 화면만 사용한다.</summary>
        public IReadOnlyList<CharacterDefinition> OwnedCharacters => owned != null ? owned.OwnedCharacters : Array.Empty<CharacterDefinition>();

        /// <summary>
        /// 저장 트랜잭션이 새 보유 캐릭터를 성공적으로 확정한 뒤, 카탈로그 기반 로스터의 출전 파티만
        /// 다시 읽는다. 영입은 partyCharacterIds를 바꾸지 않으므로 목록과 현재 전투 캐릭터를 바꾸지 않는다.
        /// </summary>
        public void RefreshOwnedCharactersAfterExternalSave()
        {
            if (!UsesCatalog) return;
            BuildUsableEntries();
        }

        /// <summary>파티 저장이 성공한 뒤 출전 목록을 다시 읽는다. 저장하거나 자동 합류시키지 않는다.</summary>
        public void RefreshPartyAfterExternalSave()
        {
            if (!UsesCatalog) return;
            CharacterDefinition previous = current;
            BuildUsableEntries();
            if (previous != null && ResolveUsable(previous) != null) return;
            // current만 바꾸면 화면의 Runtime Actor가 이전 캐릭터를 계속 연기한다. 일반 교체와 같은
            // 적용 경로를 써서 로스터와 화면을 원자적으로 맞춘다.
            ApplyActiveCharacter(ResolveFirstAvailablePartyCharacter());
        }

        /// <summary>지금 전투 중인 캐릭터. 사용 가능한 항목이 하나도 없으면 null이다.</summary>
        public CharacterDefinition Current => current;

        /// <summary>
        /// 이 로스터가 목록의 근거로 삼는 <b>활성 캐릭터 카탈로그</b>. 연결되지 않은 과도기 구성에서는
        /// null이다.
        ///
        /// <b>읽기 전용 이음매다.</b> 캐릭터 id를 근거로 무언가를 계산하는 다른 시스템(스킬 해금 등)이
        /// "지금 어떤 카탈로그가 쓰이고 있는가"를 자기 Inspector 칸으로 한 벌 더 들고 있으면, 두 곳이
        /// 서로 다른 카탈로그를 가리키는 씬을 만들 수 있다 - 그러면 로스터가 인정한 캐릭터와 스킬이
        /// 인정한 캐릭터가 달라진다. 근거는 이 하나뿐이다.
        /// </summary>
        public CharacterCatalog Catalog => catalog;

        /// <summary>지금 전투 중인 캐릭터가 <b>새 공격을 시작</b>할 수 있는지(행동력 &gt; 0).
        /// PlayerCharacterAnimator가 입력을 받을지 판단할 때 Target.HasAttackableTarget과 함께 본다.
        ///
        /// <b>로스터가 아예 없는 씬</b>에서만 항상 true다 - 행동력 시스템을 쓰지 않는 씬의 기존 전투를
        /// 막지 않기 위함이다.
        ///
        /// <b>로스터가 있는데 보유한 캐릭터가 하나도 없으면 false다.</b> 예전에는 "목록이 비었으면
        /// true"였는데, 보유가 저장 데이터로 정해진 뒤로 그것은 위험한 판정이 됐다 - 아직 아무도 가지지
        /// 못한 사람이 화면에 아무도 없는 채로 공격할 수 있게 된다. <b>보유 0은 '로스터가 없다'가
        /// 아니다.</b>
        ///
        /// 카탈로그가 아직 연결되지 않은 과도기 씬(폴백)에서는 예전 판정을 그대로 둔다 - 그 구성에는
        /// 보유라는 개념 자체가 없으므로 새 규칙을 적용할 근거가 없다.
        ///
        /// <b>캐릭터는 있는데 아무도 투입되지 않은 상태</b>(전원이 회복소 슬롯에 있어 시작 캐릭터를
        /// 고르지 못한 경우)도 false다 - 화면에 아무도 없는 채로 공격 입력이 통하면 안 된다.</summary>
        public static bool CurrentCharacterCanAct
        {
            get
            {
                CharacterRoster roster = Instance;

                // 로스터를 쓰지 않는 씬 - 기존 동작 유지.
                if (roster == null) return true;

                if (roster.usableEntries.Count == 0)
                {
                    // 카탈로그 기반인데 쓸 수 있는 캐릭터가 없다 = 보유가 없다. 싸울 주체가 없다.
                    // 폴백 구성에서만 예전의 "비었으면 통과"를 유지한다.
                    return !roster.UsesCatalog;
                }

                // 캐릭터는 있는데 투입된 캐릭터가 없다 - 전투할 주체가 없으므로 공격을 허용하지 않는다.
                if (roster.current == null) return false;

                return roster.GetStamina(roster.current) > 0;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CharacterRoster] 씬에 CharacterRoster가 이미 있습니다. 이 인스턴스는 무시합니다.", this);
                enabled = false;
                return;
            }
            Instance = this;

            owned = UsesCatalog ? new OwnedCharacterCollection(catalog, SaveSystem.Data) : null;
            GrantInitialCharactersOnNewGameOnly();

            BuildUsableEntries();
            if (usableEntries.Count == 0)
            {
                Debug.LogError(UsesCatalog
                    ? "[CharacterRoster] 보유한 캐릭터가 하나도 없습니다 - 저장 데이터의 보유 목록이 " +
                      "비어 있거나 카탈로그와 겹치는 id가 없습니다."
                    : "[CharacterRoster] 사용할 수 있는 캐릭터가 하나도 없습니다 - Character Catalog를 " +
                      "연결하거나 Entries에 Character Definition을 연결하세요.", this);
                // 로스터가 인정하지 않는 캐릭터가 화면에 남아 계속 입력을 받는 것을 막는다.
                // CurrentCharacterCanAct는 "목록이 비어 있으면 true"(로스터를 쓰지 않는 씬 호환)라서,
                // 액터를 켜둔 채로 두면 아무도 투입되지 않았는데 공격이 통한다.
                if (runtimeActor != null) runtimeActor.Deactivate();
                return;
            }

            NormalizeOwnedStamina();
            ApplyDebugStartStamina();

            if (runtimeActor == null)
            {
                // current는 null로 남는다 - 사용 가능한 캐릭터가 있으므로 CurrentCharacterCanAct도
                // false가 되어 공격이 시작되지 않는다(조용히 반쪽으로 동작하지 않는다).
                Debug.LogError("[CharacterRoster] Runtime Actor가 연결되지 않았습니다 - 캐릭터를 화면에 " +
                               "투입할 수 없습니다. Inspector의 Runtime Actor에 씬의 CharacterRuntimeActor를 " +
                               "연결하세요.", this);
            }

            ApplyActiveCharacter(ResolveStartCharacter());
        }

        /// <summary>
        /// <b>새 게임일 때만</b> 표의 initially_owned 캐릭터를 지급한다.
        ///
        /// 판단의 근거는 <see cref="SaveSystem.LoadStatus"/> 하나뿐이며 "목록이 비어 있다"가 아니다 -
        /// v2에서 <b>빈 보유 목록은 정상적인 상태</b>라서, 비었다고 채우면 플레이어가 스스로 비운 목록이
        /// 다시 채워지고 불러오기가 실패한 문서 위에도 지급이 얹힌다. Loaded / Migrated /
        /// CorruptFallback / FutureVersionBlocked / MigrationFailed에서는 <b>한 항목도 만들지 않는다</b>.
        ///
        /// 여기서 저장하지 않는다 - 새 게임의 첫 저장은 기존 저장 경로가 알아서 하며, 저장되기 전에
        /// 종료해도 다음 실행이 다시 새 게임이라 같은 결과가 된다(지급은 여러 번 해도 같다).
        /// </summary>
        private void GrantInitialCharactersOnNewGameOnly()
        {
            if (owned == null) return;
            if (SaveSystem.LoadStatus != SaveLoadStatus.NewGame) return;

            int granted = owned.InitializeNewGame();
            if (granted > 0)
            {
                Debug.Log($"[CharacterRoster] 새 게임이라 시작 캐릭터 {granted}명을 지급했습니다.", this);
            }
        }

        private void OnEnable()
        {
            // 행동력이 줄어드는 유일한 근거. SessionKillCounter/PlayerProgress와 같은 구독 패턴이라
            // 몬스터가 늘어나거나 종류가 달라져도 별도 연결이 필요 없다.
            Target.AnyTargetDefeated += HandleAnyTargetDefeated;
        }

        private void OnDisable()
        {
            Target.AnyTargetDefeated -= HandleAnyTargetDefeated;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>몬스터 처치가 확정된 순간 - 지금 전투 중인 캐릭터의 행동력만 소비한다.
        /// 데미지/타격/입력이 아니라 이 경로에서만 줄어들기 때문에, 공격 템포가 다른 캐릭터끼리도
        /// 몬스터 한 마리당 비용이 같다.</summary>
        private void HandleAnyTargetDefeated(string targetId)
        {
            if (current == null || staminaCostPerDefeat <= 0) return;
            if (!defeatFilter.Accept(targetId, this)) return;

            SpendStamina(current, staminaCostPerDefeat);
        }

        /// <summary>실제로 투입할 수 있는 항목만 남긴다. 판정 근거는 정의 에셋뿐이다 - 씬에 그 캐릭터의
        /// 오브젝트가 있는지, 그 오브젝트가 어떤 프로필을 물고 있는지는 더 이상 보지 않는다(연기하는
        /// 액터가 하나뿐이라 대조할 상대가 없다).
        ///
        /// 걸러내는 것: 빈 항목/빈 정의, 중복 Character Id, 모션 프로필 미연결, 재생 가능한 Base Idle
        /// 없음. 뒤의 두 가지는 <b>서로 다른 오류 메시지</b>로 남긴다 - "프로필을 연결하지 않은 것"과
        /// "프로필은 있는데 Base Idle이 비어 있는 것"은 사용자가 해야 할 일이 전혀 다르기 때문이다.
        /// 여기서 걸러 두면 나중에 그 캐릭터를 골랐을 때 액터 적용이 실패하는 일이 없다.</summary>
        private void BuildUsableEntries()
        {
            usableEntries.Clear();

            if (UsesCatalog)
            {
                BuildUsableEntriesFromCatalog();
                return;
            }

            BuildUsableEntriesFromSerializedList();
        }

        /// <summary>
        /// 카탈로그 기반 목록. 순서는 <b>저장된 v4 고정 partyCharacterIds 슬롯</b>이고 빈 슬롯은 건너뛴다.
        /// 그 중 카탈로그에 정의가
        /// 있으며 지금도 보유한 것만 남긴 뒤 기존 재생 가능 검사를 그대로 통과시킨다.
        ///
        /// <b>여기서 저장 데이터에 손대지 않는다.</b> 보유하지 않은 캐릭터는 그냥 목록에 없을 뿐이고,
        /// 목록을 만드는 동안 항목이 생기거나 사라지지 않는다 - 목록을 여는 것만으로 캐릭터가 지급되면
        /// "보유"라는 말이 아무 뜻도 갖지 못한다.
        ///
        /// 저장 데이터의 잘못된 중복 ID도 런타임 Entries에는 한 번만 나타낸다. 이 방어는 저장 문서를
        /// 고치지 않으며, 저장 정합성의 소유자는 PartyCompositionService다.
        /// </summary>
        private void BuildUsableEntriesFromCatalog()
        {
            List<string> party = SaveSystem.Data != null ? SaveSystem.Data.partyCharacterIds : null;
            if (party == null) return;

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < party.Count; i++)
            {
                string id = party[i];
                if (string.IsNullOrEmpty(id) || !seenIds.Add(id)) continue;
                CharacterDefinition definition = catalog.Find(id);
                if (definition == null || !owned.IsOwned(definition)) continue;

                if (definition.MotionProfile == null)
                {
                    Debug.LogError($"[CharacterRoster] '{definition.CharacterId}'의 Definition에 Character " +
                                   "Motion Profile이 연결되지 않았습니다 - 연기할 모션 데이터가 없으므로 이 항목은 " +
                                   "무시합니다.", definition);
                    continue;
                }

                if (!CharacterMotionProfile.IsPlayable(definition.MotionProfile))
                {
                    Debug.LogError($"[CharacterRoster] '{definition.CharacterId}'의 프로필 " +
                                   $"'{definition.MotionProfile.name}'에 재생 가능한 Base Idle 프레임이 " +
                                   "없습니다 - 이 항목은 무시합니다.", definition.MotionProfile);
                    continue;
                }

                usableEntries.Add(new Entry { definition = definition });
            }
        }

        /// <summary>
        /// <b>과도기 폴백</b> - Character Catalog가 연결되지 않은 씬에서만 쓴다. 예전 규칙 그대로
        /// 직렬화된 목록을 검사해 쓴다.
        ///
        /// <b>여기 적힌 캐릭터는 보유가 아니다.</b> 이 경로는 저장 데이터에 항목을 만들지 않으며,
        /// 그래서 이 구성에서는 행동력을 읽고 쓰는 동작이 대부분 아무 일도 하지 않는다(보유한 상태가
        /// 없으므로). 카탈로그를 연결하는 순간 이 목록은 <b>한 항목도 읽히지 않는다</b>.
        /// </summary>
        private void BuildUsableEntriesFromSerializedList()
        {
            if (entries == null) return;

            var seenIds = new HashSet<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry == null || entry.definition == null)
                {
                    Debug.LogError($"[CharacterRoster] Entries[{i}]에 Character Definition이 비어 있습니다 - 이 항목은 무시합니다.", this);
                    continue;
                }
                if (!seenIds.Add(entry.definition.CharacterId))
                {
                    Debug.LogError($"[CharacterRoster] Character Id '{entry.definition.CharacterId}'가 중복됩니다 - " +
                                   "저장 데이터가 서로 섞이므로 뒤에 있는 항목은 무시합니다.", this);
                    continue;
                }
                if (entry.definition.MotionProfile == null)
                {
                    Debug.LogError($"[CharacterRoster] '{entry.definition.CharacterId}'의 Definition에 Character " +
                                   "Motion Profile이 연결되지 않았습니다 - 연기할 모션 데이터가 없으므로 이 항목은 " +
                                   "무시합니다.", entry.definition);
                    continue;
                }
                if (!CharacterMotionProfile.IsPlayable(entry.definition.MotionProfile))
                {
                    Debug.LogError($"[CharacterRoster] '{entry.definition.CharacterId}'의 프로필 " +
                                   $"'{entry.definition.MotionProfile.name}'에 재생 가능한 Base Idle 프레임이 " +
                                   "없습니다 - 이 항목은 무시합니다.", entry.definition.MotionProfile);
                    continue;
                }

                usableEntries.Add(entry);
            }
        }

        /// <summary>
        /// <b>이미 보유한</b> 캐릭터의 행동력만 쓸 수 있는 값으로 맞춘다. -1("아직 초기화되지 않음")은
        /// 정의의 Max Stamina로 확정하고, 그 밖의 값은 0 ~ Max로 자른다(Max를 나중에 낮춘 경우).
        ///
        /// <b>없는 항목을 만들지 않는다.</b> 예전에는 이 자리가 "로스터에 있으면 저장 항목을 만든다"였고,
        /// 그것이 곧 캐릭터 지급이었다 - v2에서 보유는 저장 데이터가 정하므로, 여기서 항목을 만들면
        /// 목록을 켜는 것만으로 캐릭터가 생긴다. 지급 경로는
        /// <see cref="OwnedCharacterCollection.InitializeNewGame"/> 하나뿐이다.
        /// </summary>
        private void NormalizeOwnedStamina()
        {
            for (int i = 0; i < usableEntries.Count; i++)
            {
                CharacterDefinition definition = usableEntries[i].definition;
                if (!TryGetOwnedState(definition, out _, out CharacterSaveState state)) continue;

                state.currentStamina = Mathf.Clamp(
                    state.currentStamina < 0 ? definition.MaxStamina : state.currentStamina,
                    0,
                    definition.MaxStamina);
            }
        }

        /// <summary>
        /// 이 로스터가 쓰는 <b>정식 정의</b>와 그 저장 상태를 함께 찾는다. <b>만들지 않는다.</b>
        ///
        /// <b>반드시 목록을 먼저 지난다.</b> 저장 문서만 보고 상태를 찾으면, 카탈로그에 없는
        /// 저장 전용 id(예전 빌드에서 남은 캐릭터)를 가리키는 정의를 만들어 넘기는 것만으로 그 값을
        /// 읽고 고칠 수 있게 된다 - 그 id는 <b>보존하되 감춰야 하는</b> 값이므로, 지금 쓸 수 있는
        /// 캐릭터(usableEntries)로 해석되지 않으면 여기서 끝난다.
        ///
        /// 해석은 <b>CharacterId의 Ordinal 완전 일치</b>이므로 같은 id의 수동 에셋으로 물어도 같은
        /// 정식 정의와 같은 상태를 얻는다.
        /// </summary>
        private bool TryGetOwnedState(
            CharacterDefinition definition, out CharacterDefinition canonical, out CharacterSaveState state)
        {
            canonical = null;
            state = null;

            // 카탈로그가 없는 폴백 구성에는 보유라는 개념이 없다 - 상태를 만들지 않으므로 언제나 실패다.
            if (owned == null) return false;

            string id = definition != null ? definition.CharacterId : null;
            if (string.IsNullOrEmpty(id)) return false;
            canonical = catalog != null ? catalog.Find(id) : null;
            if (canonical == null) return false;

            return owned.TryGetState(canonical.CharacterId, out state);
        }

        /// <summary>
        /// 목록에도 있고 <b>지금도 보유 중인</b> 캐릭터의 정식 정의. 둘 중 하나라도 아니면 null이다.
        ///
        /// <see cref="ResolveUsable"/>만으로는 부족한 이유는 usableEntries가 Awake 때 한 번 만든
        /// 목록이기 때문이다 - 그 뒤에 저장 문서에서 보유가 사라져도 목록에는 그대로 남아 있어서,
        /// 목록만 보는 판정은 <b>이미 잃은 캐릭터를 아직 가진 것처럼</b> 다룬다.
        /// </summary>
        private CharacterDefinition ResolveOwnedUsable(CharacterDefinition definition)
        {
            // 카탈로그가 없는 폴백 구성에는 <b>보유라는 개념 자체가 없다</b> - 저장 문서를 다시 확인할
            // 대상이 없으므로 예전처럼 목록만 보고 답한다. 여기서 null을 돌려주면 그 구성의 교체 판정과
            // 상태 변경 알림이 통째로 죽는다(모든 캐릭터가 "쓸 수 없음"이 된다).
            if (owned == null) return ResolveUsable(definition);

            CharacterDefinition canonical = ResolveUsable(definition);
            if (canonical == null) return null;
            return owned.TryGetState(canonical.CharacterId, out _) ? canonical : null;
        }

        /// <summary>
        /// 넘어온 정의를 <b>이 로스터가 실제로 쓰는 정의</b>로 바꾼다. 같은 id를 가진 수동 에셋을
        /// 넘겨받아도 생성 에셋으로 이어지도록, 비교는 참조가 아니라 CharacterId로 한다 - 씬과 표가
        /// 서로 다른 에셋을 들고 있어도 같은 캐릭터를 가리킬 수 있어야 한다.
        ///
        /// 목록에 없으면 null이다(= 지금 쓸 수 없는 캐릭터).
        /// </summary>
        private CharacterDefinition ResolveUsable(CharacterDefinition definition)
        {
            if (definition == null) return null;

            string id = definition.CharacterId;
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < usableEntries.Count; i++)
            {
                CharacterDefinition candidate = usableEntries[i].definition;
                if (candidate != null && string.Equals(candidate.CharacterId, id, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// 앱을 켤 때 전투에 투입할 캐릭터를 고른다. <b>회복소 슬롯에 들어 있는 캐릭터는 절대 고르지
        /// 않는다</b> - 지난 실행에서 회복을 걸어 둔 캐릭터가 시작하자마자 Active가 되면, 시간이 흘러
        /// 행동력이 1 이상이 되는 순간 회복 중인 캐릭터로 공격할 수 있게 된다(자동 합류와 다름없다).
        ///
        /// 이 판정을 하는 시점에는 RecoveryService가 아직 만들어지지 않았으므로,
        /// <see cref="Recovery.RecoveryService.IsCharacterInRecovery"/>가 저장된 recoverySlots를 직접
        /// 보는 폴백으로 답한다 - 회복소가 있을 때와 같은 근거다.
        ///
        /// 선택 순서는 결정적이다: Default Character가 쓸 수 있으면 그것, 아니면 Entries 순서대로
        /// 처음 만나는 비회복 캐릭터. 전원이 회복 중이면 <b>null</b>을 돌려주고 아무도 켜지 않는다
        /// (회복 중인 캐릭터를 대신 켜지 않는다).
        /// </summary>
        private CharacterDefinition ResolveStartCharacter()
        {
            // Default Character는 씬이 들고 있는 <b>수동 에셋</b>일 수 있다. id로 이 로스터가 실제로
            // 쓰는 정의(카탈로그의 생성 에셋)로 바꿔 놓아야, 같은 캐릭터인데 참조가 달라서 "목록에
            // 없다"고 판정하는 일이 없다.
            CharacterDefinition resolvedDefault = ResolveUsable(defaultCharacter);

            if (resolvedDefault != null)
            {
                if (!Recovery.RecoveryService.IsCharacterInRecovery(resolvedDefault)) return resolvedDefault;

                Debug.Log($"[CharacterRoster] Default Character('{resolvedDefault.CharacterId}')가 회복소에 " +
                          "있어 다른 캐릭터로 시작합니다.", this);
            }
            else if (defaultCharacter != null)
            {
                Debug.LogWarning($"[CharacterRoster] Default Character('{defaultCharacter.CharacterId}')를 쓸 수 " +
                                 "없어(보유하지 않았거나 목록에 없음) 목록의 첫 번째 캐릭터로 시작합니다.", this);
            }

            for (int i = 0; i < usableEntries.Count; i++)
            {
                CharacterDefinition candidate = usableEntries[i].definition;
                if (!Recovery.RecoveryService.IsCharacterInRecovery(candidate)) return candidate;
            }

            // 출전 파티원이 전부 회복소에 들어가 있다. 회복 중인 캐릭터를 억지로 켜지 않고 아무도
            // 투입하지 않은 상태로 시작한다 - CurrentCharacterCanAct가 false가 되어 공격도 막힌다.
            Debug.LogWarning("[CharacterRoster] 출전 파티원 모두가 회복소에 있어 전투에 투입할 캐릭터가 " +
                             "없습니다 - 회복이 끝난 캐릭터를 합류시키면 다시 선택할 수 있습니다.", this);
            return null;
        }

        /// <summary>외부 파티 변경으로 현재 캐릭터가 빠졌을 때 파티 순서대로 다음 사용 가능 캐릭터를 찾는다.</summary>
        private CharacterDefinition ResolveFirstAvailablePartyCharacter()
        {
            for (int i = 0; i < usableEntries.Count; i++)
            {
                CharacterDefinition candidate = usableEntries[i].definition;
                if (candidate != null && !Recovery.RecoveryService.IsCharacterInRecovery(candidate)) return candidate;
            }

            return null;
        }

        // ---- IPartyCharacterLevelSource ----

        /// <inheritdoc/>
        /// <remarks>
        /// <b>usableEntries를 순회한다.</b> <see cref="BuildUsableEntries"/>가 걸러낸, 재생 가능한
        /// 모션 프로필이 있는 출전 파티원만 대상이다. <see cref="OwnedCharacterCollection.OwnedCharacters"/>를
        /// 직접 쓰면 비파티 또는 프로필 검증에 실패한 정의가 포함되어, 화면에 투입할 수 없는 캐릭터의 레벨이
        /// 던전 입장 판정에 쓰인다.
        ///
        /// 항목마다 <see cref="TryGetOwnedState"/>로 <b>지금도 보유 중인지</b> 재확인한다 -
        /// usableEntries는 Awake 때 한 번 만든 목록이라, 그 뒤에 저장 문서에서 보유가 사라진
        /// 캐릭터도 남아 있기 때문이다.
        /// </remarks>
        public int HighestPartyCharacterLevel
        {
            get
            {
                if (owned == null) return 0;

                int highest = 0;
                for (int i = 0; i < usableEntries.Count; i++)
                {
                    Entry entry = usableEntries[i];
                    if (entry == null || entry.definition == null) continue;

                    if (!TryGetOwnedState(entry.definition, out _, out CharacterSaveState state)) continue;
                    int level = state.level < 1 ? 1 : state.level;
                    if (level > highest) highest = level;
                }
                return highest;
            }
        }

        // ---- 조회 ----

        /// <summary>보유한 캐릭터의 레벨. 그 밖에는(null, 미보유, 카탈로그에
        /// 없는 저장 전용 id) 0이며 저장 데이터에 항목이 생기지 않는다 - 값을 물어보는 것만으로
        /// 캐릭터가 지급되거나 감춰 둔 값이 드러나면 안 된다.</summary>
        public int GetLevel(CharacterDefinition definition)
        {
            return TryGetOwnedState(definition, out _, out CharacterSaveState state) ? state.level : 0;
        }

        /// <summary>보유하고 지금 쓸 수 있는 캐릭터의 <b>이번 레벨 진행도</b>. 규칙은
        /// <see cref="GetLevel"/>과 같다 - 그 밖에는 0이고 저장 데이터에 항목이 생기지 않는다.
        ///
        /// 이름을 <c>GetCurrentExp</c>가 아니라 <c>GetExp</c>로 둔 이유는 이 로스터의 조회 가족
        /// (<see cref="GetLevel"/>/<see cref="GetStamina"/>)이 모두 "정의를 받아 그 캐릭터의 값을
        /// 돌려준다"는 같은 모양이기 때문이다 - <c>Current~</c>는 "지금 전투 중인 캐릭터"를 뜻하는
        /// 말로 이미 쓰이고 있어서, 인자를 받는 조회에 붙이면 두 뜻이 겹친다.
        ///
        /// <b>다음 레벨까지 얼마가 필요한지는 여기서 답하지 않는다.</b> 그것은 저장된 값이 아니라
        /// 성장 규칙이고, 규칙의 주인은 <see cref="CharacterProgressionService"/> 하나뿐이다 -
        /// 로스터가 같은 계산을 한 벌 더 들고 있으면 두 곳이 서로 다른 답을 하는 날이 온다.</summary>
        public int GetExp(CharacterDefinition definition)
        {
            return TryGetOwnedState(definition, out _, out CharacterSaveState state) ? state.currentExp : 0;
        }

        /// <summary>
        /// <b>지금 전투 중인</b> 캐릭터의 정식 정의와 저장 상태를 함께 찾는다. <b>만들지 않는다.</b>
        ///
        /// 성장(경험치)처럼 "현재 캐릭터의 저장 항목을 직접 고쳐야 하는" 시스템이 쓰라고 낸
        /// <b>단 하나의 이음매</b>다. 값을 하나씩 읽어 가는 조회로는 부족한 이유는, 읽은 값을 고쳐
        /// 다시 넣는 사이에 다른 캐릭터로 바뀌면 <b>엉뚱한 캐릭터의 항목</b>에 결과가 적히기 때문이다.
        ///
        /// 판정은 기존 보유 경로(<see cref="TryGetOwnedState"/>)를 그대로 지난다 - 투입된 캐릭터가
        /// 없거나, 목록에 없거나, 지금 이 순간 보유가 사라졌거나, 카탈로그가 없는 과도기 구성이면
        /// false이고 그때는 <b>저장 문서에 아무 항목도 만들지 않는다</b>.
        /// </summary>
        public bool TryGetCurrentState(out CharacterDefinition canonical, out CharacterSaveState state)
        {
            canonical = null;
            state = null;

            if (current == null) return false;

            return TryGetOwnedState(current, out canonical, out state);
        }

        /// <summary>보유한 캐릭터의 현재 행동력. 규칙은 <see cref="GetLevel"/>과 같다.</summary>
        public int GetStamina(CharacterDefinition definition)
        {
            return TryGetOwnedState(definition, out _, out CharacterSaveState state) ? state.currentStamina : 0;
        }

        /// <summary>
        /// 행동력의 상한. <b>넘어온 에셋이 아니라 이 로스터가 쓰는 정식 정의</b>의 값이다 - 같은 id의
        /// 수동 에셋이 다른 Max Stamina를 들고 있어도 실제 상한은 표(카탈로그)가 정한 값 하나여야
        /// 한다.
        ///
        /// <b>지금 이 순간의 저장 문서까지 확인한다.</b> usableEntries는 Awake 때 한 번 만든 목록이라,
        /// 그 뒤에 보유가 사라진 캐릭터도 계속 들어 있다 - 목록만 믿으면 이미 잃은 캐릭터의 상한이
        /// 그대로 나온다. 쓸 수 없거나 더 이상 보유하지 않는 캐릭터는 0이다.
        /// </summary>
        public int GetMaxStamina(CharacterDefinition definition)
        {
            // 카탈로그가 없는 폴백 구성에서는 보유라는 개념이 없으므로 예전처럼 넘어온 정의를 그대로 본다.
            if (owned == null) return definition != null ? definition.MaxStamina : 0;

            return TryGetOwnedState(definition, out CharacterDefinition canonical, out _) ? canonical.MaxStamina : 0;
        }

        /// <summary>이 캐릭터로 교체할 수 없는 이유. <see cref="SwapBlockReason.None"/>이면 교체 가능하다.</summary>
        public SwapBlockReason GetSwapBlockReason(CharacterDefinition definition)
        {
            // 보유하지 않았거나 목록에 없는 캐릭터는 여기서 끝난다 - 상태를 만들지 않으므로 물어보는
            // 것만으로 지급되지 않는다.
            //
            // <b>지금 저장 문서에 상태가 남아 있는지까지 본다.</b> Awake 이후에 보유가 사라졌다면
            // 그 캐릭터는 이미 투입 중이었더라도, 행동력이 남아 있었더라도 교체 대상이 아니다 -
            // 목록(usableEntries)만 믿으면 잃은 캐릭터로 계속 갈아탈 수 있다.
            CharacterDefinition usable = ResolveOwnedUsable(definition);
            if (usable == null) return SwapBlockReason.NotAvailable;

            // 연기할 액터가 없으면 어떤 캐릭터도 투입할 수 없다 - 행동력/회복 상태보다 먼저 본다.
            // 이 상태에서는 current가 항상 null이라 AlreadyCurrent와 겹치지 않는다.
            if (runtimeActor == null) return SwapBlockReason.NotAvailable;
            if (usable == current) return SwapBlockReason.AlreadyCurrent;
            // 회복 중에는 행동력이 이미 차 있어도 교체할 수 없다 - 행동력 값이 아니라 "슬롯에 있는가"가
            // 근거이므로 행동력 판정보다 먼저 본다.
            if (Recovery.RecoveryService.IsCharacterInRecovery(usable)) return SwapBlockReason.InRecovery;
            if (GetStamina(usable) <= 0) return SwapBlockReason.NoStamina;
            return SwapBlockReason.None;
        }

        // ---- 변경 ----

        /// <summary>전투 캐릭터를 교체한다. 교체가 <b>실제로 일어났을 때만</b> true를 돌려준다 - 예전에는
        /// 자격 판정만 통과하면 무조건 true였는데, 이제 적용 주체가 런타임 액터라 "자격은 있는데 적용에
        /// 실패하는" 경우(액터 미연결 등)가 존재한다. 그때는 false와 함께 이유를
        /// <see cref="SwapBlockReason.NotAvailable"/>로 돌려준다 - 호출부(교체 패널)가 실패를 그대로
        /// 로그/표시하므로, "false인데 사유는 None"인 상태를 만들지 않는다.</summary>
        public bool TrySwitchTo(CharacterDefinition definition, out SwapBlockReason reason)
        {
            reason = GetSwapBlockReason(definition);
            if (reason != SwapBlockReason.None) return false;

            // 판정을 통과했다면 이 로스터가 쓰는 정의가 반드시 있다. 넘어온 것이 같은 id의 수동
            // 에셋이어도 <b>실제로 투입되는 것은 카탈로그의 생성 정의</b>다.
            if (ApplyActiveCharacter(ResolveUsable(definition))) return true;

            // 자격은 통과했는데 적용이 실패했다(설정 문제) - 지금 이 캐릭터는 투입할 수 없는 상태다.
            reason = SwapBlockReason.NotAvailable;
            return false;
        }

        public bool TrySwitchTo(CharacterDefinition definition)
        {
            return TrySwitchTo(definition, out _);
        }

        // 순환 교체(SwitchToNext)는 ControlDock의 테스트 버튼이 유일한 호출부였는데, 그 버튼이
        // 행동력 전체 충전(StaminaRefillTestButton)으로 바뀌면서 호출부가 사라져 제거했다. 교체
        // 경로는 이제 캐릭터 교체 패널의 TrySwitchTo 하나뿐이다.

        /// <summary>행동력을 소비한다(음수를 넣으면 회복이지만, 회복 규칙 자체는 아직 없다).
        /// 값이 실제로 바뀐 경우에만 저장하고 <see cref="CharacterStateChanged"/>를 보낸다.</summary>
        public void SpendStamina(CharacterDefinition definition, int amount)
        {
            if (definition == null || amount == 0) return;
            SetStamina(definition, GetStamina(definition) - amount);
        }

        /// <summary>현재 행동력을 직접 지정한다(0 ~ Max Stamina로 잘린다). 값이 실제로 바뀐 경우에만
        /// 저장하므로, 이미 0인 캐릭터가 다시 0으로 지정돼도 파일을 쓰지 않는다.
        ///
        /// <b>회복소 슬롯에 들어 있는 캐릭터는 거부한다.</b> 회복 중 행동력은 저장된 시작 시각으로부터
        /// 계산되는 값이라, 바깥에서 덮어써도 다음 진행 확인에서 되돌아가 "값이 제멋대로 튀는" 것처럼
        /// 보인다 - 조용히 넘기지 않고 경고를 남긴다.</summary>
        public void SetStamina(CharacterDefinition definition, int value)
        {
            if (definition == null) return;

            // <b>쓸 수 없는 캐릭터에는 아무 일도 하지 않는다.</b> 미보유든, 카탈로그에 없는 저장 전용
            // id든 마찬가지다 - 항목을 만들지 않고 저장하지도 알리지도 않는다. 회복 판정보다 먼저 보는
            // 이유는, 우리가 다루지 않는 캐릭터에 대해 경고까지 남길 이유가 없기 때문이다.
            if (!TryGetOwnedState(definition, out CharacterDefinition canonical, out CharacterSaveState state)) return;

            if (Recovery.RecoveryService.IsCharacterInRecovery(canonical))
            {
                Debug.LogWarning($"[CharacterRoster] '{canonical.CharacterId}'는 회복소 슬롯에 있어 행동력을 " +
                                 "바깥에서 바꿀 수 없습니다 - 합류시킨 뒤에 변경하세요.", this);
                return;
            }

            // 상한도 이벤트 인자도 <b>정식 정의</b>를 쓴다 - 수동 에셋이 더 큰 Max를 들고 있어도
            // 그 값으로 잘리지 않고, 구독자는 언제나 같은 객체를 받는다.
            int clamped = Mathf.Clamp(value, 0, canonical.MaxStamina);
            if (clamped == state.currentStamina) return;

            state.currentStamina = clamped;
            SaveSystem.Save();
            CharacterStateChanged?.Invoke(canonical);
        }

        // ---- 회복소 전용 경로 ----
        //
        // 회복소(Recovery.RecoveryStation)만 쓰는 두 메서드다. 저장과 알림을 호출부가 마지막에 한 번씩만
        // 하도록 "메모리 변경"과 "이벤트 발생"을 나눠 두었다 - 슬롯 3개가 같은 프레임에 한 단계씩 올라도
        // 파일 쓰기가 3번 일어나지 않게 하기 위함이다.

        /// <summary>회복 진행 결과를 현재 행동력에 반영한다. <b>메모리만 바꾸고 저장하지 않으며 이벤트도
        /// 보내지 않는다.</b> 저장과 알림은 회복소가 한 덩어리로 처리한다.</summary>
        /// <returns>값이 실제로 달라졌으면 true.</returns>
        public bool ApplyRecoveryStamina(CharacterDefinition definition, int value)
        {
            // 쓸 수 없는 캐릭터(미보유/저장 전용 id)는 회복 대상이 될 수 없다 - 상태를 만들지 않고
            // 그대로 false다. 상한은 정식 정의의 것을 쓴다.
            if (!TryGetOwnedState(definition, out CharacterDefinition canonical, out CharacterSaveState state))
            {
                return false;
            }

            int clamped = Mathf.Clamp(value, 0, canonical.MaxStamina);
            if (clamped == state.currentStamina) return false;

            state.currentStamina = clamped;
            return true;
        }

        /// <summary>회복소가 저장을 마친 뒤, 값이 바뀐 캐릭터에 대해 상태 변경 이벤트를 대신 보내 달라고
        /// 요청한다. 기존 UI(캐릭터 리스트/행동력 표시)가 이 이벤트를 이미 구독하고 있어 회복소가 별도
        /// 연결을 만들 필요가 없다.</summary>
        public void RaiseCharacterStateChanged(CharacterDefinition definition)
        {
            // 쓸 수 없는 캐릭터의 상태 변경은 알릴 것이 없다 - 구독자(리스트/행동력 표시)가 목록에
            // 없는 캐릭터를 받아 그리려 하면 그때서야 없는 값을 묻게 된다. 알릴 때는 언제나
            // <b>정식 정의</b>를 넘겨, 구독자가 받는 객체가 목록에 있는 그 객체와 같게 한다.
            //
            // 보유가 사라진 캐릭터도 마찬가지로 알리지 않는다 - 구독자가 받아서 그리려는 순간
            // 그 값들은 이미 없다.
            CharacterDefinition canonical = ResolveOwnedUsable(definition);
            if (canonical == null) return;

            CharacterStateChanged?.Invoke(canonical);
        }

        // ---- 개발용 진입점 (정식 사용자 UI에 노출하지 않는다) ----

        // 예전에 있던 RefillAllStaminaToMax(보유 캐릭터 전원의 행동력을 최대치로 되돌리는 테스트 경로)는
        // 제거했다. 회복은 회복소에서 재화를 내고 시간을 기다리는 규칙 하나뿐이며, 그 규칙을 우회해
        // 행동력을 채우는 공개 API가 남아 있으면 회복소가 계산한 값과 충돌한다. 유일한 호출부였던
        // ControlDock의 테스트 버튼(StaminaRefillTestButton)도 함께 무력화했다.

        /// <summary>지금 전투 중인 캐릭터의 행동력을 0으로 만든다 - 소진 상태 표시와 공격 차단을
        /// 바로 확인하기 위한 개발용 단축 경로다.</summary>
        [ContextMenu("Debug - Drain Current Character Stamina")]
        public void DrainCurrentStamina()
        {
            if (current == null) return;
            SetStamina(current, 0);
        }

        /// <summary>Override Stamina On Start가 켜져 있을 때만 동작한다 - 캐릭터들의 현재 행동력을
        /// 지정 값으로 맞추고 한 번만 저장한다. 실제 저장 데이터를 덮어쓰므로 켜져 있다는 사실이
        /// 로그에 반드시 남게 한다.
        ///
        /// <b>회복소 슬롯에 들어 있는 캐릭터는 건너뛴다.</b> 회복 중 행동력은 저장된 시작 시각으로부터
        /// 계산되는 값인데, 회복소의 진행 계산은 <b>현재 값을 하한으로</b> 삼기 때문에(회복이 행동력을
        /// 깎지 않는다는 규칙) 여기서 덮어쓴 값이 그대로 눌러앉는다. 최대치로 덮으면 완료 판정이 바로
        /// true가 되어 회복이 공짜로 끝나 버린다 - 개발용 플래그가 회복 데이터를 망가뜨리지 않도록
        /// 대상에서 제외한다.</summary>
        private void ApplyDebugStartStamina()
        {
            if (!overrideStaminaOnStart) return;

            int applied = 0;
            int skipped = 0;
            for (int i = 0; i < usableEntries.Count; i++)
            {
                CharacterDefinition definition = usableEntries[i].definition;
                if (Recovery.RecoveryService.IsCharacterInRecovery(definition))
                {
                    skipped++;
                    continue;
                }

                // 보유한 캐릭터만 건드린다 - 개발용 플래그가 캐릭터를 지급해서는 안 된다.
                if (!TryGetOwnedState(definition, out _, out CharacterSaveState state)) continue;

                state.currentStamina = Mathf.Clamp(debugStartStamina, 0, definition.MaxStamina);
                applied++;
            }

            if (applied > 0) SaveSystem.Save();

            Debug.LogWarning($"[CharacterRoster] 개발용 Override Stamina On Start가 켜져 있어 캐릭터 {applied}명의 " +
                             $"행동력을 {debugStartStamina}(으)로 덮어썼습니다" +
                             (skipped > 0 ? $"(회복소에 있는 {skipped}명은 제외)" : "") +
                             " - 실제 플레이 검증 전에 끄세요.", this);
        }

        /// <summary>전투 캐릭터를 실제로 갈아 끼운다 - 런타임 액터 하나에 이 캐릭터의 프로필을 적용하고,
        /// <b>적용이 성공했을 때만</b> current를 옮긴다. 캐릭터 오브젝트를 순회하며 켜고 끄는 경로는
        /// 더 이상 없다.
        ///
        /// <paramref name="next"/>가 null이면 <b>아무도 투입하지 않은 상태</b>가 된다(액터를 끄고
        /// current를 null로 둔다). 출전 파티원이 전부 회복소에 있을 때 쓰이며, 회복 중인 캐릭터를
        /// 대신 켜지 않기 위한 정상 경로다 - 시작 시점에도 이 경로가 그대로 관측되도록 상태 변경
        /// 신호를 보낸다.
        ///
        /// 적용에 실패하면 current를 <b>건드리지 않는다</b> - 액터는 직전 캐릭터를 그대로 화면에
        /// 유지(롤백)하므로, 로스터가 기억하는 캐릭터와 화면에 보이는 캐릭터가 어긋나지 않는다.</summary>
        /// <returns>이 호출로 상태가 실제로 적용됐으면 true.</returns>
        private bool ApplyActiveCharacter(CharacterDefinition next)
        {
            if (next == null)
            {
                if (runtimeActor != null) runtimeActor.Deactivate();
                SetCurrentCharacter(null);
                return true;
            }

            if (runtimeActor == null)
            {
                Debug.LogError($"[CharacterRoster] '{next.CharacterId}'를 투입할 수 없습니다 - Runtime Actor가 " +
                               "연결되지 않았습니다. 현재 캐릭터를 바꾸지 않습니다.", this);
                return false;
            }

            if (!runtimeActor.TryApply(next))
            {
                // 액터가 이미 직전 상태로 되돌려 놓았다 - 여기서 current를 옮기지 않는 것이 화면과
                // 로스터를 일치시키는 유일한 방법이다.
                Debug.LogError($"[CharacterRoster] '{next.CharacterId}' 적용에 실패했습니다 - 현재 캐릭터를 " +
                               "바꾸지 않습니다(액터 로그에서 원인을 확인하세요).", this);
                return false;
            }

            SetCurrentCharacter(next);
            return true;
        }

        /// <summary>current를 옮기고 그 사실을 알린다. 콤보는 "이 캐릭터가 지금까지 이어온 타격 수"라서
        /// 새 캐릭터가 물려받으면 안 된다 - 물려받으면 첫 타격부터 상위 티어 공격 풀이 뽑힌다.</summary>
        private void SetCurrentCharacter(CharacterDefinition next)
        {
            current = next;
            ComboManager.ResetCombo();
            CurrentCharacterChanged?.Invoke(current);
        }

    }
}
