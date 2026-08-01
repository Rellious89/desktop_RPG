using System;
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
    /// </summary>
    [DisallowMultipleComponent]
    public class DungeonListItemView : MonoBehaviour
    {
        [Header("References (에디터에서 직접 연결한다 - 이름으로 찾지 않는다)")]
        [Tooltip("항목 클릭을 받는 Button(루트). 비워두면 이 GameObject의 Button을 쓴다.")]
        [SerializeField] private Button selectButton;

        [Tooltip("던전 이름을 표시할 TextMeshProUGUI(list_DungeonName).")]
        [SerializeField] private TextMeshProUGUI nameText;

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

        /// <summary>이 항목이 표시 중인 던전. 패널이 선택 표시를 갱신할 때 비교에 쓴다.</summary>
        public DungeonDefinition BoundDungeon => boundDungeon;

        /// <summary>지금 선택 표시가 켜져 있는지. 검증/디버깅용 읽기 전용 값이다.</summary>
        public bool IsSelected => selected;

        /// <summary>지금 화면에 그려져 있는 이름. 검증/디버깅용 읽기 전용 값이다.</summary>
        public string CurrentNameText => nameText != null ? nameText.text : null;

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
            ApplyName(dungeon);
            ApplyRepresentative(dungeon);
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
            selected = false;
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
    }
}
