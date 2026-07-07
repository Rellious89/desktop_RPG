using UnityEngine;

namespace DesktopWindow
{
    /// <summary>
    /// target의 렌더러 바운드를 계산해 카메라를 자동으로 프레이밍한다.
    /// 확보되는 대로 더미 에셋을 교체해도 카메라를 수동으로 재조정할 필요가 없도록 하기 위함(에셋 선배치 -> 수치 역산 전략 지원).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class AutoFrameTarget : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float padding = 1.3f;
        [SerializeField] private Vector3 viewDirection = new Vector3(0f, -0.15f, -1f);

        [Tooltip("타겟을 화면 세로축의 어느 높이에 둘지. 0 = 화면 하단, 0.5 = 중앙, 1 = 화면 상단. " +
                 "모니터 전체를 화면으로 쓰면서 하단은 건물, 상단은 UI로 남겨두려면 0에 가깝게 설정.")]
        [SerializeField, Range(0f, 1f)] private float verticalAnchor = 0.5f;

        private Camera cam;

        private void Awake()
        {
            cam = GetComponent<Camera>();
        }

        private void Start()
        {
            FrameTarget();
        }

        [ContextMenu("Frame Target Now")]
        private void FrameTarget()
        {
            if (target == null) return;

            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            float distance = Mathf.Max(bounds.extents.magnitude, 0.01f) * padding;
            Vector3 direction = viewDirection.normalized;

            transform.position = bounds.center - direction * distance;

            // verticalAnchor가 0.5(중앙)에서 벗어난 만큼 LookAt 지점을 위/아래로 옮겨서
            // 타겟이 화면 중앙이 아니라 원하는 높이(예: 하단)에 놓이도록 보정한다.
            float halfHeightAtDistance = distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float verticalOffset = halfHeightAtDistance * (1f - verticalAnchor * 2f);

            transform.LookAt(bounds.center + Vector3.up * verticalOffset);
        }
    }
}
