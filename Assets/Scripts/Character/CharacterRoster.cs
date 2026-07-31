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
    /// 교체 방식은 기존 구조를 그대로 쓴다: 씬에 미리 배치된 캐릭터 GameObject를 켜고 끈다.
    /// PlayerCharacterAnimator/AttackMovement가 OnDisable에서 대기열·충전·발사체·이동을 스스로
    /// 정리하고 OnEnable에서 Base Idle부터 다시 시작하므로, 공격 도중에 교체해도 이전 캐릭터의
    /// 입력이나 자세가 남지 않는다.
    ///
    /// 데이터 원천은 두 곳으로 명확히 나뉜다.
    ///   - <see cref="CharacterDefinition"/> 에셋: 캐릭터가 무엇인지(이름/초상화/최대 행동력)
    ///   - SaveData.characters: 캐릭터가 지금 어떤 상태인지(레벨/현재 행동력)
    /// 씬 오브젝트는 "그 캐릭터를 화면에 그리는 수단"일 뿐, 목록의 근거가 아니다.
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
    public class CharacterRoster : MonoBehaviour
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
            /// <summary>로스터에 없거나 씬 오브젝트가 연결되지 않은 캐릭터.</summary>
            NotAvailable,
            /// <summary>회복소 슬롯에 들어가 있다(회복 중이거나, 회복이 끝나 합류를 기다리는 중).
            /// 행동력이 남아 있어도 교체할 수 없다 - 슬롯에서 합류시켜야 다시 쓸 수 있다.</summary>
            InRecovery,
        }

        [Serializable]
        public class Entry
        {
            [Tooltip("이 캐릭터의 정의 에셋.")]
            public CharacterDefinition definition;

            [Tooltip("씬에 배치된 이 캐릭터의 GameObject(PlayerCharacterAnimator가 붙어 있는 오브젝트).")]
            public GameObject characterObject;
        }

        [Header("Roster")]
        [Tooltip("보유 캐릭터 목록. 순서가 곧 교체 패널 리스트 순서이자 테스트 버튼의 순환 순서다.")]
        [SerializeField] private List<Entry> entries = new List<Entry>();

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

        // 같은 처치 이벤트가 두 번 들어와도 행동력이 두 번 깎이지 않게 막는 최소 방어(판정 규칙은
        // DefeatEventFilter 참고). 보상 지급 쪽은 자기 필터를 따로 들고 있다 - 하나를 공유하면
        // 먼저 처리한 쪽이 다른 쪽의 이벤트를 삼켜서 둘이 서로에게 영향을 준다.
        private readonly DefeatEventFilter defeatFilter = new DefeatEventFilter();

        public IReadOnlyList<Entry> Entries => usableEntries;

        /// <summary>지금 전투 중인 캐릭터. 사용 가능한 항목이 하나도 없으면 null이다.</summary>
        public CharacterDefinition Current => current;

        /// <summary>지금 전투 중인 캐릭터가 <b>새 공격을 시작</b>할 수 있는지(행동력 &gt; 0).
        /// PlayerCharacterAnimator가 입력을 받을지 판단할 때 Target.HasAttackableTarget과 함께 본다.
        ///
        /// 로스터가 없는 씬이나 사용 가능한 캐릭터가 <b>하나도 없는</b> 구성에서는 항상 true를 돌려준다 -
        /// 행동력 시스템을 쓰지 않는 씬의 기존 전투를 막지 않기 위함이다.
        ///
        /// 반면 <b>캐릭터는 있는데 아무도 투입되지 않은 상태</b>(전원이 회복소 슬롯에 있어 시작
        /// 캐릭터를 고르지 못한 경우)는 false다. 예전에는 이 경우도 "current가 null" 하나로 묶여
        /// true가 됐는데, 그러면 화면에 아무도 없는 채로 공격 입력이 통한다.</summary>
        public static bool CurrentCharacterCanAct
        {
            get
            {
                CharacterRoster roster = Instance;
                // 로스터를 쓰지 않는 씬 / 로스터는 있지만 캐릭터 목록이 비어 있는 구성 - 기존 동작 유지.
                if (roster == null || roster.usableEntries.Count == 0) return true;

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

            BuildUsableEntries();
            if (usableEntries.Count == 0)
            {
                Debug.LogError("[CharacterRoster] 사용할 수 있는 캐릭터가 하나도 없습니다 - Entries에 " +
                               "Definition과 씬 캐릭터 오브젝트를 연결하세요.", this);
                return;
            }

            SyncSaveStates();
            ApplyDebugStartStamina();
            ApplyActiveCharacter(ResolveStartCharacter());
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

        /// <summary>비어 있거나 중복된 슬롯을 걸러내고, 정의와 씬 오브젝트가 서로 다른 캐릭터를
        /// 가리키는 설정 실수(프로필 불일치)를 시작 시 한 번에 드러낸다.</summary>
        private void BuildUsableEntries()
        {
            usableEntries.Clear();
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
                if (entry.characterObject == null)
                {
                    Debug.LogError($"[CharacterRoster] '{entry.definition.CharacterId}'에 씬 캐릭터 오브젝트가 " +
                                   "연결되지 않았습니다 - 이 항목은 무시합니다.", this);
                    continue;
                }
                if (!seenIds.Add(entry.definition.CharacterId))
                {
                    Debug.LogError($"[CharacterRoster] Character Id '{entry.definition.CharacterId}'가 중복됩니다 - " +
                                   "저장 데이터가 서로 섞이므로 뒤에 있는 항목은 무시합니다.", this);
                    continue;
                }

                WarnOnProfileMismatch(entry);
                usableEntries.Add(entry);
            }
        }

        /// <summary>정의가 참조하는 모션 프로필과 씬 오브젝트가 실제로 재생하는 프로필이 다르면,
        /// 리스트에 표시되는 이름/초상화와 화면에 나오는 캐릭터가 어긋난다 - 조용히 두지 않는다.</summary>
        private void WarnOnProfileMismatch(Entry entry)
        {
            if (entry.definition.MotionProfile == null) return;

            var animator = entry.characterObject.GetComponent<PlayerCharacterAnimator>();
            if (animator == null || animator.MotionProfile == null) return;
            if (animator.MotionProfile == entry.definition.MotionProfile) return;

            Debug.LogError($"[CharacterRoster] '{entry.definition.CharacterId}'의 Definition이 참조하는 프로필" +
                           $"('{entry.definition.MotionProfile.name}')과 씬 오브젝트 '{entry.characterObject.name}'의 " +
                           $"프로필('{animator.MotionProfile.name}')이 다릅니다.", this);
        }

        /// <summary>저장 문서에 이번 로스터의 캐릭터 항목이 모두 존재하도록 맞춘다. 새로 추가된
        /// 캐릭터는 정의의 기본값으로 채우고, 저장된 현재 행동력은 최대치를 넘지 않게 자른다
        /// (Max Stamina를 나중에 낮춘 경우).</summary>
        private void SyncSaveStates()
        {
            for (int i = 0; i < usableEntries.Count; i++)
            {
                CharacterDefinition definition = usableEntries[i].definition;
                CharacterSaveState state = GetOrCreateState(definition);
                state.currentStamina = Mathf.Clamp(
                    state.currentStamina < 0 ? definition.MaxStamina : state.currentStamina,
                    0,
                    definition.MaxStamina);
            }
        }

        private CharacterSaveState GetOrCreateState(CharacterDefinition definition)
        {
            List<CharacterSaveState> states = SaveSystem.Data.characters;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] != null && states[i].characterId == definition.CharacterId) return states[i];
            }

            var created = new CharacterSaveState
            {
                characterId = definition.CharacterId,
                level = 1,
                currentStamina = definition.MaxStamina,
            };
            states.Add(created);
            return created;
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
            if (defaultCharacter != null && IndexOf(defaultCharacter) >= 0)
            {
                if (!Recovery.RecoveryService.IsCharacterInRecovery(defaultCharacter)) return defaultCharacter;

                Debug.Log($"[CharacterRoster] Default Character('{defaultCharacter.CharacterId}')가 회복소에 " +
                          "있어 다른 캐릭터로 시작합니다.", this);
            }
            else if (defaultCharacter != null)
            {
                Debug.LogWarning($"[CharacterRoster] Default Character('{defaultCharacter.CharacterId}')가 Entries에 " +
                                 "없어 목록의 첫 번째 캐릭터로 시작합니다.", this);
            }

            for (int i = 0; i < usableEntries.Count; i++)
            {
                CharacterDefinition candidate = usableEntries[i].definition;
                if (!Recovery.RecoveryService.IsCharacterInRecovery(candidate)) return candidate;
            }

            // 보유 캐릭터가 전부 회복소에 들어가 있다. 회복 중인 캐릭터를 억지로 켜지 않고 아무도
            // 투입하지 않은 상태로 시작한다 - CurrentCharacterCanAct가 false가 되어 공격도 막힌다.
            Debug.LogWarning("[CharacterRoster] 보유한 모든 캐릭터가 회복소에 있어 전투에 투입할 캐릭터가 " +
                             "없습니다 - 회복이 끝난 캐릭터를 합류시키면 다시 선택할 수 있습니다.", this);
            return null;
        }

        // ---- 조회 ----

        public int GetLevel(CharacterDefinition definition)
        {
            return definition == null ? 0 : GetOrCreateState(definition).level;
        }

        public int GetStamina(CharacterDefinition definition)
        {
            return definition == null ? 0 : GetOrCreateState(definition).currentStamina;
        }

        public int GetMaxStamina(CharacterDefinition definition)
        {
            return definition == null ? 0 : definition.MaxStamina;
        }

        /// <summary>이 캐릭터로 교체할 수 없는 이유. <see cref="SwapBlockReason.None"/>이면 교체 가능하다.</summary>
        public SwapBlockReason GetSwapBlockReason(CharacterDefinition definition)
        {
            if (definition == null || IndexOf(definition) < 0) return SwapBlockReason.NotAvailable;
            if (definition == current) return SwapBlockReason.AlreadyCurrent;
            // 회복 중에는 행동력이 이미 차 있어도 교체할 수 없다 - 행동력 값이 아니라 "슬롯에 있는가"가
            // 근거이므로 행동력 판정보다 먼저 본다.
            if (Recovery.RecoveryService.IsCharacterInRecovery(definition)) return SwapBlockReason.InRecovery;
            if (GetStamina(definition) <= 0) return SwapBlockReason.NoStamina;
            return SwapBlockReason.None;
        }

        // ---- 변경 ----

        /// <summary>전투 캐릭터를 교체한다. 교체가 실제로 일어났으면 true, 막혔으면 false를 돌려주고
        /// 그 이유를 <paramref name="reason"/>에 담는다(호출부가 사용자에게 표시할 수 있도록).</summary>
        public bool TrySwitchTo(CharacterDefinition definition, out SwapBlockReason reason)
        {
            reason = GetSwapBlockReason(definition);
            if (reason != SwapBlockReason.None) return false;

            ApplyActiveCharacter(definition);
            return true;
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
            if (Recovery.RecoveryService.IsCharacterInRecovery(definition))
            {
                Debug.LogWarning($"[CharacterRoster] '{definition.CharacterId}'는 회복소 슬롯에 있어 행동력을 " +
                                 "바깥에서 바꿀 수 없습니다 - 합류시킨 뒤에 변경하세요.", this);
                return;
            }

            CharacterSaveState state = GetOrCreateState(definition);
            int clamped = Mathf.Clamp(value, 0, definition.MaxStamina);
            if (clamped == state.currentStamina) return;

            state.currentStamina = clamped;
            SaveSystem.Save();
            CharacterStateChanged?.Invoke(definition);
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
            if (definition == null) return false;

            CharacterSaveState state = GetOrCreateState(definition);
            int clamped = Mathf.Clamp(value, 0, definition.MaxStamina);
            if (clamped == state.currentStamina) return false;

            state.currentStamina = clamped;
            return true;
        }

        /// <summary>회복소가 저장을 마친 뒤, 값이 바뀐 캐릭터에 대해 상태 변경 이벤트를 대신 보내 달라고
        /// 요청한다. 기존 UI(캐릭터 리스트/행동력 표시)가 이 이벤트를 이미 구독하고 있어 회복소가 별도
        /// 연결을 만들 필요가 없다.</summary>
        public void RaiseCharacterStateChanged(CharacterDefinition definition)
        {
            if (definition == null) return;
            CharacterStateChanged?.Invoke(definition);
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

                GetOrCreateState(definition).currentStamina = Mathf.Clamp(debugStartStamina, 0, definition.MaxStamina);
                applied++;
            }

            if (applied > 0) SaveSystem.Save();

            Debug.LogWarning($"[CharacterRoster] 개발용 Override Stamina On Start가 켜져 있어 캐릭터 {applied}명의 " +
                             $"행동력을 {debugStartStamina}(으)로 덮어썼습니다" +
                             (skipped > 0 ? $"(회복소에 있는 {skipped}명은 제외)" : "") +
                             " - 실제 플레이 검증 전에 끄세요.", this);
        }

        /// <summary>이전 캐릭터를 먼저 끄고 새 캐릭터를 켠다 - 순서를 지켜야 두 캐릭터가 같은 프레임에
        /// 동시에 활성화되지 않는다. 목록에 있는 나머지 캐릭터도 모두 꺼서, 씬에서 실수로 켜둔
        /// 오브젝트가 남아 함께 공격하는 상황을 막는다.
        ///
        /// <paramref name="next"/>가 null이면 <b>아무도 투입하지 않은 상태</b>가 된다(모든 캐릭터
        /// 오브젝트를 끄고 <see cref="current"/>를 null로 둔다). 보유 캐릭터가 전부 회복소에 있을 때
        /// 쓰이며, 회복 중인 캐릭터를 대신 켜지 않기 위한 정상 경로다.</summary>
        private void ApplyActiveCharacter(CharacterDefinition next)
        {
            for (int i = 0; i < usableEntries.Count; i++)
            {
                Entry entry = usableEntries[i];
                if (next != null && entry.definition == next) continue;
                if (entry.characterObject.activeSelf) entry.characterObject.SetActive(false);
            }

            int index = IndexOf(next);
            // next가 null이면 index도 -1이지만, 그때는 "아무도 없음"이 정상 결과이므로 여기서 멈추지
            // 않고 current를 null로 갱신하고 이벤트까지 보낸다. 로스터에 없는 캐릭터를 넘긴 경우
            // (index < 0 이면서 next != null)만 상태를 바꾸지 않고 돌아간다.
            if (index < 0 && next != null) return;

            if (index >= 0) usableEntries[index].characterObject.SetActive(true);

            bool changed = current != next;
            current = next;
            if (!changed) return;

            // 콤보는 "이 캐릭터가 지금까지 이어온 타격 수"라서 새 캐릭터가 물려받으면 안 된다 -
            // 물려받으면 첫 타격부터 상위 티어 공격 풀이 뽑힌다.
            ComboManager.ResetCombo();
            CurrentCharacterChanged?.Invoke(current);
        }

        private int IndexOf(CharacterDefinition definition)
        {
            if (definition == null) return -1;
            for (int i = 0; i < usableEntries.Count; i++)
            {
                if (usableEntries[i].definition == definition) return i;
            }
            return -1;
        }
    }
}
