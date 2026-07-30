using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 같은 GameObject의 ScrollRect를 <b>처음 열릴 때 한 번만</b> 지정한 위치로 맞춘다. 스크롤 UI마다
    /// 하나씩 붙여 쓰는 재사용 컴포넌트이며, 전역에서 모든 ScrollRect를 건드리지 않는다 - 값도 컴포넌트마다
    /// 따로 갖는다.
    ///
    /// 원하는 동작:
    /// <code>
    /// 최초 오픈        -> 지정한 초기 위치에서 시작
    /// 닫고 다시 오픈   -> 사용자가 마지막으로 보던 위치 유지
    /// </code>
    ///
    /// <b>레이아웃 계산이 끝난 뒤에 적용한다.</b> 리스트 항목이 만들어지고 Content 크기가 확정되기
    /// 전에 normalized position을 넣으면, 그 뒤의 레이아웃 계산에서 다시 중앙 같은 값으로 되돌아간다.
    /// 그래서 두 번에 걸쳐 적용한다.
    ///   1. OnEnable에서 즉시 한 번(첫 프레임에 엉뚱한 위치가 잠깐 보이는 것을 막는 선적용)
    ///   2. 다음 프레임에 Canvas/레이아웃을 강제로 갱신한 뒤 다시 한 번(이때가 실제로 확정되는 적용)
    /// 두 번 모두 "최초 1회 적용"의 일부이며, 완료 표시는 2번이 끝난 뒤에만 한다 - 1번만 하고 패널이
    /// 곧바로 닫힌 경우에는 아직 제대로 적용되지 않았으므로 다음 오픈에 다시 시도한다.
    ///
    /// 완료 표시는 인스턴스 필드라 <b>씬을 새로 시작하면 다시 초기 위치가 적용된다</b>(정적 상태나
    /// 저장 값을 쓰지 않는다). 명시적으로 다시 맞추고 싶으면
    /// <see cref="ResetToInitialPosition"/>을 호출한다.
    ///
    /// 목록 갱신에는 관여하지 않는다 - 항목이 다시 만들어져도 이 컴포넌트는 아무 것도 하지 않으므로
    /// 사용자가 보던 위치가 유지된다.
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    [DisallowMultipleComponent]
    public class ScrollRectInitialPosition : MonoBehaviour
    {
        [Tooltip("끄면 초기 위치를 건드리지 않는다(ScrollRect 기본 동작 그대로). " +
                 "ResetToInitialPosition()은 이 설정과 무관하게 항상 동작한다.")]
        [SerializeField] private bool applyInitialPosition = true;

        [Tooltip("최초 오픈 시 가로 위치. 0 = 맨 왼쪽, 1 = 맨 오른쪽.")]
        [Range(0f, 1f)]
        [SerializeField] private float initialHorizontalNormalizedPosition = 0f;

        [Tooltip("최초 오픈 시 세로 위치. Unity ScrollRect 기준으로 1 = 맨 위, 0 = 맨 아래.")]
        [Range(0f, 1f)]
        [SerializeField] private float initialVerticalNormalizedPosition = 1f;

        private ScrollRect scrollRect;
        private Coroutine applyRoutine;

        /// <summary>최초 적용이 끝났는지. 인스턴스 필드이므로 씬을 다시 시작하면 false부터 시작한다.</summary>
        private bool hasAppliedInitialPosition;

        /// <summary>최초 적용이 끝난 뒤에는 true - 이 값이 true인 동안 이 컴포넌트는 스크롤 위치를
        /// 건드리지 않는다.</summary>
        public bool HasAppliedInitialPosition => hasAppliedInitialPosition;

        private void Awake()
        {
            scrollRect = GetComponent<ScrollRect>();
        }

        private void OnEnable()
        {
            // 두 번째 오픈부터는 아무 것도 하지 않는다 - 사용자가 보던 위치를 그대로 남긴다.
            if (hasAppliedInitialPosition) return;

            if (!applyInitialPosition)
            {
                hasAppliedInitialPosition = true;
                return;
            }

            BeginApply();
        }

        private void OnDisable()
        {
            // 코루틴은 비활성화와 함께 중단된다 - 다음 오픈에서 다시 시작할 수 있게 핸들만 비운다.
            applyRoutine = null;
        }

        /// <summary>지금 즉시 초기 위치로 되돌린다(설정된 값 기준). 오브젝트가 꺼져 있으면 레이아웃
        /// 계산을 기다릴 수 없으므로, 완료 표시만 해제해서 다음에 켜질 때 최초 적용 경로를 다시 타게 한다.</summary>
        public void ResetToInitialPosition()
        {
            if (!isActiveAndEnabled)
            {
                hasAppliedInitialPosition = false;
                return;
            }

            hasAppliedInitialPosition = false;
            BeginApply();
        }

        private void BeginApply()
        {
            ApplyNow(); // 선적용 - 첫 프레임 깜빡임 방지
            if (applyRoutine == null) applyRoutine = StartCoroutine(ApplyAfterLayout());
        }

        /// <summary>같은 프레임에 일어나는 항목 생성/레이아웃 요청이 끝난 뒤에 확정 적용한다.
        /// 한 프레임 기다린 다음 Canvas와 Content 레이아웃을 강제로 갱신해서, Content 크기가 확정된
        /// 상태에서 normalized position을 넣는다.</summary>
        private IEnumerator ApplyAfterLayout()
        {
            yield return null;

            Canvas.ForceUpdateCanvases();

            RectTransform content = scrollRect != null ? scrollRect.content : null;
            if (content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            Canvas.ForceUpdateCanvases();

            ApplyNow();

            hasAppliedInitialPosition = true;
            applyRoutine = null;
        }

        /// <summary>가로/세로 값을 그대로 넣고 남아 있던 관성을 지운다 - 관성을 지우지 않으면 방금
        /// 맞춘 위치가 다음 프레임에 밀려난다. ScrollRect의 Horizontal/Vertical 체크와 무관하게 두
        /// 축을 모두 적용한다(그 체크는 사용자 입력 허용 여부이고, 위치 자체는 어느 쪽이든 의미가 있다).</summary>
        private void ApplyNow()
        {
            if (scrollRect == null) return;

            scrollRect.horizontalNormalizedPosition = initialHorizontalNormalizedPosition;
            scrollRect.verticalNormalizedPosition = initialVerticalNormalizedPosition;
            scrollRect.velocity = Vector2.zero;
        }
    }
}
