using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Building;
using Common;
using Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BuildingEditor.Tests
{
    /// <summary>
    /// 건설 시작 규칙(<see cref="BuildingConstructionService"/>)의 시험.
    ///
    /// <b>파일도 시계도 진짜를 쓰지 않는다.</b> 저장 문서는 리플렉션으로 끼워 넣은 메모리 위의
    /// <see cref="SaveData"/>이고, 저장 함수와 시계는 서비스에 <b>주입</b>한다 - 그래서 여기서
    /// 확인하는 "저장 몇 번 / 어떤 시각이 적히는가"가 실행 환경에 따라 달라지지 않는다.
    ///
    /// 확인하는 계약은 여섯이다.
    /// <list type="number">
    ///   <item>비용을 낼 수 없으면 <b>아무것도</b> 바뀌지 않는다(기록도, 값도, 저장도, 알림도).</item>
    ///   <item>낼 수 있으면 비용과 기록이 <b>한 번의 저장</b>으로 함께 남는다.</item>
    ///   <item>저장이 실패하면 <b>전부 되돌아간다</b> - 성공을 뜻하는 신호는 하나도 나가지 않는다.</item>
    ///   <item>같은 건물은 두 번 시작되지 않는다(중복 클릭도, 재진입도).</item>
    ///   <item>단계와 남은 시간은 <b>기록과 시각에서만</b> 파생되며 조회는 파일을 건드리지 않는다.</item>
    ///   <item>완성 안내는 <b>한 번뿐</b>이고, 그 사실이 파일에 남아 앱을 다시 켜도 되풀이되지 않는다.</item>
    /// </list>
    /// </summary>
    public sealed class BuildingConstructionServiceTests
    {
        private static readonly FieldInfo SaveDataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo SaveOverrideField =
            typeof(InventoryManager).GetField("saveOverride", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>시험이 쓰는 고정 시각. 기록되는 문자열을 글자 그대로 확인하기 위해 UTC로 고정한다.</summary>
        private static readonly DateTime FixedNowUtc =
            new DateTime(2026, 8, 22, 10, 30, 0, DateTimeKind.Utc);

        private const string FixedNowText = "2026-08-22T10:30:00.0000000Z";

        private readonly List<Object> created = new List<Object>();

        private GameObject host;
        private InventoryManager inventory;
        private object originalSaveData;

        private int saveCalls;
        private bool saveSucceeds;
        private int inventorySaveCalls;
        private int changedCount;
        private int startedCount;
        private DateTime nowUtc;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(SaveDataField, "SaveSystem.data를 찾지 못했습니다 - 시험이 실제 저장 파일을 씁니다.");
            Assert.IsNotNull(SaveOverrideField);

            originalSaveData = SaveDataField.GetValue(null);
            SaveDataField.SetValue(null, new SaveData());

            saveCalls = 0;
            saveSucceeds = true;
            nowUtc = FixedNowUtc;

            // 인벤토리 쪽 저장 경로는 <b>한 번도</b> 쓰이면 안 된다 - 비용은 저장하지 않는 경로로
            // 빠지고 저장은 서비스가 한 번만 한다.
            inventorySaveCalls = 0;
            SaveOverrideField.SetValue(null, new Func<bool>(() => { inventorySaveCalls++; return true; }));

            host = new GameObject("BuildingConstructionServiceTests");
            created.Add(host);
            inventory = host.AddComponent<InventoryManager>();
            EditModeLifecycle.Invoke(inventory, "Awake");

            changedCount = 0;
            startedCount = 0;
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

        // ---- 낼 수 없을 때 ----

        [Test]
        public void 재화가_1_모자라면_아무것도_바뀌지_않는다()
        {
            SaveSystem.Data.currency = 1999;
            BuildingConstructionService service = CreateService();
            BuildingDefinition building = CreateBuilding(costAmount: 2000);

            BuildingConstructionStartResult result = service.TryStartConstruction(building);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(BuildingConstructionStartCode.CostRejected, result.Code);
            Assert.AreEqual(InventoryCostFailureReason.InsufficientCurrency, result.Cost.Reason);

            Assert.AreEqual(1999, SaveSystem.Data.currency, "실패한 시작은 재화를 건드리지 않는다");
            Assert.AreEqual(0, SaveSystem.Data.buildingConstructions.Count, "기록이 생기면 안 된다");
            Assert.AreEqual(0, saveCalls, "저장은 한 번도 일어나면 안 된다");
            Assert.AreEqual(0, changedCount, "바뀐 것이 없으므로 알림도 없다");
            Assert.AreEqual(0, startedCount, "실패한 시작에 시작 이벤트가 오면 안 된다");
        }

        [Test]
        public void 화면_판정_뒤에_재화가_줄면_최종_판정에서_막고_기록도_남기지_않는다()
        {
            SaveSystem.Data.currency = 2000;
            BuildingConstructionService service = CreateService();
            BuildingDefinition building = CreateBuilding(costAmount: 2000);

            // 팝업이 "낼 수 있다"를 그린 시점.
            Assert.IsTrue(service.EvaluateCost(building).IsPayable);

            // 그 사이에 다른 경로가 재화를 썼다.
            SaveSystem.Data.currency = 1;

            BuildingConstructionStartResult result = service.TryStartConstruction(building);

            Assert.AreEqual(BuildingConstructionStartCode.CostRejected, result.Code,
                "화면에 남아 있던 판정을 믿으면 모자란 채로 지나간다");
            Assert.AreEqual(1, SaveSystem.Data.currency);
            Assert.AreEqual(0, SaveSystem.Data.buildingConstructions.Count,
                "판정에서 막힌 요청이 임시 기록을 남기면 안 된다");
            Assert.AreEqual(0, saveCalls);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, startedCount);
        }

        [Test]
        public void 아이템이_모자라면_재화도_빠지지_않는다()
        {
            ItemDefinition plank = CreateItem("plank");
            RegisterItems(plank);
            SaveSystem.Data.currency = 999999;
            Hold("plank", 2);

            BuildingConstructionService service = CreateService();
            BuildingDefinition building = CreateBuilding(costAmount: 100, itemCost: plank, itemCount: 3);

            BuildingConstructionStartResult result = service.TryStartConstruction(building);

            Assert.AreEqual(BuildingConstructionStartCode.CostRejected, result.Code);
            Assert.AreEqual(InventoryCostFailureReason.InsufficientItem, result.Cost.Reason);
            Assert.AreEqual(999999, SaveSystem.Data.currency, "비용은 전부 내거나 하나도 내지 않거나다");
            Assert.AreEqual(2, SaveSystem.Data.items[0].count);
            Assert.AreEqual(0, SaveSystem.Data.buildingConstructions.Count);
            Assert.AreEqual(0, saveCalls);
        }

        [Test]
        public void 건물_정의가_없으면_시작하지_않는다()
        {
            BuildingConstructionService service = CreateService();

            Assert.AreEqual(BuildingConstructionStartCode.InvalidBuilding,
                service.TryStartConstruction(null).Code);
            Assert.AreEqual(BuildingConstructionStartCode.InvalidBuilding,
                service.TryStartConstruction(CreateBuilding(buildingId: string.Empty)).Code);
            Assert.AreEqual(0, saveCalls);
        }

        // ---- 낼 수 있을 때 ----

        [Test]
        public void 재화가_정확히_같으면_한_번의_저장으로_비용과_기록이_함께_남는다()
        {
            SaveSystem.Data.currency = 2000;
            BuildingConstructionService service = CreateService();
            service.ConstructionStarted += (b, s) => startedCount++;
            BuildingDefinition building = CreateBuilding(costAmount: 2000, buildTimeSeconds: 60);

            BuildingConstructionStartResult result = service.TryStartConstruction(building);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, SaveSystem.Data.currency, "정확히 같은 금액은 낼 수 있다");
            Assert.AreEqual(1, SaveSystem.Data.buildingConstructions.Count);
            Assert.AreEqual("1", SaveSystem.Data.buildingConstructions[0].buildingId);
            Assert.AreEqual(1, saveCalls, "비용과 기록은 한 번의 저장으로 함께 기록된다");
            Assert.AreEqual(0, inventorySaveCalls, "인벤토리가 따로 저장하면 저장이 두 번이 된다");
            Assert.AreEqual(1, changedCount, "성공한 시작의 알림은 정확히 한 번이다");
            Assert.AreEqual(1, startedCount, "시작 이벤트도 정확히 한 번이다");
        }

        [Test]
        public void 재화가_남으면_남는_만큼만_남기고_시작한다()
        {
            SaveSystem.Data.currency = 2190;
            BuildingConstructionService service = CreateService();
            BuildingDefinition building = CreateBuilding(costAmount: 2000);

            Assert.IsTrue(service.TryStartConstruction(building).Success);

            Assert.AreEqual(190, SaveSystem.Data.currency);
            Assert.AreEqual(1, SaveSystem.Data.buildingConstructions.Count);
            Assert.AreEqual(1, saveCalls);
            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void 아이템_비용도_재화와_함께_한_번에_빠진다()
        {
            ItemDefinition plank = CreateItem("plank");
            ItemDefinition nail = CreateItem("nail");
            RegisterItems(plank, nail);
            SaveSystem.Data.currency = 500;
            Hold("plank", 5);
            Hold("nail", 3);

            BuildingConstructionService service = CreateService();
            BuildingDefinition building = CreateBuilding(costAmount: 200, itemCost: plank, itemCount: 5);

            Assert.IsTrue(service.TryStartConstruction(building).Success);

            Assert.AreEqual(300, SaveSystem.Data.currency);
            Assert.AreEqual(1, SaveSystem.Data.items.Count, "수량이 정확히 0이 된 항목은 지워진다");
            Assert.AreEqual("nail", SaveSystem.Data.items[0].itemId, "다른 아이템은 그대로 남는다");
            Assert.AreEqual(1, saveCalls);
            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void 비용이_0이어도_저장과_알림은_각각_한_번이다()
        {
            SaveSystem.Data.currency = 0;
            BuildingConstructionService service = CreateService();
            service.ConstructionStarted += (b, s) => startedCount++;
            BuildingDefinition building = CreateBuilding(costAmount: 0, buildTimeSeconds: 0);

            Assert.IsTrue(service.TryStartConstruction(building).Success);

            Assert.AreEqual(1, SaveSystem.Data.buildingConstructions.Count,
                "낼 것이 없어도 남길 기록은 있다");
            Assert.AreEqual(1, saveCalls, "비용이 0이어도 기록은 저장되어야 한다");
            Assert.AreEqual(1, changedCount, "성공의 신호는 언제나 한 벌이다");
            Assert.AreEqual(1, startedCount);
        }

        [Test]
        public void 저장_호출_한_번이_차감된_비용과_얹힌_기록을_함께_본다()
        {
            // "한 번의 저장으로 함께 기록된다"의 진짜 의미는 <b>그 한 번의 호출이 본 문서</b>에 둘 다
            // 들어 있다는 것이다. 저장 함수 안에서 그 순간의 문서를 직접 들여다본다.
            ItemDefinition plank = CreateItem("plank");
            RegisterItems(plank);
            SaveSystem.Data.currency = 2190;
            Hold("plank", 5);

            int observedCurrency = -1;
            int observedPlank = -1;
            int observedConstructions = -1;
            string observedBuildingId = null;
            string observedStartedAt = null;

            var service = new BuildingConstructionService(
                inventory,
                () => SaveSystem.Data,
                () =>
                {
                    saveCalls++;

                    SaveData data = SaveSystem.Data;
                    observedCurrency = data.currency;
                    observedPlank = inventory.GetItemCount("plank");
                    observedConstructions = data.buildingConstructions.Count;
                    if (data.buildingConstructions.Count > 0)
                    {
                        observedBuildingId = data.buildingConstructions[0].buildingId;
                        observedStartedAt = data.buildingConstructions[0].startedAtUtc;
                    }

                    return true;
                },
                () => nowUtc);

            Assert.IsTrue(service.TryStartConstruction(
                CreateBuilding(costAmount: 2000, itemCost: plank, itemCount: 5)).Success);

            Assert.AreEqual(1, saveCalls, "저장은 한 번뿐이다");
            Assert.AreEqual(190, observedCurrency, "저장이 볼 때 재화는 이미 빠져 있어야 한다");
            Assert.AreEqual(0, observedPlank, "저장이 볼 때 아이템도 이미 빠져 있어야 한다");
            Assert.AreEqual(1, observedConstructions, "같은 호출이 건설 기록도 함께 봐야 한다");
            Assert.AreEqual("1", observedBuildingId);
            Assert.AreEqual(FixedNowText, observedStartedAt,
                "기록은 저장 직전에 완성된 상태로 얹혀 있어야 한다");
        }

        // ---- 시각 ----

        [Test]
        public void 시각은_주입된_UTC_시계로_저장_서식_그대로_적힌다()
        {
            SaveSystem.Data.currency = 2000;
            BuildingConstructionService service = CreateService();
            BuildingDefinition building = CreateBuilding(costAmount: 2000, buildTimeSeconds: 90);

            Assert.IsTrue(service.TryStartConstruction(building).Success);

            BuildingConstructionSaveState state = SaveSystem.Data.buildingConstructions[0];
            Assert.AreEqual(FixedNowText, state.startedAtUtc);
            Assert.AreEqual("2026-08-22T10:31:30.0000000Z", state.completeAtUtc,
                "완성 시각은 시작 시각 + buildTimeSeconds(초)다");
            Assert.AreEqual(SaveData.FormatTimestamp(FixedNowUtc), state.startedAtUtc,
                "저장 파일 안에서 시각을 적는 방법은 하나뿐이다");
        }

        [Test]
        public void 건설_시간이_0이면_시작과_완성_시각이_같다()
        {
            BuildingConstructionService service = CreateService();

            Assert.IsTrue(service.TryStartConstruction(CreateBuilding(costAmount: 0, buildTimeSeconds: 0)).Success);

            BuildingConstructionSaveState state = SaveSystem.Data.buildingConstructions[0];
            Assert.AreEqual(state.startedAtUtc, state.completeAtUtc);
        }

        [Test]
        public void 로컬_시각을_주는_시계도_UTC로_적힌다()
        {
            nowUtc = FixedNowUtc.ToLocalTime();
            BuildingConstructionService service = CreateService();

            Assert.IsTrue(service.TryStartConstruction(CreateBuilding(costAmount: 0)).Success);

            Assert.AreEqual(FixedNowText, SaveSystem.Data.buildingConstructions[0].startedAtUtc,
                "시계의 Kind에 따라 기록되는 값이 달라지면 안 된다");
        }

        // ---- 중복 / 재진입 ----

        [Test]
        public void 같은_건물을_다시_시작하면_비용이_두_번_빠지지_않는다()
        {
            SaveSystem.Data.currency = 5000;
            BuildingConstructionService service = CreateService();
            service.ConstructionStarted += (b, s) => startedCount++;
            BuildingDefinition building = CreateBuilding(costAmount: 2000);

            Assert.IsTrue(service.TryStartConstruction(building).Success);
            BuildingConstructionStartResult second = service.TryStartConstruction(building);

            Assert.AreEqual(BuildingConstructionStartCode.AlreadyStarted, second.Code);
            Assert.AreEqual(3000, SaveSystem.Data.currency, "두 번째 요청은 비용을 건드리지 않는다");
            Assert.AreEqual(1, SaveSystem.Data.buildingConstructions.Count);
            Assert.AreEqual(1, saveCalls);
            Assert.AreEqual(1, changedCount);
            Assert.AreEqual(1, startedCount);
        }

        [Test]
        public void 시작_이벤트_안에서_다시_부르면_재진입으로_막힌다()
        {
            SaveSystem.Data.currency = 5000;
            BuildingConstructionService service = CreateService();
            BuildingDefinition other = CreateBuilding(buildingId: "2", costAmount: 1000);

            BuildingConstructionStartResult? reentrant = null;
            service.ConstructionStarted += (b, s) =>
            {
                startedCount++;
                reentrant = service.TryStartConstruction(other);
            };

            LogAssert.Expect(LogType.Warning, new Regex("이미 처리 중"));

            Assert.IsTrue(service.TryStartConstruction(CreateBuilding(costAmount: 2000)).Success);

            Assert.IsTrue(reentrant.HasValue, "시작 이벤트 안에서의 요청이 실제로 지나가야 한다");
            Assert.AreEqual(BuildingConstructionStartCode.Reentrant, reentrant.Value.Code);
            Assert.AreEqual(3000, SaveSystem.Data.currency, "재진입한 요청의 비용은 빠지면 안 된다");
            Assert.AreEqual(1, SaveSystem.Data.buildingConstructions.Count);
            Assert.AreEqual(1, saveCalls);
            Assert.AreEqual(1, startedCount);
        }

        [Test]
        public void 재진입이_끝나면_다음_요청은_정상으로_받는다()
        {
            SaveSystem.Data.currency = 5000;
            BuildingConstructionService service = CreateService();
            BuildingDefinition other = CreateBuilding(buildingId: "2", costAmount: 1000);

            service.ConstructionStarted += (b, s) => service.TryStartConstruction(other);
            LogAssert.Expect(LogType.Warning, new Regex("이미 처리 중"));

            Assert.IsTrue(service.TryStartConstruction(CreateBuilding(costAmount: 2000)).Success);
            Assert.IsTrue(service.TryStartConstruction(other).Success, "잠금이 풀리지 않으면 영영 못 짓는다");

            Assert.AreEqual(2000, SaveSystem.Data.currency);
            Assert.AreEqual(2, SaveSystem.Data.buildingConstructions.Count);
            Assert.AreEqual(2, saveCalls);
        }

        // ---- 저장 실패 ----

        [Test]
        public void 저장이_실패하면_기록도_재화도_아이템도_그_자리_그대로_되돌아간다()
        {
            ItemDefinition before = CreateItem("before");
            ItemDefinition plank = CreateItem("plank");
            ItemDefinition after = CreateItem("after");
            RegisterItems(before, plank, after);

            SaveSystem.Data.currency = 2190;
            Hold("before", 4);
            Hold("plank", 3);   // 정확히 0이 되어 <b>지워질</b> 항목 - 앞뒤로 다른 아이템이 있다.
            Hold("after", 9);

            InventoryItemState firstRow = SaveSystem.Data.items[0];
            InventoryItemState depletedRow = SaveSystem.Data.items[1];
            InventoryItemState lastRow = SaveSystem.Data.items[2];

            BuildingConstructionService service = CreateService();
            service.ConstructionStarted += (b, s) => startedCount++;
            BuildingDefinition building = CreateBuilding(costAmount: 2000, itemCost: plank, itemCount: 3);

            saveSucceeds = false;
            LogAssert.Expect(LogType.Error, new Regex("저장하지 못해"));

            BuildingConstructionStartResult result = service.TryStartConstruction(building);

            Assert.AreEqual(BuildingConstructionStartCode.SaveFailed, result.Code);
            Assert.AreEqual(2190, SaveSystem.Data.currency, "재화가 되돌아와야 한다");

            Assert.AreEqual(3, SaveSystem.Data.items.Count, "통째로 지워졌던 아이템 항목도 되살아나야 한다");
            Assert.AreEqual("before", SaveSystem.Data.items[0].itemId);
            Assert.AreEqual("plank", SaveSystem.Data.items[1].itemId,
                "되살아난 항목이 맨 뒤로 밀리면 실패한 시작 하나가 인벤토리 표시 순서를 영구히 바꾼다");
            Assert.AreEqual("after", SaveSystem.Data.items[2].itemId);
            Assert.AreEqual(4, SaveSystem.Data.items[0].count);
            Assert.AreEqual(3, SaveSystem.Data.items[1].count);
            Assert.AreEqual(9, SaveSystem.Data.items[2].count);

            Assert.AreSame(firstRow, SaveSystem.Data.items[0], "항목 객체까지 그대로여야 한다");
            Assert.AreSame(depletedRow, SaveSystem.Data.items[1],
                "지워졌던 항목도 새로 만들지 않고 원래 객체가 제자리로 돌아온다");
            Assert.AreSame(lastRow, SaveSystem.Data.items[2]);

            Assert.AreEqual(0, SaveSystem.Data.buildingConstructions.Count, "임시로 얹은 기록을 걷어 내야 한다");
            Assert.AreEqual(1, saveCalls, "저장은 한 번만 시도한다");
            Assert.AreEqual(0, changedCount, "실패한 시작에 성공 알림이 나가면 안 된다");
            Assert.AreEqual(0, startedCount, "실패한 시작에 시작 이벤트가 오면 안 된다");
        }

        [Test]
        public void 저장에_실패한_뒤에도_다시_시도할_수_있다()
        {
            SaveSystem.Data.currency = 2000;
            BuildingConstructionService service = CreateService();
            BuildingDefinition building = CreateBuilding(costAmount: 2000);

            saveSucceeds = false;
            LogAssert.Expect(LogType.Error, new Regex("저장하지 못해"));
            Assert.AreEqual(BuildingConstructionStartCode.SaveFailed,
                service.TryStartConstruction(building).Code);

            saveSucceeds = true;
            Assert.IsTrue(service.TryStartConstruction(building).Success,
                "되돌린 뒤에는 처음과 같은 상태여야 한다");

            Assert.AreEqual(0, SaveSystem.Data.currency);
            Assert.AreEqual(1, SaveSystem.Data.buildingConstructions.Count);
            Assert.AreEqual(2, saveCalls);
            Assert.AreEqual(1, changedCount, "성공한 한 번만 알린다");
        }

        [Test]
        public void 저장이_실패하면_다른_건설_기록의_순서는_그대로다()
        {
            SaveSystem.Data.buildingConstructions.Add(new BuildingConstructionSaveState { buildingId = "2" });
            SaveSystem.Data.buildingConstructions.Add(new BuildingConstructionSaveState { buildingId = "3" });
            SaveSystem.Data.currency = 2000;

            BuildingConstructionService service = CreateService();
            saveSucceeds = false;
            LogAssert.Expect(LogType.Error, new Regex("저장하지 못해"));

            service.TryStartConstruction(CreateBuilding(costAmount: 2000));

            Assert.AreEqual(2, SaveSystem.Data.buildingConstructions.Count);
            Assert.AreEqual("2", SaveSystem.Data.buildingConstructions[0].buildingId);
            Assert.AreEqual("3", SaveSystem.Data.buildingConstructions[1].buildingId);
        }

        // ---- 조회 ----

        [Test]
        public void 조회는_기록을_만들지_않는다()
        {
            BuildingConstructionService service = CreateService();

            Assert.IsFalse(service.HasConstruction("1"));
            Assert.IsNull(service.FindConstruction("1"));
            Assert.IsFalse(service.HasConstruction(CreateBuilding()));

            Assert.AreEqual(0, SaveSystem.Data.buildingConstructions.Count,
                "물어보기만 했는데 기록이 생기면 '기록의 존재 = 시작됨'이 무너진다");
            Assert.AreEqual(0, saveCalls);
        }

        [Test]
        public void 목록이_null이어도_조회가_목록을_만들지_않는다()
        {
            SaveSystem.Data.buildingConstructions = null;
            BuildingConstructionService service = CreateService();

            Assert.IsFalse(service.HasConstruction("1"));
            Assert.IsNull(SaveSystem.Data.buildingConstructions, "조회는 문서를 한 글자도 바꾸지 않는다");
        }

        [Test]
        public void 조회는_Ordinal_완전_일치다()
        {
            SaveSystem.Data.buildingConstructions.Add(new BuildingConstructionSaveState { buildingId = "1" });
            BuildingConstructionService service = CreateService();

            Assert.IsTrue(service.HasConstruction("1"));
            Assert.IsFalse(service.HasConstruction(" 1 "), "앞뒤 공백을 다듬지 않는다");
            Assert.IsFalse(service.HasConstruction("01"));
            Assert.IsFalse(service.HasConstruction(string.Empty));
            Assert.IsFalse(service.HasConstruction((string)null));
        }

        [Test]
        public void 조회는_같은_id가_두_줄이어도_처음_것을_돌려준다()
        {
            var first = new BuildingConstructionSaveState { buildingId = "1", startedAtUtc = "first" };
            SaveSystem.Data.buildingConstructions.Add(null);
            SaveSystem.Data.buildingConstructions.Add(first);
            SaveSystem.Data.buildingConstructions.Add(
                new BuildingConstructionSaveState { buildingId = "1", startedAtUtc = "second" });

            BuildingConstructionService service = CreateService();

            Assert.AreSame(first, service.FindConstruction("1"), "null 항목도 건너뛴다");
        }

        // ---- 다시 켰을 때 ----

        [Test]
        public void 완성_시각이_지난_기록도_남아_있어_다시_시작할_수_없다()
        {
            // 앱을 껐다 켠 상황 - 기록만 저장 문서에 남아 있고 서비스는 새로 만들어진다.
            SaveSystem.Data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = "1",
                startedAtUtc = "2020-01-01T00:00:00.0000000Z",
                completeAtUtc = "2020-01-01T00:01:00.0000000Z",
            });
            SaveSystem.Data.currency = 999999;

            BuildingConstructionService service = CreateService();
            BuildingDefinition building = CreateBuilding(costAmount: 2000);

            Assert.IsTrue(service.HasConstruction("1"), "완성 시각이 지나도 기록은 남는다");
            Assert.AreEqual(BuildingConstructionStartCode.AlreadyStarted,
                service.TryStartConstruction(building).Code);
            Assert.AreEqual(999999, SaveSystem.Data.currency);
            Assert.AreEqual(0, saveCalls);
        }

        // ---- 진행 단계 ----

        [Test]
        public void 기록이_없으면_아직_시작하지_않은_단계다()
        {
            BuildingConstructionService service = CreateService();

            BuildingConstructionStatus status = service.GetStatus("1");

            Assert.AreEqual(BuildingConstructionPhase.NotStarted, status.Phase);
            Assert.IsFalse(status.HasRecord);
            Assert.AreEqual(TimeSpan.Zero, status.Remaining);
            Assert.IsNull(status.State);
        }

        [Test]
        public void 완성_시각이_남아_있으면_짓는_중이고_남은_시간을_돌려준다()
        {
            AddConstruction("1", completeAtUtc: FixedNowUtc.AddSeconds(60));
            BuildingConstructionService service = CreateService();

            BuildingConstructionStatus status = service.GetStatus("1");

            Assert.AreEqual(BuildingConstructionPhase.InProgress, status.Phase);
            Assert.AreEqual(60d, status.Remaining.TotalSeconds, 0.001d);
            Assert.AreEqual("00:01:00", BuildingInfoFormatter.FormatRemaining(status.Remaining));
        }

        [Test]
        public void 하루를_넘게_남아도_되감기지_않는다()
        {
            AddConstruction("1", completeAtUtc: FixedNowUtc.AddHours(25));
            BuildingConstructionService service = CreateService();

            Assert.AreEqual("25:00:00",
                BuildingInfoFormatter.FormatRemaining(service.GetStatus("1").Remaining));
        }

        [Test]
        public void 남은_시간이_1초보다_짧으면_1초로_보이고_아직_짓는_중이다()
        {
            AddConstruction("1", completeAtUtc: FixedNowUtc.AddMilliseconds(400));
            BuildingConstructionService service = CreateService();

            BuildingConstructionStatus status = service.GetStatus("1");

            Assert.AreEqual(BuildingConstructionPhase.InProgress, status.Phase,
                "아직 완성 시각이 오지 않았다");
            Assert.AreEqual("00:00:01", BuildingInfoFormatter.FormatRemaining(status.Remaining));
        }

        [Test]
        public void 완성_시각과_같은_순간부터_완성이다()
        {
            AddConstruction("1", completeAtUtc: FixedNowUtc);
            BuildingConstructionService service = CreateService();

            Assert.AreEqual(BuildingConstructionPhase.Completed, service.GetStatus("1").Phase);

            nowUtc = FixedNowUtc.AddSeconds(1);
            Assert.AreEqual(BuildingConstructionPhase.Completed, service.GetStatus("1").Phase);
        }

        [Test]
        public void 완성_시각을_읽을_수_없으면_읽을_수_없는_단계이고_기록은_남아_있다()
        {
            SaveSystem.Data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = "1",
                startedAtUtc = FixedNowText,
                completeAtUtc = "읽을 수 없는 값",
            });
            BuildingConstructionService service = CreateService();

            BuildingConstructionStatus status = service.GetStatus("1");

            Assert.AreEqual(BuildingConstructionPhase.Unreadable, status.Phase);
            Assert.IsTrue(status.HasRecord, "손상된 기록도 '이미 시작했다'이다 - 건설 버튼은 돌아오지 않는다");
            Assert.AreEqual(TimeSpan.Zero, status.Remaining);
        }

        [Test]
        public void 단계_조회는_저장도_기록_변경도_하지_않는다()
        {
            AddConstruction("1", completeAtUtc: FixedNowUtc.AddSeconds(60));
            BuildingConstructionService service = CreateService();

            service.GetStatus("1");
            service.GetStatus("1");

            Assert.AreEqual(0, saveCalls, "남은 시간을 볼 때마다 파일을 쓰면 안 된다");
            Assert.AreEqual(1, SaveSystem.Data.buildingConstructions.Count);
            Assert.IsFalse(SaveSystem.Data.buildingConstructions[0].completionNotified);
        }

        // ---- 완성 확정 ----

        [Test]
        public void 완성을_확정하면_표식이_남고_저장은_한_번뿐이다()
        {
            AddConstruction("1", completeAtUtc: FixedNowUtc.AddSeconds(-1));
            BuildingConstructionService service = CreateService();

            int completed = 0;
            service.ConstructionCompleted += _ => completed++;

            Assert.AreEqual(BuildingConstructionCompleteCode.Notified, service.TryNotifyCompletion("1"));

            Assert.IsTrue(SaveSystem.Data.buildingConstructions[0].completionNotified);
            Assert.AreEqual(1, saveCalls);
            Assert.AreEqual(1, completed);
        }

        [Test]
        public void 완성_확정은_두_번_일어나지_않는다()
        {
            AddConstruction("1", completeAtUtc: FixedNowUtc.AddSeconds(-1));
            BuildingConstructionService service = CreateService();

            int completed = 0;
            service.ConstructionCompleted += _ => completed++;

            service.TryNotifyCompletion("1");
            Assert.AreEqual(BuildingConstructionCompleteCode.AlreadyNotified, service.TryNotifyCompletion("1"));
            Assert.AreEqual(BuildingConstructionCompleteCode.AlreadyNotified, service.TryNotifyCompletion("1"));

            Assert.AreEqual(1, saveCalls, "확정은 한 번만 저장한다");
            Assert.AreEqual(1, completed);
        }

        [Test]
        public void 앱을_다시_켜도_이미_안내한_완성은_다시_안내되지_않는다()
        {
            // 저장 파일에 표식이 남아 있는 상태 그대로 - 서비스는 새로 만들어진다.
            AddConstruction("1", completeAtUtc: FixedNowUtc.AddDays(-3), completionNotified: true);
            BuildingConstructionService service = CreateService();

            int completed = 0;
            service.ConstructionCompleted += _ => completed++;

            Assert.AreEqual(BuildingConstructionCompleteCode.AlreadyNotified, service.TryNotifyCompletion("1"));
            Assert.AreEqual(0, saveCalls);
            Assert.AreEqual(0, completed);
        }

        [Test]
        public void 아직_짓는_중이면_확정하지_않는다()
        {
            AddConstruction("1", completeAtUtc: FixedNowUtc.AddSeconds(1));
            BuildingConstructionService service = CreateService();

            Assert.AreEqual(BuildingConstructionCompleteCode.NotComplete, service.TryNotifyCompletion("1"));
            Assert.IsFalse(SaveSystem.Data.buildingConstructions[0].completionNotified);
            Assert.AreEqual(0, saveCalls);
        }

        [Test]
        public void 기록이_없거나_읽을_수_없으면_확정하지_않는다()
        {
            BuildingConstructionService service = CreateService();
            Assert.AreEqual(BuildingConstructionCompleteCode.NotStarted, service.TryNotifyCompletion("1"));

            SaveSystem.Data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = "1",
                startedAtUtc = FixedNowText,
                completeAtUtc = null,
            });

            Assert.AreEqual(BuildingConstructionCompleteCode.Unreadable, service.TryNotifyCompletion("1"));
            Assert.AreEqual(0, saveCalls);
        }

        [Test]
        public void 확정_저장이_실패하면_표식을_되돌리고_다음_시도에서_다시_확정한다()
        {
            AddConstruction("1", completeAtUtc: FixedNowUtc.AddSeconds(-1));
            BuildingConstructionService service = CreateService();

            int completed = 0;
            service.ConstructionCompleted += _ => completed++;

            saveSucceeds = false;
            LogAssert.Expect(LogType.Error, new Regex("완성 표식을 저장하지"));

            Assert.AreEqual(BuildingConstructionCompleteCode.SaveFailed, service.TryNotifyCompletion("1"));
            Assert.IsFalse(SaveSystem.Data.buildingConstructions[0].completionNotified,
                "저장하지 못한 표식이 메모리에 남으면 안내를 보지도 못한 채 잃는다");
            Assert.AreEqual(0, completed, "실패한 확정은 알리지 않는다");

            // 같은 실패가 이어져도 기록은 한 번뿐이다(다시 시도하는 것은 그대로다).
            Assert.AreEqual(BuildingConstructionCompleteCode.SaveFailed, service.TryNotifyCompletion("1"));

            saveSucceeds = true;
            Assert.AreEqual(BuildingConstructionCompleteCode.Notified, service.TryNotifyCompletion("1"));
            Assert.IsTrue(SaveSystem.Data.buildingConstructions[0].completionNotified);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(3, saveCalls, "실패 두 번과 성공 한 번");
        }

        // ---- 도우미 ----

        /// <summary>건설 기록 한 줄을 저장 문서에 직접 넣는다(시작 경로를 거치지 않는다) - 여기서
        /// 확인하려는 것은 "기록이 이렇게 있을 때 어떤 단계인가"뿐이다.</summary>
        private static void AddConstruction(
            string buildingId, DateTime completeAtUtc, bool completionNotified = false)
        {
            SaveSystem.Data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = buildingId,
                startedAtUtc = SaveData.FormatTimestamp(completeAtUtc.AddMinutes(-1)),
                completeAtUtc = SaveData.FormatTimestamp(completeAtUtc),
                completionNotified = completionNotified,
            });
        }

        private BuildingConstructionService CreateService()
        {
            return new BuildingConstructionService(
                inventory,
                () => SaveSystem.Data,
                () => { saveCalls++; return saveSucceeds; },
                () => nowUtc);
        }

        private void Hold(string itemId, int count)
        {
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = itemId, count = count });
        }

        /// <summary>씬 목록에 정의를 등록한 인벤토리를 다시 만든다(Awake가 조회표를 만든다).</summary>
        private void RegisterItems(params ItemDefinition[] items)
        {
            var so = new SerializedObject(inventory);
            SerializedProperty catalog = so.FindProperty("itemCatalog");
            catalog.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                catalog.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            EditModeLifecycle.Invoke(inventory, "Awake");
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
            string buildingId = "1", int buildTimeSeconds = 60, int costAmount = 2000,
            ItemDefinition itemCost = null, int itemCount = 1)
        {
            var building = ScriptableObject.CreateInstance<BuildingDefinition>();
            created.Add(building);

            var so = new SerializedObject(building);
            so.FindProperty("buildingId").stringValue = buildingId;
            so.FindProperty("buildTimeSeconds").intValue = buildTimeSeconds;
            so.FindProperty("costCurrencyId").stringValue = "jewel";
            so.FindProperty("costCurrencyAmount").intValue = costAmount;

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
    }
}
