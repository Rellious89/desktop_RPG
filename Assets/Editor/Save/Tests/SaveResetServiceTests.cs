using System;
using System.Collections.Generic;
using Common;
using CommonEditor.Save;
using NUnit.Framework;

namespace CommonEditor.SaveTests
{
    /// <summary>
    /// <see cref="SaveResetService"/> 집중 시험. <b>실제 저장 파일을 절대 건드리지 않는다</b> - 격리된
    /// 메모리 <see cref="SaveData"/>와 호출 횟수를 세는 저장 대리자만 쓴다(SaveSystem도, persistentDataPath도
    /// 건드리지 않는다).
    ///
    /// 확인하는 것은 하나다 - <b>고른 항목만 정확히 초기화하고, 저장은 한 번만 하고, 실패하면 전부 되돌리며,
    /// 나머지 필드는 그대로 두는가.</b>
    /// </summary>
    public sealed class SaveResetServiceTests
    {
        /// <summary>세 초기화 대상과 몇 개의 비대상 필드를 모두 0이 아닌 값으로 채운 문서를 만든다 -
        /// 비대상이 기본값으로 되돌아오는 사고가 있으면 보이도록 전부 눈에 띄는 값으로 둔다.</summary>
        private static SaveData MakePopulated()
        {
            return new SaveData
            {
                currency = 1250,
                currentLevel = 7,
                currentExp = 240,
                totalKillCount = 133,
                saveRevision = 5,
                items = new List<InventoryItemState>
                {
                    new InventoryItemState { itemId = "red_potion", count = 3 },
                    new InventoryItemState { itemId = "blue_potion", count = 1 },
                },
                buildingConstructions = new List<BuildingConstructionSaveState>
                {
                    new BuildingConstructionSaveState { buildingId = "1", startedAtUtc = "s", completeAtUtc = "c" },
                },
                recruitmentCycles = new List<RecruitmentCycleSaveState>
                {
                    new RecruitmentCycleSaveState
                    {
                        recruitmentAccessId = "Inn_Normal_Access",
                        startedAtUtc = "rs",
                        readyAtUtc = "rr",
                    },
                },
                characters = new List<CharacterSaveState>
                {
                    new CharacterSaveState { characterId = "CatKnight", level = 5, currentExp = 7, currentStamina = 21 },
                },
                recoverySlots = new List<RecoverySlotSaveState>
                {
                    new RecoverySlotSaveState { characterId = "Barbarian", startStamina = 2 },
                },
            };
        }

        private static Func<bool> Counting(out Box<int> calls, bool succeeds = true)
        {
            var box = new Box<int>();
            calls = box;
            return () => { box.Value++; return succeeds; };
        }

        private sealed class Box<T> { public T Value; }

        private static void AssertNonTargetsPreserved(SaveData data)
        {
            Assert.AreEqual(7, data.currentLevel, "계정 레벨은 초기화 대상이 아닙니다.");
            Assert.AreEqual(240, data.currentExp);
            Assert.AreEqual(133, data.totalKillCount);
            Assert.AreEqual(1, data.characters.Count, "캐릭터 보유는 절대 건드리지 않습니다.");
            Assert.AreEqual("CatKnight", data.characters[0].characterId);
            Assert.AreEqual(5, data.characters[0].level);
            Assert.AreEqual(1, data.recoverySlots.Count, "회복소는 초기화 대상이 아닙니다.");
            Assert.AreEqual("Barbarian", data.recoverySlots[0].characterId);
        }

        // ---- 1. Item만 ----

        [Test]
        public void Item만_초기화하고_나머지는_보존한다()
        {
            SaveData data = MakePopulated();
            SaveResetResult result = SaveResetService.Apply(data, SaveResetTargets.Item, Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(0, data.items.Count, "아이템은 빈 목록이 됩니다.");
            Assert.AreEqual(1250, data.currency, "재화는 그대로입니다.");
            Assert.AreEqual(1, data.buildingConstructions.Count, "건축 기록은 그대로입니다.");
            Assert.AreEqual(1, data.recruitmentCycles.Count, "모집 주기도 그대로입니다.");
            AssertNonTargetsPreserved(data);
            Assert.AreEqual(1, calls.Value);
        }

        // ---- 2. Currency만 ----

        [Test]
        public void Currency만_0으로_만들고_나머지는_보존한다()
        {
            SaveData data = MakePopulated();
            SaveResetResult result = SaveResetService.Apply(data, SaveResetTargets.Currency, Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(0, data.currency);
            Assert.AreEqual(2, data.items.Count, "아이템은 그대로입니다.");
            Assert.AreEqual(1, data.buildingConstructions.Count, "건축 기록은 그대로입니다.");
            Assert.AreEqual(1, data.recruitmentCycles.Count, "모집 주기도 그대로입니다.");
            AssertNonTargetsPreserved(data);
            Assert.AreEqual(1, calls.Value);
        }

        // ---- 3. Construction만 ----

        [Test]
        public void Construction만_초기화하고_나머지는_보존한다()
        {
            SaveData data = MakePopulated();
            SaveResetResult result = SaveResetService.Apply(data, SaveResetTargets.Construction, Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(0, data.buildingConstructions.Count);
            Assert.AreEqual(0, data.recruitmentCycles.Count, "Construction은 모집 주기도 함께 지웁니다.");
            Assert.AreEqual(2, data.items.Count, "아이템은 그대로입니다.");
            Assert.AreEqual(1250, data.currency, "재화는 그대로입니다.");
            AssertNonTargetsPreserved(data);
            Assert.AreEqual(1, calls.Value);
        }

        // ---- 4. All ----

        [Test]
        public void All은_세_항목을_모두_초기화한다()
        {
            SaveData data = MakePopulated();
            SaveResetResult result = SaveResetService.Apply(data, SaveResetTargets.All, Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(0, data.items.Count);
            Assert.AreEqual(0, data.currency);
            Assert.AreEqual(0, data.buildingConstructions.Count);
            Assert.AreEqual(0, data.recruitmentCycles.Count);
            AssertNonTargetsPreserved(data);
            Assert.AreEqual(1, calls.Value);
        }

        // ---- 5. All에서 하나 해제한 조합 ----

        [Test]
        public void All에서_Currency를_해제하면_Item과_Construction만_초기화한다()
        {
            SaveData data = MakePopulated();
            SaveResetTargets targets = SaveResetTargets.All & ~SaveResetTargets.Currency;

            SaveResetResult result = SaveResetService.Apply(data, targets, Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(SaveResetTargets.Item | SaveResetTargets.Construction, result.AppliedTargets);
            Assert.AreEqual(0, data.items.Count, "Item은 초기화됩니다.");
            Assert.AreEqual(0, data.buildingConstructions.Count, "Construction은 초기화됩니다.");
            Assert.AreEqual(0, data.recruitmentCycles.Count, "모집 주기도 Construction에 종속됩니다.");
            Assert.AreEqual(1250, data.currency, "해제한 Currency는 그대로여야 합니다.");
            AssertNonTargetsPreserved(data);
            Assert.AreEqual(1, calls.Value);
        }

        // ---- 6. 미선택 ----

        [Test]
        public void 아무것도_고르지_않으면_저장하지_않고_아무것도_바꾸지_않는다()
        {
            SaveData data = MakePopulated();
            SaveResetResult result = SaveResetService.Apply(data, SaveResetTargets.None, Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.NothingSelected, result.Outcome);
            Assert.AreEqual(0, calls.Value, "고른 항목이 없으면 저장 대리자를 부르지 않습니다.");
            Assert.AreEqual(2, data.items.Count);
            Assert.AreEqual(1250, data.currency);
            Assert.AreEqual(1, data.buildingConstructions.Count);
            Assert.AreEqual(1, data.recruitmentCycles.Count);
            AssertNonTargetsPreserved(data);
        }

        // ---- 7. 저장 호출 정확히 1회 ----

        [Test]
        public void 성공하면_저장_대리자를_정확히_한_번만_부른다()
        {
            SaveData data = MakePopulated();
            SaveResetService.Apply(data, SaveResetTargets.All, Counting(out Box<int> calls));

            Assert.AreEqual(1, calls.Value, "선택 항목이 몇 개든 저장은 한 번에 모아 한 번만 합니다.");
        }

        // ---- 8. 저장 실패 시 전체 롤백 ----

        [Test]
        public void 저장에_실패하면_선택_항목을_전부_되돌린다()
        {
            SaveData data = MakePopulated();
            List<InventoryItemState> originalItems = data.items;
            List<BuildingConstructionSaveState> originalConstructions = data.buildingConstructions;
            List<RecruitmentCycleSaveState> originalRecruitmentCycles = data.recruitmentCycles;

            SaveResetResult result =
                SaveResetService.Apply(data, SaveResetTargets.All, Counting(out Box<int> calls, succeeds: false));

            Assert.AreEqual(SaveResetOutcome.SaveFailed, result.Outcome);
            Assert.AreEqual(1, calls.Value, "실패해도 저장 시도는 한 번뿐입니다.");

            // 원래 값으로 전부 복구 - 성공한 일부만 남는 부분 초기화가 없어야 한다.
            Assert.AreSame(originalItems, data.items, "실패하면 원래 아이템 목록 참조로 되돌립니다.");
            Assert.AreEqual(2, data.items.Count);
            Assert.AreEqual(1250, data.currency, "재화도 되돌립니다.");
            Assert.AreSame(originalConstructions, data.buildingConstructions, "건축 기록도 되돌립니다.");
            Assert.AreEqual(1, data.buildingConstructions.Count);
            Assert.AreSame(originalRecruitmentCycles, data.recruitmentCycles, "모집 주기도 되돌립니다.");
            Assert.AreEqual(1, data.recruitmentCycles.Count);
            AssertNonTargetsPreserved(data);
        }

        // ---- 9. 비대상 필드 보존(성공 경로) ----

        [Test]
        public void 초기화해도_캐릭터_회복소_계정진행은_그대로다()
        {
            SaveData data = MakePopulated();
            SaveResetService.Apply(data, SaveResetTargets.All, Counting(out Box<int> _));

            AssertNonTargetsPreserved(data);
            Assert.AreEqual(5, data.saveRevision, "저장 메타데이터는 이 로직이 건드리지 않습니다(저장 대리자의 몫).");
        }

        // ---- 방어: 정의되지 않은 비트는 무시 ----

        [Test]
        public void 정의되지_않은_비트만_들어오면_미선택으로_취급한다()
        {
            SaveData data = MakePopulated();
            SaveResetResult result =
                SaveResetService.Apply(data, (SaveResetTargets)(1 << 5), Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.NothingSelected, result.Outcome);
            Assert.AreEqual(0, calls.Value);
        }

        // ---- 방어: null 인자 ----

        [Test]
        public void data가_null이면_예외를_던진다()
        {
            Assert.Throws<ArgumentNullException>(
                () => SaveResetService.Apply(null, SaveResetTargets.Item, () => true));
        }

        [Test]
        public void save_대리자가_null이면_예외를_던진다()
        {
            Assert.Throws<ArgumentNullException>(
                () => SaveResetService.Apply(new SaveData(), SaveResetTargets.Item, null));
        }
    }
}
