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
        private LocalizedTextReference boundElapsedFormat;
        private LocalizedTextReference boundDayOrMoreText;
        private string localizedElapsedFormat;
        private string localizedDayOrMoreText;
        private string elapsedTimeFallback;
        private bool usesDayOrMoreText;
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

        /// <summary>
        /// 01_UI / 34의 HH:mm:ss 토큰만 현재 시간 값으로 바꾼다. 참조가 비었거나 토큰이 사라진
        /// 비정상 형식에서는 시간 값 자체를 반환해 결과가 공백이 되지 않게 한다.
        /// </summary>
        public static string ApplyElapsedFormat(string localizedFormat, string elapsedValue)
        {
            if (string.IsNullOrEmpty(elapsedValue)) return string.Empty;
            if (string.IsNullOrEmpty(localizedFormat)) return elapsedValue;
            if (localizedFormat.IndexOf("HH:mm:ss", StringComparison.Ordinal) < 0)
                return elapsedValue;

            return localizedFormat.Replace("HH:mm:ss", elapsedValue);
        }

        private void ApplyElapsedTime(double elapsedSeconds)
        {
            // 사용자가 lb_Timer에 연결한 LocalizedTMPText의 01_UI / 34 참조를 그대로 인계받는다.
            // 정적 컴포넌트는 끄고 이 패널이 같은 참조를 구독해야 시간 토큰과 Locale 변경을 함께
            // 처리할 수 있으며, 프리팹에 중복 Localization 필드를 추가할 필요도 없다.
            LocalizedTextReference elapsedFormat = null;
            if (elapsedTimeText.TryGetComponent(out LocalizedTMPText staticLocalizer))
            {
                elapsedFormat = staticLocalizer.TextReference;
                staticLocalizer.enabled = false;
            }

            usesDayOrMoreText = !TryFormatElapsedTime(elapsedSeconds, out string formatted);
            elapsedTimeFallback = usesDayOrMoreText
                ? FormatElapsedTimeWithoutDayLimit(elapsedSeconds)
                : formatted;
            localizedElapsedFormat = null;
            localizedDayOrMoreText = null;
            RefreshElapsedTimeText();

            if (elapsedFormat != null && elapsedFormat.HasReference)
            {
                boundElapsedFormat = elapsedFormat;
                boundElapsedFormat.StringChanged += ApplyLocalizedElapsedFormat;
            }

            if (!usesDayOrMoreText) return;

            if (dayOrMoreText != null && dayOrMoreText.HasReference)
            {
                boundDayOrMoreText = dayOrMoreText;
                boundDayOrMoreText.StringChanged += ApplyLocalizedDayOrMoreText;
                return;
            }

            Debug.LogWarning("[DungeonResultPanel] 24시간 이상 문구(01_UI / 38)가 연결되지 않아 " +
                             "시간 값 자체를 표시합니다.", this);
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

        private void ApplyLocalizedElapsedFormat(string value)
        {
            localizedElapsedFormat = value;
            RefreshElapsedTimeText();
        }

        private void ApplyLocalizedDayOrMoreText(string value)
        {
            localizedDayOrMoreText = value;
            RefreshElapsedTimeText();
        }

        private void RefreshElapsedTimeText()
        {
            if (elapsedTimeText == null) return;

            string value = usesDayOrMoreText && !string.IsNullOrEmpty(localizedDayOrMoreText)
                ? localizedDayOrMoreText
                : elapsedTimeFallback;
            elapsedTimeText.text = ApplyElapsedFormat(localizedElapsedFormat, value);
        }

        private void UnbindLocalizedText()
        {
            if (boundDungeonName != null)
            {
                boundDungeonName.StringChanged -= ApplyLocalizedDungeonName;
                boundDungeonName = null;
            }

            if (boundElapsedFormat != null)
            {
                boundElapsedFormat.StringChanged -= ApplyLocalizedElapsedFormat;
                boundElapsedFormat = null;
            }

            if (boundDayOrMoreText != null)
            {
                boundDayOrMoreText.StringChanged -= ApplyLocalizedDayOrMoreText;
                boundDayOrMoreText = null;
            }

            localizedElapsedFormat = null;
            localizedDayOrMoreText = null;
            elapsedTimeFallback = null;
            usesDayOrMoreText = false;
        }

        private void ClearRewardItems()
        {
            for (int i = 0; i < spawnedItems.Count; i++)
            {
                DungeonResultRewardItemView item = spawnedItems[i];
                if (item == null) continue;

                // Destroy는 프레임 끝에 실행된다 - 그 전에 그리던 보상을 비워, 마우스를 올린 채 패널이
                // 갱신돼도 사라질 줄의 툴팁이 화면에 남지 않게 한다.
                item.Clear();
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

        private static string FormatElapsedTimeWithoutDayLimit(double elapsedSeconds)
        {
            double normalized = NormalizeElapsedSeconds(elapsedSeconds);
            long totalSeconds = normalized >= long.MaxValue
                ? long.MaxValue
                : (long)Math.Floor(normalized);
            long hours = totalSeconds / 3600L;
            long minutes = totalSeconds % 3600L / 60L;
            long seconds = totalSeconds % 60L;
            return string.Format(
                CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", hours, minutes, seconds);
        }
    }
}
