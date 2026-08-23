using System;
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
    /// <summary>Inn recruitment's read-only screen state. Only initialization and a successful draw save.</summary>
    [DisallowMultipleComponent]
    public sealed class RecruitmentUiController : MonoBehaviour
    {
        [SerializeField] private FieldModeManager fieldModeManager;
        [SerializeField] private FieldTransitionSequencer transitionSequencer;
        [SerializeField] private Camera stageCamera;
        [SerializeField] private Transform uiAnchor;
        [SerializeField] private RectTransform interactionParent;
        [SerializeField] private GameObject openInnButton;
        [SerializeField] private string buildingId = "1";
        [SerializeField] private RecruitmentAccessCatalog accessCatalog;
        [SerializeField] private RecruitmentTypeCatalog typeCatalog;
        [SerializeField] private RecruitmentPoolCatalog poolCatalog;
        [SerializeField] private CharacterAcquisitionCatalog acquisitionCatalog;
        [SerializeField] private CharacterCatalog characterCatalog;
        [SerializeField] private GameObject progressRoot;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private TextMeshProUGUI timerText;
        [SerializeField] private TextMeshProUGUI percentText;
        [SerializeField] private GameObject standbyRoot;
        [SerializeField] private Button recruitmentButton;
        [SerializeField] private GameObject resultRoot;
        [SerializeField] private Image portraitImage;
        [SerializeField] private TextMeshProUGUI characterNameText;
        [SerializeField] private TextMeshProUGUI worldNameText;
        [SerializeField] private GameObject newLabel;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private LocalizedTextReference acquiredToastMessage = new LocalizedTextReference();
        [SerializeField] private LocalizedTextReference returnedToastMessage = new LocalizedTextReference();

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
                cycle, accessCatalog, typeCatalog, poolCatalog, acquisitionCatalog, new SystemRecruitmentRandom());
            resolution = new RecruitmentCandidateResolutionService(() => SaveSystem.Data, SaveSystem.Save,
                () => DateTime.UtcNow, cycle, characterCatalog);
        }

        private void Refresh()
        {
            initializedThisRefresh = false;
            if (!IsTownReady() || !TryPosition()) { HideAll(); return; }
            EnsureServices();
            RecruitmentCycleStatus status = cycle.GetStatus(buildingId);
            if (status.Phase == RecruitmentCyclePhase.NotInitialized)
            {
                if (initializedThisRefresh) { HideAll(); return; }
                initializedThisRefresh = true;
                RecruitmentCycleInitializeResult initialized = cycle.TryInitialize(buildingId);
                if (!initialized.Success && initialized.Code != RecruitmentCycleInitializeCode.AlreadyInitialized)
                {
                    WarnUnreadable(initialized.Code.ToString()); HideAll(); return;
                }
                status = cycle.GetStatus(buildingId);
            }
            if (status.Phase == RecruitmentCyclePhase.Unreadable) { WarnUnreadable(status.Access.Outcome.ToString()); HideAll(); return; }
            if (status.Phase == RecruitmentCyclePhase.Locked) { HideAll(); return; }
            if (status.State != null && !string.IsNullOrEmpty(status.State.pendingCharacterId)) { ShowResult(status.State.pendingCharacterId); return; }
            if (status.Phase == RecruitmentCyclePhase.Waiting) { ShowProgress(status); return; }
            if (status.Phase == RecruitmentCyclePhase.Ready) { ShowStandby(); return; }
            HideAll();
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
            return true;
        }

        private static void Position(GameObject target, Vector2 point)
        {
            if (target != null && target.transform is RectTransform rect) rect.anchoredPosition = point;
        }

        private void ShowProgress(RecruitmentCycleStatus status)
        {
            DateTime start; DateTime ready;
            if (!SaveData.TryParseTimestamp(status.State.startedAtUtc, out start) || !SaveData.TryParseTimestamp(status.State.readyAtUtc, out ready)) { WarnUnreadable("timestamps"); HideAll(); return; }
            double total = Math.Max(0d, (ready - start).TotalSeconds);
            double elapsed = Math.Max(0d, (DateTime.UtcNow - start).TotalSeconds);
            float progress = total <= 0d ? 1f : Mathf.Clamp01((float)(elapsed / total));
            long seconds = Math.Max(0L, (long)Math.Ceiling(status.Remaining.TotalSeconds));
            if (progress >= 1f) { ShowStandby(); return; }
            if (progressSlider != null) progressSlider.value = progress;
            if (timerText != null) timerText.text = FormatSeconds(seconds);
            if (percentText != null) percentText.text = Mathf.FloorToInt(progress * 100f) + "%";
            Set(progressRoot, true); Set(standbyRoot, false); Set(resultRoot, false); Set(openInnButton, false);
        }

        private void ShowStandby() { UnbindCharacter(); Set(progressRoot, false); Set(standbyRoot, true); Set(resultRoot, false); Set(openInnButton, false); }
        private void ShowResult(string id)
        {
            CharacterDefinition character = characterCatalog != null ? characterCatalog.Find(id) : null;
            if (character == null) { WarnUnreadable("missing character " + id); HideAll(); return; }
            if (boundCharacter != character) BindCharacter(character);
            if (portraitImage != null) portraitImage.sprite = character.Portrait;
            if (newLabel != null) newLabel.SetActive(!Owned(id));
            SetResultButtonsInteractable(!resolving);
            Set(progressRoot, false); Set(standbyRoot, false); Set(resultRoot, true); Set(openInnButton, false);
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
        private bool Owned(string id)
        {
            if (SaveSystem.Data?.characters == null) return false;
            foreach (CharacterSaveState state in SaveSystem.Data.characters) if (state != null && state.characterId == id) return true;
            return false;
        }
        private void HideAll() { UnbindCharacter(); Set(progressRoot, false); Set(standbyRoot, false); Set(resultRoot, false); Set(openInnButton, false); }
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
    }
}
