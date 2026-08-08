using System.Collections.Generic;
using System.Text.RegularExpressions;
using Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace InventoryEditor.Tests
{
    /// <summary>
    /// 재화 정의/카탈로그 시험. 확인하려는 것은 <b>식별자를 다루는 규칙</b> 하나다 - 비어 있으면
    /// 파일 이름으로 대체하지 않고, <b>적힌 문자열을 자동으로 정규화하지 않으며</b>(대소문자도
    /// 앞뒤 공백도 그대로 둔다), 겹치면 먼저 작성한 쪽이 남는다.
    ///
    /// 에셋은 전부 메모리 위에서만 만들고(<see cref="ScriptableObject.CreateInstance{T}()"/>)
    /// 직렬화 필드는 <see cref="SerializedObject"/>로 채운다 - 임포터가 쓰게 될 것과 같은 칸을 그대로
    /// 쓰므로, 프로퍼티 이름이 바뀌면 시험이 먼저 깨진다. 디스크에도 씬에도 아무것도 남기지 않는다.
    /// </summary>
    public sealed class CurrencyCatalogTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object asset in created)
            {
                if (asset != null) Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        // ---- CurrencyDefinition ----

        [Test]
        public void BlankId_IsInvalidAndNeverFallsBackToAssetName()
        {
            CurrencyDefinition blank = CreateCurrency(string.Empty);
            blank.name = "Gold";

            Assert.IsFalse(blank.IsValid, "식별자가 비어 있으면 유효하지 않다.");
            Assert.AreEqual(string.Empty, blank.CurrencyId, "에셋 파일 이름으로 대체하면 안 된다.");
        }

        [Test]
        public void WhitespaceOnlyId_IsInvalidAndReadsAsEmpty()
        {
            CurrencyDefinition spaces = CreateCurrency("   ");
            spaces.name = "Gold";

            Assert.IsFalse(spaces.IsValid, "공백만 적힌 값은 유효한 식별자가 아니다.");
            Assert.AreEqual(string.Empty, spaces.CurrencyId);
        }

        [Test]
        public void SurroundingWhitespace_IsPreserved_NotSilentlyTrimmed()
        {
            CurrencyDefinition padded = CreateCurrency("  Jewel  ");

            Assert.AreEqual("  Jewel  ", padded.CurrencyId,
                "손으로 적은 id는 앞뒤 공백까지 그대로 남는다 - 말없이 'Jewel'로 바꾸지 않는다.");
            Assert.IsTrue(padded.IsValid, "공백이 붙어 있어도 내용이 있으면 유효한 id다.");
        }

        [Test]
        public void PaddedId_DoesNotCollideWithTheUnpaddedOne()
        {
            // 정규화를 하지 않으므로 둘은 서로 다른 재화다 - 중복으로 걸러지지도, 서로 조회되지도 않는다.
            CurrencyDefinition padded = CreateCurrency("  Jewel  ");
            CurrencyDefinition plain = CreateCurrency("Jewel");
            CurrencyCatalog catalog = CreateCatalog(padded, plain);

            Assert.AreEqual(2, catalog.Count);
            Assert.AreSame(padded, catalog.Find("  Jewel  "));
            Assert.AreSame(plain, catalog.Find("Jewel"));
        }

        [Test]
        public void LocalizedName_IsNeverNull_AndReportsMissingReference()
        {
            CurrencyDefinition currency = CreateCurrency("gold");

            Assert.IsNotNull(currency.LocalizedName, "참조 객체 자체는 항상 있어야 한다.");
            Assert.IsFalse(currency.HasLocalizedName, "Table/Key를 지정하지 않았으면 참조는 비어 있다.");
        }

        [Test]
        public void DefaultPresentationFields_AreEmptyAndZero()
        {
            CurrencyDefinition currency = CreateCurrency("gold");

            Assert.IsNull(currency.Icon, "아이콘을 지정하지 않으면 null이다 - 표시하는 쪽이 판단한다.");
            Assert.AreEqual(0, currency.DisplayOrder);
        }

        [Test]
        public void DisplayOrder_IsReadBackAsAuthored()
        {
            Assert.AreEqual(-3, CreateCurrency("gold", -3).DisplayOrder);
            Assert.AreEqual(20, CreateCurrency("jewel", 20).DisplayOrder);
        }

        // ---- CurrencyCatalog ----

        [Test]
        public void Catalog_KeepsAuthoredOrder_IgnoringDisplayOrder()
        {
            // displayOrder는 정렬 값일 뿐이고 목록을 만들지 않는다 - 순서는 작성 순서 그대로다.
            CurrencyDefinition jewel = CreateCurrency("jewel", 99);
            CurrencyDefinition gold = CreateCurrency("gold", 1);
            CurrencyCatalog catalog = CreateCatalog(jewel, gold);

            Assert.AreEqual(2, catalog.Count);
            Assert.AreSame(jewel, catalog.Currencies[0], "작성 순서가 앞이면 목록에서도 앞이다.");
            Assert.AreSame(gold, catalog.Currencies[1]);
        }

        [Test]
        public void Catalog_IsCaseSensitive_TreatsLowercaseAndUppercaseJewelAsDistinct()
        {
            CurrencyDefinition lower = CreateCurrency("jewel");
            CurrencyDefinition upper = CreateCurrency("Jewel");
            CurrencyCatalog catalog = CreateCatalog(lower, upper);

            Assert.AreEqual(2, catalog.Count, "대소문자만 다른 id는 서로 다른 재화다 - 중복이 아니다.");
            Assert.AreSame(lower, catalog.Find("jewel"));
            Assert.AreSame(upper, catalog.Find("Jewel"));
            Assert.IsNull(catalog.Find("JEWEL"), "조회도 대소문자를 구분한다.");
        }

        [Test]
        public void Catalog_DuplicateId_KeepsFirstAndDropsLater()
        {
            CurrencyDefinition first = CreateCurrency("gold", 1);
            CurrencyDefinition duplicate = CreateCurrency("gold", 2);
            duplicate.name = "GoldCopy";

            ExpectDuplicateError("GoldCopy");
            CurrencyCatalog catalog = CreateCatalog(first, duplicate);

            Assert.AreEqual(1, catalog.Count);
            Assert.AreSame(first, catalog.Currencies[0], "먼저 작성한 쪽이 남는다.");
            Assert.AreSame(first, catalog.Find("gold"));
        }

        [Test]
        public void Catalog_ExcludesNullSlotsAndInvalidEntries()
        {
            CurrencyDefinition valid = CreateCurrency("gold");
            CurrencyDefinition blank = CreateCurrency(string.Empty);
            blank.name = "Nameless";

            ExpectNullSlotWarning();
            ExpectMissingIdError("Nameless");
            CurrencyCatalog catalog = CreateCatalog(null, blank, valid);

            Assert.AreEqual(1, catalog.Count);
            Assert.AreSame(valid, catalog.Currencies[0]);
        }

        [Test]
        public void Catalog_EmptyList_ReturnsEmptyNotNull()
        {
            CurrencyCatalog catalog = CreateCatalog();

            Assert.IsNotNull(catalog.Currencies, "비어 있는 카탈로그도 정상 상태다 - null이 아니다.");
            Assert.AreEqual(0, catalog.Count);
            Assert.IsNull(catalog.Find("gold"));
        }

        [Test]
        public void Find_BlankOrUnknownId_ReturnsNull()
        {
            CurrencyCatalog catalog = CreateCatalog(CreateCurrency("gold"));

            Assert.IsNull(catalog.Find(null));
            Assert.IsNull(catalog.Find(string.Empty));
            Assert.IsNull(catalog.Find("   "));
            Assert.IsNull(catalog.Find("jewel"));
        }

        [Test]
        public void Find_DoesNotTrimTheQuery()
        {
            CurrencyDefinition gold = CreateCurrency("gold");
            CurrencyCatalog catalog = CreateCatalog(gold);

            Assert.IsNull(catalog.Find("  gold  "),
                "조회 값의 공백을 대신 지워 주지 않는다 - 공백이 붙은 키는 찾지 못하는 것으로 드러나야 한다.");
            Assert.AreSame(gold, catalog.Find("gold"), "정확히 같은 문자열일 때만 찾는다.");
        }

        [Test]
        public void Read_IsCached_AndMarkDirty_ForcesAFreshCheck()
        {
            // 검사가 다시 도는지는 <b>로그가 다시 남는지</b>로 본다 - 목록을 SerializedObject로 고치면
            // OnValidate가 먼저 캐시를 지워버려서, 목록을 바꿔 보는 방식으로는 MarkDirty만 따로
            // 증명할 수 없다.
            CurrencyDefinition blank = CreateCurrency(string.Empty);
            blank.name = "Nameless";
            CurrencyDefinition gold = CreateCurrency("gold");

            ExpectMissingIdError("Nameless");
            CurrencyCatalog catalog = CreateCatalog(blank, gold);

            Assert.AreEqual(1, catalog.Count, "첫 조회에서 검사가 돌고 결과가 캐시된다.");

            // 캐시된 상태에서 여러 번 읽어도 검사는 다시 돌지 않는다 - 다시 돌았다면 등록하지 않은
            // 에러 로그가 남아 이 시험이 실패한다.
            Assert.AreEqual(1, catalog.Count);
            Assert.AreSame(gold, catalog.Currencies[0]);
            Assert.AreSame(gold, catalog.Find("gold"));

            // MarkDirty 뒤에는 검사가 한 번 더 돌아야 하고, 그래서 같은 에러가 다시 남는다.
            // 다시 돌지 않으면 아래 기대가 채워지지 않아 시험이 실패한다.
            ExpectMissingIdError("Nameless");
            catalog.MarkDirty();

            Assert.AreEqual(1, catalog.Count, "다시 검사해도 걸러내는 기준과 결과는 같다.");
            Assert.AreSame(gold, catalog.Currencies[0]);
        }

        [Test]
        public void EditingTheList_IsVisibleOnTheNextRead()
        {
            CurrencyDefinition gold = CreateCurrency("gold");
            CurrencyCatalog catalog = CreateCatalog(gold);

            Assert.AreEqual(1, catalog.Count, "먼저 한 번 읽어서 캐시를 만든다.");

            CurrencyDefinition jewel = CreateCurrency("jewel");
            SetCurrencies(catalog, gold, jewel);
            catalog.MarkDirty();

            Assert.AreEqual(2, catalog.Count, "목록을 고친 뒤의 조회는 새 내용을 본다.");
            Assert.AreSame(jewel, catalog.Currencies[1], "덧붙인 항목은 작성 순서대로 뒤에 온다.");
            Assert.AreSame(jewel, catalog.Find("jewel"));
        }

        // ---- 도우미 ----

        /// <summary>임포터가 쓰게 될 것과 같은 직렬화 필드로 채운다.</summary>
        private CurrencyDefinition CreateCurrency(string currencyId, int displayOrder = 0)
        {
            var currency = ScriptableObject.CreateInstance<CurrencyDefinition>();
            created.Add(currency);

            var serialized = new SerializedObject(currency);
            serialized.FindProperty("currencyId").stringValue = currencyId;
            serialized.FindProperty("displayOrder").intValue = displayOrder;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return currency;
        }

        private CurrencyCatalog CreateCatalog(params CurrencyDefinition[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<CurrencyCatalog>();
            created.Add(catalog);
            catalog.name = "TestCurrencyCatalog";

            SetCurrencies(catalog, entries);
            return catalog;
        }

        private static void SetCurrencies(CurrencyCatalog catalog, params CurrencyDefinition[] entries)
        {
            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty("currencies");
            list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = entries[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // 카탈로그가 제외할 때 남기는 로그는 <b>기대한 것으로 등록해 둔다</b> - 등록하지 않으면
        // 에러 로그 하나로 시험이 실패하고, 등록해 두면 "정말 그 이유로 걸러냈는지"까지 확인된다.

        private static void ExpectNullSlotWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CurrencyCatalog\].*비어 있어"));
        }

        private static void ExpectMissingIdError(string assetName)
        {
            LogAssert.Expect(LogType.Error, new Regex($@"\[CurrencyCatalog\].*'{assetName}'.*Currency Id가"));
        }

        private static void ExpectDuplicateError(string assetName)
        {
            LogAssert.Expect(LogType.Error, new Regex($@"\[CurrencyCatalog\].*'{assetName}'.*겹쳐"));
        }
    }
}
