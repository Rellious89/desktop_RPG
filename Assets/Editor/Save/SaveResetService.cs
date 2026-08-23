using System;
using System.Collections.Generic;
using Common;

namespace CommonEditor.Save
{
    /// <summary>
    /// 초기화할 저장 항목을 고르는 플래그. <b>개발 도구 전용</b>이며 런타임 코드는 쓰지 않는다.
    ///
    /// <see cref="All"/>은 "지금 이 도구가 지원하는 초기화 항목 전체"라는 뜻이지 새 계정을 만드는 것이
    /// 아니다 - 캐릭터·계정 진행·회복소 같은 다른 필드는 여기에 들어 있지 않으므로 손대지 않는다.
    ///
    /// 값은 <c>1 &lt;&lt; n</c>로 떨어뜨려 두어 <c>EnumFlagsField</c>가 항목별 토글로 그리고,
    /// <see cref="All"/>을 고른 뒤 하나만 해제하는 조합이 그대로 만들어지게 한다.
    /// </summary>
    [Flags]
    public enum SaveResetTargets
    {
        None = 0,
        Item = 1 << 0,
        Currency = 1 << 1,
        Construction = 1 << 2,
        All = Item | Currency | Construction,
    }

    /// <summary><see cref="SaveResetService.Apply"/>의 결과 갈래.</summary>
    public enum SaveResetOutcome
    {
        /// <summary>고른 항목이 없어 아무것도 하지 않았다 - 저장도 하지 않는다.</summary>
        NothingSelected,

        /// <summary>선택 항목을 모두 초기화하고 저장에 성공했다.</summary>
        Success,

        /// <summary>저장에 실패해 <b>변경 전 상태로 전부 되돌렸다</b>(부분 초기화는 남지 않는다).</summary>
        SaveFailed,
    }

    /// <summary><see cref="SaveResetService.Apply"/>가 무엇을 어떻게 했는지.</summary>
    public readonly struct SaveResetResult
    {
        public SaveResetOutcome Outcome { get; }

        /// <summary>실제로 적용을 시도한 항목(잡음 비트를 걸러낸 값). 아무것도 안 골랐으면
        /// <see cref="SaveResetTargets.None"/>이다.</summary>
        public SaveResetTargets AppliedTargets { get; }

        public SaveResetResult(SaveResetOutcome outcome, SaveResetTargets appliedTargets)
        {
            Outcome = outcome;
            AppliedTargets = appliedTargets;
        }

        public bool Saved => Outcome == SaveResetOutcome.Success;
    }

    /// <summary>
    /// 저장 데이터의 <b>일부 항목만</b> 초기화하는 순수 로직. <see cref="SaveResetWindow"/>가 이 자리로
    /// <see cref="SaveSystem.Data"/>와 <see cref="SaveSystem.Save"/>를 넘기고, 시험은 격리된 메모리
    /// <see cref="SaveData"/>와 저장 대리자를 넘긴다 - 그래서 이 로직은 실제 저장 파일을 알지 못한다.
    ///
    /// <b>SaveSystem에 런타임 Reset API를 만들지 않으려는 것이 이 분리의 목적이다.</b> 초기화는 개발
    /// 도구에서만 필요하므로, 저장 계층은 그대로 두고 여기(Editor 전용)에서 <see cref="SaveData"/>의
    /// 필드를 직접 고친 뒤 넘겨받은 저장 대리자를 <b>정확히 한 번</b> 부른다.
    ///
    /// <b>전부 아니면 전무다.</b> 선택 항목을 모두 메모리에 적용한 뒤 한 번에 저장하고, 저장이 실패하면
    /// 이번에 바꾼 필드를 전부 원래 값으로 되돌린다 - 성공한 일부만 남는 부분 초기화를 만들지 않는다.
    /// </summary>
    public static class SaveResetService
    {
        /// <summary>
        /// 선택한 항목을 초기화하고 <paramref name="save"/>를 <b>최대 한 번</b> 부른다.
        ///
        /// <list type="bullet">
        ///   <item>고른 항목이 없으면 아무것도 바꾸지 않고 저장도 하지 않는다
        ///         (<see cref="SaveResetOutcome.NothingSelected"/>).</item>
        ///   <item>선택 항목을 모두 메모리에 적용한 뒤 <paramref name="save"/>를 한 번 부른다.</item>
        ///   <item>저장이 false를 돌려주면 이번에 바꾼 필드를 전부 되돌린다
        ///         (<see cref="SaveResetOutcome.SaveFailed"/>).</item>
        /// </list>
        /// </summary>
        /// <param name="data">고칠 저장 문서. null이면 <see cref="ArgumentNullException"/>.</param>
        /// <param name="targets">초기화할 항목. <see cref="SaveResetTargets.All"/> 밖의 비트는 무시한다.</param>
        /// <param name="save">메모리 적용 뒤 파일에 기록하는 대리자. 성공하면 true. null이면
        /// <see cref="ArgumentNullException"/>.</param>
        public static SaveResetResult Apply(SaveData data, SaveResetTargets targets, Func<bool> save)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (save == null) throw new ArgumentNullException(nameof(save));

            // 정의되지 않은 비트가 섞여 들어와도 지원하는 세 항목만 본다. 모집 주기는 별도 대상이
            // 아니라 Construction의 종속 기록이므로 그 비트와 함께 움직인다.
            SaveResetTargets effective = targets & SaveResetTargets.All;
            if (effective == SaveResetTargets.None)
            {
                return new SaveResetResult(SaveResetOutcome.NothingSelected, SaveResetTargets.None);
            }

            bool resetItems = (effective & SaveResetTargets.Item) != 0;
            bool resetCurrency = (effective & SaveResetTargets.Currency) != 0;
            bool resetConstruction = (effective & SaveResetTargets.Construction) != 0;

            // 되돌릴 수 있게 바꿀 필드의 원래 값을 들고 있는다. 목록은 빈 목록으로 <b>교체</b>하고 예전
            // 참조를 그대로 보관하므로, 되돌리기는 참조를 도로 끼우는 것으로 끝난다(깊은 복사가 없다).
            List<InventoryItemState> oldItems = resetItems ? data.items : null;
            int oldCurrency = resetCurrency ? data.currency : 0;
            List<BuildingConstructionSaveState> oldConstructions =
                resetConstruction ? data.buildingConstructions : null;
            List<RecruitmentCycleSaveState> oldRecruitmentCycles =
                resetConstruction ? data.recruitmentCycles : null;

            if (resetItems) data.items = new List<InventoryItemState>();
            if (resetCurrency) data.currency = 0;
            if (resetConstruction)
            {
                data.buildingConstructions = new List<BuildingConstructionSaveState>();
                data.recruitmentCycles = new List<RecruitmentCycleSaveState>();
            }

            bool saved;
            try
            {
                saved = save();
            }
            catch
            {
                // 대리자가 터져도 메모리는 원래대로 돌려놓고 예외는 그대로 올려보낸다 - 부분 초기화가
                // 남는 것보다 호출부가 실패를 알아채는 편이 낫다.
                Rollback(data, resetItems, oldItems, resetCurrency, oldCurrency, resetConstruction,
                    oldConstructions, oldRecruitmentCycles);
                throw;
            }

            if (!saved)
            {
                Rollback(data, resetItems, oldItems, resetCurrency, oldCurrency, resetConstruction,
                    oldConstructions, oldRecruitmentCycles);
                return new SaveResetResult(SaveResetOutcome.SaveFailed, effective);
            }

            return new SaveResetResult(SaveResetOutcome.Success, effective);
        }

        private static void Rollback(
            SaveData data,
            bool resetItems, List<InventoryItemState> oldItems,
            bool resetCurrency, int oldCurrency,
            bool resetConstruction, List<BuildingConstructionSaveState> oldConstructions,
            List<RecruitmentCycleSaveState> oldRecruitmentCycles)
        {
            if (resetItems) data.items = oldItems;
            if (resetCurrency) data.currency = oldCurrency;
            if (resetConstruction)
            {
                data.buildingConstructions = oldConstructions;
                data.recruitmentCycles = oldRecruitmentCycles;
            }
        }
    }
}
