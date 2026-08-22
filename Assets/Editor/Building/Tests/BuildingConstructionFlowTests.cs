using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Building;
using Common;
using Inventory;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BuildingEditor.Tests
{
    /// <summary>
    /// 확인 버튼에서 시작까지의 <b>연결</b> 시험 - 팝업이 건설 서비스에 넘기고, 그 답을 화면에 옮기는
    /// 자리만 본다(비용/기록/저장의 규칙 자체는
    /// <see cref="BuildingConstructionServiceTests"/>가 확인한다).
    ///
    /// <b>실제 저장 파일은 건드리지 않는다.</b> 저장 문서는 메모리 위의 것을 끼워 넣고, 서비스에는
    /// 시험이 만든 저장 함수와 고정 시계를 주입한다.
    ///
    /// 안내 문구(01_UI / 42)가 표에 실제로 있는지도 여기서 함께 확인한다 - 코드가 문구를 지어내지
    /// 않으므로, 표에 값이 없으면 화면에 아무것도 뜨지 않는 것이 곧 결함이다.
    /// </summary>
    public sealed class BuildingConstructionFlowTests
    {
        private const string PrefabPath = "Assets/Art/UI/Prefab/dialog/dialog_BuildingPopup.prefab";
        private const string SharedTablePath = "Assets/Localization/Tables/01_UI/01_UI Shared Data.asset";
        private const string EnglishTablePath = "Assets/Localization/Tables/01_UI/01_UI_en.asset";
        private const string KoreanTablePath = "Assets/Localization/Tables/01_UI/01_UI_ko-KR.asset";

        /// <summary>01_UI / 41(비용 부족 경고). 이번 단계에서 <b>지워지지 않았는지</b>까지 확인한다.</summary>
        private const long WarningKeyId = 8970130103984128L;

        /// <summary>01_UI / 42(건설 시작 안내).</summary>
        private const long ConstructionStartedKeyId = 8970130103984129L;

        private static readonly FieldInfo SaveDataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo SaveOverrideField =
            typeof(InventoryManager).GetField("saveOverride", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly DateTime FixedNowUtc =
            new DateTime(2026, 8, 22, 10, 30, 0, DateTimeKind.Utc);

        private readonly List<Object> created = new List<Object>();

        private GameObject inventoryHost;
        private InventoryManager inventory;
        private object originalSaveData;
        private int saveCalls;
        private bool saveSucceeds;
        private int changedCount;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(SaveDataField, "SaveSystem.data를 찾지 못했습니다 - 시험이 실제 저장 파일을 씁니다.");
            Assert.IsNotNull(SaveOverrideField);

            originalSaveData = SaveDataField.GetValue(null);
            SaveDataField.SetValue(null, new SaveData());

            saveCalls = 0;
            saveSucceeds = true;
            changedCount = 0;

            SaveOverrideField.SetValue(null, new Func<bool>(() =>
                throw new InvalidOperationException(
                    "건설은 인벤토리의 저장 경로를 쓰지 않습니다 - 저장은 서비스가 한 번만 합니다.")));

            inventoryHost = new GameObject("BuildingConstructionFlowTests");
            created.Add(inventoryHost);
            inventory = inventoryHost.AddComponent<InventoryManager>();
            EditModeLifecycle.Invoke(inventory, "Awake");

            InventoryManager.InventoryChanged += CountChanged;
        }

        [TearDown]
        public void TearDown()
        {
            InventoryManager.InventoryChanged -= CountChanged;

            if (inventory != null) EditModeLifecycle.Invoke(inventory, "OnDestroy");
            inventory = null;

            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();

            SaveOverrideField.SetValue(null, null);
            SaveDataField.SetValue(null, originalSaveData);
        }

        private void CountChanged() => changedCount++;

        // ---- 확인 성공 ----

        [Test]
        public void 확인이_성공하면_팝업이_닫히고_비용과_기록이_함께_남는다()
        {
            SaveSystem.Data.currency = 2190;

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out TextMeshProUGUI warning);
            panel.SetConstructionService(CreateService());
            panel.Bind(CreateBuilding(costAmount: 2000, buildTimeSeconds: 60));
            OpenPanel(panel);
            Assert.IsTrue(confirm.interactable);

            confirm.onClick.Invoke();

            Assert.IsFalse(panel.gameObject.activeSelf, "성공하면 평소의 닫기 경로로 닫는다");
            Assert.AreEqual(190, SaveSystem.Data.currency);
            Assert.AreEqual(1, SaveSystem.Data.buildingConstructions.Count);
            Assert.AreEqual("1", SaveSystem.Data.buildingConstructions[0].buildingId);
            Assert.AreEqual("2026-08-22T10:30:00.0000000Z",
                SaveSystem.Data.buildingConstructions[0].startedAtUtc);
            Assert.AreEqual(1, saveCalls, "비용과 기록은 한 번의 저장으로 함께 남는다");
            Assert.AreEqual(1, changedCount);
            Assert.IsTrue(panel.LastStartResult.HasValue);
            Assert.IsTrue(panel.LastStartResult.Value.Success);

            EditModeLifecycle.RaiseDisable(panel);
            Assert.IsFalse(warning.gameObject.activeSelf, "닫힌 팝업에 경고가 남으면 안 된다");
            Assert.IsFalse(confirm.interactable);
            Assert.IsFalse(panel.HasLocalizationSubscriptions);
            Assert.IsFalse(panel.HasInventorySubscription);
        }

        // ---- 확인 실패 ----

        [Test]
        public void 저장이_실패하면_팝업이_열린_채_경고가_켜지고_값도_그대로다()
        {
            SaveSystem.Data.currency = 2190;
            saveSucceeds = false;

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out TextMeshProUGUI warning);
            panel.SetConstructionService(CreateService());
            panel.Bind(CreateBuilding(costAmount: 2000));
            OpenPanel(panel);
            Assert.IsTrue(confirm.interactable);
            Assert.IsFalse(warning.gameObject.activeSelf);

            LogAssert.Expect(LogType.Error, new Regex("저장하지 못해"));
            confirm.onClick.Invoke();

            Assert.IsTrue(panel.gameObject.activeSelf, "실패한 확인은 팝업을 닫지 않는다");
            Assert.IsTrue(warning.gameObject.activeSelf,
                "보유량은 충분했으므로 판정만으로는 실패가 드러나지 않는다 - 그래도 경고는 켜져야 한다");
            Assert.IsFalse(confirm.interactable);
            Assert.AreEqual(2190, SaveSystem.Data.currency, "되돌렸으므로 재화는 그대로다");
            Assert.AreEqual(0, SaveSystem.Data.buildingConstructions.Count);
            Assert.AreEqual(0, changedCount, "실패한 시작에 성공 알림이 나가면 안 된다");
            Assert.AreEqual(BuildingConstructionStartCode.SaveFailed, panel.LastStartResult.Value.Code);
        }

        [Test]
        public void 인벤토리가_바뀌면_실패_표시가_풀리고_다시_판정한다()
        {
            SaveSystem.Data.currency = 2190;
            saveSucceeds = false;

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out TextMeshProUGUI warning);
            panel.SetConstructionService(CreateService());
            panel.Bind(CreateBuilding(costAmount: 2000));
            OpenPanel(panel);

            LogAssert.Expect(LogType.Error, new Regex("저장하지 못해"));
            confirm.onClick.Invoke();
            Assert.IsTrue(warning.gameObject.activeSelf);

            // 다른 경로가 재화를 바꿔 알려 왔다 - 지금 값이 다시 근거가 된다.
            inventory.NotifyChangedAfterExternalSave();

            Assert.IsFalse(warning.gameObject.activeSelf);
            Assert.IsTrue(confirm.interactable);
        }

        [Test]
        public void 다시_열면_직전_실패의_경고를_들고_오지_않는다()
        {
            SaveSystem.Data.currency = 2190;
            saveSucceeds = false;

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out TextMeshProUGUI warning);
            panel.SetConstructionService(CreateService());
            BuildingDefinition building = CreateBuilding(costAmount: 2000);
            panel.Bind(building);
            OpenPanel(panel);

            LogAssert.Expect(LogType.Error, new Regex("저장하지 못해"));
            confirm.onClick.Invoke();
            Assert.IsTrue(warning.gameObject.activeSelf);

            panel.Close();
            EditModeLifecycle.RaiseDisable(panel);
            Assert.IsFalse(warning.gameObject.activeSelf, "닫으면서 표시가 초기화된다");

            saveSucceeds = true;
            OpenPanel(panel);

            Assert.IsFalse(warning.gameObject.activeSelf, "다시 열 때는 지금 보유량이 근거다");
            Assert.IsTrue(confirm.interactable);
        }

        [Test]
        public void 이미_시작된_건물은_확인을_눌러도_두_번_빠지지_않는다()
        {
            SaveSystem.Data.currency = 5000;
            BuildingConstructionService service = CreateService();

            BuildingPopupPanel panel = CreatePanel(out Button confirm, out TextMeshProUGUI warning);
            panel.SetConstructionService(service);
            BuildingDefinition building = CreateBuilding(costAmount: 2000);
            panel.Bind(building);
            OpenPanel(panel);

            confirm.onClick.Invoke();
            Assert.AreEqual(3000, SaveSystem.Data.currency);

            // 같은 팝업을 다시 열어 한 번 더 눌러 본다(버튼이 숨기 전의 한 프레임을 흉내낸다).
            OpenPanel(panel);
            confirm.onClick.Invoke();

            Assert.AreEqual(3000, SaveSystem.Data.currency, "두 번째 확인은 비용을 건드리지 않는다");
            Assert.AreEqual(1, SaveSystem.Data.buildingConstructions.Count);
            Assert.AreEqual(1, saveCalls);
            Assert.IsTrue(warning.gameObject.activeSelf, "막힌 요청은 화면에 남아야 한다");
        }

        // ---- 표(01_UI) ----

        [Test]
        public void 표에_건설_시작_안내_42가_있고_비용_부족_41도_그대로다()
        {
            var shared = AssetDatabase.LoadAssetAtPath<SharedTableData>(SharedTablePath);
            Assert.IsNotNull(shared, SharedTablePath);

            SharedTableData.SharedTableEntry warning = shared.GetEntry("41");
            Assert.IsNotNull(warning, "41(비용 부족)이 사라지면 팝업의 경고가 빈 칸이 된다");
            Assert.AreEqual(WarningKeyId, warning.Id);

            SharedTableData.SharedTableEntry started = shared.GetEntry("42");
            Assert.IsNotNull(started, "42(건설 시작 안내)가 표에 있어야 한다");
            Assert.AreEqual(ConstructionStartedKeyId, started.Id,
                "씬이 가리키는 Entry Id와 표의 Id가 달라지면 문구가 비어 버린다");
        }

        [Test]
        public void 영어와_한국어_모두_42의_값을_갖는다()
        {
            var english = AssetDatabase.LoadAssetAtPath<StringTable>(EnglishTablePath);
            var korean = AssetDatabase.LoadAssetAtPath<StringTable>(KoreanTablePath);
            Assert.IsNotNull(english, EnglishTablePath);
            Assert.IsNotNull(korean, KoreanTablePath);

            StringTableEntry en = english.GetEntry(ConstructionStartedKeyId);
            StringTableEntry ko = korean.GetEntry(ConstructionStartedKeyId);

            Assert.IsNotNull(en, "영어 값이 없으면 그 언어에서 안내가 뜨지 않는다");
            Assert.IsNotNull(ko, "한국어 값이 없으면 그 언어에서 안내가 뜨지 않는다");

            // 영어 번역은 <b>미정</b>이라 지금은 한국어 임시 문구가 들어 있다. 여기서 확인하는 것은
            // "번역이 무엇인가"가 아니라 <b>어느 언어에서도 빈 칸이 아니라는 것</b>이다 - 값이 비면
            // 그 언어에서는 토스트가 통째로 뜨지 않는다(코드가 대체 문구를 지어내지 않으므로).
            Assert.IsFalse(string.IsNullOrWhiteSpace(en.LocalizedValue));
            Assert.IsFalse(string.IsNullOrWhiteSpace(ko.LocalizedValue));

            // 41도 함께 살아 있어야 한다 - 42를 더하면서 앞의 줄을 밀어내지 않았는지 본다.
            Assert.IsNotNull(english.GetEntry(WarningKeyId));
            Assert.IsNotNull(korean.GetEntry(WarningKeyId));
        }

        // ---- 도우미 ----

        private BuildingConstructionService CreateService()
        {
            return new BuildingConstructionService(
                inventory,
                () => SaveSystem.Data,
                () => { saveCalls++; return saveSucceeds; },
                () => FixedNowUtc);
        }

        private BuildingPopupPanel CreatePanel(out Button confirm, out TextMeshProUGUI warning)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);

            GameObject instance = Object.Instantiate(prefab);
            created.Add(instance);
            instance.SetActive(false);

            var panel = instance.GetComponent<BuildingPopupPanel>();
            Assert.IsNotNull(panel);

            var so = new SerializedObject(panel);
            confirm = so.FindProperty("confirmButton").objectReferenceValue as Button;
            warning = so.FindProperty("warningText").objectReferenceValue as TextMeshProUGUI;
            Assert.IsNotNull(confirm, "confirmButton");
            Assert.IsNotNull(warning, "warningText");
            return panel;
        }

        /// <summary>실제 실행에서 Open이 켠 오브젝트에 이어지는 OnEnable까지 재현한다.</summary>
        private static void OpenPanel(BuildingPopupPanel panel)
        {
            panel.Open();
            EditModeLifecycle.RaiseEnable(panel);
        }

        private BuildingDefinition CreateBuilding(int costAmount = 2000, int buildTimeSeconds = 60)
        {
            var building = ScriptableObject.CreateInstance<BuildingDefinition>();
            created.Add(building);

            var so = new SerializedObject(building);
            so.FindProperty("buildingId").stringValue = "1";
            so.FindProperty("buildTimeSeconds").intValue = buildTimeSeconds;
            so.FindProperty("costCurrencyId").stringValue = "jewel";
            so.FindProperty("costCurrencyAmount").intValue = costAmount;
            so.FindProperty("costItems").arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            return building;
        }
    }
}
