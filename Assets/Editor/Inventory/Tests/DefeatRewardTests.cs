using System.Collections.Generic;
using System.Reflection;
using Common;
using Dungeon;
using Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace InventoryEditor.Tests
{
    /// <summary>
    /// 처치 보상 판정 시험. <b>난수는 전부 주입한다</b> - 판정 함수가 난수원을 인자로 받으므로
    /// 경계값을 그대로 넣어 확인할 수 있고, 씬도 InventoryManager도 SaveSystem도 건드리지 않는다.
    ///
    /// 확률의 단위는 만분율이고 난수는 0~9999다. 슬롯은 앞에서부터 누적 구간을 나눠 가지며,
    /// 한 번의 처치가 주는 아이템은 최대 한 종류다.
    /// </summary>
    public sealed class DefeatRewardTests
    {
        private readonly List<Object> created = new List<Object>();

        private Random.State randomStateBeforeTest;

        /// <summary>
        /// 전역 난수 상태를 시험 전 모습 그대로 적어 둔다. 프로덕션 난수원 시험이
        /// <see cref="Random.InitState"/>로 시드를 고정하므로, 되돌리지 않으면 <b>이 시험이 고정한 시드를
        /// 뒤따르는 시험과 에디터가 그대로 물려받는다</b> - 실행 순서에 따라 결과가 달라지는 시험은
        /// 아무것도 증명하지 못한다.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            randomStateBeforeTest = Random.state;
        }

        [TearDown]
        public void TearDown()
        {
            // 난수 상태를 <b>먼저</b> 되돌린다 - 아래 정리 중에 예외가 나도 전역 상태는 반드시 제자리로
            // 돌아가야 한다.
            Random.state = randomStateBeforeTest;

            foreach (Object asset in created)
            {
                if (asset != null) Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        // ---- 아이템 누적 구간 경계 ----

        [Test]
        public void ItemRoll_CumulativeIntervalBoundaries_AreExact()
        {
            ItemDefinition first = CreateItem("50000");
            ItemDefinition second = CreateItem("50001");
            MonsterDefinition monster = CreateMonster(
                Drop(first, 3000, 1), Drop(second, 2000, 1));

            AssertSelectedItem(monster, 0, first, 1);
            AssertSelectedItem(monster, 2999, first, 1);
            AssertSelectedItem(monster, 3000, second, 1);
            AssertSelectedItem(monster, 4999, second, 1);
            AssertNoItem(monster, 5000);
            AssertNoItem(monster, 9999);
        }

        [Test]
        public void ItemRoll_AllZeroChances_NeverAwardItem()
        {
            ItemDefinition item = CreateItem("50000");
            MonsterDefinition monster = CreateMonster(
                Drop(item, 0, 1), Drop(item, 0, 1), Drop(item, 0, 1));

            foreach (int roll in new[] { 0, 1, 5000, 9999 }) AssertNoItem(monster, roll);
        }

        [Test]
        public void ItemRoll_FullChance_AlwaysAwardsItem()
        {
            ItemDefinition item = CreateItem("50000");
            MonsterDefinition monster = CreateMonster(Drop(item, 10000, 1));

            foreach (int roll in new[] { 0, 1, 5000, 9999 }) AssertSelectedItem(monster, roll, item, 1);
        }

        [Test]
        public void ItemRoll_SumIsFullRange_HasNoMiss()
        {
            ItemDefinition first = CreateItem("50000");
            ItemDefinition second = CreateItem("50001");
            MonsterDefinition monster = CreateMonster(Drop(first, 5000, 1), Drop(second, 5000, 3));

            AssertSelectedItem(monster, 0, first, 1);
            AssertSelectedItem(monster, 4999, first, 1);
            AssertSelectedItem(monster, 5000, second, 3);
            AssertSelectedItem(monster, 9999, second, 3);
        }

        [Test]
        public void ItemRoll_FourthEntry_IsIgnoredEntirely()
        {
            ItemDefinition a = CreateItem("50000");
            ItemDefinition b = CreateItem("50001");
            ItemDefinition c = CreateItem("50002");
            ItemDefinition fourth = CreateItem("50003");
            MonsterDefinition monster = CreateMonster(
                Drop(a, 3000, 1), Drop(b, 3000, 1), Drop(c, 3000, 1), Drop(fourth, 3000, 1));

            AssertSelectedItem(monster, 8999, c, 1);

            // 4번째 칸이 확률에 더해졌다면 9000~9999도 아이템이 나왔을 것이다.
            AssertNoItem(monster, 9000);
            AssertNoItem(monster, 9999);
        }

        [Test]
        public void ItemRoll_UsesExactlyOneRandomCall_WithFullRange()
        {
            ItemDefinition item = CreateItem("50000");
            MonsterDefinition monster = CreateMonster(Drop(item, 5000, 1));
            var random = new FakeRandom(1234);

            TestDefeatRewardDistributor.BuildReward(monster, random.Next);

            Assert.AreEqual(1, random.CallCount, "재화 지정이 없으면 난수는 아이템 판정 한 번뿐이다.");
            Assert.AreEqual((long)TestDefeatRewardDistributor.ItemRollRange, random.Bounds[0],
                "아이템 난수의 범위는 0~9999여야 한다.");
        }

        [Test]
        public void ItemRoll_NoDropSlots_StillRollsExactlyOnce()
        {
            MonsterDefinition monster = CreateMonster();
            var random = new FakeRandom(0);

            TestDefeatRewardDistributor.DefeatReward reward =
                TestDefeatRewardDistributor.BuildReward(monster, random.Next);

            Assert.AreEqual(1, random.CallCount, "슬롯이 없어도 뽑는 횟수는 달라지지 않는다.");
            Assert.IsFalse(reward.HasItem);
        }

        [Test]
        public void ItemResult_IsAtMostOneItemType()
        {
            ItemDefinition a = CreateItem("50000");
            ItemDefinition b = CreateItem("50001");
            ItemDefinition c = CreateItem("50002");
            MonsterDefinition monster = CreateMonster(
                Drop(a, 3000, 2), Drop(b, 3000, 2), Drop(c, 4000, 2));

            foreach (int roll in new[] { 0, 2999, 3000, 5999, 6000, 9999 })
            {
                TestDefeatRewardDistributor.DefeatReward reward =
                    TestDefeatRewardDistributor.BuildReward(monster, new FakeRandom(roll).Next);

                Assert.IsTrue(reward.HasItem, $"합이 10000이면 roll {roll}에서도 아이템이 나온다.");
                Assert.AreEqual(2, reward.ItemCount, "지급 개수는 뽑힌 슬롯의 개수 하나뿐이다.");
            }
        }

        [Test]
        public void DuplicateItemSlots_UseSelectedSlotCountOnly()
        {
            ItemDefinition item = CreateItem("50000");
            MonsterDefinition monster = CreateMonster(Drop(item, 2000, 1), Drop(item, 3000, 2));

            // 같은 아이템이어도 구간은 따로다 - 뽑힌 쪽의 개수만 쓰고 절대 더하지 않는다(1+2=3이 아니다).
            AssertSelectedItem(monster, 0, item, 1);
            AssertSelectedItem(monster, 1999, item, 1);
            AssertSelectedItem(monster, 2000, item, 2);
            AssertSelectedItem(monster, 4999, item, 2);
            AssertNoItem(monster, 5000);
        }

        // ---- 재화 ----

        [Test]
        public void Currency_NotSpecifiedAtAll_PaysNothingAndDoesNotRoll()
        {
            ItemDefinition item = CreateItem("50000");
            MonsterDefinition monster = CreateMonster(Drop(item, 10000, 1));
            var random = new FakeRandom(0);

            TestDefeatRewardDistributor.DefeatReward reward =
                TestDefeatRewardDistributor.BuildReward(monster, random.Next);

            Assert.AreEqual(0, reward.Currency);
            Assert.IsFalse(reward.HasCurrency);
            Assert.AreEqual(1, random.CallCount, "재화 지정이 없으면 재화 난수를 뽑지 않는다.");
        }

        // ---- 재화 지급의 근거는 연결된 정의 하나뿐 ----

        [Test]
        public void Currency_NoLinkedDefinition_PaysNothingAndDoesNotRoll()
        {
            // 금액 칸은 채워져 있지만 재화가 연결되어 있지 않다 - 금액만으로는 지급이 성립하지 않는다.
            MonsterDefinition monster = CreateMonster((CurrencyDefinition)null, 5, 9);
            var random = new FakeRandom(0, 3);

            TestDefeatRewardDistributor.DefeatReward reward =
                TestDefeatRewardDistributor.BuildReward(monster, random.Next);

            Assert.IsFalse(monster.HasCurrencyReward);
            Assert.IsNull(monster.Currency);
            Assert.AreEqual(string.Empty, monster.CurrencyId);
            Assert.AreEqual(0, reward.Currency);
            Assert.AreEqual(1, random.CallCount, "재화 난수를 뽑지 않는다(아이템 판정 1회뿐).");
        }

        [Test]
        public void Currency_LinkedDefinitionWithBlankId_IsRejectedAndPaysNothing()
        {
            // 참조는 있지만 그 정의가 쓸 수 없는 상태(id가 빈 정의)다. 반쯤 유효한 참조로 지급하면
            // id 없는 재화가 지급되므로, HasCurrencyReward부터 false여야 한다.
            MonsterDefinition monster = CreateMonster(CreateCurrency(string.Empty), 5, 9);
            var random = new FakeRandom(0, 3);

            TestDefeatRewardDistributor.DefeatReward reward =
                TestDefeatRewardDistributor.BuildReward(monster, random.Next);

            Assert.IsFalse(monster.HasCurrencyReward, "id가 없는 정의는 지급 근거가 되지 못한다.");
            Assert.IsNull(monster.Currency, "쓸 수 없는 정의는 null로 돌려준다 - 반쯤 유효한 참조를 넘기지 않는다.");
            Assert.AreEqual(string.Empty, monster.CurrencyId);
            Assert.AreEqual(0, reward.Currency);
            Assert.IsFalse(reward.HasCurrency);
            Assert.AreEqual(1, random.CallCount, "재화 난수를 뽑지 않는다.");
        }

        [Test]
        public void Currency_MonsterHasNoSerializedIdStringThatCouldAuthorizePayout()
        {
            // 예전 경로(직렬화된 currencyId 문자열)가 남아 있으면 참조 없이도 지급을 허가할 수 있다.
            // 칸 자체가 사라졌는지 직렬화 레이아웃에서 확인한다 - 이름만 안 쓰는 것으로는 부족하다.
            MonsterDefinition monster = CreateMonster((CurrencyDefinition)null, 5, 9);
            var serialized = new SerializedObject(monster);

            Assert.IsNull(serialized.FindProperty("currencyId"),
                "MonsterDefinition에 currencyId 문자열 칸이 남아 있으면 지급 근거가 둘이 된다.");

            SerializedProperty currency = serialized.FindProperty("currency");
            Assert.IsNotNull(currency, "재화는 참조 칸으로 존재해야 한다.");
            Assert.AreEqual(SerializedPropertyType.ObjectReference, currency.propertyType,
                "재화 칸이 문자열로 되돌아가면 임포터가 넣는 참조가 조용히 버려진다.");
        }

        [Test]
        public void Currency_IdComesFromTheLinkedDefinitionExactly()
        {
            // 대소문자를 구분하고 다듬지 않는다 - 몬스터 쪽에서 값을 한 번 더 손보면 정의와 달라진다.
            foreach (string id in new[] { "jewel", "Jewel", "gold_bar" })
            {
                CurrencyDefinition currency = CreateCurrency(id);
                MonsterDefinition monster = CreateMonster(currency, 1, 1);

                Assert.IsTrue(monster.HasCurrencyReward, $"'{id}'는 유효한 재화다.");
                Assert.AreSame(currency, monster.Currency, $"'{id}' 정의를 그대로 돌려줘야 한다.");
                Assert.AreEqual(id, monster.CurrencyId, $"'{id}'가 한 글자도 달라지면 안 된다.");
            }
        }

        [Test]
        public void Currency_LinkedDefinition_StillPaysFixedAndRangeAmounts()
        {
            // 참조로 바뀌어도 금액 규칙은 그대로다 - 고정은 난수 없이, 범위는 양 끝을 포함해서.
            MonsterDefinition fixedAmount = CreateMonster(CreateCurrency("jewel"), 7, 7);
            var noRoll = new FakeRandom(0);
            Assert.AreEqual(7, TestDefeatRewardDistributor.BuildReward(fixedAmount, noRoll.Next).Currency);
            Assert.AreEqual(1, noRoll.CallCount, "min==max면 재화 난수를 뽑지 않는다.");

            MonsterDefinition range = CreateMonster(CreateCurrency("jewel"), 5, 9);
            Assert.AreEqual(5, TestDefeatRewardDistributor.BuildReward(range, new FakeRandom(0, 0).Next).Currency);
            Assert.AreEqual(9, TestDefeatRewardDistributor.BuildReward(range, new FakeRandom(0, 4).Next).Currency);
        }

        [Test]
        public void Currency_FixedAmount_NeedsNoRandomCall()
        {
            MonsterDefinition monster = CreateMonster("gold", 7, 7);
            var random = new FakeRandom(0);

            TestDefeatRewardDistributor.DefeatReward reward =
                TestDefeatRewardDistributor.BuildReward(monster, random.Next);

            Assert.AreEqual(7, reward.Currency);
            Assert.AreEqual(1, random.CallCount, "min==max면 난수 없이 고정 금액이다.");
        }

        [Test]
        public void Currency_Range_HitsBothEndpoints()
        {
            MonsterDefinition monster = CreateMonster("gold", 5, 9);

            var atMin = new FakeRandom(0, 0);
            Assert.AreEqual(5, TestDefeatRewardDistributor.BuildReward(monster, atMin.Next).Currency);
            Assert.AreEqual(2, atMin.CallCount, "아이템 1회 + 재화 1회.");
            Assert.AreEqual(5L, atMin.Bounds[1], "양 끝을 포함하려면 범위는 max-min+1이어야 한다.");

            var atMax = new FakeRandom(0, 4);
            Assert.AreEqual(9, TestDefeatRewardDistributor.BuildReward(monster, atMax.Next).Currency);
        }

        [Test]
        public void Currency_FullIntRange_ReachesBothEndpoints()
        {
            // 검증기가 허용하는 가장 넓은 구간. 양 끝을 포함하면 칸이 2^31개라 int 경계로는 표현할 수
            // 없다 - 경계를 int로 좁히면 최댓값이 영원히 나오지 않는다.
            MonsterDefinition monster = CreateMonster("gold", 0, int.MaxValue);
            const long span = (long)int.MaxValue + 1L;

            var atMin = new FakeRandom(0L, 0L);
            Assert.AreEqual(0, TestDefeatRewardDistributor.BuildReward(monster, atMin.Next).Currency);
            Assert.AreEqual(span, atMin.Bounds[1], "재화 난수 요청 범위는 max-min+1 = 2^31이어야 한다.");
            Assert.AreEqual(TestDefeatRewardDistributor.MaxExclusiveBound, span);

            var atMax = new FakeRandom(0L, span - 1L);
            Assert.AreEqual(
                int.MaxValue,
                TestDefeatRewardDistributor.BuildReward(monster, atMax.Next).Currency,
                "구간의 마지막 칸은 int.MaxValue여야 한다.");
        }

        [Test]
        public void Currency_FullIntRange_ClampsFaultyRandomWithoutOverflow()
        {
            MonsterDefinition monster = CreateMonster("gold", 0, int.MaxValue);
            const long span = (long)int.MaxValue + 1L;

            // 약속을 어긴 값(구간보다 큼 / 음수)이 와도 int를 넘치지 않고 Min~Max 안에 머문다.
            Assert.AreEqual(
                int.MaxValue,
                TestDefeatRewardDistributor.BuildReward(monster, new FakeRandom(0L, span + 1000L).Next).Currency);
            Assert.AreEqual(
                0,
                TestDefeatRewardDistributor.BuildReward(monster, new FakeRandom(0L, -5L).Next).Currency);
        }

        [Test]
        public void ProductionRandom_FullIntRange_StaysInsideAndSpreads()
        {
            // 실제 게임이 쓰는 난수원. 시드를 고정해 결정적으로 돌린다.
            Random.InitState(20260808);

            const long span = (long)int.MaxValue + 1L;
            long min = long.MaxValue;
            long max = long.MinValue;

            for (int i = 0; i < 512; i++)
            {
                long value = TestDefeatRewardDistributor.RollUnityRandomBelow(span);

                Assert.GreaterOrEqual(value, 0L, "2^31 구간의 난수가 음수가 되면 안 된다.");
                Assert.Less(value, span, "2^31 구간의 난수가 구간을 벗어나면 안 된다.");

                if (value < min) min = value;
                if (value > max) max = value;
            }

            // 16비트 + 15비트를 이어 붙인 값이 31비트 전체를 쓰는지 - 한쪽 절반에만 몰리면 균등하지 않다.
            Assert.Less(min, span / 4L, "낮은 쪽 값이 전혀 나오지 않는다.");
            Assert.Greater(max, span / 4L * 3L, "int.MaxValue 근처 값이 전혀 나오지 않는다.");
        }

        [Test]
        public void ProductionRandom_SmallBound_UsesSingleIntegerDraw()
        {
            Random.InitState(20260808);

            for (int i = 0; i < 128; i++)
            {
                long value = TestDefeatRewardDistributor.RollUnityRandomBelow(
                    TestDefeatRewardDistributor.ItemRollRange);

                Assert.GreaterOrEqual(value, 0L);
                Assert.Less(value, TestDefeatRewardDistributor.ItemRollRange,
                    "아이템 판정 난수는 0~9999를 벗어나지 않는다.");
            }
        }

        [Test]
        public void Currency_OutOfRangeRandom_StaysInsideBounds()
        {
            MonsterDefinition monster = CreateMonster("gold", 5, 9);

            Assert.AreEqual(9, TestDefeatRewardDistributor.BuildReward(monster, new FakeRandom(0, 999).Next).Currency);
            Assert.AreEqual(5, TestDefeatRewardDistributor.BuildReward(monster, new FakeRandom(0, -3).Next).Currency);
        }

        [Test]
        public void Currency_IsIndependentOfItemOutcome()
        {
            ItemDefinition item = CreateItem("50000");
            MonsterDefinition monster = CreateMonster("gold", 5, 9, Drop(item, 3000, 1));

            // 아이템이 나온 처치와 나오지 않은 처치가 같은 재화를 준다(재화 난수값이 같으므로).
            TestDefeatRewardDistributor.DefeatReward hit =
                TestDefeatRewardDistributor.BuildReward(monster, new FakeRandom(0, 2).Next);
            TestDefeatRewardDistributor.DefeatReward miss =
                TestDefeatRewardDistributor.BuildReward(monster, new FakeRandom(9999, 2).Next);

            Assert.IsTrue(hit.HasItem);
            Assert.IsFalse(miss.HasItem);
            Assert.AreEqual(7, hit.Currency);
            Assert.AreEqual(7, miss.Currency, "아이템이 나오지 않아도 재화는 그대로 지급된다.");
            Assert.IsFalse(miss.IsEmpty, "재화만 있어도 빈 보상이 아니다.");
        }

        [Test]
        public void EmptyReward_IsDetectedSoNoToastIsShown()
        {
            ItemDefinition item = CreateItem("50000");
            MonsterDefinition monster = CreateMonster(Drop(item, 3000, 1));

            TestDefeatRewardDistributor.DefeatReward reward =
                TestDefeatRewardDistributor.BuildReward(monster, new FakeRandom(9999).Next);

            Assert.IsTrue(reward.IsEmpty, "아이템도 재화도 없으면 빈 보상이다.");
        }

        [Test]
        public void NullMonster_ProducesEmptyRewardWithoutRolling()
        {
            var random = new FakeRandom(0);

            TestDefeatRewardDistributor.DefeatReward reward =
                TestDefeatRewardDistributor.BuildReward(null, random.Next);

            Assert.IsTrue(reward.IsEmpty);
            Assert.AreEqual(0, random.CallCount);
        }

        // ---- 도우미 ----

        private static void AssertSelectedItem(
            MonsterDefinition monster, int roll, ItemDefinition expected, int expectedCount)
        {
            TestDefeatRewardDistributor.DefeatReward reward =
                TestDefeatRewardDistributor.BuildReward(monster, new FakeRandom(roll).Next);

            Assert.AreSame(expected, reward.Item, $"roll {roll}에서 기대한 아이템이 아니다.");
            Assert.AreEqual(expectedCount, reward.ItemCount, $"roll {roll}의 지급 개수가 다르다.");
            Assert.IsTrue(reward.HasItem);
        }

        private static void AssertNoItem(MonsterDefinition monster, int roll)
        {
            TestDefeatRewardDistributor.DefeatReward reward =
                TestDefeatRewardDistributor.BuildReward(monster, new FakeRandom(roll).Next);

            Assert.IsNull(reward.Item, $"roll {roll}에서는 아이템이 나오면 안 된다.");
            Assert.AreEqual(0, reward.ItemCount);
        }

        /// <summary>정해진 값을 순서대로 돌려주는 난수원. 몇 번 불렸는지와 어떤 범위로 불렸는지를 남긴다.
        /// 경계와 값이 <b>long</b>인 것은 재화 구간이 2^31까지 커질 수 있기 때문이다.</summary>
        private sealed class FakeRandom
        {
            private readonly long[] values;
            private int index;

            public FakeRandom(params long[] values)
            {
                this.values = values;
            }

            public List<long> Bounds { get; } = new List<long>();

            public int CallCount => Bounds.Count;

            public long Next(long exclusiveMax)
            {
                Bounds.Add(exclusiveMax);
                return index < values.Length ? values[index++] : 0L;
            }
        }

        private ItemDefinition CreateItem(string itemId)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            created.Add(item);

            var serialized = new SerializedObject(item);
            serialized.FindProperty("itemId").stringValue = itemId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        private MonsterDefinition CreateMonster(params DropSpec[] drops)
        {
            // 재화를 지정하지 않은 몬스터. 어느 오버로드인지 분명해지도록 참조 쪽으로 못박는다.
            return CreateMonster((CurrencyDefinition)null, 0, 0, drops);
        }

        /// <summary>재화 id 문자열로 부르던 자리를 그대로 잇는 도우미. <b>id는 이제 재화 정의를 거쳐서만
        /// 몬스터에 닿는다</b> - 그 정의를 만들어 연결하므로, 임포터가 하는 일과 같은 모양이다.</summary>
        private MonsterDefinition CreateMonster(string currencyId, int min, int max, params DropSpec[] drops)
        {
            CurrencyDefinition currency =
                string.IsNullOrEmpty(currencyId) ? null : CreateCurrency(currencyId);

            return CreateMonster(currency, min, max, drops);
        }

        /// <summary>임포터가 쓰는 것과 같은 직렬화 필드로 채운다 - 런타임 클래스의 실제 칸을 그대로 쓴다.
        /// 재화는 <b>참조</b>라 정의 에셋을 그대로 넣는다(null이면 재화 보상이 없는 몬스터다).</summary>
        private MonsterDefinition CreateMonster(
            CurrencyDefinition currency, int min, int max, params DropSpec[] drops)
        {
            var monster = ScriptableObject.CreateInstance<MonsterDefinition>();
            created.Add(monster);

            var serialized = new SerializedObject(monster);
            serialized.FindProperty("monsterId").stringValue = "1";
            serialized.FindProperty("currency").objectReferenceValue = currency;
            serialized.FindProperty("currencyAmountMin").intValue = min;
            serialized.FindProperty("currencyAmountMax").intValue = max;

            SerializedProperty list = serialized.FindProperty("drops");
            list.arraySize = drops.Length;
            for (int i = 0; i < drops.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("item").objectReferenceValue = drops[i].Item;
                element.FindPropertyRelative("chanceBasisPoints").intValue = drops[i].ChanceBasisPoints;
                element.FindPropertyRelative("count").intValue = drops[i].Count;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return monster;
        }

        /// <summary>재화 정의 하나. id는 <b>적힌 그대로</b> 들어간다 - 대소문자도 다듬기도 없다.</summary>
        private CurrencyDefinition CreateCurrency(string currencyId)
        {
            var currency = ScriptableObject.CreateInstance<CurrencyDefinition>();
            created.Add(currency);

            var serialized = new SerializedObject(currency);
            serialized.FindProperty("currencyId").stringValue = currencyId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return currency;
        }

        private static DropSpec Drop(ItemDefinition item, int chanceBasisPoints, int count)
        {
            return new DropSpec(item, chanceBasisPoints, count);
        }

        private readonly struct DropSpec
        {
            public DropSpec(ItemDefinition item, int chanceBasisPoints, int count)
            {
                Item = item;
                ChanceBasisPoints = chanceBasisPoints;
                Count = count;
            }

            public ItemDefinition Item { get; }

            public int ChanceBasisPoints { get; }

            public int Count { get; }
        }
    }

    /// <summary>
    /// 재화를 <b>더할 때</b> 값이 넘치지 않는지 보는 시험. 보상 표는 금액으로 int.MaxValue까지
    /// 허용하므로, 잔액이 조금이라도 있는 상태에서 그런 보상을 받는 것은 도달 가능한 경우다.
    /// int로 더하면 그 합이 음수로 넘치고, 0 아래를 자르는 규칙이 그것을 <b>잔액 0</b>으로 바꿔
    /// 저장해 버린다 - 받았는데 오히려 전부 잃는다.
    ///
    /// <b>이 시험은 사용자의 실제 저장 파일을 읽지도 쓰지도 않는다.</b> SaveSystem은 정적 클래스라
    /// 저장 경로를 주입할 자리가 없고, 그 경로(<see cref="Application.persistentDataPath"/>)는 실제
    /// 게임이 쓰는 자리와 같다. 그래서 두 가지를 갈아 끼운다 - (1) 메모리의 SaveData를 시험 전용
    /// 객체로 바꿔 파일을 <b>읽지</b> 않게 하고(그대로 두면 최초 접근에서 실제 파일을 읽는다),
    /// (2) InventoryManager의 저장 실행 지점을 세는 함수로 바꿔 파일을 <b>쓰지</b> 않게 한다.
    /// 덕분에 "저장이 정확히 몇 번 일어났는가"를 대리 지표가 아니라 직접 셀 수 있다.
    /// </summary>
    public sealed class InventoryCurrencyOverflowTests
    {
        private static readonly FieldInfo SaveDataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo SaveOverrideField =
            typeof(InventoryManager).GetField("saveOverride", BindingFlags.NonPublic | BindingFlags.Static);

        private GameObject host;
        private InventoryManager inventory;
        private object originalSaveData;
        private int changedCount;
        private int saveCount;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(SaveDataField,
                "SaveSystem.data를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요. " +
                "그대로 두면 시험이 실제 저장 파일을 읽게 됩니다.");
            Assert.IsNotNull(SaveOverrideField,
                "InventoryManager.saveOverride를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요. " +
                "그대로 두면 시험이 실제 저장 파일을 덮어쓰게 됩니다.");

            // 1) 파일을 읽지 않게: 메모리의 저장 문서를 시험 전용 객체로 갈아 끼운다.
            originalSaveData = SaveDataField.GetValue(null);
            SaveDataField.SetValue(null, new SaveData());

            // 2) 파일을 쓰지 않게: 저장 실행 지점을 "세기만 하고 성공했다고 답하는" 함수로 바꾼다.
            saveCount = 0;
            // System.Func를 풀어서 적는다 - 이 파일 위쪽 시험이 UnityEngine.Object/Random을 쓰고 있어
            // using System을 더하면 그쪽 이름이 모호해진다.
            SaveOverrideField.SetValue(null, new System.Func<bool>(() => { saveCount++; return true; }));

            host = new GameObject("InventoryCurrencyOverflowTests");
            inventory = host.AddComponent<InventoryManager>();

            // EditMode에서는 Awake가 저절로 돌지 않는다 - 조회표를 만들려면 직접 부른다.
            Invoke(inventory, "Awake");

            changedCount = 0;
            InventoryManager.InventoryChanged += CountChanged;
        }

        [TearDown]
        public void TearDown()
        {
            InventoryManager.InventoryChanged -= CountChanged;

            if (host != null) Object.DestroyImmediate(host);
            inventory = null;

            // 갈아 끼운 두 자리를 <b>반드시</b> 되돌린다 - 남겨 두면 뒤따르는 시험이나 에디터가
            // 시험용 저장 문서를 진짜인 줄 알고 쓰게 된다.
            SaveOverrideField.SetValue(null, null);
            SaveDataField.SetValue(null, originalSaveData);
        }

        private void CountChanged() => changedCount++;

        // ---- 넘침 ----

        [Test]
        public void PositiveBalancePlusIntMax_SaturatesInsteadOfWrappingToZero()
        {
            // Sol이 짚은 바로 그 경우: 1 + int.MaxValue는 int로 더하면 int.MinValue가 되고,
            // "0 아래는 0" 규칙이 그것을 잔액 0으로 확정해 저장한다.
            SaveSystem.Data.currency = 1;

            bool changed = ApplyCurrencyDelta(int.MaxValue);

            Assert.IsTrue(changed, "값이 바뀌었으므로 true여야 한다.");
            Assert.AreEqual(int.MaxValue, SaveSystem.Data.currency,
                "1 + int.MaxValue는 int.MaxValue로 잘려야 한다 - 0이 되면 보상을 받고 전 재산을 잃는다.");
            Assert.AreNotEqual(0, SaveSystem.Data.currency, "넘침이 0으로 확정되면 안 된다.");
        }

        [Test]
        public void SaturationHoldsAcrossTheWholeUpperRange()
        {
            foreach ((int start, int delta) in new[]
                     {
                         (0, int.MaxValue),
                         (1, int.MaxValue),
                         (2, int.MaxValue),
                         (int.MaxValue / 2, int.MaxValue),
                         (int.MaxValue - 1, 2),
                         (int.MaxValue, int.MaxValue),
                     })
            {
                SaveSystem.Data.currency = start;
                ApplyCurrencyDelta(delta);

                Assert.AreEqual(int.MaxValue, SaveSystem.Data.currency,
                    $"{start} + {delta}는 int.MaxValue로 잘려야 한다.");
            }
        }

        [Test]
        public void AlreadyAtMax_ReportsNoChangeSoNothingIsSaved()
        {
            SaveSystem.Data.currency = int.MaxValue;

            Assert.IsFalse(ApplyCurrencyDelta(int.MaxValue),
                "이미 상한이면 값이 달라지지 않으므로 저장도 알림도 일어나면 안 된다.");
            Assert.AreEqual(int.MaxValue, SaveSystem.Data.currency);
        }

        // ---- 기존 의미 보존 ----

        [Test]
        public void OrdinaryAdditionIsUnchanged()
        {
            SaveSystem.Data.currency = 300;

            Assert.IsTrue(ApplyCurrencyDelta(200));
            Assert.AreEqual(500, SaveSystem.Data.currency);
        }

        [Test]
        public void NegativeDeltaStillFloorsAtZero()
        {
            // AddCurrency에 음수를 넘기면 0으로 잘리는 기존 의미(부분 지불)는 그대로여야 한다.
            SaveSystem.Data.currency = 300;
            Assert.IsTrue(ApplyCurrencyDelta(-500));
            Assert.AreEqual(0, SaveSystem.Data.currency);

            SaveSystem.Data.currency = 500;
            Assert.IsTrue(ApplyCurrencyDelta(-200));
            Assert.AreEqual(300, SaveSystem.Data.currency);
        }

        [Test]
        public void NegativeDeltaCannotUnderflowEither()
        {
            SaveSystem.Data.currency = 0;

            Assert.IsFalse(ApplyCurrencyDelta(int.MinValue), "이미 0이라 값이 달라지지 않는다.");
            Assert.AreEqual(0, SaveSystem.Data.currency, "아래로도 넘치면 안 된다.");
        }

        [Test]
        public void ZeroDeltaChangesNothing()
        {
            SaveSystem.Data.currency = 77;

            Assert.IsFalse(ApplyCurrencyDelta(0));
            Assert.AreEqual(77, SaveSystem.Data.currency);
        }

        [Test]
        public void SetCurrencyKeepsItsExistingClampingBehaviour()
        {
            inventory.SetCurrency(-5);
            Assert.AreEqual(0, SaveSystem.Data.currency, "음수 지정은 여전히 0이다.");
            Assert.AreEqual(0, saveCount, "이미 0이라 값이 바뀌지 않았으므로 저장하지 않는다.");

            inventory.SetCurrency(1234);
            Assert.AreEqual(1234, SaveSystem.Data.currency);
            Assert.AreEqual(1, saveCount);
        }

        [Test]
        public void AddCurrencyThroughThePublicApi_SaturatesAndSavesOnce()
        {
            // 공개 경로로도 넘침이 0이 되지 않는지 확인한다 - 산술은 같은 자리에서 일어나지만,
            // 실제로 사람이 부르는 API가 이 성질을 갖는다는 것이 확인되어야 한다.
            SaveSystem.Data.currency = 1;

            inventory.AddCurrency(int.MaxValue);

            Assert.AreEqual(int.MaxValue, SaveSystem.Data.currency);
            Assert.AreEqual(1, saveCount, "값이 바뀌었으므로 저장은 정확히 한 번이다.");
            Assert.AreEqual(1, changedCount);
        }

        // ---- 묶음 지급 ----

        [Test]
        public void BatchWithSaturatingReward_SavesOnceAndNotifiesOnce()
        {
            ItemDefinition item = CreateRegisteredItem("50000");
            SaveSystem.Data.currency = 1;

            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(item, 2),
                new InventoryManager.RewardItemStack(item, 3),
            };

            inventory.ApplyRewards(int.MaxValue, stacks);

            Assert.AreEqual(int.MaxValue, SaveSystem.Data.currency,
                "묶음 지급에서도 넘침이 0으로 확정되면 안 된다.");
            Assert.AreEqual(1, saveCount, "처치 한 번은 저장 한 번이다.");
            Assert.AreEqual(1, changedCount, "처치 한 번은 InventoryChanged 한 번이다.");

            // 같은 아이템 두 칸은 한 항목으로 누적된다(슬롯이 갈라지지 않는다).
            Assert.AreEqual(1, SaveSystem.Data.items.Count);
            Assert.AreEqual(5, SaveSystem.Data.items[0].count);
        }

        [Test]
        public void BatchThatChangesNothing_NeitherSavesNorNotifies()
        {
            SaveSystem.Data.currency = int.MaxValue;

            inventory.ApplyRewards(int.MaxValue, null);

            Assert.AreEqual(0, saveCount, "바뀐 것이 없으면 저장하지 않는다.");
            Assert.AreEqual(0, changedCount, "바뀐 것이 없으면 알리지도 않는다.");
        }

        // ---- 도우미 ----

        /// <summary>
        /// 재화 덧셈만 <b>메모리에서</b> 실행한다(저장도 알림도 없다). 공개 API인 AddCurrency는
        /// 실제 저장 파일에 쓰므로, 산술 자체를 확인하는 시험은 이 비공개 경로를 직접 부른다 -
        /// 파일을 전혀 건드리지 않는 것이 여기서는 더 강한 성질이다.
        /// </summary>
        private bool ApplyCurrencyDelta(int amount)
        {
            return (bool)Invoke(inventory, "ApplyCurrencyDelta", amount);
        }

        private static object Invoke(object target, string method, params object[] args)
        {
            MethodInfo info = target.GetType().GetMethod(
                method, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(info, $"{target.GetType().Name}.{method}를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");
            return info.Invoke(target, args);
        }

        /// <summary>InventoryManager의 조회표에 등록된 아이템 하나. 등록되지 않은 정의는 지급이 막히므로
        /// 씬 목록에 넣고 조회표를 다시 만든다.</summary>
        private ItemDefinition CreateRegisteredItem(string itemId)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.hideFlags = HideFlags.HideAndDontSave;

            var serializedItem = new SerializedObject(item);
            serializedItem.FindProperty("itemId").stringValue = itemId;
            serializedItem.ApplyModifiedPropertiesWithoutUndo();

            var serializedManager = new SerializedObject(inventory);
            SerializedProperty list = serializedManager.FindProperty("itemCatalog");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = item;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();

            Invoke(inventory, "BuildDefinitionLookup");
            return item;
        }
    }
}
