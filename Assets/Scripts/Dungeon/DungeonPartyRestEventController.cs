using System;
using System.Collections.Generic;
using Character;
using Common;
using Enemy;
using Field;
using Party;
using Recovery;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// 던전에서 회복소에 들어 있지 않은 출전 파티 전원의 행동력이 0이 되면 모닥불 휴식 연출을 표시하고
    /// 공격 입력을 막는다. 행동력 회복 자체는 기존 자연 회복 시스템이 계속 담당하며, 같은 유효 파티원이
    /// 전부 설정 비율까지 회복된 뒤에만 마지막 전투 캐릭터를 다시 표시하고 공격을 허용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonPartyRestEventController : MonoBehaviour
    {
        /// <summary>
        /// 현재 휴식 연출 슬롯에 배치된 캐릭터의 행동력 상태. 슬롯 순서와 회복 대상 제외는
        /// 컨트롤러의 partyBuffer를 그대로 사용하므로 Presenter가 roster를 다시 해석하지 않는다.
        /// </summary>
        public readonly struct RestSlotStatus
        {
            public RestSlotStatus(string characterId, int currentStamina, int requiredStamina)
            {
                CharacterId = characterId ?? string.Empty;
                CurrentStamina = Mathf.Max(0, currentStamina);
                RequiredStamina = Mathf.Max(0, requiredStamina);
            }

            public string CharacterId { get; }
            public int CurrentStamina { get; }
            public int RequiredStamina { get; }
        }

        private sealed class GrayscaleRendererState
        {
            public readonly SpriteRenderer Renderer;
            public readonly Material OriginalMaterial;
            public readonly MaterialPropertyBlock OriginalPropertyBlock;
            public readonly bool HadOriginalPropertyBlock;

            public GrayscaleRendererState(SpriteRenderer renderer)
            {
                Renderer = renderer;
                OriginalMaterial = renderer.sharedMaterial;
                OriginalPropertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(OriginalPropertyBlock);
                HadOriginalPropertyBlock = !OriginalPropertyBlock.isEmpty;
            }

            public void Restore()
            {
                if (Renderer == null) return;
                if (Renderer.sharedMaterial != OriginalMaterial)
                    Renderer.sharedMaterial = OriginalMaterial;
                Renderer.SetPropertyBlock(HadOriginalPropertyBlock ? OriginalPropertyBlock : null);

                // 휴식 중 외곽선 설정이 바뀌었다면 현재 전역 설정을 다시 반영한다.
                ActorOutlineController outline = Renderer.GetComponent<ActorOutlineController>();
                if (outline != null && outline.isActiveAndEnabled) outline.Refresh();
            }
        }

        private static readonly int GrayscaleAmountId = Shader.PropertyToID("_GrayscaleAmount");
        private static readonly int OutlineEnabledId = Shader.PropertyToID("_OutlineEnabled");

        private sealed class RestCharacterView
        {
            private readonly Transform slot;
            private readonly Vector3 authoredScale;
            private readonly SpriteRenderer renderer;

            private Sprite[] frames = Array.Empty<Sprite>();
            private float secondsPerFrame = 1f;
            private float elapsed;
            private int frameIndex;

            public SpriteRenderer Renderer => renderer;

            public RestCharacterView(Transform slot, SpriteRenderer presentationTemplate)
            {
                this.slot = slot;
                authoredScale = slot != null ? slot.localScale : Vector3.one;

                if (slot == null) return;

                renderer = slot.GetComponent<SpriteRenderer>();
                if (renderer == null) renderer = slot.gameObject.AddComponent<SpriteRenderer>();

                CopyPresentation(presentationTemplate);
                renderer.enabled = false;
                renderer.sprite = null;

                // 휴식용 뷰도 기존 캐릭터와 같은 전역 외곽선 설정을 사용한다. 공격/피격 기능은 붙이지
                // 않고 SpriteRenderer와 Idle 재생만 두어 전투 액터가 여러 개 생기지 않게 한다.
                if (slot.GetComponent<ActorOutlineController>() == null)
                    slot.gameObject.AddComponent<ActorOutlineController>();
            }

            public void Show(
                CharacterDefinition definition,
                SpriteRenderer presentationTemplate,
                bool flipX)
            {
                CharacterMotionProfile profile = definition != null ? definition.MotionProfile : null;
                if (renderer == null || !CharacterMotionProfile.IsPlayable(profile))
                {
                    Hide();
                    return;
                }

                CopyPresentation(presentationTemplate);
                frames = profile.BaseIdle.Frames;
                secondsPerFrame = 1f / profile.BaseIdle.AnimationFps;
                elapsed = 0f;
                frameIndex = 0;

                float actorScale = profile.Preview != null ? profile.Preview.ActorScale : 1f;
                slot.localScale = new Vector3(
                    authoredScale.x * actorScale,
                    authoredScale.y * actorScale,
                    authoredScale.z);

                renderer.color = Color.white;
                renderer.flipY = false;
                renderer.flipX = flipX;
                renderer.sprite = frames[0];
                renderer.enabled = true;
            }

            public void Tick(float deltaTime)
            {
                if (renderer == null || !renderer.enabled || frames.Length <= 1) return;

                elapsed += Mathf.Max(0f, deltaTime);
                while (elapsed >= secondsPerFrame)
                {
                    elapsed -= secondsPerFrame;
                    frameIndex = (frameIndex + 1) % frames.Length;
                    renderer.sprite = frames[frameIndex];
                }
            }

            public void Hide()
            {
                frames = Array.Empty<Sprite>();
                elapsed = 0f;
                frameIndex = 0;
                if (slot != null) slot.localScale = authoredScale;
                if (renderer == null) return;
                renderer.enabled = false;
                renderer.sprite = null;
            }

            private void CopyPresentation(SpriteRenderer template)
            {
                if (renderer == null || template == null) return;
                renderer.sortingLayerID = template.sortingLayerID;
                renderer.sortingOrder = template.sortingOrder;
                renderer.maskInteraction = template.maskInteraction;
            }
        }

        [Header("Runtime References")]
        [SerializeField] private FieldModeManager fieldModeManager;
        [SerializeField] private CharacterRoster roster;
        [SerializeField] private PlayerCharacterAnimator playerAnimator;
        [SerializeField] private SpriteRenderer playerRenderer;
        [Tooltip("현재/다음 몬스터 표시를 관리하는 던전 몬스터 대기열입니다.")]
        [SerializeField] private MonsterEncounterQueue monsterEncounterQueue;

        [Header("Rest Presentation")]
        [Tooltip("모닥불과 휴식 캐릭터 슬롯을 함께 담은 루트. 컨트롤러 자신이 아닌 별도 자식이어야 합니다.")]
        [SerializeField] private GameObject restEventRoot;
        [Tooltip("휴식 이벤트가 시작될 때 Rest Event Root 아래에 생성할 모닥불 프리팹입니다.")]
        [SerializeField] private GameObject campfirePrefab;
        [Tooltip("생성된 모닥불의 Rest Event Root 기준 로컬 위치입니다.")]
        [SerializeField] private Vector3 campfireLocalPosition = Vector3.zero;
        [Tooltip("생성된 모닥불의 Rest Event Root 기준 로컬 회전값입니다.")]
        [SerializeField] private Vector3 campfireLocalEulerAngles = Vector3.zero;
        [Tooltip("생성된 모닥불의 로컬 크기입니다.")]
        [SerializeField] private Vector3 campfireLocalScale = Vector3.one;
        [Tooltip("저장된 파티 순서대로 사용하는 휴식 캐릭터 슬롯. 현재 최대 3개입니다.")]
        [SerializeField] private Transform[] characterSlots = new Transform[3];
        [Tooltip("슬롯별 캐릭터 좌우 반전. 체크하지 않으면 원본 방향, 체크하면 X축으로 뒤집습니다.")]
        [SerializeField] private bool[] characterSlotFlipX = new bool[3];

        [Header("Monster Presentation During Rest")]
        [Tooltip("휴식 중 현재 몬스터와 다음 몬스터의 채도 제거 강도입니다. 0은 원본, 1은 완전한 회색입니다.")]
        [Range(0f, 1f)]
        [SerializeField] private float grayscaleAmount = 1f;

        private readonly List<CharacterDefinition> partyBuffer = new List<CharacterDefinition>(3);
        private RestCharacterView[] views = Array.Empty<RestCharacterView>();
        private CharacterDefinition returnCharacter;
        private bool restorePlayerRendererEnabled;
        private GameObject campfireInstance;
        private bool didWarnMissingCampfirePrefab;
        private readonly List<GrayscaleRendererState> grayscaleRendererStates =
            new List<GrayscaleRendererState>(2);
        private readonly HashSet<SpriteRenderer> grayscaleRendererBuffer = new HashSet<SpriteRenderer>();
        // MaterialPropertyBlock은 native object라 MonoBehaviour field initializer에서 만들 수 없다.
        // 휴식 효과를 실제로 처음 적용할 때 하나만 만들어 이후 갱신에서 재사용한다.
        private MaterialPropertyBlock grayscalePropertyBlock;

        public bool IsResting { get; private set; }
        public float ResumeStaminaRatio => DungeonCombatRules.MinimumStaminaRatio;
        public float GrayscaleAmount => Mathf.Clamp01(grayscaleAmount);

        /// <summary>최대 행동력과 복귀 비율로 실제 복귀에 필요한 정수 행동력을 계산한다.</summary>
        public static int CalculateRequiredStamina(int maximumStamina, float resumeRatio)
        {
            return DungeonCombatRules.CalculateRequiredStamina(maximumStamina, resumeRatio);
        }

        /// <summary>Companion 모드가 휴식 캐릭터마다 독립된 클릭 영역을 만들 때 사용하는 슬롯 수.</summary>
        public int InteractionSlotCount => characterSlots != null ? characterSlots.Length : 0;

        /// <summary>휴식 중 지정 슬롯에 표시된 캐릭터 렌더러. 비어 있거나 휴식 중이 아니면 null이다.</summary>
        public SpriteRenderer GetInteractionRenderer(int slotIndex)
        {
            if (!IsResting || slotIndex < 0 || slotIndex >= views.Length || views[slotIndex] == null) return null;
            SpriteRenderer renderer = views[slotIndex].Renderer;
            return renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy ? renderer : null;
        }

        /// <summary>
        /// 휴식 연출에 실제로 포함된 슬롯의 최신 행동력 상태를 반환한다. 비어 있거나 휴식 중이
        /// 아니면 false이며, partyBuffer의 고정 파티 순서와 회복소 제외 정책을 그대로 따른다.
        /// </summary>
        public bool TryGetRestSlotStatus(int slotIndex, out RestSlotStatus status)
        {
            status = default;
            if (!IsResting || roster == null || slotIndex < 0 || slotIndex >= partyBuffer.Count)
                return false;

            CharacterDefinition definition = partyBuffer[slotIndex];
            if (definition == null) return false;

            int required = CalculateRequiredStamina(roster.GetMaxStamina(definition), ResumeStaminaRatio);
            if (required <= 0) return false;

            status = new RestSlotStatus(
                definition.CharacterId,
                roster.GetStamina(definition),
                required);
            return true;
        }

        private void Awake()
        {
            ResolveFallbackReferences();

            if (restEventRoot == gameObject)
            {
                Debug.LogError("[DungeonPartyRestEvent] Rest Event Root는 컨트롤러 자신이 아닌 자식 오브젝트여야 합니다.", this);
                restEventRoot = null;
            }

            if (restEventRoot != null) restEventRoot.SetActive(false);
            WarnIfCampfirePrefabMissing();
            BuildViews();
        }

        private void OnEnable()
        {
            CharacterRoster.CharacterStateChanged -= HandleCharacterStateChanged;
            CharacterRoster.CharacterStateChanged += HandleCharacterStateChanged;
            CharacterRoster.RosterEntriesChanged -= HandleRosterEntriesChanged;
            CharacterRoster.RosterEntriesChanged += HandleRosterEntriesChanged;
            PartyCompositionEvents.ChangedAfterSave -= HandleRosterEntriesChanged;
            PartyCompositionEvents.ChangedAfterSave += HandleRosterEntriesChanged;
            RecoveryService.SlotsChanged -= HandleRecoverySlotsChanged;
            RecoveryService.SlotsChanged += HandleRecoverySlotsChanged;

            if (fieldModeManager != null)
            {
                fieldModeManager.FieldModeChanged -= HandleFieldModeChanged;
                fieldModeManager.FieldModeChanged += HandleFieldModeChanged;
            }

            SubscribeMonsterPresentationEvents();
        }

        private void Start()
        {
            EvaluateRestState();
        }

        private void Update()
        {
            if (!IsResting) return;
            float deltaTime = Time.unscaledDeltaTime;
            for (int i = 0; i < views.Length; i++) views[i]?.Tick(deltaTime);
        }

        private void LateUpdate()
        {
            // 외곽선 설정은 렌더러의 Material/PropertyBlock을 갱신할 수 있다. 렌더 직전에
            // 현재 몬스터 둘의 휴식 효과를 확인하면 설정 변경 후에도 회색 표시가 유지된다.
            if (IsResting) RefreshMonsterGrayscale();
        }

        private void OnDisable()
        {
            CharacterRoster.CharacterStateChanged -= HandleCharacterStateChanged;
            CharacterRoster.RosterEntriesChanged -= HandleRosterEntriesChanged;
            PartyCompositionEvents.ChangedAfterSave -= HandleRosterEntriesChanged;
            RecoveryService.SlotsChanged -= HandleRecoverySlotsChanged;
            if (fieldModeManager != null) fieldModeManager.FieldModeChanged -= HandleFieldModeChanged;
            UnsubscribeMonsterPresentationEvents();

            StopRest(false);
        }

        private void OnValidate()
        {
            grayscaleAmount = Mathf.Clamp01(grayscaleAmount);
        }

        private void HandleCharacterStateChanged(CharacterDefinition ignoredCharacter)
        {
            EvaluateRestState();
        }

        private void HandleRosterEntriesChanged()
        {
            EvaluateRestState();
            if (IsResting) RefreshRestViews();
        }

        private void HandleRecoverySlotsChanged()
        {
            // 회복 슬롯은 파티 편성을 바꾸지 않지만, 던전에서 실제로 행동 가능한 휴식 대상은 바꾼다.
            // RecoveryService의 기존 저장 기반 상태와 변경 신호만 사용해 발생/표시/복귀 판정을
            // 같은 순간에 다시 계산한다.
            EvaluateRestState();
            if (IsResting) RefreshRestViews();
        }

        private void HandleFieldModeChanged(FieldMode mode, DungeonDefinition ignoredDungeon)
        {
            if (mode != FieldMode.Dungeon)
            {
                StopRest(false);
                return;
            }

            EvaluateRestState();
        }

        private void EvaluateRestState()
        {
            if (fieldModeManager == null || roster == null) return;
            if (fieldModeManager.CurrentMode != FieldMode.Dungeon)
            {
                if (IsResting) StopRest(false);
                return;
            }

            CollectParty();
            if (partyBuffer.Count == 0)
            {
                if (IsResting) StopRest(false);
                return;
            }

            if (!IsResting)
            {
                if (AllPartyMembersExhausted()) StartRest();
                return;
            }

            if (AllPartyMembersReady()) StopRest(true);
        }

        private void StartRest()
        {
            if (IsResting || restEventRoot == null || playerAnimator == null || playerRenderer == null) return;

            IsResting = true;
            returnCharacter = roster.Current;
            restorePlayerRendererEnabled = playerRenderer.enabled;

            playerAnimator.SetPartyRestCombatBlocked(true);
            ComboManager.ResetCombo();
            playerRenderer.enabled = false;
            RefreshMonsterGrayscale();
            RefreshRestViews();
            ShowCampfire();
            restEventRoot.SetActive(true);
        }

        private void StopRest(bool restoreLastCharacter)
        {
            if (!IsResting)
            {
                ClearMonsterGrayscale();
                HideCampfire();
                if (restEventRoot != null) restEventRoot.SetActive(false);
                if (playerAnimator != null) playerAnimator.SetPartyRestCombatBlocked(false);
                return;
            }

            IsResting = false;
            ClearMonsterGrayscale();
            HideCampfire();
            if (restEventRoot != null) restEventRoot.SetActive(false);
            HideRestViews();

            if (restoreLastCharacter && returnCharacter != null && roster != null && roster.Current != returnCharacter)
                roster.TrySwitchTo(returnCharacter, out _);

            if (playerRenderer != null) playerRenderer.enabled = restorePlayerRendererEnabled;
            if (playerAnimator != null) playerAnimator.SetPartyRestCombatBlocked(false);
            returnCharacter = null;
        }

        private bool AllPartyMembersExhausted()
        {
            for (int i = 0; i < partyBuffer.Count; i++)
            {
                if (roster.GetStamina(partyBuffer[i]) > 0) return false;
            }
            return true;
        }

        private bool AllPartyMembersReady()
        {
            for (int i = 0; i < partyBuffer.Count; i++)
            {
                CharacterDefinition definition = partyBuffer[i];
                int required = CalculateRequiredStamina(
                    roster.GetMaxStamina(definition),
                    ResumeStaminaRatio);
                if (required <= 0) return false;
                if (roster.GetStamina(definition) < required) return false;
            }
            return true;
        }

        private void CollectParty()
        {
            partyBuffer.Clear();
            if (roster == null) return;

            IReadOnlyList<CharacterRoster.Entry> entries = roster.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                CharacterDefinition definition = entries[i] != null ? entries[i].definition : null;
                if (definition != null && !RecoveryService.IsCharacterInRecovery(definition))
                    partyBuffer.Add(definition);
            }
        }

        private void RefreshRestViews()
        {
            CollectParty();
            int visibleCount = Mathf.Min(views.Length, partyBuffer.Count);
            for (int i = 0; i < visibleCount; i++)
                views[i]?.Show(partyBuffer[i], playerRenderer, IsSlotFlipped(i));
            for (int i = visibleCount; i < views.Length; i++) views[i]?.Hide();
        }

        private bool IsSlotFlipped(int slotIndex)
        {
            return characterSlotFlipX != null
                   && slotIndex >= 0
                   && slotIndex < characterSlotFlipX.Length
                   && characterSlotFlipX[slotIndex];
        }

        private void HideRestViews()
        {
            for (int i = 0; i < views.Length; i++) views[i]?.Hide();
        }

        private void SubscribeMonsterPresentationEvents()
        {
            if (monsterEncounterQueue == null) return;

            monsterEncounterQueue.CurrentPromoted -= HandleMonsterPresentationChanged;
            monsterEncounterQueue.CurrentPromoted += HandleMonsterPresentationChanged;
            monsterEncounterQueue.ExitingStarted -= HandleMonsterPresentationChanged;
            monsterEncounterQueue.ExitingStarted += HandleMonsterPresentationChanged;
            monsterEncounterQueue.StandbyRefilled -= HandleMonsterPresentationChanged;
            monsterEncounterQueue.StandbyRefilled += HandleMonsterPresentationChanged;
        }

        private void UnsubscribeMonsterPresentationEvents()
        {
            if (monsterEncounterQueue == null) return;

            monsterEncounterQueue.CurrentPromoted -= HandleMonsterPresentationChanged;
            monsterEncounterQueue.ExitingStarted -= HandleMonsterPresentationChanged;
            monsterEncounterQueue.StandbyRefilled -= HandleMonsterPresentationChanged;
        }

        private void HandleMonsterPresentationChanged(TargetCombatController ignoredMonster)
        {
            if (IsResting) RefreshMonsterGrayscale();
        }

        private void RefreshMonsterGrayscale()
        {
            if (!IsResting || monsterEncounterQueue == null)
            {
                ClearMonsterGrayscale();
                return;
            }

            grayscaleRendererBuffer.Clear();
            CollectMonsterRenderers(monsterEncounterQueue.CurrentMonster);
            CollectMonsterRenderers(monsterEncounterQueue.StandbyMonster);

            for (int i = grayscaleRendererStates.Count - 1; i >= 0; i--)
            {
                GrayscaleRendererState state = grayscaleRendererStates[i];
                if (state.Renderer != null && grayscaleRendererBuffer.Contains(state.Renderer)) continue;

                state.Restore();
                grayscaleRendererStates.RemoveAt(i);
            }

            foreach (SpriteRenderer renderer in grayscaleRendererBuffer)
            {
                if (renderer == null) continue;

                GrayscaleRendererState state = FindGrayscaleState(renderer);
                if (state == null)
                {
                    state = new GrayscaleRendererState(renderer);
                    grayscaleRendererStates.Add(state);
                }

                Material material = renderer.sharedMaterial;
                if (material == null || !material.HasProperty(GrayscaleAmountId))
                {
                    Material fallback = state.OriginalMaterial;
                    if (fallback == null || !fallback.HasProperty(GrayscaleAmountId))
                    {
                        ActorOutlineSettings settings = ActorOutlineSettings.Active;
                        fallback = settings != null ? settings.OutlineMaterial : null;
                    }

                    if (fallback == null || !fallback.HasProperty(GrayscaleAmountId)) continue;
                    renderer.sharedMaterial = fallback;
                }

                if (grayscalePropertyBlock == null)
                    grayscalePropertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(grayscalePropertyBlock);
                ActorOutlineSettings outlineSettings = ActorOutlineSettings.Active;
                if (outlineSettings == null || !outlineSettings.OutlineEnabled)
                    grayscalePropertyBlock.SetFloat(OutlineEnabledId, 0f);
                grayscalePropertyBlock.SetFloat(GrayscaleAmountId, GrayscaleAmount);
                renderer.SetPropertyBlock(grayscalePropertyBlock);
                grayscalePropertyBlock.Clear();
            }

            grayscaleRendererBuffer.Clear();
        }

        private void CollectMonsterRenderers(TargetCombatController monster)
        {
            if (monster == null) return;

            // TargetCombatController가 프레임을 재생하는 본체만 변경한다. 자식의 이펙트나
            // 장식 렌더러까지 몬스터 리소스로 간주하면 휴식 연출 범위가 불필요하게 넓어진다.
            SpriteRenderer renderer = monster.GetComponent<SpriteRenderer>();
            if (renderer != null) grayscaleRendererBuffer.Add(renderer);
        }

        private GrayscaleRendererState FindGrayscaleState(SpriteRenderer renderer)
        {
            for (int i = 0; i < grayscaleRendererStates.Count; i++)
            {
                if (grayscaleRendererStates[i].Renderer == renderer) return grayscaleRendererStates[i];
            }
            return null;
        }

        private void ClearMonsterGrayscale()
        {
            for (int i = grayscaleRendererStates.Count - 1; i >= 0; i--)
                grayscaleRendererStates[i].Restore();

            grayscaleRendererStates.Clear();
            grayscaleRendererBuffer.Clear();
            grayscalePropertyBlock?.Clear();
        }

        private void ShowCampfire()
        {
            GameObject instance = EnsureCampfireInstance();
            if (instance != null) instance.SetActive(true);
        }

        private void HideCampfire()
        {
            if (campfireInstance != null) campfireInstance.SetActive(false);
        }

        private GameObject EnsureCampfireInstance()
        {
            if (campfireInstance != null) return campfireInstance;

            if (campfirePrefab == null)
            {
                WarnIfCampfirePrefabMissing();
                return null;
            }

            if (restEventRoot == null) return null;

            campfireInstance = Instantiate(campfirePrefab, restEventRoot.transform, false);
            Transform instanceTransform = campfireInstance.transform;
            instanceTransform.SetSiblingIndex(0);
            instanceTransform.localPosition = campfireLocalPosition;
            instanceTransform.localRotation = Quaternion.Euler(campfireLocalEulerAngles);
            instanceTransform.localScale = campfireLocalScale;
            campfireInstance.SetActive(false);
            return campfireInstance;
        }

        private void WarnIfCampfirePrefabMissing()
        {
            if (campfirePrefab != null || didWarnMissingCampfirePrefab) return;

            didWarnMissingCampfirePrefab = true;
            Debug.LogWarning(
                "[DungeonPartyRestEvent] Campfire Prefab이 지정되지 않았습니다. 모닥불 없이 캐릭터 휴식 이벤트를 계속 진행합니다.",
                this);
        }

        private void BuildViews()
        {
            if (characterSlots == null)
            {
                views = Array.Empty<RestCharacterView>();
                return;
            }

            views = new RestCharacterView[characterSlots.Length];
            for (int i = 0; i < characterSlots.Length; i++)
            {
                if (characterSlots[i] != null)
                    views[i] = new RestCharacterView(characterSlots[i], playerRenderer);
            }
        }

        private void ResolveFallbackReferences()
        {
            if (fieldModeManager == null) fieldModeManager = FindObjectOfType<FieldModeManager>();
            if (roster == null) roster = CharacterRoster.Instance != null
                ? CharacterRoster.Instance
                : FindObjectOfType<CharacterRoster>();
            if (playerAnimator == null) playerAnimator = FindObjectOfType<PlayerCharacterAnimator>();
            if (playerRenderer == null && playerAnimator != null)
                playerRenderer = playerAnimator.GetComponent<SpriteRenderer>();
            if (monsterEncounterQueue == null)
                monsterEncounterQueue = FindObjectOfType<MonsterEncounterQueue>();
        }
    }
}
