using System;
using CommonEditor.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.TestTools;

namespace CommonEditor.SaveTests
{
    /// <summary>
    /// <see cref="SaveResetLocalization"/> 집중 시험. Reset 창의 이름 조회가 <b>런타임 로케일 상태에 의존하지 않고</b>
    /// 에디터 에셋에서 ko-KR 값을 읽는지, 그리고 어느 단계에서 실패하든 <b>예외·오류 로그 없이 조용히 null</b>로
    /// 폴백하는지를 확인한다.
    ///
    /// 실제 프로젝트 로컬라이징 테이블에 의존하면 시험이 데이터 변화에 깨지므로, 테이블 공급자를 주입하는
    /// <see cref="SaveResetLocalization.Resolve(LocalizedString, System.Func{TableReference, StringTable})"/>
    /// 오버로드로 메모리 안 <see cref="StringTable"/>만 사용한다. 실제 에디터 조회 경로(ko-KR 고정)는 프로덕션
    /// <see cref="SaveResetLocalization.Resolve(LocalizedString)"/>가 담당한다.
    /// </summary>
    public sealed class SaveResetLocalizationTests
    {
        /// <summary>키 하나를 담은 메모리 StringTable을 만든다. SharedData를 붙여 KeyId 조회가 되게 한다.</summary>
        private static StringTable MakeTable(string key, string value, out long keyId)
        {
            var table = ScriptableObject.CreateInstance<StringTable>();
            table.SharedData = ScriptableObject.CreateInstance<SharedTableData>();
            StringTableEntry entry = table.AddEntry(key, value);
            keyId = entry.KeyId;
            return table;
        }

        private static void Destroy(StringTable table)
        {
            if (table == null) return;
            if (table.SharedData != null) UnityEngine.Object.DestroyImmediate(table.SharedData);
            UnityEngine.Object.DestroyImmediate(table);
        }

        // ---- 1. 유효한 참조 → ko-KR 값 ----

        [Test]
        public void 유효한_참조는_ko_KR_문자열을_돌려준다()
        {
            StringTable table = MakeTable("item_red_potion", "빨간 포션", out long keyId);
            try
            {
                var reference = new LocalizedString("04_Item", keyId);
                string result = SaveResetLocalization.Resolve(reference, _ => table);

                Assert.AreEqual("빨간 포션", result);
            }
            finally { Destroy(table); }
        }

        // ---- 2. SelectedLocale에 의존하지 않는다 ----

        [Test]
        public void 런타임_로케일을_건드리지_않고_주입된_테이블로_조회한다()
        {
            // 이 시험은 SelectedLocale을 설정하지 않는다(Edit Mode에서 null일 수 있는 상황). 그래도 값이 나와야 한다 -
            // 조회 경로가 런타임 로케일이 아니라 주입된 에디터 테이블만 보기 때문이다.
            StringTable table = MakeTable("chr_cat_knight", "고양이 기사", out long keyId);
            try
            {
                var reference = new LocalizedString("06_Character", keyId);
                bool providerCalled = false;

                string result = SaveResetLocalization.Resolve(reference, _ =>
                {
                    providerCalled = true;
                    return table;
                });

                Assert.AreEqual("고양이 기사", result);
                Assert.IsTrue(providerCalled, "조회는 주입된 테이블 공급자만 사용해야 합니다.");
            }
            finally { Destroy(table); }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void 서로_다른_숫자_키는_같은_테이블에서도_독립적인_이름과_캐시_키를_쓴다()
        {
            StringTable table = MakeTable("cat_knight", "고양이기사", out long catKeyId);
            StringTableEntry elfEntry = table.AddEntry("elf_archer", "엘프궁수");
            var cat = new LocalizedString("06_Character", catKeyId);
            var elf = new LocalizedString("06_Character", elfEntry.KeyId);
            var locale = new LocaleIdentifier("ko-KR");

            try
            {
                Assert.AreNotEqual(new SaveResetLocalization.CacheKey(cat, locale),
                    new SaveResetLocalization.CacheKey(elf, locale),
                    "숫자 Key ID가 다른 참조는 하나의 OnGUI 캐시 항목을 공유하면 안 됩니다.");
                Assert.AreEqual("고양이기사", SaveResetLocalization.Resolve(cat, locale, (_, __) => table));
                Assert.AreEqual("엘프궁수", SaveResetLocalization.Resolve(elf, locale, (_, __) => table));
            }
            finally { Destroy(table); }
        }

        [Test]
        public void 같은_참조도_현재_Locale이_다르면_별도_캐시_키를_쓴다()
        {
            var reference = new LocalizedString("06_Character", 347070464L);

            Assert.AreNotEqual(
                new SaveResetLocalization.CacheKey(reference, new LocaleIdentifier("ko-KR")),
                new SaveResetLocalization.CacheKey(reference, new LocaleIdentifier("en")));
        }

        // ---- 3. 빈 참조 → null ----

        [Test]
        public void 빈_참조는_null을_돌려주고_공급자를_부르지_않는다()
        {
            var empty = new LocalizedString();
            bool providerCalled = false;

            string result = SaveResetLocalization.Resolve(empty, _ => { providerCalled = true; return null; });

            Assert.IsNull(result);
            Assert.IsFalse(providerCalled, "빈 참조는 테이블을 조회하지 않습니다.");
        }

        [Test]
        public void null_참조는_null을_돌려준다()
        {
            Assert.IsNull(SaveResetLocalization.Resolve(null, _ => null));
        }

        // ---- 4. 테이블 없음 → 오류 없이 null ----

        [Test]
        public void 테이블을_찾지_못하면_오류_없이_null을_돌려준다()
        {
            var reference = new LocalizedString("없는_테이블", 123L);

            string result = SaveResetLocalization.Resolve(reference, _ => null);

            Assert.IsNull(result);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- 5. 엔트리 없음 → 오류 없이 null ----

        [Test]
        public void 엔트리를_찾지_못하면_오류_없이_null을_돌려준다()
        {
            StringTable table = MakeTable("item_red_potion", "빨간 포션", out long existingKeyId);
            try
            {
                // 테이블에는 있지만, 이 KeyId는 테이블에 없다.
                var reference = new LocalizedString("04_Item", existingKeyId + 999);

                string result = SaveResetLocalization.Resolve(reference, _ => table);

                Assert.IsNull(result);
            }
            finally { Destroy(table); }

            LogAssert.NoUnexpectedReceived();
        }

        // ---- 값이 비어 있으면 null ----

        [Test]
        public void 엔트리_값이_비어_있으면_null을_돌려준다()
        {
            StringTable table = MakeTable("item_empty", string.Empty, out long keyId);
            try
            {
                var reference = new LocalizedString("04_Item", keyId);

                string result = SaveResetLocalization.Resolve(reference, _ => table);

                Assert.IsNull(result);
            }
            finally { Destroy(table); }
        }

        // ---- 6. 반복 호출: 예외·예상치 못한 로그 없음 ----

        [Test]
        public void 반복_호출해도_예외와_예상치_못한_오류_로그가_없다()
        {
            StringTable table = MakeTable("item_red_potion", "빨간 포션", out long keyId);
            try
            {
                var hit = new LocalizedString("04_Item", keyId);
                var missTable = new LocalizedString("04_Item", keyId + 42);
                var missEntry = new LocalizedString("없는_테이블", 7L);
                var empty = new LocalizedString();

                for (int i = 0; i < 50; i++)
                {
                    Assert.AreEqual("빨간 포션", SaveResetLocalization.Resolve(hit, _ => table));
                    Assert.IsNull(SaveResetLocalization.Resolve(missTable, _ => table));
                    Assert.IsNull(SaveResetLocalization.Resolve(missEntry, _ => null));
                    Assert.IsNull(SaveResetLocalization.Resolve(empty, _ => table));
                }
            }
            finally { Destroy(table); }

            LogAssert.NoUnexpectedReceived();
        }

        // ---- 방어: 공급자가 null ----

        [Test]
        public void 테이블_공급자가_null이면_null을_돌려준다()
        {
            var reference = new LocalizedString("04_Item", 1L);

            Assert.IsNull(SaveResetLocalization.Resolve(reference, (Func<TableReference, StringTable>)null));
        }
    }
}
