using System.Collections.Generic;
using Common;
using TMPro;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// 던전 UI가 <b>런타임에 직접 채우는</b> TMP 텍스트에 <see cref="LocalizedTMPText"/>가 함께 붙어
    /// 있을 때의 처리. 두 컴포넌트가 같은 텍스트를 서로 다른 근거로 덮어쓰면(하나는 Inspector에 박힌
    /// 정적 키, 하나는 선택된 던전) 언어를 바꾸거나 선택을 옮길 때마다 어느 쪽이 마지막에 썼는지에
    /// 따라 화면이 달라진다 - 그래서 정적 쪽을 끄고 경고를 한 번 남긴다.
    ///
    /// <b>이름으로 찾지 않는다.</b> 대상은 언제나 Inspector에서 직접 연결된 TMP이고, 여기서는 그
    /// 컴포넌트에 붙어 있는지만 확인한다. 끄는 것은 실행 중 동작이며 프리팹/씬 에셋을 고치지 않는다 -
    /// 근본 정리(에디터에서 해당 LocalizedTMPText 제거)는 경고를 보고 사람이 한다.
    /// </summary>
    internal static class DungeonStaticLocalizerGuard
    {
        /// <summary>경고를 이미 남긴 주체들. 목록 항목처럼 같은 구조가 여러 개 복제되는 자리에서 같은
        /// 경고가 항목 수만큼 쏟아지지 않게 한다 - 정리해야 할 대상은 항목 <b>원본</b> 하나이므로
        /// 주체당 한 줄이면 충분하다.</summary>
        private static readonly HashSet<string> WarnedOwners = new HashSet<string>();

        /// <summary>
        /// <paramref name="target"/>에 붙어 있는 <see cref="LocalizedTMPText"/>를 끈다(있을 때만).
        /// 경고는 주체(<paramref name="owner"/>)마다 한 번만 남긴다 - 선택을 옮기거나 목록을 다시
        /// 만들 때마다 같은 경고가 반복되지 않게 하기 위함이다.
        /// </summary>
        /// <param name="owner">경고에 표시할 주체 이름(어느 컴포넌트가 이 텍스트를 채우는지).</param>
        public static void DisableIfPresent(TextMeshProUGUI target, string owner)
        {
            if (target == null) return;
            if (!target.TryGetComponent(out LocalizedTMPText staticLocalizer)) return;

            if (staticLocalizer.enabled)
            {
                // 끄는 순간 OnDisable이 StringChanged 구독을 해제하므로, 이후 Locale이 바뀌어도 이
                // 텍스트를 건드리는 것은 던전 UI 하나뿐이다.
                staticLocalizer.enabled = false;
            }

            if (!WarnedOwners.Add(owner)) return;

            Debug.LogWarning($"[{owner}] '{target.name}'에 LocalizedTMPText가 함께 붙어 있어 실행 중에는 " +
                             "꺼 둡니다 - 이 텍스트는 선택된 던전에 따라 바뀌므로 정적 키로 덮어쓰면 안 됩니다. " +
                             "에디터에서 해당 LocalizedTMPText 컴포넌트를 제거하세요.", target);
        }
    }
}
