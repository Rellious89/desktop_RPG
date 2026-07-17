using UnityEngine;

namespace Common
{
    /// <summary>
    /// StageVisualRoot 아래 배치하는 "설계용 기준 영역" - 아무것도 렌더링하지 않는다(SpriteRenderer,
    /// MeshRenderer 등 시각 컴포넌트 없음). Character/Scarecrow의 실제 스프라이트 Bounds를 매 프레임
    /// 계산해서 배치 한계를 정하면, 공격 모션 중 검이 일시적으로 크게 움직이는 것만으로도 배치
    /// 가능 범위가 프레임마다 흔들리는 문제가 생긴다 - 그래서 이 컴포넌트는 디자이너가 Inspector에서
    /// 고정한 "논리 크기"만 들고 있는 순수 데이터 홀더다.
    ///
    /// StageVisualRootController가 이 값(Width/Height)과 SafetyMarginPixels를 읽어 100% 배율 기준
    /// 스테이지 박스 크기로 쓴다. 실제 스케일(사용자 배율)은 StageVisualRootController가 곱해서 적용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class StagePlacementBounds : MonoBehaviour
    {
        [Tooltip("100% 배율 기준 스테이지 박스 논리 너비(px) - 예전 소형 창 크기와 같은 기준.")]
        [SerializeField] private float width = 480f;

        [Tooltip("100% 배율 기준 스테이지 박스 논리 높이(px).")]
        [SerializeField] private float height = 640f;

        [Tooltip("배치 가능 범위를 계산할 때 모니터 Work Area 가장자리로부터 항상 남겨둘 최소 여백(px, 100% 배율 기준) - 화면 끝에 완전히 붙어 이동 핸들에 손이 안 닿는 상황을 막는다.")]
        [SerializeField] private float safetyMarginPixels = 8f;

        public float Width => width;
        public float Height => height;
        public float SafetyMarginPixels => safetyMarginPixels;

#if UNITY_EDITOR
        /// <summary>에디터 Scene 뷰에서만 그려지는 참고용 와이어프레임 - 런타임 렌더링과 무관하다.</summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            // width/height는 픽셀 단위 논리값이라 씬 뷰의 월드 단위와 직접 대응하지 않는다 - 대략적인
            // 비율 참고용으로만 부모 위치에 사각형을 그린다(1 world unit = 100px 가정).
            Vector3 size = new Vector3(width / 100f, height / 100f, 0f);
            Gizmos.DrawWireCube(transform.position, size);
        }
#endif
    }
}
