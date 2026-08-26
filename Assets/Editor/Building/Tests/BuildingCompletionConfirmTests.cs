using System;
using System.Collections.Generic;
using Building;
using Common;
using NUnit.Framework;

namespace BuildingEditor.Tests
{
    /// <summary>
    /// <see cref="BuildingCompletionPolicy.IsConfirmedCompleted"/>가 <b>완료 버튼 클릭 전후</b>를
    /// 어떻게 가르는지에 대한 집중 시험. 메뉴 게이트와 패널 차단이 모두 이 한 정책만 재사용하므로,
    /// "예정 시각은 지났지만 아직 확정하지 않았다"와 "사용자가 확정했다"를 글자 그대로 확인한다.
    ///
    /// <b>저장 시스템을 거치지 않는다.</b> 정책은 <see cref="SaveData"/>를 인자로 받는 순수 함수라
    /// 메모리 문서 하나만 넘기면 되고, 실제 파일 근처에도 가지 않는다.
    /// </summary>
    public sealed class BuildingCompletionConfirmTests
    {
        /// <summary>기준 시각. 완료 예정은 이보다 한 시간 전(=이미 지남)으로 둔다.</summary>
        private static readonly DateTime NowUtc = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

        private static SaveData WithConstruction(string buildingId, bool notified)
        {
            return new SaveData
            {
                buildingConstructions = new List<BuildingConstructionSaveState>
                {
                    new BuildingConstructionSaveState
                    {
                        buildingId = buildingId,
                        startedAtUtc = SaveData.FormatTimestamp(NowUtc.AddHours(-2)),
                        completeAtUtc = SaveData.FormatTimestamp(NowUtc.AddHours(-1)),
                        completionNotified = notified,
                    },
                },
            };
        }

        // ---- 3. 완료 버튼 클릭 전: 확정 완료 아님 ----

        [Test]
        public void 완료_확인_대기_상태의_건물_1은_확정_완료가_아니다()
        {
            SaveData data = WithConstruction("1", notified: false);

            Assert.IsFalse(BuildingCompletionPolicy.IsConfirmedCompleted(data, "1", NowUtc),
                "예정 시각이 지났어도 사용자가 확정하기 전에는 확정 완료가 아니다");
        }

        // ---- 4. 완료 버튼 클릭 후: 확정 완료 ----

        [Test]
        public void 완료를_확정한_건물_1은_확정_완료다()
        {
            SaveData data = WithConstruction("1", notified: true);

            Assert.IsTrue(BuildingCompletionPolicy.IsConfirmedCompleted(data, "1", NowUtc),
                "예정 시각이 지났고 사용자가 확정했으면 확정 완료다");
        }

        // ---- 방어: 기록이 없으면 확정 완료가 아니다 ----

        [Test]
        public void 건설_기록이_없으면_확정_완료가_아니다()
        {
            var data = new SaveData { buildingConstructions = new List<BuildingConstructionSaveState>() };

            Assert.IsFalse(BuildingCompletionPolicy.IsConfirmedCompleted(data, "1", NowUtc));
        }
    }
}
