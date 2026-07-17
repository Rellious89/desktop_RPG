using System.Collections.Generic;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// ToastLayer(Canvas 직속) 아래에서 토스트 스택 전체를 관리한다. 새 토스트는 항상 anchor 위치에
    /// 등장하고, 이미 떠 있는 토스트는 슬롯이 한 칸씩 위로 밀려난다. 최대 maxVisibleCount개까지만
    /// 동시에 표시하며 초과분은 가장 오래된 것부터 퇴장시킨다. 개별 토스트가 자신의 visibleDuration으로
    /// 먼저 사라지는 경우에는 나머지를 다시 정렬하지 않는다 - 위치 재계산은 오직 새 토스트가 추가되는
    /// 순간에만 일어난다(DamageNumberSpawner와 같은 이유로 인스턴스를 풀링해 GC 압박을 피한다).
    ///
    /// active는 항상 "오래된 순" - active[0]이 가장 오래됐고(다음 초과 시 가장 먼저 퇴장), 마지막
    /// 원소가 가장 최근에 추가된 토스트다. 새 토스트가 들어올 때마다 이전 슬롯 값에 그냥 +1 하지
    /// 않고 이 active 목록의 현재 크기/순서만으로 목표 슬롯(0 ~ maxVisibleCount-1)을 매번 새로
    /// 계산한다 - 그래야 중간의 토스트가 먼저 만료돼도 남은 토스트가 슬롯을 건너뛰며 누적되어
    /// maxVisibleCount 밖으로 밀려나는 일이 없다.
    ///
    /// 업적/레벨업/아이템 획득/시스템 안내 등 다른 알림도 Show(ToastRequest)로 이 스택을 그대로
    /// 재사용할 수 있다. 기존의 단일 문자열 호출은 Show(string)이 내부에서 기본 ToastRequest로
    /// 변환해 처리한다.
    /// </summary>
    public class ToastManager : MonoBehaviour
    {
        public static ToastManager Instance { get; private set; }

        [Header("References")]
        [Tooltip("새 토스트가 등장하는 기준 위치(ToastLayer 로컬 좌표계).")]
        [SerializeField] private RectTransform anchor;
        [Tooltip("풀링에 사용할 토스트 템플릿. 씬에 배치된 원본이며 실행 시 비활성화되고 복제본만 쓰인다.")]
        [SerializeField] private ToastInstance template;

        [Header("Stack")]
        [SerializeField] private int maxVisibleCount = 3;
        [Tooltip("토스트 실제 높이 + 8~12px 권장.")]
        [SerializeField] private float slotSpacing = 70f;
        [SerializeField] private float enterDuration = 0.15f;
        [SerializeField] private float visibleDuration = 2.0f;
        [SerializeField] private float exitDuration = 0.25f;
        [SerializeField] private float shiftDuration = 0.18f;

        [Header("Pool")]
        [Tooltip("미리 만들어두고 재사용할 토스트 개수. 동시에 떠 있을 수 있는 최대 개수(maxVisibleCount + 퇴장 애니메이션 중인 것)보다 넉넉하게 잡는다.")]
        [SerializeField] private int poolSize = 4;

        private readonly Queue<ToastInstance> pool = new Queue<ToastInstance>();
        private readonly List<ToastInstance> active = new List<ToastInstance>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[ToastManager] 씬에 ToastManager가 이미 있습니다. 이 인스턴스는 무시합니다.", this);
                enabled = false;
                return;
            }
            Instance = this;

            if (template == null)
            {
                Debug.LogError("[ToastManager] template이 지정되지 않았습니다.", this);
                return;
            }

            for (int i = 0; i < poolSize; i++)
            {
                pool.Enqueue(CreatePooledInstance());
            }
            template.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private ToastInstance CreatePooledInstance()
        {
            ToastInstance instance = Instantiate(template, transform);
            instance.gameObject.SetActive(false);
            return instance;
        }

        /// <summary>기존 단일 문자열 호출부와의 호환 경로. 기본 ToastRequest로 변환해 처리한다.</summary>
        public void Show(string message)
        {
            Show(ToastRequest.FromMessage(message));
        }

        public void Show(ToastRequest request)
        {
            if (template == null || anchor == null) return;

            int max = Mathf.Max(1, maxVisibleCount);
            if (active.Count >= max)
            {
                // active[0] = 가장 오래된 토스트. 초과 판단과 퇴장 대상 선정에 항상 같은 "오래된 순"
                // 기준을 쓴다.
                ToastInstance oldest = active[0];
                active.RemoveAt(0);
                oldest.ForceExit(exitDuration, HandleBeginExit, ReturnToPool);
            }

            RecomputeExistingSlots();

            ToastInstance instance = pool.Count > 0 ? pool.Dequeue() : CreatePooledInstance();
            instance.gameObject.SetActive(true);

            float duration = request.duration > 0f ? request.duration : visibleDuration;
            instance.EnterAt(anchor.anchoredPosition, request, enterDuration, duration, exitDuration, HandleBeginExit, ReturnToPool);
            active.Add(instance);
        }

        /// <summary>새 토스트가 slot 0을 차지하기 전에, 현재 active 목록만으로 기존 토스트들의 목표
        /// 슬롯을 처음부터 다시 계산한다. 가장 최근 기존 토스트가 slot 1, 그다음이 slot 2, ... 순서다.
        /// 이전에 저장해둔 목표 위치에 더하지 않으므로 중간 만료로 생긴 빈 슬롯이 누적되지 않는다.</summary>
        private void RecomputeExistingSlots()
        {
            int count = active.Count;
            for (int i = 0; i < count; i++)
            {
                // active[i]는 오래된 순으로 정렬돼 있으므로, 가장 최근(count-1번째)이 slot 1,
                // 그보다 오래된 것이 slot 2, ... 가장 오래된 것(0번째)이 slot count가 된다.
                int slot = count - i;
                Vector2 target = anchor.anchoredPosition + new Vector2(0f, slot * slotSpacing);
                active[i].MoveToSlot(target, shiftDuration);
            }
        }

        private void HandleBeginExit(ToastInstance instance)
        {
            active.Remove(instance);
        }

        private void ReturnToPool(ToastInstance instance)
        {
            instance.ResetForPool();
            instance.gameObject.SetActive(false);
            pool.Enqueue(instance);
        }
    }
}
