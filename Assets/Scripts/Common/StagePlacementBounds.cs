using UnityEngine;

namespace Common
{
    /// <summary>
    /// StageVisualRoot 아래 배치하는 렌더링하지 않는 기준 데이터다. Width/Height는 기존 저장
    /// 배치값이 참조하는 논리 박스이고, Height는 Stage의 표시 배율을 계산하는 기준이기도 하다.
    /// 평소 이동 한계는 현재 보이는 캐릭터/몬스터 스프라이트 외곽으로 정한다. 해당 액터가 하나도
    /// 없을 때만 이 논리 박스가 이동 한계의 대체값으로 사용된다.
    ///
    /// 스프라이트 외곽은 드래그 시작 시 한 번 측정해서 고정하므로 애니메이션으로 경계가 떨리지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class StagePlacementBounds : MonoBehaviour
    {
        [Tooltip("기존 저장 위치의 기준이 되는 논리 박스 너비(px). 보이는 액터가 없을 때만 이동 한계에도 사용됩니다.")]
        [SerializeField] private float width = 480f;

        [Tooltip("Stage 표시 배율과 기존 저장 위치의 기준이 되는 논리 박스 높이(px). 보이는 액터가 없을 때만 이동 한계에도 사용됩니다. 이 값을 줄이면 캐릭터 표시 크기도 줄어듭니다.")]
        [SerializeField] private float height = 640f;

        [Tooltip("캐릭터/몬스터 외곽과 모니터 Work Area 가장자리 사이에 남길 최소 여백(px). 액터가 없을 때는 논리 박스에 적용됩니다.")]
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
