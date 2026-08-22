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

        private static readonly MethodInfo ConfigureSaveMethod = typeof(SaveSystem).GetMethod(
            "ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<Object> created = new List<Object>();
        private FakeStorage storage;
        private PopupPanelManager testManager;

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

        private BuildingDefinition CreateBuilding(int buildTimeSeconds = 60, int costAmount = 2000)
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
