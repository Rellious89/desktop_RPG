using NUnit.Framework;
using Recruitment;

namespace RecruitmentEditor.Tests
{
    /// <summary>
    /// 모집 화면이 <b>지금 무엇을 켤지</b> 고르는 판정만 본다(9.3F).
    ///
    /// <b>씬도 카메라도 저장 파일도 쓰지 않는다.</b>
    /// <see cref="RecruitmentUiController.ResolveState"/>는 주기 단계·보존된 후보·후보 소진 여부
    /// 셋만 받는 순수한 함수이므로, 우선순위가 어긋나는 것을 여기서 글자 그대로 붙잡을 수 있다.
    /// </summary>
    public sealed class RecruitmentUiStateTests
    {
        private const string Pending = "CatMage";

        // ---- 후보가 남아 있으면 기존 화면 그대로 ----

        [Test]
        public void Waiting_WithCandidatesLeft_StaysOnProgress()
        {
            Assert.AreEqual(RecruitmentUiState.Progress, RecruitmentUiController.ResolveState(
                RecruitmentCyclePhase.Waiting, null, hasEligibleCandidate: true));
        }

        [Test]
        public void Ready_WithCandidatesLeft_StaysOnStandby()
        {
            Assert.AreEqual(RecruitmentUiState.Standby, RecruitmentUiController.ResolveState(
                RecruitmentCyclePhase.Ready, string.Empty, hasEligibleCandidate: true));
        }

        // ---- 후보가 없으면 소진 화면 ----

        [Test]
        public void NoCandidateLeft_ReplacesBothProgressAndStandby()
        {
            Assert.AreEqual(RecruitmentUiState.Exhausted, RecruitmentUiController.ResolveState(
                RecruitmentCyclePhase.Waiting, null, hasEligibleCandidate: false),
                "뽑을 사람이 없는데 남은 시간을 세어 보여 주는 것은 거짓말이다.");

            Assert.AreEqual(RecruitmentUiState.Exhausted, RecruitmentUiController.ResolveState(
                RecruitmentCyclePhase.Ready, string.Empty, hasEligibleCandidate: false),
                "뽑을 사람이 없는데 모집 버튼을 보여 주면 눌러도 아무 일이 없다.");
        }

        // ---- 보존된 후보가 가장 앞선다 ----

        [Test]
        public void PendingCandidate_OutranksExhaustion()
        {
            // 마지막 한 명을 뽑아 둔 상태다 - 후보 표는 이미 비었지만 그 용병은 와 있다.
            Assert.AreEqual(RecruitmentUiState.Result, RecruitmentUiController.ResolveState(
                RecruitmentCyclePhase.Ready, Pending, hasEligibleCandidate: false),
                "소진 화면이 덮으면 이미 온 용병을 등록도 돌려보내기도 할 수 없다.");

            Assert.AreEqual(RecruitmentUiState.Result, RecruitmentUiController.ResolveState(
                RecruitmentCyclePhase.Waiting, Pending, hasEligibleCandidate: false));
        }

        [Test]
        public void PendingCandidate_OutranksProgressAndStandby()
        {
            Assert.AreEqual(RecruitmentUiState.Result, RecruitmentUiController.ResolveState(
                RecruitmentCyclePhase.Waiting, Pending, hasEligibleCandidate: true));

            Assert.AreEqual(RecruitmentUiState.Result, RecruitmentUiController.ResolveState(
                RecruitmentCyclePhase.Ready, Pending, hasEligibleCandidate: true));
        }

        /// <summary>돌려보내면 후보 보존이 풀리고, 그 캐릭터는 소유권이 생기지 않았으므로 다시 후보가 된다.</summary>
        [Test]
        public void ReturningTheCandidate_GoesBackToTheNormalRecruitmentState()
        {
            Assert.AreEqual(RecruitmentUiState.Standby, RecruitmentUiController.ResolveState(
                RecruitmentCyclePhase.Ready, null, hasEligibleCandidate: true));
        }

        // ---- 잠김·미초기화·읽을 수 없음은 소진 화면도 감춘다 ----
        // 마을 밖과 필드 전환 중은 이 판정보다 앞선 자리(Refresh의 IsTownReady 관문)에서 걸러져
        // 그대로 Hidden으로 간다 - 어느 쪽이든 켜지는 화면은 여기와 같은 Hidden 하나다.

        [TestCase(RecruitmentCyclePhase.Locked)]
        [TestCase(RecruitmentCyclePhase.NotInitialized)]
        [TestCase(RecruitmentCyclePhase.Unreadable)]
        public void NonRunningPhases_HideEverything(RecruitmentCyclePhase phase)
        {
            Assert.AreEqual(RecruitmentUiState.Hidden,
                RecruitmentUiController.ResolveState(phase, null, hasEligibleCandidate: false));

            Assert.AreEqual(RecruitmentUiState.Hidden,
                RecruitmentUiController.ResolveState(phase, Pending, hasEligibleCandidate: false),
                "잠긴 여관에서는 보존된 후보도 소진 안내도 보이지 않는다.");
        }
    }
}
