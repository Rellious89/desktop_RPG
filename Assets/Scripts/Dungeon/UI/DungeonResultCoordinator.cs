using System.Collections;
using Field;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// 완료 세션 FIFO와 결과 패널을 연결한다. Peek으로 표시하고 정상 확인 시 같은 시퀀스의
    /// 선두 결과 하나만 Consume한다. 보상 지급과 저장은 수행하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonResultCoordinator : MonoBehaviour
    {
        [SerializeField] private DungeonSessionTracker sessionTracker;
        [SerializeField] private DungeonResultPanel resultPanel;
        [SerializeField] private FieldTransitionSequencer transitionSequencer;

        private DungeonSessionTracker subscribedTracker;
        private DungeonResultPanel subscribedPanel;
        private FieldTransitionSequencer subscribedSequencer;
        private Coroutine showNextRoutine;

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            TryShowNextCompletedSession();
        }

        private void Start()
        {
            ResolveReferences();
            Subscribe();
            TryShowNextCompletedSession();
        }

        private void OnDisable()
        {
            if (showNextRoutine != null)
            {
                StopCoroutine(showNextRoutine);
                showNextRoutine = null;
            }
            Unsubscribe();
        }

        private void ResolveReferences()
        {
            if (sessionTracker == null)
                sessionTracker = FindObjectOfType<DungeonSessionTracker>(true);
            if (resultPanel == null)
                resultPanel = FindObjectOfType<DungeonResultPanel>(true);
            if (transitionSequencer == null)
                transitionSequencer = FieldTransitionSequencer.Instance ??
                                      FindObjectOfType<FieldTransitionSequencer>(true);

            if (sessionTracker == null)
                Debug.LogError("[DungeonResultCoordinator] DungeonSessionTracker를 찾지 못했습니다.", this);
            if (resultPanel == null)
                Debug.LogError("[DungeonResultCoordinator] DungeonResultPanel을 찾지 못했습니다.", this);
        }

        private void Subscribe()
        {
            Unsubscribe();

            if (sessionTracker != null)
            {
                sessionTracker.SessionCompleted += HandleSessionCompleted;
                subscribedTracker = sessionTracker;
            }
            if (resultPanel != null)
            {
                resultPanel.ConfirmationRequested += HandleConfirmationRequested;
                subscribedPanel = resultPanel;
            }
            if (transitionSequencer != null)
            {
                transitionSequencer.TransitionCompleted += HandleTransitionCompleted;
                subscribedSequencer = transitionSequencer;
            }
        }

        private void Unsubscribe()
        {
            if (subscribedTracker != null)
            {
                subscribedTracker.SessionCompleted -= HandleSessionCompleted;
                subscribedTracker = null;
            }
            if (subscribedPanel != null)
            {
                subscribedPanel.ConfirmationRequested -= HandleConfirmationRequested;
                subscribedPanel = null;
            }
            if (subscribedSequencer != null)
            {
                subscribedSequencer.TransitionCompleted -= HandleTransitionCompleted;
                subscribedSequencer = null;
            }
        }

        private void HandleSessionCompleted(DungeonSessionSnapshot _)
        {
            TryShowNextCompletedSession();
        }

        private void HandleTransitionCompleted()
        {
            TryShowNextCompletedSession();
        }

        private void TryShowNextCompletedSession()
        {
            if (!isActiveAndEnabled || sessionTracker == null || resultPanel == null) return;
            if (resultPanel.gameObject.activeSelf || resultPanel.HasSnapshot) return;
            if (transitionSequencer != null && transitionSequencer.isActiveAndEnabled &&
                transitionSequencer.IsPlaying) return;
            if (!sessionTracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snapshot)) return;

            resultPanel.ShowSnapshot(snapshot);
        }

        private void HandleConfirmationRequested(long displayedSequence)
        {
            if (sessionTracker == null) return;
            if (!sessionTracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot head)) return;
            if (head.SessionSequence != displayedSequence) return;
            if (!sessionTracker.TryConsumeNextCompletedSession(out DungeonSessionSnapshot consumed)) return;
            if (consumed.SessionSequence != displayedSequence)
            {
                Debug.LogError("[DungeonResultCoordinator] 확인 중 FIFO 선두가 변경되었습니다.", this);
                return;
            }

            if (showNextRoutine == null)
                showNextRoutine = StartCoroutine(ShowNextFrame());
        }

        private IEnumerator ShowNextFrame()
        {
            yield return null;
            showNextRoutine = null;
            TryShowNextCompletedSession();
        }
    }
}
