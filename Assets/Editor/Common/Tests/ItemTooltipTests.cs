using System;
using System.Collections.Generic;
using System.Reflection;
using Common;
using DesktopWindow;
using Dungeon;
using Inventory;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CommonEditor.Tests
{
    /// <summary>
    /// 아이템 툴팁의 <b>프리팹 계약</b>과 <b>런타임 동작</b>을 함께 보는 시험.
    ///
    /// <b>프리팹은 읽기만 한다.</b> 사용자가 만든 item_ToolTip / pn_Inventory / list_item의 시각
    /// 값(위치, 폰트, 색, Layout)은 이 시험의 관심사가 아니고, 확인하는 것은 "스크립트가 무엇을
    /// 가리키는가"와 "입력을 받지 않는가"처럼 <b>코드가 기대하는 구조</b>뿐이다.
    ///
    /// <b>런타임 동작은 메모리 위의 Canvas에서 확인한다.</b> Play Mode에 들어가지 않고 프리팹을
    /// Instantiate해 실제 컴포넌트를 그대로 돌린다 - 만든 오브젝트는 전부 TearDown에서 지운다.
    /// 로컬라이징 문자열은 비동기라 EditMode에서 도착을 기다릴 수 없으므로, <b>참조가 없는
    /// 아이템의 대체 경로</b>는 그대로 확인하고 <b>문자열이 도착했을 때의 경로</b>는 뷰의 콜백을
    /// 직접 불러 확인한다(구독 자체가 걸리고 풀리는지는 내부 플래그로 본다).
    ///
    /// <b>배선만은 씬을 열어서 본다.</b> 컨트롤러가 하나뿐이라는 것은 프리팹 하나로는 확인할 수 없는
    /// 사실이고, 세 화면이 <b>같은</b> 컨트롤러에 닿는지도 셋이 함께 놓인 씬에서만 보인다. 씬은
    /// Additive로 열고 <b>저장하지 않고</b> 닫으므로 프로젝트 파일은 바뀌지 않는다.
    /// </summary>
    public sealed class ItemTooltipTests
    {
        private const string TooltipPrefabPath = "Assets/Art/UI/Prefab/Inventory/item_ToolTip.prefab";
        private const string InventoryPrefabPath = "Assets/Art/UI/Prefab/panel/pn_Inventory.prefab";
        private const string SlotPrefabPath = "Assets/Art/UI/Prefab/Inventory/list_item.prefab";
        private const string UiSharedDataPath = "Assets/Localization/Tables/01_UI/01_UI Shared Data.asset";

        /// <summary>컨트롤러가 하나만 있어야 하는 자리. 툴팁의 주인은 패널이 아니라 <b>씬</b>이다.</summary>
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";

        private const string PanelUiObjectName = "Panel_UI";

        /// <summary>던전 상세의 대표 보상 한 칸과 정산 결과의 아이템 한 줄. 인벤토리 슬롯과 함께
        /// <b>같은</b> 컨트롤러를 쓰는 세 주인이다.</summary>
        private const string RewardPreviewPrefabPath = "Assets/Art/UI/Prefab/Dungeon/item_DungeonReward.prefab";

        private const string ResultRewardPrefabPath =
            "Assets/Art/UI/Prefab/Dungeon/DungeonReward/item_DungeonResultReward.prefab";

        /// <summary>툴팁 제목이 가리켜야 하는 01_UI의 숫자 키("Information" / "아이템 정보").</summary>
        private const string TitleNumericKey = "39";

        private const string NameObjectName = "lb_ItemName";
        private const string DescriptionObjectName = "lb_description";
        private const string CountObjectName = "lb_ItemCount";
        private const string IconObjectName = "sp_ItemIcon";
        private const string LayoutRootObjectName = "bg";
        private const string BottomObjectName = "Bottom";
        private const string TitleObjectName = "lb_Title";

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        /// <summary>이 시험이 만든 툴팁 뷰. 구독을 들고 있을 수 있어서 TearDown에서 반드시 비운다 -
        /// 프로젝트 에셋(생성된 ItemDefinition)에 건 구독이 남으면 다음 시험까지 따라간다.</summary>
        private readonly List<ItemTooltipView> views = new List<ItemTooltipView>();

        /// <summary>이 시험이 만든 컨트롤러. 컨트롤러가 만든 인스턴스도 구독을 들고 있을 수 있다.</summary>
        private readonly List<ItemTooltipController> controllers = new List<ItemTooltipController>();

        private GameObject canvasObject;
        private RectTransform tooltipParent;
        private RectTransform panelRoot;

        [TearDown]
        public void TearDown()
        {
            foreach (ItemTooltipController controller in controllers)
            {
                if (controller != null) controller.Hide();
            }

            controllers.Clear();

            foreach (ItemTooltipView view in views)
            {
                if (view != null) view.Clear();
            }

            views.Clear();

            if (canvasObject != null) UnityEngine.Object.DestroyImmediate(canvasObject);
            canvasObject = null;
            tooltipParent = null;
            panelRoot = null;

            foreach (UnityEngine.Object asset in created)
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        // ---- 프리팹 계약 (읽기 전용) ----

        [Test]
        public void TooltipPrefab_HasTheViewWithEveryReferenceWired()
        {
            GameObject prefab = LoadPrefab(TooltipPrefabPath);

            var view = prefab.GetComponent<ItemTooltipView>();
            Assert.IsNotNull(view, "item_ToolTip 루트에 ItemTooltipView가 있어야 한다.");

            var serialized = new SerializedObject(view);
            AssertReferenceIs(serialized, "nameText", NameObjectName);
            AssertReferenceIs(serialized, "descriptionText", DescriptionObjectName);
            AssertReferenceIs(serialized, "countText", CountObjectName);
            AssertReferenceIs(serialized, "iconImage", IconObjectName);
            AssertReferenceIs(serialized, "layoutRoot", LayoutRootObjectName);
        }

        [Test]
        public void TooltipPrefab_KeepsTheCountTemplateAuthoredInThePrefab()
        {
            GameObject prefab = LoadPrefab(TooltipPrefabPath);
            TextMeshProUGUI count = FindChild(prefab.transform, CountObjectName).GetComponent<TextMeshProUGUI>();

            Assert.AreEqual("{0}", count.text,
                "수량 형식은 프리팹이 소유한다 - 코드가 형식을 다시 적으면 프리팹을 고쳐도 반영되지 않는다.");
        }

        [Test]
        public void TooltipPrefab_TitleStaysOnTheExistingLocalizedLabel()
        {
            GameObject prefab = LoadPrefab(TooltipPrefabPath);
            Transform title = FindChild(prefab.transform, TitleObjectName);

            var localized = title.GetComponent<LocalizedTMPText>();
            Assert.IsNotNull(localized, "제목은 기존 LocalizedTMPText가 그대로 담당해야 한다.");

            var serialized = new SerializedObject(localized);
            SerializedProperty keyId = serialized
                .FindProperty("text")
                .FindPropertyRelative("m_TableEntryReference")
                .FindPropertyRelative("m_KeyId");

            Assert.AreEqual(
                ReadSharedEntries(UiSharedDataPath)[TitleNumericKey],
                keyId.longValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"제목은 01_UI의 숫자 키 {TitleNumericKey}를 가리켜야 한다.");
        }

        [Test]
        public void TooltipPrefab_KeepsBottomInactive()
        {
            GameObject prefab = LoadPrefab(TooltipPrefabPath);
            Transform bottom = FindChild(prefab.transform, BottomObjectName);

            Assert.IsNotNull(bottom, "Bottom 오브젝트가 사라졌다 - 사용자 프리팹 구조를 바꾸지 않는다.");
            Assert.IsFalse(bottom.gameObject.activeSelf, "Bottom은 꺼진 채로 남아야 한다(이번 범위 밖).");
        }

        [Test]
        public void TooltipPrefab_TakesNoInput()
        {
            GameObject prefab = LoadPrefab(TooltipPrefabPath);

            Assert.AreEqual(0, prefab.GetComponentsInChildren<Button>(true).Length,
                "툴팁에는 Button이 없어야 한다 - 툴팁은 입력을 받지 않는다.");
            Assert.AreEqual(0, prefab.GetComponentsInChildren<WindowInputRegion>(true).Length,
                "툴팁은 클릭 관통 영역을 등록하지 않는다.");
        }

        [Test]
        public void InventoryPrefab_OwnsNoTooltipControllerOfItsOwn()
        {
            GameObject panel = LoadPrefab(InventoryPrefabPath);

            Assert.AreEqual(0, panel.GetComponentsInChildren<ItemTooltipController>(true).Length,
                "pn_Inventory는 자기 컨트롤러를 갖지 않는다 - 세 화면이 씬 Panel_UI의 하나를 함께 쓴다. " +
                "패널마다 붙이면 툴팁 인스턴스가 패널 수만큼 생긴다.");
            Assert.IsNull(panel.GetComponent<HoverTooltipController>(),
                "메뉴바 툴팁 컨트롤러를 다시 쓰지 않는다 - 아이템 툴팁은 전용 컨트롤러를 쓴다.");
        }

        // ---- 씬 배선: 컨트롤러는 하나뿐이고 세 화면이 함께 쓴다 (읽기 전용) ----

        [Test]
        public void Scene_PanelUiOwnsTheOnlyTooltipController()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                ItemTooltipController[] controllersInScene = ControllersIn(scene);
                Assert.AreEqual(1, controllersInScene.Length,
                    "씬의 ItemTooltipController는 정확히 하나여야 한다 - 둘이면 툴팁 인스턴스도 둘이 된다.");

                ItemTooltipController controller = controllersInScene[0];
                Assert.AreEqual(PanelUiObjectName, controller.gameObject.name,
                    "컨트롤러는 Panel_UI가 소유한다 - 패널(pn_*)이 아니라 그 부모다.");

                var serialized = new SerializedObject(controller);
                Assert.AreEqual(
                    AssetDatabase.LoadAssetAtPath<GameObject>(TooltipPrefabPath),
                    serialized.FindProperty("tooltipPrefab").objectReferenceValue,
                    "컨트롤러가 item_ToolTip 프리팹을 가리켜야 한다.");
                Assert.AreSame(
                    (RectTransform)controller.transform,
                    serialized.FindProperty("tooltipRoot").objectReferenceValue,
                    "툴팁을 붙일 부모는 Panel_UI 자신이어야 한다 - 패널 안쪽은 Mask 아래라 잘린다.");
                Assert.AreEqual(0f, serialized.FindProperty("tooltipDelay").floatValue,
                    "아이템 툴팁의 기본 대기시간은 0(즉시)이다.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void Scene_EveryHoverOwnerResolvesTheSameSharedController()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var spawned = new List<GameObject>();
            try
            {
                ItemTooltipController[] controllersInScene = ControllersIn(scene);
                Assert.AreEqual(1, controllersInScene.Length);
                ItemTooltipController shared = controllersInScene[0];

                InventorySlotView slot = Array.Find(
                    UnityEngine.Object.FindObjectsOfType<InventorySlotView>(true),
                    value => value.gameObject.scene == scene);
                Assert.IsNotNull(slot, "씬의 pn_Inventory에 슬롯이 하나도 없습니다.");

                var dungeonPanel = Array.Find(
                    UnityEngine.Object.FindObjectsOfType<DungeonPanel>(true),
                    value => value.gameObject.scene == scene);
                Assert.IsNotNull(dungeonPanel, "씬에 pn_Dungeon이 없습니다.");

                var resultPanel = Array.Find(
                    UnityEngine.Object.FindObjectsOfType<DungeonResultPanel>(true),
                    value => value.gameObject.scene == scene);
                Assert.IsNotNull(resultPanel, "씬에 pn_DungeonResult가 없습니다.");

                // 두 던전 칸은 런타임에 만들어지는 것이라 씬에 미리 놓여 있지 않다 - 실제와 같은 자리에
                // 하나씩 만들어 부모 탐색이 같은 컨트롤러에 닿는지 본다.
                GameObject preview = UnityEngine.Object.Instantiate(
                    LoadPrefab(RewardPreviewPrefabPath), dungeonPanel.transform, false);
                spawned.Add(preview);

                GameObject resultItem = UnityEngine.Object.Instantiate(
                    LoadPrefab(ResultRewardPrefabPath), resultPanel.transform, false);
                spawned.Add(resultItem);

                Assert.AreSame(shared, ItemTooltipController.FindSharedController(slot),
                    "인벤토리 슬롯이 씬의 공용 컨트롤러에 닿아야 한다.");
                Assert.AreSame(shared,
                    ItemTooltipController.FindSharedController(
                        preview.GetComponent<DungeonRewardPreviewView>()),
                    "던전 대표 보상 칸이 같은 공용 컨트롤러에 닿아야 한다.");
                Assert.AreSame(shared,
                    ItemTooltipController.FindSharedController(
                        resultItem.GetComponent<DungeonResultRewardItemView>()),
                    "던전 정산 결과 줄이 같은 공용 컨트롤러에 닿아야 한다.");
            }
            finally
            {
                for (int i = spawned.Count - 1; i >= 0; i--)
                {
                    if (spawned[i] != null) UnityEngine.Object.DestroyImmediate(spawned[i]);
                }

                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void SlotPrefab_HandlesPointerEnterAndExit()
        {
            GameObject prefab = LoadPrefab(SlotPrefabPath);

            var slot = prefab.GetComponent<InventorySlotView>();
            Assert.IsNotNull(slot, "list_item에 InventorySlotView가 있어야 한다.");
            Assert.IsInstanceOf<IPointerEnterHandler>(slot);
            Assert.IsInstanceOf<IPointerExitHandler>(slot);
        }

        // ---- 뷰: 값 그리기와 대체 ----

        [Test]
        public void Bind_WithoutLocalizedReferences_UsesTheDisplayName()
        {
            ItemTooltipView view = NewTooltipInstance();
            ItemDefinition definition = NewItem("50000", displayName: "더미아이템1");

            view.Bind(definition, 3);

            Assert.AreEqual("더미아이템1", Text(view, "nameText"));
        }

        [Test]
        public void Bind_WithoutDisplayName_FallsBackToTheItemId()
        {
            ItemTooltipView view = NewTooltipInstance();
            ItemDefinition definition = NewItem("50000", displayName: string.Empty);

            view.Bind(definition, 1);

            Assert.AreEqual("50000", Text(view, "nameText"),
                "표시 이름이 비어 있으면 저장 키를 그대로 보여 준다 - 빈 줄로 두면 무엇인지 알 수 없다.");
        }

        [Test]
        public void Bind_WithoutDescriptionReference_LeavesTheDescriptionEmpty()
        {
            ItemTooltipView view = NewTooltipInstance();

            view.Bind(NewItem("50000", "더미아이템1"), 1);

            Assert.AreEqual(string.Empty, Text(view, "descriptionText"),
                "설명이 없으면 빈 줄이다 - 이름을 한 번 더 적으면 같은 글자가 두 줄이 된다.");
        }

        [Test]
        public void Bind_FormatsTheCountWithThePrefabTemplate()
        {
            ItemTooltipView view = NewTooltipInstance();

            view.Bind(NewItem("50000", "더미아이템1"), 12);

            Assert.AreEqual("12", Text(view, "countText"));
        }

        [Test]
        public void Bind_FallsBackToThePlainNumberWhenTheTemplateHasNoPlaceholder()
        {
            ItemTooltipView view = NewTooltipInstance();

            // 프리팹의 형식은 첫 Bind가 읽어 둔다 - 그 뒤에 바꿔야 "형식이 잘못된 상태"가 된다.
            view.Bind(NewItem("50000", "더미아이템1"), 1);

            // 중괄호가 아예 없는 문구다. string.Format은 이런 형식을 예외 없이 그대로 돌려주므로,
            // 예외만 붙잡으면 수량이 통째로 사라진 채 "보유 수량"만 남는다.
            SetPrivate(view, "countFormat", "보유 수량");

            view.Bind(NewItem("50000", "더미아이템1"), 7);

            Assert.AreEqual("7", Text(view, "countText"),
                "{0}이 없는 형식은 수량을 넣을 자리가 없다 - 문구를 그대로 두면 수량이 사라진다.");
        }

        [Test]
        public void Bind_FallsBackToThePlainNumberWhenTheTemplateIsMalformed()
        {
            ItemTooltipView view = NewTooltipInstance();
            view.Bind(NewItem("50000", "더미아이템1"), 1);

            // {0}은 있지만 넘겨준 적 없는 {1}이 함께 있어 string.Format이 던지는 경우다.
            SetPrivate(view, "countFormat", "개수 {0} / {1}");

            view.Bind(NewItem("50000", "더미아이템1"), 7);

            Assert.AreEqual("7", Text(view, "countText"),
                "형식이 잘못됐다고 수량 자체를 감추지는 않는다.");
        }

        [Test]
        public void Bind_KeepsATemplateThatWrapsThePlaceholder()
        {
            ItemTooltipView view = NewTooltipInstance();
            view.Bind(NewItem("50000", "더미아이템1"), 1);
            SetPrivate(view, "countFormat", "x{0}");

            view.Bind(NewItem("50000", "더미아이템1"), 7);

            Assert.AreEqual("x7", Text(view, "countText"),
                "{0}이 있는 형식은 프리팹이 적은 그대로 쓴다 - 대체 경로가 정상 형식을 삼키면 안 된다.");
        }

        [Test]
        public void Bind_ShowsTheIconOnlyWhenTheItemHasOne()
        {
            ItemTooltipView view = NewTooltipInstance();
            var icon = (Image)GetPrivate(view, "iconImage");

            view.Bind(NewItem("50000", "더미아이템1"), 1);
            Assert.IsFalse(icon.enabled, "아이콘이 없는 아이템에서 Image를 켜 두면 흰 사각형이 그려진다.");

            view.Bind(NewItem("50001", "더미아이템2", NewSprite()), 1);
            Assert.IsTrue(icon.enabled);
        }

        // ---- 뷰: 로컬라이징 구독 수명 ----

        [Test]
        public void LocalizedDelivery_ReplacesTheFallbackAndAsksForANewLayout()
        {
            ItemTooltipView view = NewTooltipInstance();
            view.Bind(NewItem("50000", "더미아이템1"), 1);

            int layoutChanges = 0;
            view.LayoutChanged += () => layoutChanges++;

            Deliver(view, "OnNameChanged", "Dummy Item 1");
            Deliver(view, "OnDescriptionChanged", "Tooltip description");

            Assert.AreEqual("Dummy Item 1", Text(view, "nameText"));
            Assert.AreEqual("Tooltip description", Text(view, "descriptionText"));
            Assert.AreEqual(2, layoutChanges,
                "높이가 달라질 수 있으므로 문자열이 도착할 때마다 다시 배치할 기회를 줘야 한다.");
        }

        [Test]
        public void LocalizedDelivery_OfAnEmptyStringFallsBackAgain()
        {
            ItemTooltipView view = NewTooltipInstance();
            view.Bind(NewItem("50000", "더미아이템1"), 1);

            Deliver(view, "OnNameChanged", string.Empty);

            Assert.AreEqual("더미아이템1", Text(view, "nameText"),
                "번역 값이 비어 있으면 이름 칸을 비우지 말고 대체 이름을 쓴다.");
        }

        [Test]
        public void Subscription_IsOnlyTakenWhenTheItemHasReferences()
        {
            ItemTooltipView view = NewTooltipInstance();

            view.Bind(NewItem("50000", "더미아이템1"), 1);
            Assert.IsFalse((bool)GetPrivate(view, "subscribed"),
                "참조가 없는 아이템에는 구독할 것이 없다.");

            view.Bind(NewItemWithReferences("50001"), 1);
            Assert.IsTrue((bool)GetPrivate(view, "subscribed"));
        }

        [Test]
        public void Subscription_IsReleasedByClearAndByRebinding()
        {
            ItemTooltipView view = NewTooltipInstance();

            view.Bind(NewItemWithReferences("50000"), 1);
            view.Clear();
            Assert.IsFalse((bool)GetPrivate(view, "subscribed"), "Clear는 구독을 끊어야 한다.");
            Assert.IsNull(view.BoundDefinition);

            view.Bind(NewItemWithReferences("50001"), 1);
            view.Bind(NewItemWithReferences("50002"), 1);
            Assert.IsTrue((bool)GetPrivate(view, "subscribed"));
            Assert.AreEqual("50002", view.BoundDefinition.ItemId,
                "다시 그릴 때 이전 아이템의 구독은 끊기고 새 아이템만 남아야 한다.");
        }

        [Test]
        public void Subscription_IsReleasedWhenTheTooltipIsDisabled()
        {
            ItemTooltipView view = NewTooltipInstance();
            view.Bind(NewItemWithReferences("50000"), 1);

            // EditMode에서는 SetActive만으로 OnDisable이 불리지 않는다(ExecuteAlways가 아니다) -
            // 확인하려는 것은 "꺼질 때 무엇을 하는가"이므로 그 경로를 직접 지나간다.
            InvokeLifecycle(view, "OnDisable");

            Assert.IsFalse((bool)GetPrivate(view, "subscribed"),
                "꺼진 툴팁이 구독을 들고 있으면 Locale이 바뀔 때마다 보이지 않는 툴팁이 갱신된다.");
        }

        // ---- 컨트롤러: 인스턴스와 주인 ----

        [Test]
        public void Controller_ReusesASingleInstanceAcrossSlots()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView slotB);

            controller.RequestShow(slotA, NewItem("50000", "A"), 1, (RectTransform)slotA.transform);
            controller.RequestShow(slotB, NewItem("50001", "B"), 1, (RectTransform)slotB.transform);

            Assert.AreEqual(1, tooltipParent.GetComponentsInChildren<ItemTooltipView>(true).Length,
                "슬롯을 오갈 때마다 프리팹을 새로 만들면 화면에 툴팁이 여럿 남는다.");
        }

        [Test]
        public void Controller_PutsTheInstanceOutsideThePanelAndInFront()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);

            controller.RequestShow(slotA, NewItem("50000", "A"), 1, (RectTransform)slotA.transform);

            Transform instance = controller.View.transform;
            Assert.AreSame(tooltipParent, instance.parent,
                "툴팁은 Mask가 걸린 슬롯 영역 밖(Panel_UI)에 붙어야 잘리지 않는다.");
            Assert.AreEqual(tooltipParent.childCount - 1, instance.GetSiblingIndex(),
                "툴팁은 형제 중 맨 뒤여야 다른 패널보다 앞에 그려진다.");
        }

        [Test]
        public void Controller_TurnsOffEveryRaycastTargetOnTheInstance()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);

            controller.RequestShow(slotA, NewItem("50000", "A"), 1, (RectTransform)slotA.transform);

            foreach (Graphic graphic in controller.View.GetComponentsInChildren<Graphic>(true))
            {
                Assert.IsFalse(graphic.raycastTarget,
                    $"'{graphic.name}'이 입력을 받으면 슬롯 Hover가 끊기고 클릭/스크롤을 가로챈다.");
            }
        }

        [Test]
        public void Controller_HidesWhenTheOwningSlotExits()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);
            controller.RequestShow(slotA, NewItem("50000", "A"), 1, (RectTransform)slotA.transform);

            controller.CancelShow(slotA);

            Assert.IsFalse(controller.View.gameObject.activeSelf);
            Assert.IsNull(controller.VisibleOwner);
        }

        [Test]
        public void Controller_IgnoresALateExitFromThePreviousSlot()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView slotB);

            controller.RequestShow(slotA, NewItem("50000", "A"), 1, (RectTransform)slotA.transform);
            controller.RequestShow(slotB, NewItem("50001", "B"), 1, (RectTransform)slotB.transform);

            // A에서 B로 빠르게 넘어가면 A의 Exit가 B의 Enter보다 늦게 도착할 수 있다.
            controller.CancelShow(slotA);

            Assert.IsTrue(controller.View.gameObject.activeSelf, "뒤늦은 A의 Exit가 B의 툴팁을 지우면 안 된다.");
            Assert.AreSame(slotB, controller.VisibleOwner);
        }

        [Test]
        public void Controller_HidesWhenItIsDisabled()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);
            controller.RequestShow(slotA, NewItem("50000", "A"), 1, (RectTransform)slotA.transform);

            // 패널을 닫는 것과 같은 경로다 - 패널이 꺼지면 이 컴포넌트도 함께 꺼진다.
            // EditMode에서는 SetActive가 OnDisable을 부르지 않으므로 그 경로를 직접 지나간다.
            panelRoot.gameObject.SetActive(false);
            InvokeLifecycle(controller, "OnDisable");

            Assert.IsFalse(controller.View.gameObject.activeSelf, "패널을 닫으면 툴팁도 사라져야 한다.");
            Assert.IsNull(controller.VisibleOwner);
        }

        // ---- 컨트롤러: 위치 ----

        [Test]
        public void Controller_PlacesTheTooltipToTheRightOfTheSlot()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);
            MoveSlot(slotA, new Vector2(-300f, 0f));

            controller.RequestShow(slotA, NewItem("50000", "A"), 1, (RectTransform)slotA.transform);

            Rect slot = LocalRect((RectTransform)slotA.transform);
            Rect tooltip = LocalRect(controller.View.PlacementRect);

            Assert.GreaterOrEqual(tooltip.xMin, slot.xMax,
                "자리가 있으면 툴팁은 슬롯의 오른쪽에 붙는다.");
        }

        [Test]
        public void Controller_FlipsToTheLeftWhenTheRightSideDoesNotFit()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);
            MoveSlot(slotA, new Vector2(370f, 0f));

            controller.RequestShow(slotA, NewItem("50000", "A"), 1, (RectTransform)slotA.transform);

            Rect slot = LocalRect((RectTransform)slotA.transform);
            Rect tooltip = LocalRect(controller.View.PlacementRect);

            Assert.LessOrEqual(tooltip.xMax, slot.xMin,
                "오른쪽에 자리가 없으면 왼쪽으로 넘겨야 한다.");
            Assert.GreaterOrEqual(tooltip.xMin, -400f - 0.01f, "왼쪽으로 넘긴 뒤에도 화면 안이어야 한다.");
        }

        [Test]
        public void Controller_ClampsTheTooltipInsideTheCanvasVertically()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);
            MoveSlot(slotA, new Vector2(-300f, -280f));

            controller.RequestShow(slotA, NewItem("50000", "A"), 1, (RectTransform)slotA.transform);

            Rect tooltip = LocalRect(controller.View.PlacementRect);

            Assert.GreaterOrEqual(tooltip.yMin, -300f - 0.01f,
                "설명이 길어져 높이가 커져도 툴팁 아래쪽이 화면 밖으로 나가면 안 된다.");
            Assert.LessOrEqual(tooltip.yMax, 300f + 0.01f);
        }

        // ---- 슬롯 ----

        [Test]
        public void Slot_RemembersWhatItDraws()
        {
            BuildScene(out ItemTooltipController _, out InventorySlotView slotA, out InventorySlotView _2);
            ItemDefinition definition = NewItem("50000", "A");

            slotA.SetItem(definition, 4);

            Assert.AreSame(definition, slotA.Definition);
            Assert.AreEqual(4, slotA.Count);
        }

        [Test]
        public void Slot_ClearsWhatItDrawsWhenEmptied()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);
            slotA.SetItem(NewItem("50000", "A"), 4);
            slotA.OnPointerEnter(null);

            slotA.SetEmpty();

            Assert.IsNull(slotA.Definition);
            Assert.AreEqual(0, slotA.Count);
            Assert.IsFalse(controller.View.gameObject.activeSelf, "빈 칸이 된 슬롯의 툴팁은 남으면 안 된다.");
        }

        [Test]
        public void Slot_WithNothingToShow_DoesNotAskForATooltip()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);
            slotA.SetEmpty();

            slotA.OnPointerEnter(null);

            Assert.IsNull(controller.View, "빈 칸에서는 툴팁 인스턴스를 만들 이유조차 없다.");
        }

        [Test]
        public void Slot_HidesTheTooltipWhenTheListIsRefreshedWithAnotherItem()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);
            slotA.SetItem(NewItem("50000", "A"), 1);
            slotA.OnPointerEnter(null);
            Assert.IsTrue(controller.View.gameObject.activeSelf);

            // 마우스를 올린 채 인벤토리가 갱신되어 같은 자리에 다른 아이템이 들어온 상황이다.
            slotA.SetItem(NewItem("50001", "B"), 1);

            Assert.IsFalse(controller.View.gameObject.activeSelf,
                "화면의 아이템과 툴팁의 아이템이 서로 달라지면 안 된다.");
        }

        [Test]
        public void Slot_HidesTheTooltipOnPointerExit()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);
            slotA.SetItem(NewItem("50000", "A"), 1);
            slotA.OnPointerEnter(null);

            slotA.OnPointerExit(null);

            Assert.IsFalse(controller.View.gameObject.activeSelf);
        }

        // ---- 뷰: 수량이 없는 화면과 long 수량 ----

        [Test]
        public void Bind_WithoutACount_EmptiesAndTurnsOffTheCountObject()
        {
            ItemTooltipView view = NewTooltipInstance();

            view.Bind(NewItem("50002", "더미아이템3"));

            var count = (TextMeshProUGUI)GetPrivate(view, "countText");
            Assert.IsFalse(view.HasBoundCount, "수량 없는 표시에서는 수량을 그리지 않는다.");
            Assert.AreEqual(string.Empty, count.text, "수량 칸에 글자가 남으면 안 된다.");
            Assert.IsFalse(count.enabled);
            Assert.IsFalse(count.gameObject.activeSelf,
                "글자만 비우면 빈 줄만큼의 자리가 남는다 - 오브젝트까지 꺼야 한다.");
        }

        [Test]
        public void Bind_WithoutACount_LeavesNoStaleNumberFromThePreviousItem()
        {
            ItemTooltipView view = NewTooltipInstance();
            view.Bind(NewItem("50000", "더미아이템1"), 7);

            view.Bind(NewItem("50002", "더미아이템3"));

            var count = (TextMeshProUGUI)GetPrivate(view, "countText");
            Assert.AreEqual(string.Empty, count.text, "직전 아이템의 숫자가 남으면 안 된다.");
            Assert.IsFalse(count.gameObject.activeSelf);
            Assert.AreEqual(0L, view.BoundCount);
        }

        [Test]
        public void Bind_WithACountAgain_TurnsTheCountBackOn()
        {
            ItemTooltipView view = NewTooltipInstance();
            view.Bind(NewItem("50002", "더미아이템3"));

            view.Bind(NewItem("50000", "더미아이템1"), 3);

            var count = (TextMeshProUGUI)GetPrivate(view, "countText");
            Assert.IsTrue(count.gameObject.activeSelf,
                "수량 없는 표시를 지나온 뒤에도 다시 보여야 한다 - 컴포넌트만 켜면 화면에 나오지 않는다.");
            Assert.IsTrue(count.enabled);
            Assert.AreEqual("3", count.text);
        }

        [Test]
        public void Bind_KeepsALongCountExactly()
        {
            ItemTooltipView view = NewTooltipInstance();

            view.Bind(NewItem("50000", "더미아이템1"), long.MaxValue);

            Assert.AreEqual(long.MaxValue, view.BoundCount, "long 수량이 좁혀지면 안 된다.");
            Assert.AreEqual("9223372036854775807", Text(view, "countText"));
        }

        // ---- 던전 대표 보상 미리보기: 수량 없는 툴팁 ----

        [Test]
        public void RewardPreview_RemembersWhatItDraws()
        {
            BuildScene(out ItemTooltipController _, out InventorySlotView _1, out InventorySlotView _2);
            DungeonRewardPreviewView preview = NewRewardPreview(new Vector2(-300f, 0f));
            ItemDefinition item = NewItem("50002", "더미아이템3");

            preview.Bind(item);

            Assert.AreSame(item, preview.BoundItem);
        }

        [Test]
        public void RewardPreview_ClearsWhatItDraws()
        {
            BuildScene(out ItemTooltipController _, out InventorySlotView _1, out InventorySlotView _2);
            DungeonRewardPreviewView preview = NewRewardPreview(new Vector2(-300f, 0f));
            preview.Bind(NewItem("50002", "더미아이템3"));

            preview.Clear();

            Assert.IsNull(preview.BoundItem, "빈 칸에 예전 보상의 툴팁이 뜨면 안 된다.");
        }

        [Test]
        public void RewardPreview_AsksForATooltipWithoutACount()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView _1, out InventorySlotView _2);
            DungeonRewardPreviewView preview = NewRewardPreview(new Vector2(-300f, 0f));
            ItemDefinition item = NewItem("50002", "더미아이템3");
            preview.Bind(item);

            preview.OnPointerEnter(null);

            Assert.AreSame(preview, controller.VisibleOwner);
            Assert.AreSame(item, controller.View.BoundDefinition);
            Assert.IsFalse(controller.View.HasBoundCount,
                "미리보기에는 보여줄 수량이 없다 - 인벤토리 보유 수량을 끌어오지 않는다.");

            var count = (TextMeshProUGUI)GetPrivate(controller.View, "countText");
            Assert.AreEqual(string.Empty, count.text);
            Assert.IsFalse(count.gameObject.activeSelf);
        }

        [Test]
        public void RewardPreview_WithNothingToShow_DoesNotAskForATooltip()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView _1, out InventorySlotView _2);
            DungeonRewardPreviewView preview = NewRewardPreview(new Vector2(-300f, 0f));

            preview.OnPointerEnter(null);

            Assert.IsNull(controller.View, "빈 칸에서는 툴팁 인스턴스를 만들지도 않는다.");
        }

        [Test]
        public void RewardPreview_HidesTheTooltipOnPointerExit()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView _1, out InventorySlotView _2);
            DungeonRewardPreviewView preview = NewRewardPreview(new Vector2(-300f, 0f));
            preview.Bind(NewItem("50002", "더미아이템3"));
            preview.OnPointerEnter(null);

            preview.OnPointerExit(null);

            Assert.IsFalse(controller.IsVisible);
        }

        [Test]
        public void RewardPreview_HidesTheTooltipWhenItIsCleared()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView _1, out InventorySlotView _2);
            DungeonRewardPreviewView preview = NewRewardPreview(new Vector2(-300f, 0f));
            preview.Bind(NewItem("50002", "더미아이템3"));
            preview.OnPointerEnter(null);

            preview.Clear();

            Assert.IsFalse(controller.IsVisible);
        }

        [Test]
        public void RewardPreview_HidesTheTooltipWhenItIsReboundToAnotherItem()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView _1, out InventorySlotView _2);
            DungeonRewardPreviewView preview = NewRewardPreview(new Vector2(-300f, 0f));
            preview.Bind(NewItem("50002", "더미아이템3"));
            preview.OnPointerEnter(null);

            preview.Bind(NewItem("50003", "더미아이템4"));

            Assert.IsFalse(controller.IsVisible,
                "던전 선택이 바뀌면 같은 자리의 칸이 다른 보상을 그린다 - 예전 툴팁이 남으면 안 된다.");
        }

        [Test]
        public void RewardPreview_HidesTheTooltipWhenItIsDisabled()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView _1, out InventorySlotView _2);
            DungeonRewardPreviewView preview = NewRewardPreview(new Vector2(-300f, 0f));
            preview.Bind(NewItem("50002", "더미아이템3"));
            preview.OnPointerEnter(null);

            // EditMode에서는 SetActive가 OnDisable을 부르지 않으므로 그 경로를 직접 지나간다.
            preview.gameObject.SetActive(false);
            InvokeLifecycle(preview, "OnDisable");

            Assert.IsFalse(controller.IsVisible, "꺼진 칸은 Exit를 받지 못한다 - 스스로 내려야 한다.");
        }

        // ---- 던전 정산 결과 줄: 이번 세션 획득 수량 ----

        [Test]
        public void ResultRewardItem_ShowsTheSessionCountExactly()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView _1, out InventorySlotView _2);
            DungeonResultRewardItemView item = NewResultRewardItem(new Vector2(-300f, 0f));
            ItemDefinition definition = NewItem("50000", "더미아이템1");
            item.Bind(new DungeonSessionItemReward(definition, "50000", long.MaxValue));

            item.OnPointerEnter(null);

            Assert.AreSame(item, controller.VisibleOwner);
            Assert.AreSame(definition, controller.View.BoundDefinition);
            Assert.IsTrue(controller.View.HasBoundCount);
            Assert.AreEqual(long.MaxValue, controller.View.BoundCount,
                "정산 화면의 숫자와 툴팁의 숫자는 같아야 한다 - 스냅샷 값을 그대로 넘긴다.");
            Assert.AreEqual("9223372036854775807", Text(controller.View, "countText"));
        }

        [Test]
        public void ResultRewardItem_WithoutADefinition_DoesNotAskForATooltip()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView _1, out InventorySlotView _2);
            DungeonResultRewardItemView item = NewResultRewardItem(new Vector2(-300f, 0f));
            item.Bind(new DungeonSessionItemReward(null, "50000", 3L));

            item.OnPointerEnter(null);

            Assert.IsNull(controller.View, "정의가 없으면 툴팁이 그릴 내용 자체가 없다.");
        }

        [Test]
        public void ResultRewardItem_HidesTheTooltipOnPointerExit()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView _1, out InventorySlotView _2);
            DungeonResultRewardItemView item = NewResultRewardItem(new Vector2(-300f, 0f));
            item.Bind(new DungeonSessionItemReward(NewItem("50000", "더미아이템1"), "50000", 3L));
            item.OnPointerEnter(null);

            item.OnPointerExit(null);

            Assert.IsFalse(controller.IsVisible);
        }

        [Test]
        public void ResultRewardItem_HidesTheTooltipWhenItIsClearedOrDisabled()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView _1, out InventorySlotView _2);
            DungeonResultRewardItemView item = NewResultRewardItem(new Vector2(-300f, 0f));
            item.Bind(new DungeonSessionItemReward(NewItem("50000", "더미아이템1"), "50000", 3L));
            item.OnPointerEnter(null);

            item.Clear();
            Assert.IsFalse(controller.IsVisible);

            item.Bind(new DungeonSessionItemReward(NewItem("50002", "더미아이템3"), "50002", 5L));
            item.OnPointerEnter(null);
            Assert.IsTrue(controller.IsVisible);

            // EditMode에서는 SetActive가 OnDisable을 부르지 않으므로 그 경로를 직접 지나간다.
            item.gameObject.SetActive(false);
            InvokeLifecycle(item, "OnDisable");
            Assert.IsFalse(controller.IsVisible, "꺼진 줄은 Exit를 받지 못한다 - 스스로 내려야 한다.");
        }

        // ---- 세 화면이 함께 쓰는 인스턴스 하나 ----

        [Test]
        public void Controller_ReusesASingleInstanceAcrossAllThreeOwners()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);
            DungeonRewardPreviewView preview = NewRewardPreview(new Vector2(-200f, 0f));
            DungeonResultRewardItemView result = NewResultRewardItem(new Vector2(-100f, 0f));

            slotA.SetItem(NewItem("50000", "더미아이템1"), 2);
            slotA.OnPointerEnter(null);
            ItemTooltipView first = controller.View;
            slotA.OnPointerExit(null);

            preview.Bind(NewItem("50002", "더미아이템3"));
            preview.OnPointerEnter(null);
            Assert.AreSame(first, controller.View);
            preview.OnPointerExit(null);

            result.Bind(new DungeonSessionItemReward(NewItem("50003", "더미아이템4"), "50003", 9L));
            result.OnPointerEnter(null);
            Assert.AreSame(first, controller.View);

            Assert.AreEqual(1, tooltipParent.GetComponentsInChildren<ItemTooltipView>(true).Length,
                "세 화면을 오가도 툴팁 인스턴스는 하나뿐이어야 한다.");
        }

        [Test]
        public void Controller_IgnoresALateExitFromAnotherScreensOwner()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);
            DungeonRewardPreviewView preview = NewRewardPreview(new Vector2(-200f, 0f));

            slotA.SetItem(NewItem("50000", "더미아이템1"), 1);
            slotA.OnPointerEnter(null);

            preview.Bind(NewItem("50002", "더미아이템3"));
            preview.OnPointerEnter(null);

            // 같은 프레임에 뒤늦게 도착한 슬롯의 Exit가 이미 뜬 미리보기의 툴팁을 지우면 안 된다.
            slotA.OnPointerExit(null);

            Assert.IsTrue(controller.IsVisible);
            Assert.AreSame(preview, controller.VisibleOwner);
        }

        // ---- 패널이 앞으로 나와도 툴팁이 맨 앞에 남는다 ----

        [Test]
        public void Controller_ReturnsToTheTopSiblingAfterAPanelIsBroughtForward()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);
            ItemDefinition item = NewItem("50000", "더미아이템1");
            slotA.SetItem(item, 4);
            slotA.OnPointerEnter(null);

            RectTransform instance = (RectTransform)controller.View.transform;

            // PopupPanelManager.FocusPanel이 하는 일과 같다 - 패널을 Panel_UI 안에서 맨 뒤 형제로 보낸다.
            panelRoot.SetAsLastSibling();
            Assert.AreNotEqual(tooltipParent.childCount - 1, instance.GetSiblingIndex(),
                "이 시험의 전제: 패널이 앞으로 나오면 툴팁이 그 뒤로 밀린다.");

            InvokeLifecycle(controller, "LateUpdate");

            Assert.AreEqual(tooltipParent.childCount - 1, instance.GetSiblingIndex(),
                "떠 있는 툴팁은 프레임 끝에 다시 맨 앞으로 돌아와야 한다.");
            Assert.IsTrue(controller.IsVisible, "순서만 되돌린다 - 클릭 때문에 숨기지 않는다.");
            Assert.AreSame(slotA, controller.VisibleOwner, "주인은 그대로다 - 다시 바인딩하지 않는다.");
            Assert.AreSame(item, controller.View.BoundDefinition);
            Assert.AreSame(instance, (RectTransform)controller.View.transform,
                "인스턴스를 새로 만들지 않는다.");
        }

        [Test]
        public void Controller_WithNothingShown_CreatesNoInstanceInLateUpdate()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView _1, out InventorySlotView _2);

            InvokeLifecycle(controller, "LateUpdate");

            Assert.IsNull(controller.View, "떠 있는 것이 없으면 인스턴스를 만들 이유가 없다.");
            Assert.IsFalse(controller.IsVisible);
        }

        [Test]
        public void Controller_AfterExit_StaysHiddenThroughLateUpdate()
        {
            BuildScene(out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView _);
            slotA.SetItem(NewItem("50000", "더미아이템1"), 1);
            slotA.OnPointerEnter(null);
            slotA.OnPointerExit(null);

            InvokeLifecycle(controller, "LateUpdate");

            Assert.IsFalse(controller.IsVisible, "내려간 툴팁을 프레임 끝에 다시 올리지 않는다.");
        }

        // ---- 도우미 ----

        private static GameObject LoadPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"'{path}'를 찾지 못했습니다.");
            return prefab;
        }

        private static void AssertReferenceIs(SerializedObject serialized, string fieldName, string objectName)
        {
            SerializedProperty property = serialized.FindProperty(fieldName);
            Assert.IsNotNull(property, $"{fieldName} 칸이 없습니다.");

            var reference = property.objectReferenceValue as Component;
            Assert.IsNotNull(reference, $"{fieldName}가 연결되지 않았습니다.");
            Assert.AreEqual(objectName, reference.gameObject.name, $"{fieldName}가 다른 오브젝트를 가리킵니다.");
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root.name == name) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChild(root.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>메모리 위의 Canvas 하나와 슬롯 두 개. 실제 컴포넌트를 그대로 돌린다.</summary>
        private void BuildScene(
            out ItemTooltipController controller, out InventorySlotView slotA, out InventorySlotView slotB)
        {
            canvasObject = new GameObject("TestCanvas", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasObject.GetComponent<Canvas>();

            // World Space로 두면 카메라 없이도 RectTransform이 그대로 800x600 사각형이 된다.
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = new Vector2(800f, 600f);
            canvasRect.position = Vector3.zero;

            tooltipParent = NewStretchedChild(canvasRect, PanelUiObjectName);
            panelRoot = NewStretchedChild(tooltipParent, "pn_Inventory");

            // 실제 배선과 같은 자리다 - 컨트롤러는 패널이 아니라 Panel_UI가 소유하고, 세 화면은
            // 부모 탐색으로 이 하나에 닿는다.
            controller = tooltipParent.gameObject.AddComponent<ItemTooltipController>();
            SetPrivate(controller, "tooltipPrefab", LoadPrefab(TooltipPrefabPath));
            SetPrivate(controller, "tooltipRoot", tooltipParent);
            controllers.Add(controller);

            slotA = NewSlot("slotA", new Vector2(-300f, 0f));
            slotB = NewSlot("slotB", new Vector2(-200f, 0f));
        }

        private InventorySlotView NewSlot(string name, Vector2 position)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadPrefab(SlotPrefabPath), panelRoot);
            instance.name = name;

            var rect = (RectTransform)instance.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(40f, 40f);
            rect.anchoredPosition = position;

            return instance.GetComponent<InventorySlotView>();
        }

        private static void MoveSlot(InventorySlotView slot, Vector2 position)
        {
            ((RectTransform)slot.transform).anchoredPosition = position;
        }

        /// <summary>던전 상세의 대표 보상 한 칸. 실제와 같이 패널 아래에 놓아 부모 탐색이 통하게 한다.</summary>
        private DungeonRewardPreviewView NewRewardPreview(Vector2 position)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                LoadPrefab(RewardPreviewPrefabPath), panelRoot);
            PlaceOwner(instance, position);
            return instance.GetComponent<DungeonRewardPreviewView>();
        }

        /// <summary>던전 정산 결과의 아이템 한 줄. 같은 이유로 패널 아래에 놓는다.</summary>
        private DungeonResultRewardItemView NewResultRewardItem(Vector2 position)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                LoadPrefab(ResultRewardPrefabPath), panelRoot);
            PlaceOwner(instance, position);
            return instance.GetComponent<DungeonResultRewardItemView>();
        }

        private static void PlaceOwner(GameObject instance, Vector2 position)
        {
            var rect = (RectTransform)instance.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(40f, 40f);
            rect.anchoredPosition = position;
        }

        /// <summary><paramref name="scene"/>에 속한 컨트롤러만 고른다 - 다른 씬이나 이 시험이 만든
        /// 메모리 위 오브젝트가 섞이면 "하나뿐"이라는 판정이 흐려진다.</summary>
        private static ItemTooltipController[] ControllersIn(Scene scene)
        {
            return Array.FindAll(
                UnityEngine.Object.FindObjectsOfType<ItemTooltipController>(true),
                value => value.gameObject.scene == scene);
        }

        private static RectTransform NewStretchedChild(RectTransform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        /// <summary>컨트롤러를 거치지 않고 툴팁 프리팹만 하나 띄운다 - 뷰 자체의 동작을 보는 시험용이다.</summary>
        private ItemTooltipView NewTooltipInstance()
        {
            if (canvasObject == null)
            {
                BuildScene(out ItemTooltipController _, out InventorySlotView _1, out InventorySlotView _2);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                LoadPrefab(TooltipPrefabPath), tooltipParent);

            var view = instance.GetComponent<ItemTooltipView>();
            views.Add(view);
            return view;
        }

        /// <summary>디스크에 남지 않는 ItemDefinition.</summary>
        private ItemDefinition NewItem(string itemId, string displayName, Sprite icon = null)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            created.Add(definition);

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("itemId").stringValue = itemId;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("icon").objectReferenceValue = icon;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return definition;
        }

        /// <summary>실제 04_Item 참조를 가진 생성 에셋. 구독이 걸리고 풀리는지를 보는 시험용이다.</summary>
        private static ItemDefinition NewItemWithReferences(string itemId)
        {
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(TableDataEditor.TableDataPaths.ItemAssetPath(itemId));

            Assert.IsNotNull(definition, $"생성 에셋이 없습니다 - Table Data Rebuild를 먼저 실행하세요: {itemId}");
            Assert.IsTrue(definition.HasLocalizedName && definition.HasLocalizedDescription,
                $"{itemId}의 이름/설명 참조가 비어 있어 구독 시험을 할 수 없다.");

            return definition;
        }

        private Sprite NewSprite()
        {
            var texture = new Texture2D(4, 4);
            created.Add(texture);

            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            created.Add(sprite);
            return sprite;
        }

        /// <summary>부모 로컬 좌표로 본 사각형. 위치 판정은 전부 이 좌표계에서 한다.</summary>
        private static Rect LocalRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            Transform parent = rect.parent;
            Vector3 min = parent.InverseTransformPoint(corners[0]);
            Vector3 max = parent.InverseTransformPoint(corners[2]);

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static string Text(ItemTooltipView view, string fieldName)
        {
            return ((TextMeshProUGUI)GetPrivate(view, fieldName)).text;
        }

        /// <summary>Unity가 EditMode에서 부르지 않는 생명주기 콜백을 직접 지나간다.</summary>
        private static void InvokeLifecycle(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(method, $"{target.GetType().Name}.{methodName}을 찾지 못했습니다.");
            method.Invoke(target, null);
        }

        private static void Deliver(ItemTooltipView view, string methodName, string localized)
        {
            MethodInfo method = typeof(ItemTooltipView).GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(method, $"ItemTooltipView.{methodName}을 찾지 못했습니다.");
            method.Invoke(view, new object[] { localized });
        }

        private static object GetPrivate(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{target.GetType().Name}.{fieldName}을 찾지 못했습니다.");
            return field.GetValue(target);
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{target.GetType().Name}.{fieldName}을 찾지 못했습니다.");
            field.SetValue(target, value);
        }

        /// <summary>Shared Data의 숫자 키 -> 내부 Entry Key ID.</summary>
        private static Dictionary<string, string> ReadSharedEntries(string sharedDataPath)
        {
            Assert.IsTrue(System.IO.File.Exists(sharedDataPath), $"'{sharedDataPath}'가 없습니다.");

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            string pendingId = null;

            foreach (string line in System.IO.File.ReadAllLines(sharedDataPath))
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("- m_Id:", StringComparison.Ordinal))
                {
                    pendingId = trimmed.Substring("- m_Id:".Length).Trim();
                    continue;
                }

                if (pendingId == null || !trimmed.StartsWith("m_Key:", StringComparison.Ordinal)) continue;

                map[trimmed.Substring("m_Key:".Length).Trim()] = pendingId;
                pendingId = null;
            }

            return map;
        }
    }
}
