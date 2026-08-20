using System;
using System.Collections.Generic;
using System.Globalization;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dungeon
{
    /// <summary>
    /// 완료된 던전 세션 스냅샷을 표시하는 확인형 모달. 표시만 담당하며 보상 지급이나 저장은 하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonResultPanel : ModalPanel
    {
        public const double DayThresholdSeconds = 24d * 60d * 60d;

        [Header("Result Text")]
        [SerializeField] private TextMeshProUGUI dungeonNameText;
        [SerializeField] private TextMeshProUGUI elapsedTimeText;
        [SerializeField] private TextMeshProUGUI defeatedMonsterCountText;
        [SerializeField] private TextMeshProUGUI earnedCurrencyText;

        [Header("Result Items")]
        [SerializeField] private RectTransform rewardItemContent;
        [SerializeField] private DungeonResultRewardItemView rewardItemPrefab;

        [Header("Confirm")]
        [SerializeField] private Button confirmButton;

        [Header("Localization")]
        [Tooltip("24시간 이상일 때 표시할 01_UI / 38 참조.")]
        [SerializeField] private LocalizedTextReference dayOrMoreText = new LocalizedTextReference();

        private readonly List<DungeonResultRewardItemView> spawnedItems =
            new List<DungeonResultRewardItemView>();

        private DungeonSessionSnapshot snapshot;
        private LocalizedTextReference boundDungeonName;
        private LocalizedTextReference boundElapsedText;
        private bool referencesValidated;

        public event Action<long> ConfirmationRequested;

        public bool HasSnapshot => snapshot != null;
        public long DisplayedSessionSequence => snapshot != null ? snapshot.SessionSequence : 0L;
        public int SpawnedRewardItemCount => spawnedItems.Count;

        /// <summary>표시 가능한 참조가 모두 있을 때만 스냅샷을 보관하고 패널을 연다.</summary>
        public bool ShowSnapshot(DungeonSessionSnapshot value)
        {
            if (value == null || !ValidateReferences()) return false;

            snapshot = value;
            Open();
            return true;
        }

        protected override void OnModalOpened()
        {
            if (confirmButton == null) return;
            confirmButton.onClick.RemoveListener(Close);
            confirmButton.onClick.AddListener(Close);
        }

        protected override void OnModalClosed()
        {
            if (confirmButton != null) confirmButton.onClick.RemoveListener(Close);
            UnbindLocalizedText();
            ClearRewardItems();
            snapshot = null;
        }

        protected override void OnCloseRequested()
        {
            if (snapshot != null)
                ConfirmationRequested?.Invoke(snapshot.SessionSequence);
        }

        protected override void RefreshContents()
        {
            UnbindLocalizedText();
            ClearRewardItems();

            if (snapshot == null)
            {
                ClearDynamicTexts();
                return;
            }

            BindDungeonName(snapshot.DungeonDefinition);
            ApplyElapsedTime(snapshot.ElapsedSeconds);

            defeatedMonsterCountText.text =
                snapshot.DefeatedMonsterCount.ToString(CultureInfo.InvariantCulture);
            earnedCurrencyText.text =
                snapshot.EarnedCurrency.ToString(CultureInfo.InvariantCulture);

            for (int i = 0; i < snapshot.EarnedItems.Count; i++)
            {
                DungeonResultRewardItemView item = Instantiate(rewardItemPrefab, rewardItemContent, false);
                item.Bind(snapshot.EarnedItems[i]);
                spawnedItems.Add(item);
            }
        }

        public static bool TryFormatElapsedTime(double elapsedSeconds, out string formatted)
        {
            double normalized = NormalizeElapsedSeconds(elapsedSeconds);
            if (normalized >= DayThresholdSeconds)
            {
                formatted = null;
                return false;
            }

            long totalSeconds = (long)Math.Floor(normalized);
            long hours = totalSeconds / 3600L;
            long minutes = totalSeconds % 3600L / 60L;
            long seconds = totalSeconds % 60L;
            formatted = string.Format(
                CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", hours, minutes, seconds);
            return true;
        }

        private void ApplyElapsedTime(double elapsedSeconds)
        {
            DungeonStaticLocalizerGuard.DisableIfPresent(elapsedTimeText, nameof(DungeonResultPanel));

            if (TryFormatElapsedTime(elapsedSeconds, out string formatted))
            {
                elapsedTimeText.text = formatted;
                return;
            }

            elapsedTimeText.text = string.Empty;
            if (dayOrMoreText == null || !dayOrMoreText.HasReference)
            {
                Debug.LogWarning("[DungeonResultPanel] 24시간 이상 문구(01_UI / 38)가 연결되지 않았습니다.", this);
                return;
            }

            boundElapsedText = dayOrMoreText;
            boundElapsedText.StringChanged += ApplyLocalizedElapsedText;
        }

        private void BindDungeonName(DungeonDefinition dungeon)
        {
            DungeonStaticLocalizerGuard.DisableIfPresent(dungeonNameText, nameof(DungeonResultPanel));
            dungeonNameText.text = string.Empty;

            if (dungeon == null || !dungeon.HasDungeonName) return;

            boundDungeonName = dungeon.DungeonName;
            boundDungeonName.StringChanged += ApplyLocalizedDungeonName;
        }

        private void ApplyLocalizedDungeonName(string value)
        {
            if (dungeonNameText != null) dungeonNameText.text = value;
        }

        private void ApplyLocalizedElapsedText(string value)
        {
            if (elapsedTimeText != null) elapsedTimeText.text = value;
        }

        private void UnbindLocalizedText()
        {
            if (boundDungeonName != null)
            {
                boundDungeonName.StringChanged -= ApplyLocalizedDungeonName;
                boundDungeonName = null;
            }

            if (boundElapsedText != null)
            {
                boundElapsedText.StringChanged -= ApplyLocalizedElapsedText;
                boundElapsedText = null;
            }
        }

        private void ClearRewardItems()
        {
            for (int i = 0; i < spawnedItems.Count; i++)
            {
                DungeonResultRewardItemView item = spawnedItems[i];
                if (item == null) continue;
                item.gameObject.SetActive(false);
                Destroy(item.gameObject);
            }
            spawnedItems.Clear();
        }

        private void ClearDynamicTexts()
        {
            if (dungeonNameText != null) dungeonNameText.text = string.Empty;
            if (elapsedTimeText != null) elapsedTimeText.text = string.Empty;
            if (defeatedMonsterCountText != null) defeatedMonsterCountText.text = string.Empty;
            if (earnedCurrencyText != null) earnedCurrencyText.text = string.Empty;
        }

        private bool ValidateReferences()
        {
            if (referencesValidated) return true;

            bool valid = dungeonNameText != null && elapsedTimeText != null &&
                         defeatedMonsterCountText != null && earnedCurrencyText != null &&
                         rewardItemContent != null && rewardItemPrefab != null && confirmButton != null;
            if (!valid)
            {
                Debug.LogError("[DungeonResultPanel] 필수 Inspector 참조가 누락되어 정산 결과를 표시하지 않습니다.", this);
                return false;
            }

            referencesValidated = true;
            return true;
        }

        private static double NormalizeElapsedSeconds(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0d) return 0d;
            return value;
        }
    }
}
