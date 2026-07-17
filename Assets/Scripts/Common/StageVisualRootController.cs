using UnityEngine;

namespace Common
{
    /// <summary>
    /// StageVisualRoot(캐릭터/적/이펙트를 담는 부모 오브젝트)가 화면의 어디에, 얼마나 크게 보이는지를
    /// 관리한다. StageVisualRoot의 자식들(Character/Scarecrow/ImpactPoint 등)의 로컬 좌표는 절대
    /// 건드리지 않는다 - 공격 판정 거리, 이펙트 스폰 오프셋 등 기존 전투 로직이 전부 그 좌표들을
    /// 기준으로 authoring돼 있어서, 값을 바꾸면 화면 호스트 구조와 무관한 게임플레이 수치가 같이
    /// 흔들릴 위험이 있다.
    ///
    /// 이 트랜스폼(StageVisualRoot) 자신의 월드 position/localScale만 조정해서 화면상 위치/크기를
    /// 옮긴다 - 자식들은 로컬 좌표 그대로 부모를 따라간다.
    ///
    /// stageCamera(Main Camera)의 Viewport Rect는 항상 전체 화면(0,0,1,1)으로 고정한다 - 절대 줄이지
    /// 않는다(Camera.rect를 줄이면 그 밖의 영역이 매 프레임 클리어되지 않아 잔상이 남는 문제가 있었다).
    ///
    /// 배치 가능 범위는 카메라 여백이 아니라 placementBounds(StagePlacementBounds, 렌더링하지 않는
    /// 순수 데이터 홀더)의 논리 크기 + safetyMargin으로 계산한다 - 실제 스프라이트 Bounds를 매 프레임
    /// 재는 방식은 공격 모션 중 검이 크게 움직이는 것만으로 배치 한계가 흔들리는 문제가 있어 쓰지 않는다.
    ///
    /// 계산 방식: orthographic 카메라는 화면 세로 방향에 worldUnitsPerPixel = 2*orthographicSize /
    /// 화면 높이(px) 비율을 항상 유지한다(가로도 마찬가지 - aspect가 상쇄되어 세로와 같은 비율이 됨).
    /// placementBounds.Height(예전 소형 창 시절 기준 픽셀 높이)가 현재 모니터 Work Area 높이에서
    /// 몇 %를 차지해야 하는지로 스케일 배율을 구하고(모니터가 커질수록 같은 논리 크기가 화면에서
    /// 차지하는 비율이 작아지므로 그만큼 StageVisualRoot를 확대해서 "예전과 비슷한 체감 크기"를
    /// 유지한다), 그 배율이 반영된 스테이지 박스를 화면 우측/하단 여백(정규화 비율)에 맞춰 배치할
    /// 목표 화면 좌표를 구한 뒤, 카메라의 화면-월드 변환식을 역으로 풀어 StageVisualRoot의 월드
    /// position을 계산한다. targetScreenX/Y와 스테이지 박스 픽셀 크기는 ApplyPlacement가 계산할
    /// 때마다 캐싱해두고, TryGetUnityScreenRect는 그 캐시를 그대로 돌려준다(매 프레임 재계산 없음).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class StageVisualRootController : MonoBehaviour, ILayoutDraggable
    {
        public static StageVisualRootController Instance { get; private set; }

        public const string Id = "stage";

        [Header("Stage Camera (항상 전체 화면 Viewport로 고정된다)")]
        [Tooltip("비워두면 Camera.main을 사용한다.")]
        [SerializeField] private Camera stageCamera;

        [Header("배치 범위 기준 (렌더링하지 않는 데이터 홀더)")]
        [Tooltip("StageVisualRoot의 자식으로 둔 StagePlacementBounds. 비워두면 GetComponentInChildren로 찾는다.")]
        [SerializeField] private StagePlacementBounds placementBounds;

        [Header("100% 기준 시각 크기")]
        [Tooltip("StageVisualRoot(캐릭터/적/이펙트)가 실제로 렌더링되는 크기에만 곱해지는 배율이다 - " +
            "StagePlacementBounds(배치/클램프용 footprint)는 건드리지 않으므로 이 값을 바꿔도 배치 가능 " +
            "범위나 드래그 히트 영역은 변하지 않는다. tgl_size의 50/100/150은 이 값(=userScale 1일 때의 " +
            "기준)에 각각 0.5/1.0/1.5를 곱해 적용된다. 기본값 1은 기존 계산식과 동일한 크기이므로, " +
            "실제로 얼마나 키울지는 Play 모드/Windows 빌드에서 눈으로 보며 조정해야 한다.")]
        [SerializeField] private float baseVisualScale = 1f;

        [Header("기본 배치 (화면 우측 하단 기준 여백, 0~1 비율)")]
        [SerializeField] [Range(0f, 1f)] private float defaultRightMarginFraction = 0.02f;
        [SerializeField] [Range(0f, 1f)] private float defaultBottomMarginFraction = 0.02f;

        [Header("Layout Mode 시각 피드백 (선택)")]
        [Tooltip("Layout Mode 중 강조 표시할 대상(선택) - 예: 반투명 외곽선 스프라이트를 담은 자식 오브젝트. 비워두면 로그만 남긴다.")]
        [SerializeField] private GameObject highlightVisual;

        [Header("전투 연출")]
        [Tooltip("타격 이펙트 등 전투 연출 인스턴스를 담는 공용 컨테이너(StageVisualRoot의 자식이어야 " +
            "한다). HitEffectSpawner 등이 이 Transform 아래에 생성/풀링해서 StageVisualRoot의 위치/배율을 " +
            "정확히 한 번만(Transform 계층을 통해 자동으로) 상속받는다 - 별도 스케일 보정 코드를 두지 않는다.")]
        [SerializeField] private Transform combatFxRoot;

        public string GroupId => Id;

        public Transform CombatFxRoot => combatFxRoot;

        private float userScale = 1f;
        private float rightMarginFraction;
        private float bottomMarginFraction;

        // 아직 모니터 Work Area 정보를 받기 전에도 계산이 극단적인 값이 되지 않도록 쓰는 잠정
        // 기본값. TransparentWindowController.ApplyStartupPlacement가 실제 값을 곧 밀어준다.
        private int workAreaWidth = 1920;
        private int workAreaHeight = 1080;

        // ApplyPlacement가 계산할 때마다 캐싱하는 스테이지 박스의 Unity 스크린 좌표(좌하단 원점).
        // TryGetUnityScreenRect가 매 프레임 재계산 없이 그대로 돌려준다.
        private Rect cachedScreenRect;
        private bool hasCachedScreenRect;

        private void Awake()
        {
            Instance = this;

            if (stageCamera == null)
            {
                stageCamera = Camera.main;
            }

            if (stageCamera != null)
            {
                stageCamera.rect = new Rect(0f, 0f, 1f, 1f);
            }

            if (placementBounds == null)
            {
                placementBounds = GetComponentInChildren<StagePlacementBounds>();
            }

            if (placementBounds == null)
            {
                Debug.LogError("[StageVisualRootController] StagePlacementBounds가 할당되지 않았습니다 - StageVisualRoot 아래에 배치하고 Inspector에 연결해주세요.");
            }

            if (combatFxRoot == null)
            {
                Debug.LogError("[StageVisualRootController] combatFxRoot가 할당되지 않았습니다 - StageVisualRoot 아래에 CombatFxRoot를 만들고 Inspector에 연결해주세요. 연결되지 않으면 전투 이펙트가 Stage 배율을 상속받지 못합니다.");
            }

            rightMarginFraction = defaultRightMarginFraction;
            bottomMarginFraction = defaultBottomMarginFraction;

            if (highlightVisual != null) highlightVisual.SetActive(false);

            ApplyPlacement();
        }

        /// <summary>SizeToggleButton(tgl_size)이 호출하는 진입점 - StageVisualRoot가 화면에서
        /// 차지하는 시각적 크기만 바꾼다(네이티브 창/카메라 Viewport는 무관).</summary>
        public void SetUserScale(float scale)
        {
            if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale)) return;
            userScale = scale;
            ApplyPlacement();
        }

        public void NotifyWorkAreaChanged(int widthPixels, int heightPixels)
        {
            if (widthPixels <= 0 || heightPixels <= 0) return;
            workAreaWidth = widthPixels;
            workAreaHeight = heightPixels;
            ApplyPlacement();
        }

        public void SetPlacement(float rightMargin, float bottomMargin)
        {
            rightMarginFraction = Mathf.Clamp01(rightMargin);
            bottomMarginFraction = Mathf.Clamp01(bottomMargin);
            ApplyPlacement();
        }

        public void ResetToDefaultPlacement()
        {
            SetPlacement(defaultRightMarginFraction, defaultBottomMarginFraction);
        }

        public (float rightMarginFraction, float bottomMarginFraction) GetPlacement()
        {
            return (rightMarginFraction, bottomMarginFraction);
        }

        public void ApplyDragDeltaPixels(int deltaXPixels, int deltaYPixels)
        {
            if (workAreaWidth <= 0 || workAreaHeight <= 0) return;

            // 여백은 "가장자리로부터의 거리"라 오른쪽/아래로 끌수록(델타 양수) 줄어드는 방향으로 적용된다.
            rightMarginFraction = Mathf.Clamp01(rightMarginFraction - (float)deltaXPixels / workAreaWidth);
            bottomMarginFraction = Mathf.Clamp01(bottomMarginFraction - (float)deltaYPixels / workAreaHeight);
            ApplyPlacement();
        }

        public bool TryGetUnityScreenRect(out Rect unityScreenRect)
        {
            unityScreenRect = cachedScreenRect;
            return hasCachedScreenRect;
        }

        public void SetLayoutModeActive(bool active)
        {
            if (highlightVisual != null)
            {
                highlightVisual.SetActive(active);
            }
            else
            {
                Debug.Log($"[StageVisualRootController] Layout Mode {(active ? "진입" : "종료")} - highlightVisual이 연결돼 있지 않아 시각 피드백 없이 동작합니다.");
            }
        }

        public string GetDebugState()
        {
            return $"rect={cachedScreenRect} hasRect={hasCachedScreenRect} placementBoundsAssigned={placementBounds != null}";
        }

        private void ApplyPlacement()
        {
            if (stageCamera == null || placementBounds == null || workAreaWidth <= 0 || workAreaHeight <= 0 || !stageCamera.orthographic)
            {
                hasCachedScreenRect = false;
                return;
            }

            // 모니터가 커질수록 같은 월드 크기가 차지하는 화면 비율이 작아지므로, 기준 높이가 항상
            // "화면의 몇 %"를 차지하도록 역산해서 스케일을 구한다 - 그래야 해상도가 달라져도 체감
            // 크기가 비슷하게 유지된다. baseVisualScale은 이 비율 자체에 곱하는 순수 배율이라
            // workAreaHeight/DPI 보정과 중복되지 않는다(아래 stageWidthPixels/stageHeightPixels,
            // 즉 배치 footprint 계산에는 곱하지 않으므로 클램프 범위는 그대로 유지된다).
            float scaleFactor = Mathf.Max(0.01f, placementBounds.Height * baseVisualScale * userScale / workAreaHeight);
            transform.localScale = new Vector3(scaleFactor, scaleFactor, transform.localScale.z);

            float stageWidthPixels = placementBounds.Width * userScale;
            float stageHeightPixels = placementBounds.Height * userScale;
            float safetyMargin = placementBounds.SafetyMarginPixels;

            float maxRightMarginPixels = Mathf.Max(safetyMargin, workAreaWidth - stageWidthPixels - safetyMargin);
            float maxBottomMarginPixels = Mathf.Max(safetyMargin, workAreaHeight - stageHeightPixels - safetyMargin);
            float rightMarginPixels = Mathf.Clamp(rightMarginFraction * workAreaWidth, safetyMargin, maxRightMarginPixels);
            float bottomMarginPixels = Mathf.Clamp(bottomMarginFraction * workAreaHeight, safetyMargin, maxBottomMarginPixels);

            // Unity 화면 좌표계는 (0,0)이 좌하단이다. Stage 박스의 "중심"이 목표 화면 좌표에 오도록
            // 계산한다(StageVisualRoot 로컬 원점이 대략 그 중심 근방에 authoring돼 있다는 전제 -
            // Character/Scarecrow가 원점 부근에 배치돼 있음).
            float targetScreenX = workAreaWidth - rightMarginPixels - stageWidthPixels / 2f;
            float targetScreenY = bottomMarginPixels + stageHeightPixels / 2f;

            float worldUnitsPerPixel = 2f * stageCamera.orthographicSize / workAreaHeight;

            Vector3 cameraPosition = stageCamera.transform.position;
            float worldX = cameraPosition.x + (targetScreenX - workAreaWidth / 2f) * worldUnitsPerPixel;
            float worldY = cameraPosition.y + (targetScreenY - workAreaHeight / 2f) * worldUnitsPerPixel;

            transform.position = new Vector3(worldX, worldY, transform.position.z);

            cachedScreenRect = new Rect(
                targetScreenX - stageWidthPixels / 2f,
                targetScreenY - stageHeightPixels / 2f,
                stageWidthPixels,
                stageHeightPixels);
            hasCachedScreenRect = true;
        }
    }
}
