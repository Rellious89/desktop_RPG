using System;
using UnityEditor.Localization;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace CommonEditor.Save
{
    /// <summary>
    /// Reset 창 전용 <b>에디터 로컬라이징 조회</b>. 런타임 <see cref="LocalizedString.GetLocalizedString()"/>이나
    /// <c>LocalizationSettings.SelectedLocale</c>·초기화 작업을 전혀 건드리지 않고, 에디터 에셋에서
    /// ko-KR <see cref="StringTable"/>을 직접 읽어 이름을 얻는다.
    ///
    /// 이렇게 하는 이유: Edit Mode에서는 <c>SelectedLocale</c>이 null일 수 있고, 그 상태로 런타임 API를 부르면
    /// Reset 창 <c>OnGUI</c>가 갱신될 때마다 Localization 비동기 작업이 오류 로그를 쏟아 낸다. 조회 경로만
    /// 에디터 전용 API로 바꿔 이 오류를 없앤다. 전역 로케일을 바꾸거나 초기화하지 않는다.
    ///
    /// Reset 창 자체가 한국어로 구성되어 있으므로 이 도구는 ko-KR을 고정 기준으로 쓴다. 이름은 일반 문자열이라
    /// Smart String 실행이나 변수 치환 없이 <see cref="StringTableEntry.Value"/> 원본만 사용한다.
    /// </summary>
    internal static class SaveResetLocalization
    {
        /// <summary>Reset 창이 이름을 고정 한국어로 표시하기 위한 기준 로케일.</summary>
        internal static readonly LocaleIdentifier KoreanLocale = new LocaleIdentifier("ko-KR");

        /// <summary>기본 ko-KR 에디터 조회로 <paramref name="reference"/>를 해석한다.</summary>
        internal static string Resolve(LocalizedString reference) => Resolve(reference, KoreanTableProvider);

        /// <summary>
        /// 조회 순서: 빈 참조면 null → 테이블 조회 실패면 null → 엔트리 없으면 null → 값이 비면 null → 원본 값 반환.
        /// 테이블 공급자는 시험에서 주입할 수 있도록 매개변수로 받는다(기본값은 <see cref="KoreanTableProvider"/>).
        /// 어느 단계에서 실패해도 예외나 로그 없이 조용히 null을 돌려준다 - 이름 표시가 실패해도 창은 떠 있어야 한다.
        /// </summary>
        internal static string Resolve(LocalizedString reference, Func<TableReference, StringTable> tableProvider)
        {
            if (reference == null || reference.IsEmpty) return null;
            if (tableProvider == null) return null;

            StringTable table = tableProvider(reference.TableReference);
            if (table == null) return null;

            StringTableEntry entry = table.GetEntryFromReference(reference.TableEntryReference);
            if (entry == null) return null;

            string value = entry.Value;
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>주어진 테이블 참조의 ko-KR <see cref="StringTable"/>을 에디터 에셋에서 직접 찾는다.
        /// 컬렉션이나 ko-KR 테이블이 없으면 null.</summary>
        internal static StringTable KoreanTableProvider(TableReference tableReference)
        {
            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(tableReference);
            if (collection == null) return null;

            return collection.GetTable(KoreanLocale) as StringTable;
        }
    }
}
