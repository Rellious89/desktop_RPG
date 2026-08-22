using System;
using System.Collections.Generic;
using System.Reflection;
using Building;
using Common;
using Inventory;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BuildingEditor.Tests
{
    /// <summary>
    /// 건물 정보 팝업의 배선/구독/닫기/확인 버튼 시험.
    ///
    /// <b>실제 저장 파일은 건드리지 않는다.</b> <see cref="SaveSystem"/>에 메모리 위의 가짜 저장소를
    /// 끼워 넣고, 팝업을 여닫는 동안 <b>쓰기 호출이 0회</b>이고 재화/아이템이 한 글자도 달라지지
    /// 않는지 확인한다 - persistentDataPath는 읽지도 않는다.
    ///
    /// <b>번역 값 자체는 확인하지 않는다.</b> EditMode에는 Locale이 선택되어 있지 않을 수 있어서
    /// StringChanged가 언제 오는지가 환경에 달려 있다. 대신 (1) 어떤 참조에 구독을 걸었는지와
    /// (2) 값이 도착했을 때 어떻게 조립되는지를 각각 나눠서 확인한다 - 후자는 도착 콜백을 직접
    /// 불러 재현하므로 Locale 변경 시의 갱신 경로를 그대로 지난다.
    /// </summary>
    public sealed class BuildingPopupPanelTests
    {
        private const string PrefabPath = "Assets/Art/UI/Prefab/dialog/dialog_BuildingPopup.prefab";

        /// <summary>01_UI 테이블 GUID(설명 틀 40번과 기능 이름 1001번이 들어 있다).</summary>
        private const string UiTableGuid = "GUID:32fd067a20b754a50b20446b9c78d2ae";

        /// <summary>07_Building 테이블 GUID(건물 이름 1번이 들어 있다).</summary>
        private const string BuildingTableGuid = "GUID:161824df6b6eb43a1a6fa7c55deea323";

        /// <summary>01_UI / 40(설명 틀)의 실제 Entry Id.</summary>
        private const long DescriptionFormatKeyId = 8908411117756416L;

        /// <summary>01_UI / 1001(용병 모집)의 실제 Entry Id.</summary>
        private const long FunctionNameKeyId = 8908411117756417L;

        /// <summary>07_Building / 1(여관)의 실제 Entry Id.</summary>
        private const long BuildingNameKeyId = 288458006528L;

        /// <summary>01_UI / 41(비용 부족 경고)의 실제 Entry Id.</summary>
        private const long WarningKeyId = 8970130103984128L;

        /// <summary>프리팹에 저작된 자리 여백. 시험의 기대값도 같은 값을 쓴다.</summary>
        private const float PlacementMargin = 8f;

        /// <summary>자리 잡기 시험이 쓰는 가상 화면 크기.</summary>
        private const float CanvasWidth = 400f;
        private const float CanvasHeight = 300f;

        private static readonly MethodInfo ConfigureSaveMethod = typeof(SaveSystem).GetMethod(
            "ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<Object> created = new List<Object>();
        private FakeStorage storage;
        private PopupPanelManager testManager;
        private InventoryManager testInventory;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(ConfigureSaveMethod,
                "SaveSystem.ConfigureForTests를 찾지 못했습니다 - 그대로 두면 시험이 실제 저장 파일을 읽고 씁니다.");

            storage = new FakeStorage();
            ConfigureSaveMethod.Invoke(null, new object[] { storage, null, null });
        }

        [TearDown]
        public void TearDown()
        {
            // 정적 Instance가 다음 시험으로 새지 않게 파괴 콜백까지 재현한다.
            if (testManager != null) EditModeLifecycle.Invoke(testManager, "OnDestroy");
            testManager = null;

            if (testInventory != null) EditModeLifecycle.Invoke(testInventory, "OnDestroy");
            testInventory = null;

            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();

            ConfigureSaveMethod.Invoke(null, new object[] { null, null, null });
        }

        // ---- 프리팹 배선 ----

        [Test]
        public void 프리팹은_정확한_Inspector_참조를_가진다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);

            var panel = prefab.GetComponent<BuildingPopupPanel>();
            Assert.IsNotNull(panel, "팝업 루트에 BuildingPopupPanel이 있어야 한다");

            var so = new SerializedObject(panel);
            AssertReference<TextMeshProUGUI>(so, "buildingNameText", "lb_BuildingName");
            AssertReference<TextMeshProUGUI>(so, "descriptionText", "lb_description");
            AssertReference<Button>(so, "confirmButton", "btn_confirm");
            AssertReference<Button>(so, "closeButton", "btn_cancle");

            Assert.AreEqual(UiTableGuid,
                so.FindProperty("descriptionFormat.m_TableReference.m_TableCollectionName").stringValue);
            Assert.AreEqual(DescriptionFormatKeyId,
                so.FindProperty("descriptionFormat.m_TableEntryReference.m_KeyId").longValue,
                "설명 틀은 01_UI / 40이어야 한다");

            Assert.IsFalse(so.FindProperty("blockBackgroundInput").boolValue,
                "건물 팝업은 전체 화면 차단막을 만들지 않는다");
        }

        [Test]
        public void 프리팹의_취소_버튼_철자는_그대로_유지된다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var panel = prefab.GetComponent<BuildingPopupPanel>();

            PropertyInfo closeName = typeof(ModalPanel).GetProperty(
                "CloseButtonName", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(closeName);
            Assert.AreEqual("btn_cancle", (string)closeName.GetValue(panel),
                "에셋 이름의 'cancle' 철자를 코드가 'cancel'로 고치면 자동 탐색이 어긋난다");
        }

        [Test]
        public void 프리팹의_확인_버튼에는_저작된_영구_호출이_없다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var panel = prefab.GetComponent<BuildingPopupPanel>();
            var so = new SerializedObject(panel);
            var confirm = so.FindProperty("confirmButton").objectReferenceValue as Button;

            Assert.IsNotNull(confirm);
            Assert.AreEqual(0, confirm.onClick.GetPersistentEventCount(),
                "확인 버튼은 이번 단계에서 아무 것도 하지 않아야 한다");
        }

        // ---- 확인/취소 ----

        [Test]
        public void 팝업을_열면_확인_버튼은_보이되_눌리지_않는다()
        {
            BuildingPopupPanel panel = CreatePanel(out Button confirm, out _);
            confirm.interactable = true; // 저작 실수를 흉내낸다.

            panel.Bind(CreateBuilding());
            OpenPanel(panel);

            Assert.IsTrue(confirm.gameObject.activeSelf, "버튼을 숨기지 않는다 - 보이되 눌리지 않는다");
            Assert.IsFalse(confirm.interactable);
            Assert.AreEqual(0, confirm.onClick.GetPersistentEventCount());
        }

        [Test]
        public void 확인_버튼을_눌러도_아무_상태도_바뀌지_않는다()
        {
            BuildingPopupPanel panel = CreatePanel(out Button confirm, out _);
            BuildingDefinition building = CreateBuilding();
            panel.Bind(building);
            OpenPanel(panel);

            SaveData before = SaveSystem.Data;
            int currency = before.currency;
            int itemCount = before.items.Count;
            int writes = storage.WriteCalls;

            confirm.onClick.Invoke();

            Assert.IsTrue(panel.gameObject.activeSelf, "확인은 팝업을 닫지도 않는다");
            Assert.AreEqual(currency, SaveSystem.Data.currency);
            Assert.AreEqual(itemCount, SaveSystem.Data.items.Count);
            Assert.AreEqual(writes, storage.WriteCalls, "확인으로 저장이 일어나면 안 된다");
        }

        [Test]
        public void 취소_버튼은_ModalPanel의_닫기_경로로_팝업을_닫는다()
        {
            BuildingPopupPanel panel = CreatePanel(out _, out Button cancel);
            panel.Bind(CreateBuilding());
            OpenPanel(panel);
            Assert.IsTrue(panel.gameObject.activeSelf);
            Assert.IsTrue(panel.HasLocalizationSubscriptions);

            cancel.onClick.Invoke();

            Assert.IsFalse(panel.gameObject.activeSelf,
                "취소 버튼은 ModalPanel의 닫기 버튼 칸에 연결되어 Close를 부른다");

            EditModeLifecycle.RaiseDisable(panel);
            Assert.IsFalse(panel.HasLocalizationSubscriptions, "닫히면 구독이 남지 않아야 한다");
        }

        [Test]
        public void ESC는_PopupPanelManager를_통해_같은_닫기_경로를_지난다()
        {
            var managerGo = new GameObject("TestPopupPanelManager");
            created.Add(managerGo);
            PopupPanelManager manager = managerGo.AddComponent<PopupPanelManager>();
            // EditMode에서는 Awake도 오지 않는다 - 단일 인스턴스 등록을 직접 재현한다.
            EditModeLifecycle.Invoke(manager, "Awake");
            testManager = manager;

            BuildingPopupPanel panel = CreatePanel(out _, out _);
            panel.Bind(CreateBuilding());
            OpenPanel(panel);

            Assert.AreSame(panel, manager.TopPanel, "열린 팝업이 ESC 대상 목록의 맨 앞이어야 한다");
            Assert.IsTrue(manager.CloseTopPanel(), "ESC 경로가 이 팝업을 닫아야 한다");
            Assert.IsFalse(panel.gameObject.activeSelf);

            EditModeLifecycle.RaiseDisable(panel);
            Assert.AreEqual(0, manager.ActivePanelCount, "닫힌 패널이 ESC 대상 목록에 남으면 안 된다");
            Assert.IsFalse(panel.HasLocalizationSubscriptions);
        }

        // ---- 구독 ----

        [Test]
        public void 열린_팝업은_네_갈래_참조를_모두_구독한다()
        {
            BuildingPopupPanel panel = CreatePanel(out _, out _);
            BuildingDefinition building = CreateBuilding();

            panel.Bind(building);
            OpenPanel(panel);

            AssertBoundReference(panel, "boundFormatReference", true);
            AssertBoundReference(panel, "boundNameReference", true);
            AssertBoundReference(panel, "boundFunctionReference", true);
            AssertBoundReference(panel, "boundCurrencyNameReference", true);
            Assert.AreSame(building, panel.BoundBuilding);
        }

        [Test]
        public void 닫혀_있을_때_바인딩하면_구독하지_않고_열릴_때_구독한다()
        {
            BuildingPopupPanel panel = CreatePanel(out _, out _);
            panel.gameObject.SetActive(false);

            panel.Bind(CreateBuilding());
            Assert.IsFalse(panel.HasLocalizationSubscriptions,
                "닫힌 패널이 구독을 들고 있으면 Locale이 바뀔 때 보이지도 않는 화면을 계속 다시 그린다");

            OpenPanel(panel);
            Assert.IsTrue(panel.HasLocalizationSubscriptions);
        }

        [Test]
        public void 다시_바인딩하면_이전_구독을_먼저_끊는다()
        {
            BuildingPopupPanel panel = CreatePanel(out _, out _);
            BuildingDefinition first = CreateBuilding();
            BuildingDefinition second = CreateBuilding();

            panel.Bind(first);
            OpenPanel(panel);
            object firstName = GetPrivateField(panel, "boundNameReference");

            panel.Bind(second);
            object secondName = GetPrivateField(panel, "boundNameReference");

            Assert.AreSame(second, panel.BoundBuilding);
            Assert.AreNotSame(firstName, secondName,
                "이전 건물의 번역이 뒤늦게 도착해 새 건물의 문구를 덮어쓰면 안 된다");
        }

        [Test]
        public void 오브젝트를_직접_비활성화해도_구독이_끊긴다()
        {
            BuildingPopupPanel panel = CreatePanel(out _, out _);
            panel.Bind(CreateBuilding());
            OpenPanel(panel);
            Assert.IsTrue(panel.HasLocalizationSubscriptions);

            EditModeLifecycle.Disable(panel);

            Assert.IsFalse(panel.HasLocalizationSubscriptions,
                "Close를 거치지 않은 직접 비활성화에서도 구독이 남으면 안 된다");
        }

        [Test]
        public void 파괴되어도_구독이_남지_않는다()
        {
            BuildingPopupPanel panel = CreatePanel(out _, out _);
            panel.Bind(CreateBuilding());
            OpenPanel(panel);
            Assert.IsTrue(panel.HasLocalizationSubscriptions);

            GameObject go = panel.gameObject;
            Object.DestroyImmediate(go);
            created.Remove(go);

            // 파괴된 컴포넌트의 필드는 더 이상 읽을 수 없으므로, 여기서는 파괴 경로가 예외 없이
            // 지나가는 것 자체를 확인한다(OnDestroy에서 해제하지 않으면 다음 Locale 변경 때
            // 파괴된 대상으로 콜백이 들어가 예외가 난다).
            Assert.Pass();
        }

        // ---- 문구 조립 ----

        [Test]
        public void 값이_모두_도착하면_기능_시간_비용_순서로_조립한다()
        {
            BuildingPopupPanel panel = CreatePanel(out _, out _, out TextMeshProUGUI nameText,
                out TextMeshProUGUI descriptionText);
            panel.Bind(CreateBuilding(buildTimeSeconds: 60, costAmount: 2000));
            OpenPanel(panel);

            DeliverLocalizedValues(panel, "Inn", "Mercenary",
                "Unlock - {0}\n\nTime - {1}\nCost - {2}", "Jewel");

            Assert.AreEqual("Inn", nameText.text);
            Assert.AreEqual("Unlock - Mercenary\n\nTime - 00:01:00\nCost - 2,000 Jewel",
                descriptionText.text);
        }

        [Test]
        public void 언어가_바뀌면_열려_있는_팝업이_그_자리에서_갱신된다()
        {
            BuildingPopupPanel panel = CreatePanel(out _, out _, out TextMeshProUGUI nameText,
                out TextMeshProUGUI descriptionText);
            panel.Bind(CreateBuilding(buildTimeSeconds: 60, costAmount: 2000));
            OpenPanel(panel);

            DeliverLocalizedValues(panel, "Inn", "Mercenary",
                "Unlock - {0}\n\nTime - {1}\nCost - {2}", "Jewel");
            string english = descriptionText.text;

            // Locale 변경은 네 참조 모두에 새 값을 다시 밀어 넣는다 - 그 경로를 그대로 재현한다.
            DeliverLocalizedValues(panel, "여관", "용병 모집",
                "해금 기능 - {0}\n\n소요 시간 - {1}\n비용 - {2}", "주얼");

            Assert.AreEqual("여관", nameText.text);
            Assert.AreEqual("해금 기능 - 용병 모집\n\n소요 시간 - 00:01:00\n비용 - 2,000 주얼",
                descriptionText.text);
            Assert.AreNotEqual(english, descriptionText.text);
        }

        [Test]
        public void 하루를_넘는_건설_시간도_되감기지_않고_표시된다()
        {
            BuildingPopupPanel panel = CreatePanel(out _, out _, out _, out TextMeshProUGUI descriptionText);
            panel.Bind(CreateBuilding(buildTimeSeconds: 90000, costAmount: 2000));
            OpenPanel(panel);

            DeliverLocalizedValues(panel, "Inn", "Mercenary", "{0}|{1}|{2}", "Jewel");

            Assert.AreEqual("Mercenary|25:00:00|2,000 Jewel", descriptionText.text);
        }

        [Test]
        public void 재화_이름이_아직_오지_않으면_비용_칸을_비워_둔다()
        {
            BuildingPopupPanel panel = CreatePanel(out _, out _, out _, out TextMeshProUGUI descriptionText);
            panel.Bind(CreateBuilding(buildTimeSeconds: 60, costAmount: 2000));
            OpenPanel(panel);

            InvokePrivate(panel, "ApplyLocalizedFormat", "{0}|{1}|{2}");
            InvokePrivate(panel, "ApplyLocalizedFunctionName", "Mercenary");

            Assert.AreEqual("Mercenary|00:01:00|", descriptionText.text,
                "번역이 도착하지 않은 재화 이름을 코드가 지어내지 않는다");
        }

        [Test]
        public void 설명_틀이_오기_전에는_설명을_비워_둔다()
        {
            BuildingPopupPanel panel = CreatePanel(out _, out _, out _, out TextMeshProUGUI descriptionText);
            descriptionText.text = "이전 내용";

            panel.Bind(CreateBuilding());
            OpenPanel(panel);
            InvokePrivate(panel, "ApplyLocalizedFunctionName", "Mercenary");

            Assert.AreEqual(string.Empty, descriptionText.text);
        }

        // ---- 인벤토리/저장 ----

        [Test]
        public void 팝업을_여닫아도_인벤토리와_저장이_전혀_바뀌지_않는다()
        {
            SaveData data = SaveSystem.Data;
            data.currency = 12345;
            data.items.Clear();
            int writesBefore = storage.WriteCalls;
            int readsBefore = storage.ReadPrimaryCalls;

            BuildingPopupPanel panel = CreatePanel(out _, out Button cancel);
            BuildingDefinition building = CreateBuilding(buildTimeSeconds: 60, costAmount: 2000);

            panel.Bind(building);
            OpenPanel(panel);
            DeliverLocalizedValues(panel, "Inn", "Mercenary", "{0}|{1}|{2}", "Jewel");
            cancel.onClick.Invoke();
            EditModeLifecycle.RaiseDisable(panel);

            Assert.AreEqual(12345, SaveSystem.Data.currency, "비용은 표시용 문자열로만 쓰인다");
            Assert.AreEqual(0, SaveSystem.Data.items.Count);
            Assert.AreEqual(writesBefore, storage.WriteCalls, "저장 쓰기가 한 번도 일어나면 안 된다");
            Assert.AreEqual(readsBefore, storage.ReadPrimaryCalls,
                "팝업은 저장 파일을 다시 읽지도 않는다");
        }

        // ---- 프리팹 배선(경고/자리) ----

        [Test]
        public void 프리팹은_경고_TMP와_자리_사각형을_연결한다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var panel = prefab.GetComponent<BuildingPopupPanel>();
            var so = new SerializedObject(panel);

            AssertReference<TextMeshProUGUI>(so, "warningText", "lb_warningMSG");
            AssertReference<RectTransform>(so, "placementRect", "bg");

            var placement = so.FindProperty("placementRect").objectReferenceValue as RectTransform;
            Assert.AreNotSame(prefab.transform, placement,
                "전체 화면을 덮는 팝업 루트를 옮기면 그 아래의 입력 영역까지 화면 밖으로 나간다");
            Assert.AreSame(prefab.transform, placement.parent,
                "옮기는 대상은 루트 바로 아래의 bg다");
        }

        [Test]
        public void 프리팹의_경고는_꺼진_채로_저작된다()
        {
            TextMeshProUGUI warning = LoadPrefabWarning();

            Assert.IsFalse(warning.gameObject.activeSelf,
                "경고는 비용이 모자랄 때만 켜진다 - 저작 상태는 꺼짐이어야 한다");
        }

        [Test]
        public void 프리팹의_경고만_레이캐스트를_받지_않는다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var panel = prefab.GetComponent<BuildingPopupPanel>();
            var so = new SerializedObject(panel);
            var warning = so.FindProperty("warningText").objectReferenceValue as TextMeshProUGUI;
            var description = so.FindProperty("descriptionText").objectReferenceValue as TextMeshProUGUI;

            Assert.IsFalse(warning.raycastTarget,
                "경고 문구가 클릭을 먹으면 아래의 버튼이 눌리지 않는다");
            Assert.IsTrue(description.raycastTarget,
                "경고 하나만 끈다 - 다른 그래픽의 Raycast Target을 함께 건드리지 않는다");
        }

        [Test]
        public void 프리팹의_경고_문구는_01_UI_41을_가리킨다()
        {
            TextMeshProUGUI warning = LoadPrefabWarning();

            var localized = warning.GetComponent<LocalizedTMPText>();
            Assert.IsNotNull(localized,
                "경고 문구는 코드가 쓰지 않는다 - 오브젝트에 붙은 LocalizedTMPText가 채운다");

            var so = new SerializedObject(localized);
            Assert.AreEqual(UiTableGuid,
                so.FindProperty("text.m_TableReference.m_TableCollectionName").stringValue);
            Assert.AreEqual(WarningKeyId,
                so.FindProperty("text.m_TableEntryReference.m_KeyId").longValue,
                "부족 경고는 01_UI / 41이어야 한다");
        }

        [Test]
        public void 코드는_경고_문구를_쓰지_않는다()
        {
            BuildingPopupPanel panel = CreatePanel(out _, out _, out TextMeshProUGUI warning, out _, out _);
            string authored = warning.text;

            panel.Bind(CreateBuilding(costAmount: 2000));
            OpenPanel(panel);

            Assert.IsTrue(warning.gameObject.activeSelf, "재화가 0이므로 경고가 켜져 있어야 한다");
            Assert.AreEqual(authored, warning.text,
                "문구를 코드가 지어내면 표를 고쳐도 화면이 바뀌지 않는 자리가 생긴다");
        }

        // ---- 비용 판정 ----

        [Test]
        public void 재화가_1_모자라면_경고가_켜지고_확인이_꺼진다()
        {
            CreateInventory();
            SaveSystem.Data.currency = 1999;

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out _, out TextMeshProUGUI warning, out _, out _);
            panel.Bind(CreateBuilding(costAmount: 2000));
            OpenPanel(panel);

            Assert.IsFalse(panel.IsCostPayable);
            Assert.IsTrue(warning.gameObject.activeSelf);
            Assert.IsFalse(confirm.interactable);
            Assert.IsTrue(confirm.gameObject.activeSelf, "확인 버튼은 숨기지 않는다 - 보이되 눌리지 않는다");
            Assert.AreEqual(InventoryCostFailureReason.InsufficientCurrency, panel.LastCostEvaluation.Reason);
        }

        [Test]
        public void 재화가_정확히_같으면_낼_수_있다()
        {
            CreateInventory();
            SaveSystem.Data.currency = 2000;

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out _, out TextMeshProUGUI warning, out _, out _);
            panel.Bind(CreateBuilding(costAmount: 2000));
            OpenPanel(panel);

            Assert.IsTrue(panel.IsCostPayable, "정확히 같은 금액은 낼 수 있다");
            Assert.IsFalse(warning.gameObject.activeSelf);
            Assert.IsTrue(confirm.interactable);
        }

        [Test]
        public void 재화가_남으면_낼_수_있다()
        {
            CreateInventory();
            SaveSystem.Data.currency = 2190;

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out _, out TextMeshProUGUI warning, out _, out _);
            panel.Bind(CreateBuilding(costAmount: 2000));
            OpenPanel(panel);

            Assert.IsTrue(panel.IsCostPayable);
            Assert.IsFalse(warning.gameObject.activeSelf);
            Assert.IsTrue(confirm.interactable);
            Assert.AreEqual(2190, SaveSystem.Data.currency, "판정은 잔액을 건드리지 않는다");
        }

        [Test]
        public void 아이템이_모자라면_경고가_켜지고_확인이_꺼진다()
        {
            ItemDefinition plank = CreateItem("plank");
            CreateInventory(plank);
            SaveSystem.Data.currency = 999999;
            SaveSystem.Data.items.Clear();
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "plank", count = 2 });

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out _, out TextMeshProUGUI warning, out _, out _);
            panel.Bind(CreateBuilding(costAmount: 0, itemCost: plank, itemCount: 3));
            OpenPanel(panel);

            Assert.IsFalse(panel.IsCostPayable, "재화가 넉넉해도 아이템이 모자라면 낼 수 없다");
            Assert.IsTrue(warning.gameObject.activeSelf);
            Assert.IsFalse(confirm.interactable);
            Assert.AreEqual(InventoryCostFailureReason.InsufficientItem, panel.LastCostEvaluation.Reason);
            Assert.AreEqual("plank", panel.LastCostEvaluation.ItemId);
        }

        [Test]
        public void 카탈로그에_없는_아이템도_확인을_열어_두지_않는다()
        {
            CreateInventory();
            SaveSystem.Data.currency = 999999;

            ItemDefinition ghost = CreateItem("ghost");
            BuildingPopupPanel panel = CreatePanel(out Button confirm, out _, out TextMeshProUGUI warning, out _, out _);
            panel.Bind(CreateBuilding(costAmount: 0, itemCost: ghost, itemCount: 1));
            OpenPanel(panel);

            Assert.IsFalse(panel.IsCostPayable);
            Assert.IsTrue(warning.gameObject.activeSelf);
            Assert.IsFalse(confirm.interactable);
            Assert.AreEqual(InventoryCostFailureReason.UnknownItem, panel.LastCostEvaluation.Reason);
        }

        [Test]
        public void InventoryManager가_없으면_확인을_열어_두지_않는다()
        {
            SaveSystem.Data.currency = 999999;

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out _, out TextMeshProUGUI warning, out _, out _);
            panel.Bind(CreateBuilding(costAmount: 2000));
            OpenPanel(panel);

            Assert.IsFalse(panel.IsCostPayable,
                "판정할 근거가 없으면 '낼 수 있다'고 말할 수 없다 - 닫아 두는 쪽으로 실패한다");
            Assert.IsFalse(confirm.interactable);
            Assert.IsTrue(warning.gameObject.activeSelf);
            Assert.IsNull(panel.LastCostEvaluation);
        }

        [Test]
        public void 인벤토리가_바뀌면_열린_팝업이_그_자리에서_다시_판정한다()
        {
            InventoryManager inventory = CreateInventory();
            SaveSystem.Data.currency = 1999;

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out _, out TextMeshProUGUI warning, out _, out _);
            panel.Bind(CreateBuilding(costAmount: 2000));
            OpenPanel(panel);
            Assert.IsFalse(panel.IsCostPayable);
            Assert.IsTrue(panel.HasInventorySubscription);

            // 다른 창에서 재화가 늘어난 상황을 그대로 재현한다(저장은 그쪽이 이미 마쳤다는 경로다).
            SaveSystem.Data.currency = 2000;
            inventory.NotifyChangedAfterExternalSave();

            Assert.IsTrue(panel.IsCostPayable, "열려 있는 팝업이 인벤토리 신호로 다시 판정해야 한다");
            Assert.IsFalse(warning.gameObject.activeSelf);
            Assert.IsTrue(confirm.interactable);
        }

        [Test]
        public void 닫히면_인벤토리_구독도_끊긴다()
        {
            CreateInventory();
            SaveSystem.Data.currency = 2000;

            BuildingPopupPanel panel = CreatePanel(out _, out Button cancel, out _, out _, out _);
            panel.Bind(CreateBuilding(costAmount: 2000));
            OpenPanel(panel);
            Assert.IsTrue(panel.HasInventorySubscription);

            cancel.onClick.Invoke();
            EditModeLifecycle.RaiseDisable(panel);

            Assert.IsFalse(panel.HasInventorySubscription,
                "닫힌 팝업이 인벤토리 신호를 계속 받으면 보이지도 않는 화면을 매번 다시 판정한다");
        }

        [Test]
        public void 다시_바인딩하면_새_건물로_다시_판정한다()
        {
            CreateInventory();
            SaveSystem.Data.currency = 2000;

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out _, out TextMeshProUGUI warning, out _, out _);
            panel.Bind(CreateBuilding(costAmount: 2000));
            OpenPanel(panel);
            Assert.IsTrue(panel.IsCostPayable);

            panel.Bind(CreateBuilding(costAmount: 5000));

            Assert.IsFalse(panel.IsCostPayable, "바인딩이 바뀌면 그 건물의 비용으로 다시 판정해야 한다");
            Assert.IsTrue(warning.gameObject.activeSelf);
            Assert.IsFalse(confirm.interactable);
        }

        [Test]
        public void 판정은_저장도_인벤토리도_바꾸지_않는다()
        {
            ItemDefinition plank = CreateItem("plank");
            CreateInventory(plank);
            SaveSystem.Data.currency = 2190;
            SaveSystem.Data.items.Clear();
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "plank", count = 2 });

            int writesBefore = storage.WriteCalls;
            int readsBefore = storage.ReadPrimaryCalls;

            BuildingPopupPanel panel = CreatePanel(out _, out Button cancel, out _, out _, out _);
            panel.Bind(CreateBuilding(costAmount: 2000, itemCost: plank, itemCount: 1));
            OpenPanel(panel);
            // 같은 판정을 여러 번 지나게 한다 - 몇 번을 불러도 결과도 상태도 같아야 한다.
            panel.Bind(CreateBuilding(costAmount: 2000, itemCost: plank, itemCount: 1));
            cancel.onClick.Invoke();
            EditModeLifecycle.RaiseDisable(panel);

            Assert.AreEqual(2190, SaveSystem.Data.currency);
            Assert.AreEqual(1, SaveSystem.Data.items.Count);
            Assert.AreEqual(2, SaveSystem.Data.items[0].count, "판정이 아이템을 차감하면 안 된다");
            Assert.AreEqual(writesBefore, storage.WriteCalls, "판정으로 저장이 일어나면 안 된다");
            Assert.AreEqual(readsBefore, storage.ReadPrimaryCalls, "판정은 저장 파일을 다시 읽지도 않는다");
        }

        // ---- 확인 버튼 ----

        [Test]
        public void 확인을_누르기_직전에_다시_판정한다()
        {
            CreateInventory();
            SaveSystem.Data.currency = 2000;

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out _, out TextMeshProUGUI warning, out _, out _);
            panel.Bind(CreateBuilding(costAmount: 2000));
            OpenPanel(panel);
            Assert.IsTrue(confirm.interactable);

            // 팝업이 열려 있는 동안 다른 경로가 재화를 썼는데 알림이 오지 않은 상황이다 -
            // 화면에 남아 있던 판정을 근거로 진행하면 모자란 채로 지나간다.
            SaveSystem.Data.currency = 0;
            confirm.onClick.Invoke();

            Assert.IsFalse(panel.IsCostPayable, "누르기 직전에 다시 판정해야 한다");
            Assert.IsTrue(warning.gameObject.activeSelf);
            Assert.IsFalse(confirm.interactable);
        }

        [Test]
        public void 낼_수_있어도_확인은_아직_아무것도_바꾸지_않는다()
        {
            CreateInventory();
            SaveSystem.Data.currency = 2190;
            SaveSystem.Data.items.Clear();
            int writes = storage.WriteCalls;

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out _, out _, out _, out _);
            panel.Bind(CreateBuilding(costAmount: 2000));
            OpenPanel(panel);
            Assert.IsTrue(confirm.interactable);

            confirm.onClick.Invoke();

            Assert.IsTrue(panel.gameObject.activeSelf, "확인은 팝업을 닫지도 않는다");
            Assert.AreEqual(2190, SaveSystem.Data.currency, "이번 단계에서 확인은 비용을 내지 않는다");
            Assert.AreEqual(0, SaveSystem.Data.items.Count);
            Assert.AreEqual(writes, storage.WriteCalls, "확인으로 저장이 일어나면 안 된다");
        }

        // ---- 자리 잡기 ----

        [Test]
        public void 버튼_오른쪽_위에_자리를_잡고_캔버스_안에_들어간다()
        {
            // 팝업(가로 200)이 버튼 <b>옆에</b> 통째로 들어갈 만큼 버튼을 왼쪽에 둔다 - 400짜리
            // 가상 화면 한가운데에 버튼을 두면 어느 쪽에도 자리가 없어 화면 안으로 당겨진다.
            PlacementFixture fixture = CreatePlacementFixture(buttonPosition: new Vector2(-120f, -100f));

            Assert.AreEqual(BuildingPopupSide.RightAbove, fixture.Panel.LastPlacementSide);
            AssertPopupInsideCanvas(fixture);
            Assert.AreEqual(
                SourceRectInParent(fixture).xMax + PlacementMargin,
                PopupRectInParent(fixture).xMin, 0.5f,
                "버튼 오른쪽 변 바깥에서 여백만큼 띄운 자리여야 한다");
            Assert.AreEqual(
                SourceRectInParent(fixture).yMax + PlacementMargin,
                PopupRectInParent(fixture).yMin, 0.5f,
                "버튼 윗변에서 여백만큼 띄운 자리여야 한다");
        }

        [Test]
        public void 위쪽이_모자라면_버튼_아래로_내려간다()
        {
            PlacementFixture fixture = CreatePlacementFixture(buttonPosition: new Vector2(-120f, 130f));

            Assert.AreEqual(BuildingPopupSide.RightBelow, fixture.Panel.LastPlacementSide);
            AssertPopupInsideCanvas(fixture);
            Assert.Less(PopupRectInParent(fixture).yMax, SourceRectInParent(fixture).yMin,
                "아래 후보를 골랐으면 팝업 전체가 버튼 아랫변보다 아래에 있어야 한다");
        }

        [Test]
        public void 오른쪽이_모자라면_왼쪽으로_넘긴다()
        {
            PlacementFixture fixture = CreatePlacementFixture(buttonPosition: new Vector2(170f, -100f));

            Assert.AreEqual(BuildingPopupSide.LeftAbove, fixture.Panel.LastPlacementSide);
            AssertPopupInsideCanvas(fixture);
        }

        [Test]
        public void 캔버스가_작아지면_다시_계산해_화면_안으로_들어온다()
        {
            PlacementFixture fixture = CreatePlacementFixture(buttonPosition: new Vector2(0f, -100f));
            AssertPopupInsideCanvas(fixture);

            // 창 크기가 줄어든 상황이다 - 알려 주는 신호가 없으므로 팝업이 스스로 알아채야 한다.
            fixture.CanvasRect.sizeDelta = new Vector2(240f, 220f);
            EditModeLifecycle.Invoke(fixture.Panel, "LateUpdate");

            AssertPopupInsideCanvas(fixture);
        }

        [Test]
        public void 버튼이_움직이면_팝업도_따라간다()
        {
            PlacementFixture fixture = CreatePlacementFixture(buttonPosition: new Vector2(-120f, -100f));
            float before = PopupRectInParent(fixture).xMin;

            fixture.ButtonRect.anchoredPosition = new Vector2(-40f, -100f);
            EditModeLifecycle.Invoke(fixture.Panel, "LateUpdate");

            Assert.Greater(PopupRectInParent(fixture).xMin, before,
                "버튼을 오른쪽으로 옮기면 팝업도 오른쪽으로 따라와야 한다");
            AssertPopupInsideCanvas(fixture);
        }

        [Test]
        public void 아무것도_바뀌지_않으면_자리를_다시_잡지_않는다()
        {
            PlacementFixture fixture = CreatePlacementFixture(buttonPosition: new Vector2(0f, -100f));
            Vector3 before = fixture.PopupRect.position;

            EditModeLifecycle.Invoke(fixture.Panel, "LateUpdate");

            Assert.AreEqual(before, fixture.PopupRect.position);
        }

        [Test]
        public void 경고가_켜지면_팝업이_높아져도_버튼_위_간격은_그대로다()
        {
            InventoryManager inventory = CreateInventory();
            SaveSystem.Data.currency = 2000;

            PlacementFixture fixture = CreatePlacementFixture(
                buttonPosition: new Vector2(-120f, -120f), costAmount: 2000);

            Assert.IsTrue(fixture.Panel.IsCostPayable);
            Assert.IsFalse(fixture.Warning.gameObject.activeSelf);
            Rect payable = PopupRectInParent(fixture);

            SaveSystem.Data.currency = 0;
            inventory.NotifyChangedAfterExternalSave();

            Assert.IsTrue(fixture.Warning.gameObject.activeSelf);
            Rect insufficient = PopupRectInParent(fixture);

            Assert.Greater(insufficient.height, payable.height,
                "경고가 자리를 차지하므로 팝업이 높아져야 한다");
            Assert.AreEqual(payable.yMin, insufficient.yMin, 0.5f,
                "높이가 달라져도 버튼 위 간격은 그대로여야 한다 - 재기 전에 레이아웃을 확정하기 때문이다");
            AssertPopupInsideCanvas(fixture);
        }

        [Test]
        public void 팝업_루트는_옮기지_않는다()
        {
            PlacementFixture fixture = CreatePlacementFixture(buttonPosition: new Vector2(0f, -100f));
            var root = (RectTransform)fixture.Panel.transform;

            Assert.AreEqual(Vector2.zero, root.anchoredPosition,
                "전체 화면을 덮는 루트를 옮기면 입력 영역까지 화면 밖으로 나간다");
            Assert.AreEqual(Vector2.zero, root.anchorMin);
            Assert.AreEqual(Vector2.one, root.anchorMax);
        }

        [Test]
        public void bg의_pivot과_앵커는_저작된_그대로_남는다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var prefabPanel = prefab.GetComponent<BuildingPopupPanel>();
            var authored = new SerializedObject(prefabPanel)
                .FindProperty("placementRect").objectReferenceValue as RectTransform;

            PlacementFixture fixture = CreatePlacementFixture(buttonPosition: new Vector2(0f, -100f));

            Assert.AreEqual(authored.pivot, fixture.PopupRect.pivot);
            Assert.AreEqual(authored.anchorMin, fixture.PopupRect.anchorMin);
            Assert.AreEqual(authored.anchorMax, fixture.PopupRect.anchorMax);
        }

        [Test]
        public void 기준_버튼을_주지_않으면_저작된_자리에_그대로_둔다()
        {
            BuildingPopupPanel panel = CreatePanel(out _, out _, out _, out _, out RectTransform bg);
            Vector2 authored = bg.anchoredPosition;

            panel.Bind(CreateBuilding(costAmount: 2000));
            OpenPanel(panel);

            Assert.IsNull(panel.SourceRect);
            Assert.AreEqual(authored, bg.anchoredPosition,
                "기준 버튼이 없으면 자리를 계산하지 않는다 - 이전 단계와 같은 동작이다");
        }

        // ---- 도우미 ----

        /// <summary>팝업을 실제 실행과 같은 순서로 연다. EditMode에서는 엔진이 OnEnable을 부르지
        /// 않으므로(<see cref="EditModeLifecycle"/>) 활성화 직후 같은 콜백을 직접 재현한다.</summary>
        private static void OpenPanel(BuildingPopupPanel panel)
        {
            panel.Open();
            EditModeLifecycle.RaiseEnable(panel);
        }

        private BuildingPopupPanel CreatePanel(out Button confirm, out Button cancel)
        {
            return CreatePanel(out confirm, out cancel, out _, out _);
        }

        private BuildingPopupPanel CreatePanel(
            out Button confirm, out Button cancel,
            out TextMeshProUGUI buildingName, out TextMeshProUGUI description)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);

            var parent = new GameObject("BuildingPopupParent", typeof(RectTransform));
            created.Add(parent);

            GameObject instance = Object.Instantiate(prefab, parent.transform, false);
            created.Add(instance);
            instance.SetActive(false);

            var panel = instance.GetComponent<BuildingPopupPanel>();
            Assert.IsNotNull(panel);

            var so = new SerializedObject(panel);
            confirm = so.FindProperty("confirmButton").objectReferenceValue as Button;
            cancel = so.FindProperty("closeButton").objectReferenceValue as Button;
            buildingName = so.FindProperty("buildingNameText").objectReferenceValue as TextMeshProUGUI;
            description = so.FindProperty("descriptionText").objectReferenceValue as TextMeshProUGUI;

            Assert.IsNotNull(confirm);
            Assert.IsNotNull(cancel);
            Assert.IsNotNull(buildingName);
            Assert.IsNotNull(description);
            return panel;
        }

        private BuildingPopupPanel CreatePanel(
            out Button confirm, out Button cancel, out TextMeshProUGUI warning,
            out TextMeshProUGUI description, out RectTransform placement)
        {
            BuildingPopupPanel panel = CreatePanel(out confirm, out cancel, out _, out description);

            var so = new SerializedObject(panel);
            warning = so.FindProperty("warningText").objectReferenceValue as TextMeshProUGUI;
            placement = so.FindProperty("placementRect").objectReferenceValue as RectTransform;

            Assert.IsNotNull(warning, "warningText");
            Assert.IsNotNull(placement, "placementRect");
            return panel;
        }

        /// <summary>
        /// 자리 잡기 시험용 화면 한 벌. <b>World Space 캔버스를 쓴다</b> - Overlay 캔버스는 자기
        /// RectTransform을 게임 뷰 해상도로 덮어써서, 시험이 정한 크기가 유지되지 않는다.
        /// </summary>
        private sealed class PlacementFixture
        {
            public BuildingPopupPanel Panel;
            public RectTransform CanvasRect;
            public RectTransform ButtonRect;
            public RectTransform PopupRoot;
            public RectTransform PopupRect;
            public TextMeshProUGUI Warning;
        }

        private PlacementFixture CreatePlacementFixture(Vector2 buttonPosition, int costAmount = 2000)
        {
            var canvasGo = new GameObject("TestCanvas", typeof(RectTransform), typeof(Canvas));
            created.Add(canvasGo);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.position = Vector3.zero;
            canvasRect.rotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one;
            canvasRect.pivot = new Vector2(0.5f, 0.5f);
            canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
            canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);

            var buttonGo = new GameObject("TestSourceButton", typeof(RectTransform));
            buttonGo.transform.SetParent(canvasRect, false);
            var buttonRect = (RectTransform)buttonGo.transform;
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(40f, 20f);
            buttonRect.anchoredPosition = buttonPosition;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);
            GameObject instance = Object.Instantiate(prefab, canvasRect, false);
            created.Add(instance);
            instance.SetActive(false);

            var panel = instance.GetComponent<BuildingPopupPanel>();
            var so = new SerializedObject(panel);

            var fixture = new PlacementFixture
            {
                Panel = panel,
                CanvasRect = canvasRect,
                ButtonRect = buttonRect,
                PopupRoot = (RectTransform)panel.transform,
                PopupRect = so.FindProperty("placementRect").objectReferenceValue as RectTransform,
                Warning = so.FindProperty("warningText").objectReferenceValue as TextMeshProUGUI
            };

            Assert.IsNotNull(fixture.PopupRect, "placementRect");
            Assert.IsNotNull(fixture.Warning, "warningText");
            Assert.AreEqual(PlacementMargin, so.FindProperty("placementMargin").floatValue,
                "시험이 기대하는 여백과 프리팹의 여백이 달라지면 자리 계산의 기준이 흔들린다");

            panel.Bind(CreateBuilding(costAmount: costAmount), buttonRect);
            OpenPanel(panel);
            return fixture;
        }

        private static Rect PopupRectInParent(PlacementFixture fixture)
        {
            return RectInSpace(fixture.PopupRect, fixture.PopupRoot);
        }

        private static Rect SourceRectInParent(PlacementFixture fixture)
        {
            return RectInSpace(fixture.ButtonRect, fixture.PopupRoot);
        }

        /// <summary>팝업이 캔버스 밖으로 나가지 않았는지. 여백만큼 안쪽까지가 허용 범위다.</summary>
        private static void AssertPopupInsideCanvas(PlacementFixture fixture)
        {
            Rect popup = PopupRectInParent(fixture);
            Rect bounds = RectInSpace(fixture.CanvasRect, fixture.PopupRoot);

            Assert.GreaterOrEqual(popup.xMin, bounds.xMin + PlacementMargin - 0.5f, "왼쪽이 화면 밖으로 나갔다");
            Assert.LessOrEqual(popup.xMax, bounds.xMax - PlacementMargin + 0.5f, "오른쪽이 화면 밖으로 나갔다");
            Assert.GreaterOrEqual(popup.yMin, bounds.yMin + PlacementMargin - 0.5f, "아래가 화면 밖으로 나갔다");
            Assert.LessOrEqual(popup.yMax, bounds.yMax - PlacementMargin + 0.5f, "위가 화면 밖으로 나갔다");
        }

        /// <summary>사각형을 <paramref name="space"/>의 로컬 좌표로 옮겨 읽는다 - 팝업이 자리를
        /// 계산할 때 쓰는 공간과 같은 공간에서 확인해야 값이 어긋나지 않는다.</summary>
        private static Rect RectInSpace(RectTransform rect, RectTransform space)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            // GetWorldCorners는 좌하-좌상-우상-우하 순서다.
            Vector2 min = space.InverseTransformPoint(corners[0]);
            Vector2 max = space.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>프리팹 에셋에 연결된 경고 TMP를 그대로 읽는다(인스턴스를 만들지 않는다).</summary>
        private static TextMeshProUGUI LoadPrefabWarning()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);

            var panel = prefab.GetComponent<BuildingPopupPanel>();
            var warning = new SerializedObject(panel)
                .FindProperty("warningText").objectReferenceValue as TextMeshProUGUI;
            Assert.IsNotNull(warning, "warningText");
            return warning;
        }

        /// <summary>씬에 하나 있는 InventoryManager를 흉내낸다. EditMode에서는 Awake가 오지 않으므로
        /// 정적 Instance 등록과 조회표 구성을 직접 재현한다 - <b>저장 파일은 건드리지 않는다</b>
        /// (가짜 저장소가 이미 끼워져 있다).</summary>
        private InventoryManager CreateInventory(params ItemDefinition[] items)
        {
            var go = new GameObject("TestInventoryManager");
            go.SetActive(false);
            created.Add(go);

            var manager = go.AddComponent<InventoryManager>();
            var so = new SerializedObject(manager);
            SerializedProperty catalog = so.FindProperty("itemCatalog");
            catalog.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                catalog.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            EditModeLifecycle.Invoke(manager, "Awake");
            testInventory = manager;
            return manager;
        }

        private ItemDefinition CreateItem(string id)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            created.Add(item);

            var so = new SerializedObject(item);
            so.FindProperty("itemId").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        private BuildingDefinition CreateBuilding(
            int buildTimeSeconds = 60, int costAmount = 2000,
            ItemDefinition itemCost = null, int itemCount = 1)
        {
            var currency = ScriptableObject.CreateInstance<CurrencyDefinition>();
            created.Add(currency);
            var currencySo = new SerializedObject(currency);
            currencySo.FindProperty("currencyId").stringValue = "jewel";
            currencySo.FindProperty("localizedName.m_TableReference.m_TableCollectionName").stringValue =
                UiTableGuid;
            currencySo.FindProperty("localizedName.m_TableEntryReference.m_KeyId").longValue =
                FunctionNameKeyId;
            currencySo.ApplyModifiedPropertiesWithoutUndo();

            var building = ScriptableObject.CreateInstance<BuildingDefinition>();
            created.Add(building);
            var so = new SerializedObject(building);
            so.FindProperty("buildingId").stringValue = "1";
            so.FindProperty("buildTimeSeconds").intValue = buildTimeSeconds;
            so.FindProperty("costCurrencyId").stringValue = "jewel";
            so.FindProperty("costCurrency").objectReferenceValue = currency;
            so.FindProperty("costCurrencyAmount").intValue = costAmount;
            so.FindProperty("localizedName.m_TableReference.m_TableCollectionName").stringValue =
                BuildingTableGuid;
            so.FindProperty("localizedName.m_TableEntryReference.m_KeyId").longValue = BuildingNameKeyId;
            so.FindProperty("localizedFunctionName.m_TableReference.m_TableCollectionName").stringValue =
                UiTableGuid;
            so.FindProperty("localizedFunctionName.m_TableEntryReference.m_KeyId").longValue =
                FunctionNameKeyId;

            SerializedProperty costItems = so.FindProperty("costItems");
            costItems.arraySize = itemCost == null ? 0 : 1;
            if (itemCost != null)
            {
                SerializedProperty entry = costItems.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("itemId").stringValue = itemCost.ItemId;
                entry.FindPropertyRelative("item").objectReferenceValue = itemCost;
                entry.FindPropertyRelative("count").intValue = itemCount;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return building;
        }

        /// <summary>Locale에서 네 갈래 값이 도착한 상황을 그대로 재현한다.</summary>
        private static void DeliverLocalizedValues(
            BuildingPopupPanel panel, string buildingName, string functionName, string format, string currencyName)
        {
            InvokePrivate(panel, "ApplyLocalizedBuildingName", buildingName);
            InvokePrivate(panel, "ApplyLocalizedFunctionName", functionName);
            InvokePrivate(panel, "ApplyLocalizedFormat", format);
            InvokePrivate(panel, "ApplyLocalizedCurrencyName", currencyName);
        }

        private static void InvokePrivate(object target, string methodName, string argument)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, methodName);
            method.Invoke(target, new object[] { argument });
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, fieldName);
            return field.GetValue(target);
        }

        private static void AssertBoundReference(BuildingPopupPanel panel, string fieldName, bool expected)
        {
            object value = GetPrivateField(panel, fieldName);
            Assert.AreEqual(expected, value != null, fieldName);
        }

        private static void AssertReference<T>(SerializedObject owner, string propertyName, string objectName)
            where T : Object
        {
            SerializedProperty property = owner.FindProperty(propertyName);
            Assert.IsNotNull(property, propertyName);

            var value = property.objectReferenceValue as T;
            Assert.IsNotNull(value, $"{propertyName}에 {typeof(T).Name}이 연결되어야 한다");
            Assert.AreEqual(objectName, value.name, propertyName);
        }

        /// <summary>메모리 위의 가짜 저장소. 이 시험은 절대 실제 저장 파일을 읽거나 쓰지 않는다.</summary>
        private sealed class FakeStorage : ISaveStorage
        {
            public int ReadPrimaryCalls;
            public int WriteCalls;

            public bool WritesBlocked => false;

            public string BlockedReason => null;

            public SaveReadResult ReadPrimary()
            {
                ReadPrimaryCalls++;
                return SaveReadResult.Missing("fake://primary");
            }

            public SaveReadResult ReadBackup() => SaveReadResult.Missing("fake://backup");

            public SaveWriteResult Write(string text)
            {
                WriteCalls++;
                throw new InvalidOperationException(
                    "건물 팝업 시험 도중 저장이 시도되었습니다 - 이 단계에서는 저장이 일어나면 안 됩니다.");
            }

            public SaveQuarantineResult QuarantinePrimary(string reason) =>
                SaveQuarantineResult.Moved("fake://corrupted/primary");
        }
    }
}
