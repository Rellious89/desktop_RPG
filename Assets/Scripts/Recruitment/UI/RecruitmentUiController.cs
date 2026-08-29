using System;
using System.Collections.Generic;
using Building;
using Character;
using Common;
using Field;
using Recovery;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Recruitment
{
    /// <summary>모집 화면이 지금 무엇을 보여 주는지. <b>한 번에 하나</b>이며, 겹쳐 켜지는 상태는 없다.</summary>
    public enum RecruitmentUiState
    {
        /// <summary>모집 UI를 전부 감춘다.</summary>
        Hidden = 0,

        /// <summary>보존된 후보가 있다 - 등록/돌려보내기를 물어본다.</summary>
        Result = 1,

        /// <summary>지금 이 모집에서 뽑을 수 있는 용병이 하나도 없다.</summary>
        Exhausted = 2,

        /// <summary>다음 방문까지 기다리는 중이다.</summary>
        Progress = 3,

        /// <summary>뽑을 수 있다.</summary>
        Standby = 4,
    }

    /// <summary>Inn recruitment's read-only screen state. Only initialization and a successful draw save.</summary>
    [DisallowMultipleComponent]
    public sealed class RecruitmentUiController : MonoBehaviour
    {
        [SerializeField] private FieldModeManager fieldModeManager;
        [SerializeField] private FieldTransitionSequencer transitionSequencer;
        [SerializeField] private Camera stageCamera;
        [SerializeField] private Transform uiAnchor;
        [SerializeField] private RectTransform interactionParent;
        [SerializeField] private string buildingId = "1";
        [SerializeField] private RecruitmentAccessCatalog accessCatalog;
        [SerializeField] private RecruitmentTypeCatalog typeCatalog;
        [SerializeField] private RecruitmentPoolCatalog poolCatalog;
        [SerializeField] private CharacterAcquisitionCatalog acquisitionCatalog;
        [SerializeField] private CharacterUnlockConditionCatalog unlockConditionCatalog;
        [SerializeField] private CharacterCatalog characterCatalog;
        [SerializeField] private GameObject progressRoot;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private TextMeshProUGUI timerText;
        [SerializeField] private TextMeshProUGUI percentText;
        [SerializeField] private GameObject standbyRoot;
        [SerializeField] private Button recruitmentButton;
        [SerializeField] private GameObject exhaustedRoot;
        [SerializeField] private GameObject resultRoot;
        [SerializeField] private Image portraitImage;
        [SerializeField] private TextMeshProUGUI characterNameText;
        [SerializeField] private TextMeshProUGUI worldNameText;
        [SerializeField] private GameObject newLabel;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private LocalizedTextReference acquiredToastMessage = new LocalizedTextReference();
        [SerializeField] private LocalizedTextReference returnedToastMessage = new LocalizedTextReference();

        /// <summary>저장 문서를 <b>그때그때 읽는</b> 보유 판정. 매 프레임 새로 만들지 않으려고 하나만 둔다 -
        /// HashSet을 미리 지어 두면 캐릭터를 얻은 순간과 화면이 어긋난다.</summary>
        private static readonly IRecruitmentOwnership Ownership = new SaveDataOwnership();

        private RecruitmentCycleService cycle;
        private RecruitmentCandidateDrawService draw;
        private RecruitmentCandidateResolutionService resolution;
        private bool drawing;
        private bool resolving;
        private bool initializedThisRefresh;
        private string warnedUnreadableCause;
        private CharacterDefinition boundCharacter;

        private void OnEnable()
        {
            if (recruitmentButton != null) recruitmentButton.onClick.AddListener(Draw);
            if (confirmButton != null) confirmButton.onClick.AddListener(Acquire);
            if (cancelButton != null) cancelButton.onClick.AddListener(Return);
            if (progressSlider != null) progressSlider.interactable = false;
            EnsureServices();
            Refresh();
        }

        private void OnDisable()
        {
            if (recruitmentButton != null) recruitmentButton.onClick.RemoveListener(Draw);
            if (confirmButton != null) confirmButton.onClick.RemoveListener(Acquire);
            if (cancelButton != null) cancelButton.onClick.RemoveListener(Return);
            UnbindCharacter();
        }

        private void LateUpdate() => Refresh();

        private void EnsureServices()
        {
            if (cycle != null) return;
            cycle = new RecruitmentCycleService(() => SaveSystem.Data, SaveSystem.Save, () => DateTime.UtcNow,
                accessCatalog, typeCatalog);
            draw = new RecruitmentCandidateDrawService(() => SaveSystem.Data, SaveSystem.Save, () => DateTime.UtcNow,
                cycle, accessCatalog, typeCatalog, poolCatalog, acquisitionCatalog, new SystemRecruitmentRandom(), unlockConditionCatalog);
            resolution = new RecruitmentCandidateResolutionService(() => SaveSystem.Data, SaveSystem.Save,
                () => DateTime.UtcNow, cycle, characterCatalog);
        }

        private void Refresh()
        {
            initializedThisRefresh = false;
            if (!IsTownReady() || !TryPosition()) { Apply(RecruitmentUiState.Hidden); return; }
            EnsureServices();
            var unlocks = new RecruitmentUnlockService(() => SaveSystem.Data, SaveSystem.Save, acquisitionCatalog, unlockConditionCatalog);
            if (!unlocks.TryPersistCurrentUnlocks()) { Apply(RecruitmentUiState.Hidden); return; }
            RecruitmentCycleStatus status = cycle.GetStatus(buildingId);
            if (status.Phase == RecruitmentCyclePhase.NotInitialized)
            {
                if (initializedThisRefresh) { Apply(RecruitmentUiState.Hidden); return; }
                initializedThisRefresh = true;
                RecruitmentCycleInitializeResult initialized = cycle.TryInitialize(buildingId);
                if (!initialized.Success && initialized.Code != RecruitmentCycleInitializeCode.AlreadyInitialized)
                {
                    WarnUnreadable(initialized.Code.ToString()); Apply(RecruitmentUiState.Hidden); return;
                }
                status = cycle.GetStatus(buildingId);
            }
            if (status.Phase == RecruitmentCyclePhase.Unreadable) { WarnUnreadable(status.Access.Outcome.ToString()); Apply(RecruitmentUiState.Hidden); return; }

            string pending = status.State != null ? status.State.pendingCharacterId : null;
            RecruitmentUiState state = ResolveState(status.Phase, pending, HasEligibleCandidate(status.Access));
            if (state == RecruitmentUiState.Result) { ShowResult(pending); return; }
            if (state == RecruitmentUiState.Progress) { ShowProgress(status); return; }
            Apply(state);
        }

        /// <summary>
        /// 지금 켤 화면 하나를 고른다. <b>순수한 판정</b>이며 저장도 난수도 씬도 건드리지 않는다.
        ///
        /// 우선순위는 보존된 후보 → 후보 소진 → 대기 → 준비 완료다. 소진이 대기·준비보다 앞서는 이유는,
        /// 뽑을 사람이 하나도 없는데 남은 시간을 세거나 누를 수 없는 모집 버튼을 보여 주는 것이
        /// 거짓말이기 때문이다. 반대로 <b>보존된 후보는 소진보다 앞선다</b> - 이미 와 있는 용병을
        /// "더 이상 소환할 수 없다"는 말로 덮어 버리면 등록도 돌려보내기도 할 수 없게 된다.
        /// </summary>
        public static RecruitmentUiState ResolveState(
            RecruitmentCyclePhase phase, string pendingCharacterId, bool hasEligibleCandidate)
        {
            if (phase != RecruitmentCyclePhase.Waiting && phase != RecruitmentCyclePhase.Ready)
            {
                return RecruitmentUiState.Hidden;
            }
            if (!string.IsNullOrEmpty(pendingCharacterId)) return RecruitmentUiState.Result;
            if (!hasEligibleCandidate) return RecruitmentUiState.Exhausted;
            return phase == RecruitmentCyclePhase.Waiting ? RecruitmentUiState.Progress : RecruitmentUiState.Standby;
        }

        /// <summary>
        /// 이 모집에서 지금 뽑을 수 있는 용병이 남아 있는지. <b>모든 캐릭터를 세지 않는다</b> - 뽑기와
        /// 같은 <see cref="RecruitmentCandidateSelector"/> 규칙을 그대로 물어보므로, 중복 모집이
        /// 허용된 캐릭터는 이미 보유 중이어도 후보로 남는다.
        /// </summary>
        private bool HasEligibleCandidate(RecruitmentAccessResolution access)
        {
            return RecruitmentCandidateSelector.HasEligibleCandidate(
                access.RecruitmentTypeId, poolCatalog, acquisitionCatalog, Ownership,
                id => SaveSystem.Data.unlockedRecruitmentCharacterIds != null && SaveSystem.Data.unlockedRecruitmentCharacterIds.Contains(id));
        }

        /// <summary>고른 화면 하나만 켠다. 켜고 끄는 자리가 <b>여기 하나뿐</b>이어서, 새 화면을 더할 때
        /// 다른 화면을 끄는 것을 잊을 수 없다.</summary>
        private void Apply(RecruitmentUiState state)
        {
            if (state != RecruitmentUiState.Result) UnbindCharacter();
            Set(progressRoot, state == RecruitmentUiState.Progress);
            Set(standbyRoot, state == RecruitmentUiState.Standby);
            Set(exhaustedRoot, state == RecruitmentUiState.Exhausted);
            Set(resultRoot, state == RecruitmentUiState.Result);
            // 여관 완료 확인 버튼(btn_Open_Inn)의 표시는 TownBuildingInteractionController 하나가 소유한다 -
            // 여기서 매 프레임 끄면 건축 완료 확인 버튼이 곧바로 사라진다.
        }

        private bool IsTownReady() => fieldModeManager != null && fieldModeManager.CurrentMode == FieldMode.Town &&
                                      (transitionSequencer == null || !transitionSequencer.IsPlaying);

        private bool TryPosition()
        {
            if (stageCamera == null || uiAnchor == null || interactionParent == null) return false;
            if (!TownBuildingInteractionController.TryProjectAnchor(stageCamera, uiAnchor.position, interactionParent,
                    TownBuildingInteractionController.ResolveEventCamera(interactionParent.GetComponentInParent<Canvas>()),
                    stageCamera.pixelWidth, stageCamera.pixelHeight, out Vector2 point)) return false;
            Position(progressRoot, point); Position(standbyRoot, point); Position(resultRoot, point);
            Position(exhaustedRoot, point);
            return true;
        }

        private static void Position(GameObject target, Vector2 point)
        {
            if (target != null && target.transform is RectTransform rect) rect.anchoredPosition = point;
        }

        private void ShowProgress(RecruitmentCycleStatus status)
        {
            DateTime start; DateTime ready;
            if (!SaveData.TryParseTimestamp(status.State.startedAtUtc, out start) || !SaveData.TryParseTimestamp(status.State.readyAtUtc, out ready)) { WarnUnreadable("timestamps"); Apply(RecruitmentUiState.Hidden); return; }
            double total = Math.Max(0d, (ready - start).TotalSeconds);
            double elapsed = Math.Max(0d, (DateTime.UtcNow - start).TotalSeconds);
            float progress = total <= 0d ? 1f : Mathf.Clamp01((float)(elapsed / total));
            long seconds = Math.Max(0L, (long)Math.Ceiling(status.Remaining.TotalSeconds));
            // 여기까지 왔다면 뽑을 수 있는 후보가 남아 있다(없으면 Exhausted로 갈렸다).
            if (progress >= 1f) { Apply(RecruitmentUiState.Standby); return; }
            if (progressSlider != null) progressSlider.value = progress;
            if (timerText != null) timerText.text = FormatSeconds(seconds);
            if (percentText != null) percentText.text = Mathf.FloorToInt(progress * 100f) + "%";
            Apply(RecruitmentUiState.Progress);
        }

        private void ShowResult(string id)
        {
            CharacterDefinition character = characterCatalog != null ? characterCatalog.Find(id) : null;
            if (character == null) { WarnUnreadable("missing character " + id); Apply(RecruitmentUiState.Hidden); return; }
            if (boundCharacter != character) BindCharacter(character);
            if (portraitImage != null) portraitImage.sprite = character.Portrait;
            if (newLabel != null) newLabel.SetActive(!Owned(id));
            SetResultButtonsInteractable(!resolving);
            Apply(RecruitmentUiState.Result);
        }
        private void Draw()
        {
            if (drawing || !IsTownReady()) return;

            EnsureServices();
            RecruitmentCycleStatus status = cycle.GetStatus(buildingId);
            if (status.Phase != RecruitmentCyclePhase.Ready || status.State == null ||
                !string.IsNullOrEmpty(status.State.pendingCharacterId)) return;

            drawing = true;
            try { draw.TryDraw(buildingId); }
            finally
            {
                drawing = false;
                Refresh();
            }
        }
        private void Acquire()
        {
            if (resolving || !IsTownReady()) return;
            EnsureServices();
            if (!HasPendingCandidate()) return;

            resolving = true;
            SetResultButtonsInteractable(false);
            try
            {
                RecruitmentCandidateResolutionResult result = resolution.TryAcquire(buildingId);
                if (!result.Success) return;

                RefreshOwnedCharacterSurfaces();
                ShowToast(acquiredToastMessage, result.Character);
                Refresh();
            }
            finally
            {
                resolving = false;
                if (resultRoot != null && resultRoot.activeSelf) SetResultButtonsInteractable(true);
            }
        }
        private void Return()
        {
            if (resolving || !IsTownReady()) return;
            EnsureServices();
            if (!HasPendingCandidate()) return;

            CharacterDefinition candidate = boundCharacter;
            resolving = true;
            SetResultButtonsInteractable(false);
            try
            {
                RecruitmentCandidateResolutionResult result = resolution.TryReturn(buildingId);
                if (!result.Success) return;

                ShowToast(returnedToastMessage, candidate);
                Refresh();
            }
            finally
            {
                resolving = false;
                if (resultRoot != null && resultRoot.activeSelf) SetResultButtonsInteractable(true);
            }
        }
        private bool HasPendingCandidate()
        {
            RecruitmentCycleStatus status = cycle.GetStatus(buildingId);
            return status.State != null && !string.IsNullOrEmpty(status.State.pendingCharacterId);
        }
        private static void RefreshOwnedCharacterSurfaces()
        {
            CharacterRoster.Instance?.RefreshOwnedCharactersAfterExternalSave();
            CharacterSwapPanel.RequestRefresh();
            RecoveryService.NotifyRosterChangedAfterExternalSave();
        }
        private static void ShowToast(LocalizedTextReference message, CharacterDefinition character)
        {
            if (message == null || !message.HasReference || character == null ||
                !character.HasLocalizedName || ToastManager.Instance == null) return;

            ToastManager.Instance.Show(message.GetLocalizedString(character.LocalizedName.GetLocalizedString()));
        }
        private void SetResultButtonsInteractable(bool value)
        {
            if (confirmButton != null) confirmButton.interactable = value;
            if (cancelButton != null) cancelButton.interactable = value;
        }
        private static bool Owned(string id) => Ownership.IsOwned(id);
        private static void Set(GameObject target, bool value) { if (target != null && target.activeSelf != value) target.SetActive(value); }
        private void WarnUnreadable(string cause) { if (warnedUnreadableCause == cause) return; warnedUnreadableCause = cause; Debug.LogWarning("[RecruitmentUiController] unreadable recruitment state: " + cause, this); }
        private void BindCharacter(CharacterDefinition character)
        {
            UnbindCharacter();
            boundCharacter = character;
            SetCharacterName(string.Empty);
            SetWorldName(string.Empty);

            if (character.HasLocalizedName)
            {
                character.LocalizedName.StringChanged += SetCharacterName;
            }
            if (character.OriginWorld != null && character.OriginWorld.HasLocalizedName)
            {
                character.OriginWorld.LocalizedName.StringChanged += SetWorldName;
            }
        }
        private void UnbindCharacter()
        {
            if (boundCharacter == null) return;
            boundCharacter.LocalizedName.StringChanged -= SetCharacterName;
            if (boundCharacter.OriginWorld != null) boundCharacter.OriginWorld.LocalizedName.StringChanged -= SetWorldName;
            boundCharacter = null;
        }
        private void SetCharacterName(string value) { if (characterNameText != null) characterNameText.text = value; }
        private void SetWorldName(string value) { if (worldNameText != null) worldNameText.text = value; }
        private static string FormatSeconds(long seconds) { return string.Format("{0:00}:{1:00}:{2:00}", seconds / 3600, (seconds / 60) % 60, seconds % 60); }

        /// <summary>
        /// 저장 문서를 <b>물어볼 때마다 훑는</b> 보유 판정. 화면이 매 프레임 물어보므로 목록도 HashSet도
        /// 만들지 않으며, 그래서 캐릭터를 등록한 <b>바로 그 프레임</b>에 답이 달라진다 - 미리 지어 둔
        /// 집합이었다면 마지막 한 명을 얻고도 모집 화면이 한 박자 늦게 바뀌었을 것이다.
        /// 비교는 <see cref="IRecruitmentOwnership"/>의 약속대로 <see cref="StringComparison.Ordinal"/>이다.
        /// </summary>
        private sealed class SaveDataOwnership : IRecruitmentOwnership
        {
            public bool IsOwned(string characterId)
            {
                if (string.IsNullOrEmpty(characterId)) return false;

                List<CharacterSaveState> characters = SaveSystem.Data?.characters;
                if (characters == null) return false;

                for (int i = 0; i < characters.Count; i++)
                {
                    CharacterSaveState state = characters[i];
                    if (state != null && string.Equals(state.characterId, characterId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
