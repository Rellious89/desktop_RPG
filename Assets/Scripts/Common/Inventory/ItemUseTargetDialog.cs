using System.Collections.Generic;
using Character;
using DesktopWindow;
using Inventory;
using Recovery;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>캐릭터 대상 사용 아이템의 대상 선택 팝업.</summary>
    [DisallowMultipleComponent]
    public sealed class ItemUseTargetDialog : ModalPanel
    {
        private const string DialogObjectName = "dialog_ItemUseTarget";
        private const string RowTemplateName = "list_Character";
        private const string GuideName = "lb_guide";

        private readonly List<ItemUseTargetCharacterView> rows = new List<ItemUseTargetCharacterView>();
        private readonly LocalizedTextReference guideFormat = new LocalizedTextReference
        {
            TableReference = "01_UI",
            TableEntryReference = "119",
        };
        private readonly LocalizedTextReference unavailableMessage = new LocalizedTextReference
        {
            TableReference = "01_UI",
            TableEntryReference = "117",
        };

        private ItemDefinition item;
        private RectTransform content;
        private GameObject rowTemplate;
        private TextMeshProUGUI guideText;
        private LocalizedTMPText guideStaticLocalizer;
        private GameObject outsideClickBlocker;
        private bool resolved;
        private bool localizationBound;
        private int openedOrRetargetedFrame = -1;

        public ItemDefinition Item => item;

        /// <summary>씬에 비활성 배치된 팝업을 찾아 필요한 컴포넌트를 연결한 뒤 연다.</summary>
        public static bool TryOpen(ItemDefinition definition)
        {
            if (definition == null || !definition.CanTargetCharacter) return false;

            ItemUseTargetDialog dialog = FindSceneDialog();
            if (dialog == null)
            {
                Debug.LogWarning("[ItemUseTargetDialog] scene에서 dialog_ItemUseTarget을 찾지 못했습니다.");
                return false;
            }

            WindowInputRegion region = dialog.GetComponent<WindowInputRegion>();
            if (region == null) region = dialog.gameObject.AddComponent<WindowInputRegion>();
            region.ReceiveMouseInput = true;

            dialog.SetItem(definition);
            // 이미 열린 상태에서 다른 아이템을 우클릭한 프레임에는 그 클릭을 외부 닫기로 다시
            // 해석하지 않는다. 이벤트 처리 순서와 무관하게 새 아이템 전환이 우선되어야 한다.
            dialog.openedOrRetargetedFrame = Time.frameCount;
            dialog.Open();
            return true;
        }

        private void Update()
        {
            if (Time.frameCount == openedOrRetargetedFrame) return;
            if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1)) return;
            if (IsPointerInsideDialog(Input.mousePosition)) return;

            // 투명 수신 영역은 UI 레이캐스트를 막지 않으므로 이 Close와 같은 클릭으로 뒤쪽 버튼,
            // 슬롯 우클릭 등의 원래 동작도 그대로 실행된다.
            Close();
        }

        private static ItemUseTargetDialog FindSceneDialog()
        {
            ItemUseTargetDialog[] existing = Resources.FindObjectsOfTypeAll<ItemUseTargetDialog>();
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null && existing[i].gameObject.scene.IsValid()) return existing[i];
            }

            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                GameObject candidate = all[i];
                if (candidate == null || candidate.name != DialogObjectName || !candidate.scene.IsValid()) continue;
                return candidate.AddComponent<ItemUseTargetDialog>();
            }

            return null;
        }

        private void SetItem(ItemDefinition definition)
        {
            if (ReferenceEquals(item, definition)) return;
            UnbindLocalization();
            item = definition;
            if (isActiveAndEnabled) BindLocalization();
        }

        protected override void OnModalOpened()
        {
            ResolveReferences();
            EnsureOutsideClickBlocker();
            if (outsideClickBlocker != null) outsideClickBlocker.SetActive(true);
            BindLocalization();
            CharacterRoster.CharacterStateChanged += HandleCharacterStateChanged;
        }

        protected override void OnModalClosed()
        {
            CharacterRoster.CharacterStateChanged -= HandleCharacterStateChanged;
            UnbindLocalization();
            ClearRows();
            if (outsideClickBlocker != null) outsideClickBlocker.SetActive(false);
        }

        protected override void RefreshContents()
        {
            ResolveReferences();
            BuildRows();
            RefreshGuide();
        }

        protected override void OnDestroy()
        {
            CharacterRoster.CharacterStateChanged -= HandleCharacterStateChanged;
            UnbindLocalization();
            if (outsideClickBlocker != null) Destroy(outsideClickBlocker);
            base.OnDestroy();
        }

        internal void HandleTargetClicked(CharacterDefinition character)
        {
            if (character == null) return;

            InventoryManager inventory = InventoryManager.Instance;
            CharacterRoster roster = CharacterRoster.Instance;
            if (inventory != null && roster != null)
            {
                var service = new CharacterItemUseService(
                    inventory, new CharacterRosterRecoveryAdapter(roster), SaveSystem.Save);
                CharacterItemUseResult result = service.TryUse(item, character);
                if (result.Success)
                {
                    Close();
                    return;
                }
            }

            ToastManager.Instance?.Show(unavailableMessage.GetLocalizedString());
        }

        private void HandleCharacterStateChanged(CharacterDefinition changed)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && ReferenceEquals(rows[i].Character, changed)) rows[i].Refresh();
            }
        }

        private void BuildRows()
        {
            ClearRows();
            if (item == null || content == null || rowTemplate == null) return;

            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null) return;

            IReadOnlyList<CharacterRoster.Entry> entries = roster.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                CharacterDefinition character = entries[i] != null ? entries[i].definition : null;
                if (character == null) continue;

                GameObject clone = Instantiate(rowTemplate, content);
                clone.name = $"{RowTemplateName}_{character.CharacterId}";
                clone.SetActive(true);

                ItemUseTargetCharacterView view = clone.GetComponent<ItemUseTargetCharacterView>();
                if (view == null) view = clone.AddComponent<ItemUseTargetCharacterView>();
                view.Bind(this, roster, character, item.UseEffectValue);
                rows.Add(view);
            }
        }

        private void ClearRows()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;
                rows[i].Unbind();
                rows[i].gameObject.SetActive(false);
                Destroy(rows[i].gameObject);
            }
            rows.Clear();
        }

        private void ResolveReferences()
        {
            if (resolved) return;
            resolved = true;

            // Content/content처럼 프리팹 쪽 표기가 달라져도 목록이 사라지지 않도록 템플릿을
            // 직접 찾고, 템플릿이 실제로 배치된 부모를 생성 컨테이너로 사용한다.
            Transform templateTransform = FindDeepChild(transform, RowTemplateName);
            rowTemplate = templateTransform != null ? templateTransform.gameObject : null;
            content = templateTransform != null ? templateTransform.parent as RectTransform : null;

            Transform guideTransform = FindDeepChild(transform, GuideName);
            if (guideTransform != null)
            {
                guideText = guideTransform.GetComponent<TextMeshProUGUI>();
                guideStaticLocalizer = guideTransform.GetComponent<LocalizedTMPText>();
                if (guideStaticLocalizer != null) guideStaticLocalizer.enabled = false;
            }

            if (content == null || rowTemplate == null)
                Debug.LogWarning("[ItemUseTargetDialog] Content/list_Character 템플릿을 찾지 못했습니다.", this);
            if (guideText == null)
                Debug.LogWarning("[ItemUseTargetDialog] lb_guide 텍스트를 찾지 못했습니다.", this);
        }

        private void EnsureOutsideClickBlocker()
        {
            if (outsideClickBlocker != null) return;
            RectTransform parent = transform.parent as RectTransform;
            if (parent == null) return;

            outsideClickBlocker = new GameObject(
                $"OutsideClickBlocker_{DialogObjectName}", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(WindowInputRegion));
            outsideClickBlocker.layer = gameObject.layer;
            RectTransform rect = outsideClickBlocker.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetSiblingIndex(transform.GetSiblingIndex());

            Image image = outsideClickBlocker.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = false;
            outsideClickBlocker.GetComponent<WindowInputRegion>().ReceiveMouseInput = true;
        }

        private bool IsPointerInsideDialog(Vector2 screenPosition)
        {
            RectTransform dialogRect = transform as RectTransform;
            if (dialogRect == null) return false;

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(dialogRect, screenPosition, eventCamera);
        }

        private void BindLocalization()
        {
            if (localizationBound || item == null) return;
            localizationBound = true;
            guideFormat.StringChanged += ApplyGuide;
            if (item.HasLocalizedName) item.LocalizedName.StringChanged += HandleItemNameChanged;
            RefreshGuide();
        }

        private void UnbindLocalization()
        {
            if (!localizationBound) return;
            guideFormat.StringChanged -= ApplyGuide;
            if (item != null && item.HasLocalizedName) item.LocalizedName.StringChanged -= HandleItemNameChanged;
            localizationBound = false;
        }

        private void HandleItemNameChanged(string _)
        {
            RefreshGuide();
        }

        private void RefreshGuide()
        {
            if (guideText == null || item == null) return;
            string itemName = item.HasLocalizedName ? item.LocalizedName.GetLocalizedString() : item.DisplayName;
            guideFormat.Arguments = new object[] { itemName };
            if (localizationBound) guideFormat.RefreshString();
            else guideText.text = guideFormat.GetLocalizedString(itemName);
        }

        private void ApplyGuide(string localized)
        {
            if (guideText != null) guideText.text = localized;
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName) return child;
                Transform found = FindDeepChild(child, childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
