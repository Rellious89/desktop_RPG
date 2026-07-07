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
            transform.LookAt(bounds.center);
        }
    }
}
