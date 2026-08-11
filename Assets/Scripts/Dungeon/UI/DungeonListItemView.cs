using System;
using System.Globalization;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dungeon
{
    /// <summary>
    /// 던전 목록의 항목 하나(item_dungeonList). 자기 값을 계산하지 않고 <see cref="DungeonPanel"/>이
    /// 넘겨준 던전 정의만 그리며, 어떤 항목이 선택 상태인지도 패널이 정해서 알려준다.
    ///
    /// <b>이름은 던전마다 다르므로 코드가 직접 구독한다.</b> Inspector에 키를 박아두는
    /// <see cref="LocalizedTMPText"/>로는 처리할 수 없어서, 정의의 <see cref="LocalizedTextReference"/>를
    /// 구독했다가 바인딩이 끊길 때 반드시 짝지어 해제한다. 같은 텍스트에 LocalizedTMPText가 남아
    /// 있으면 <see cref="DungeonStaticLocalizerGuard"/>가 실행 중에 꺼서 두 번 써지는 것을 막는다.
    ///
    /// <b>선택 표시는 새 아트를 쓰지 않는다.</b> 프리팹의 Button은 Color Tint 전환이고 대상 Graphic이
    /// 루트 Image다 - 선택된 항목은 그 Button의 ColorBlock에서 <b>Normal 색만 Selected 색으로 바꿔</b>
    /// 유지한다. 이렇게 하면 마우스를 뗀 뒤에도(Unity의 Selected 상태가 풀려도) 선택 표시가 남고,
    /// 하이라이트/눌림 색은 프리팹 값 그대로 동작한다. 원래 ColorBlock은 바인딩 시점에 기억해 두고
    /// 선택이 풀리면 되돌린다.
    ///
    /// <b>잠김 상태에서도 선택은 된다.</b> 레벨 미달로 입장할 수 없는 던전도 목록에서 고를 수 있어
    /// 상세(몬스터/보상)를 볼 수 있다 - 입장 버튼만 <see cref="DungeonPanel"/>이 잠근다. 잠김 표시는
    /// 이름과 레벨 텍스트의 알파를 낮추는 것으로, Button.interactable은 건드리지 않는다.
    ///
    /// <b>잠김 표시는 프리팹 색을 지우지 않는다.</b> 이름/레벨 텍스트의 색은 바인딩할 때마다 그대로
    /// 기억해 두고, 잠기면 <b>기억해 둔 알파에 배수를 곱해</b> 어둡게만 한다 - RGB는 건드리지 않는다.
    /// 풀리거나 바인딩이 끊기면 기억해 둔 색을 그대로 되돌리므로, 잠금이 여러 번 반복돼도 색이
    /// 점점 흐려지지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class DungeonListItemView : MonoBehaviour
    {
        /// <summary>잠긴 항목의 알파에 곱하는 배수. 절대값이 아니라 <b>프리팹에 설정된 알파에 대한
        /// 비율</b>이므로, 원래 반투명하게 만들어 둔 텍스트도 비율만큼만 더 어두워진다.</summary>
        private const float LockedAlphaMultiplier = 0.4f;

        [Header("References (에디터에서 직접 연결한다 - 이름으로 찾지 않는다)")]
        [Tooltip("항목 클릭을 받는 Button(루트). 비워두면 이 GameObject의 Button을 쓴다.")]
        [SerializeField] private Button selectButton;

        [Tooltip("던전 이름을 표시할 TextMeshProUGUI(list_DungeonName).")]
        [SerializeField] private TextMeshProUGUI nameText;

        [Tooltip("필요 레벨을 표시할 TextMeshProUGUI(lb_RequiredLevel).")]
        [SerializeField] private TextMeshProUGUI requiredLevelText;

        [Tooltip("대표 이미지를 표시할 Image(선택). 지금 프리팹에는 없으므로 비워 두면 되고, " +
                 "나중에 추가하면 연결만 하면 된다 - 정의에 이미지가 없으면 이 Image는 꺼진다.")]
        [SerializeField] private Image representativeImage;

        private DungeonDefinition boundDungeon;
        private Action<DungeonDefinition> selectionCallback;

        private LocalizedTextReference boundName;
        private bool missingNameWarned;

        private ColorBlock originalColors;
        private bool originalColorsCaptured;
        private bool selected;
        private bool locked;

        private Color originalNameColor;
        private Color originalRequiredLevelColor;
        private bool originalTextColorsCaptured;

        /// <summary>이 항목이 표시 중인 던전. 패널이 선택 표시를 갱신할 때 비교에 쓴다.</summary>
        public DungeonDefinition BoundDungeon => boundDungeon;

        /// <summary>지금 선택 표시가 켜져 있는지. 검증/디버깅용 읽기 전용 값이다.</summary>
        public bool IsSelected => selected;

        /// <summary>지금 화면에 그려져 있는 이름. 검증/디버깅용 읽기 전용 값이다.</summary>
        public string CurrentNameText => nameText != null ? nameText.text : null;

        /// <summary>지금 화면에 그려져 있는 필요 레벨 문구(예: "Lv. 5"). 검증/테스트용 읽기 전용 값이다.</summary>
        public string CurrentRequirementText => requiredLevelText != null ? requiredLevelText.text : null;

        /// <summary>이 항목이 잠김 표시(레벨 미달) 상태인지. 검증/테스트용 읽기 전용 값이다.</summary>
        public bool IsLocked => locked;

        private void Reset()
        {
            // 에디터에서 컴포넌트를 처음 붙일 때 <b>비어 있는 참조만</b> 채운다. 이름 탐색이 아니라
            // 같은 오브젝트/자식의 컴포넌트를 집는 것이며, 실행 중에는 이 경로를 지나지 않는다.
            if (selectButton == null) selectButton = GetComponent<Button>();
            if (nameText == null) nameText = GetComponentInChildren<TextMeshProUGUI>(true);
        }

        private void OnDestroy()
        {
            UnbindName();
            if (selectButton != null) selectButton.onClick.RemoveListener(HandleClicked);
        }

        /// <summary>이 항목이 담당할 던전과 클릭 콜백을 정한다. 같은 항목을 다시 바인딩해도 리스너와
        /// 문구 구독이 중복되지 않는다 - 먼저 이전 것을 끊고 다시 건다.</summary>
        public void Bind(DungeonDefinition dungeon, Action<DungeonDefinition> onSelected)
        {
            boundDungeon = dungeon;
            selectionCallback = onSelected;

            if (selectButton == null) selectButton = GetComponent<Button>();
            if (selectButton != null)
            {
                selectButton.onClick.RemoveListener(HandleClicked);
                selectButton.onClick.AddListener(HandleClicked);
            }
            else
            {
                Debug.LogError($"[DungeonListItemView] '{name}': Button이 없어 항목을 선택할 수 없습니다 - " +
                               "루트의 Button을 연결하세요.", this);
            }

            CaptureOriginalColors();
            // 잠김 표시를 되돌릴 기준색은 <b>이번 바인딩 시점</b>의 색이다 - 이전 바인딩에서 어둡게
            // 해 둔 상태로 다시 바인딩되면 그 색을 원본으로 착각하므로, 안에서 먼저 되돌린 뒤 기억한다.
            CaptureTextColors();
            locked = false;

            ApplyName(dungeon);
            ApplyRepresentative(dungeon);
            ApplyRequiredLevel(dungeon);
            SetSelected(false);
        }

        /// <summary>바인딩을 끊는다. 복제본을 지우기 전에 호출해서 문구 구독과 클릭 리스너를 남기지
        /// 않는다 - 파괴 경로에서도 <see cref="OnDestroy"/>가 한 번 더 정리한다.</summary>
        public void Unbind()
        {
            UnbindName();
            selectionCallback = null;
            boundDungeon = null;

            if (selectButton != null) selectButton.onClick.RemoveListener(HandleClicked);

            RestoreOriginalColors();
            RestoreTextColors();
            selected = false;
            locked = false;
        }

        /// <summary>선택 표시를 켜고 끈다. 패널이 이전 항목과 새 항목 양쪽에 대해 호출한다.</summary>
        public void SetSelected(bool isSelected)
        {
            selected = isSelected;

            if (selectButton == null || !originalColorsCaptured) return;

            ColorBlock colors = originalColors;
            if (isSelected)
            {
                // 마우스가 떠난 뒤에도 남아야 하므로 Normal 색 자체를 Selected 색으로 바꾼다.
                colors.normalColor = originalColors.selectedColor;
            }

            // ColorBlock을 대입하면 Selectable이 현재 상태의 색을 곧바로 다시 적용한다.
            selectButton.colors = colors;
        }

        /// <summary>접근 판정 결과를 시각에 반영한다. 잠김이면 이름/레벨 텍스트를 <b>기억해 둔 색의
        /// 알파에 <see cref="LockedAlphaMultiplier"/>를 곱해</b> 어둡게 하고, 풀렸으면 기억해 둔 색을
        /// 그대로 되돌린다 - 어느 쪽이든 결과가 현재 색이 아니라 기억해 둔 색에서 계산되므로 여러 번
        /// 호출해도 값이 누적되지 않는다. Button.interactable은 건드리지 않는다 - 잠긴 던전도 선택할 수 있다.</summary>
        public void SetAccessResult(DungeonAccessResult result)
        {
            // 바인딩 없이 호출되는 경로(테스트/직접 사용)에서도 되돌릴 기준색은 있어야 한다.
            if (!originalTextColorsCaptured) CaptureTextColors();

            locked = !result.Allowed;

            if (!locked)
            {
                RestoreTextColors();
                return;
            }

            ApplyLockedAlpha(nameText, originalNameColor);
            ApplyLockedAlpha(requiredLevelText, originalRequiredLevelColor);
        }

        private static void ApplyLockedAlpha(TextMeshProUGUI text, Color authored)
        {
            if (text == null) return;

            Color dimmed = authored;
            dimmed.a = authored.a * LockedAlphaMultiplier;
            text.color = dimmed;
        }

        private void HandleClicked()
        {
            if (boundDungeon == null) return;
            selectionCallback?.Invoke(boundDungeon);
        }

        // ---- 이름(로컬라이징) ----

        private void ApplyName(DungeonDefinition dungeon)
        {
            UnbindName();

            if (nameText == null)
            {
                Debug.LogError($"[DungeonListItemView] '{name}': 이름 텍스트가 연결되지 않아 던전 이름을 " +
                               "표시할 수 없습니다 - list_DungeonName의 TextMeshProUGUI를 연결하세요.", this);
                return;
            }

            // 같은 텍스트를 정적 키로 덮어쓰는 컴포넌트가 남아 있으면 실행 중에는 꺼 둔다.
            DungeonStaticLocalizerGuard.DisableIfPresent(nameText, nameof(DungeonListItemView));

            LocalizedTextReference reference = dungeon != null ? dungeon.DungeonName : null;
            if (reference == null || !reference.HasReference)
            {
                // 번역 값 누락은 Unity Localization의 fallback이 처리한다. 여기로 오는 것은 Table/Key
                // 참조 자체가 없는 설정 오류다 - 한국어/영어를 코드에 적어 메우지 않고 비워 둔다.
                nameText.text = string.Empty;

                if (!missingNameWarned)
                {
                    missingNameWarned = true;
                    string id = dungeon != null ? dungeon.DungeonId : "(없음)";
                    Debug.LogWarning($"[DungeonListItemView] 던전 '{id}'의 이름에 Localization Table/Key가 " +
                                     "지정되지 않아 이름을 비워 둡니다 - 던전 에셋에서 Category와 Key를 지정하세요.", this);
                }
                return;
            }

            boundName = reference;
            // 구독 자체가 최초 로드를 유발하고, 이후 Locale이 바뀌면 자동으로 다시 호출된다.
            boundName.StringChanged += ApplyLocalizedName;
        }

        private void UnbindName()
        {
            if (boundName == null) return;

            boundName.StringChanged -= ApplyLocalizedName;
            boundName = null;
        }

        private void ApplyLocalizedName(string localizedText)
        {
            if (nameText != null) nameText.text = localizedText;
        }

        // ---- 필요 레벨 ----

        /// <summary>필요 레벨 문구를 만든다. 이 문구는 번역 대상이 아니라 <b>고정 형식</b>이므로
        /// <see cref="CultureInfo.InvariantCulture"/>로 만든다 - 사용자의 CurrentCulture가 무엇이든
        /// 자릿수 구분자나 다른 숫자 표기가 끼어들지 않고 언제나 정확히 "Lv. N"이 된다.</summary>
        private void ApplyRequiredLevel(DungeonDefinition dungeon)
        {
            if (requiredLevelText == null) return;

            int level = dungeon != null ? dungeon.RequiredCharacterLevel : 1;
            requiredLevelText.text = string.Format(CultureInfo.InvariantCulture, "Lv. {0}", level);
        }

        // ---- 대표 이미지 ----

        private void ApplyRepresentative(DungeonDefinition dungeon)
        {
            if (representativeImage == null) return;

            Sprite sprite = dungeon != null ? dungeon.RepresentativeSprite : null;
            representativeImage.sprite = sprite;
            // 스프라이트가 없는 Image가 흰 사각형으로 남지 않게 컴포넌트를 끈다.
            representativeImage.enabled = sprite != null;
        }

        // ---- 선택 색 ----

        /// <summary>프리팹에 설정된 ColorBlock을 한 번만 기억한다. 선택 표시를 넣고 빼는 동안 원래
        /// 값이 남아 있어야 되돌릴 수 있다.</summary>
        private void CaptureOriginalColors()
        {
            if (originalColorsCaptured || selectButton == null) return;

            originalColors = selectButton.colors;
            originalColorsCaptured = true;
        }

        private void RestoreOriginalColors()
        {
            if (!originalColorsCaptured || selectButton == null) return;
            selectButton.colors = originalColors;
        }

        // ---- 잠김 색 ----

        /// <summary>이름/필요 레벨 텍스트에 <b>지금 설정되어 있는 색</b>을 되돌릴 기준으로 기억한다.
        /// 바인딩할 때마다 다시 기억하므로, 이전 항목에서 어둡게 해 둔 값이 원본으로 굳지 않도록
        /// 먼저 기억해 둔 색을 되돌린 뒤에 다시 읽는다.</summary>
        private void CaptureTextColors()
        {
            RestoreTextColors();

            if (nameText != null) originalNameColor = nameText.color;
            if (requiredLevelText != null) originalRequiredLevelColor = requiredLevelText.color;
            originalTextColorsCaptured = true;
        }

        private void RestoreTextColors()
        {
            if (!originalTextColorsCaptured) return;

            if (nameText != null) nameText.color = originalNameColor;
            if (requiredLevelText != null) requiredLevelText.color = originalRequiredLevelColor;
        }
    }
}
