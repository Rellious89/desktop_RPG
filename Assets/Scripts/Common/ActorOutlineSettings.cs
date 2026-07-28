using System;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 캐릭터/몬스터 외곽선의 전역 설정. 씬에 하나만 두고(StageVisualRoot 권장) 여기서 On/Off, 색상,
    /// 두께를 한 번에 관리한다 - 액터마다 값을 따로 맞출 필요가 없다. 각
    /// <see cref="ActorOutlineController"/>는 이 값을 읽어 자기 SpriteRenderer의 MaterialPropertyBlock에
    /// 적용하며, 캐릭터와 몬스터가 완전히 같은 설정을 쓴다(액터별 Override는 두지 않는다).
    ///
    /// 값이 바뀌면 <see cref="Changed"/>로 알린다 - Play 모드에서 Inspector를 만지면 활성 상태인 모든
    /// 캐릭터와 몬스터에 그 프레임에 바로 반영된다. Edit 모드에서는 컨트롤러의 OnEnable이 돌지 않아
    /// 구독자가 없으므로, OnValidate가 씬의 활성 컨트롤러를 직접 훑어서 갱신한다.
    ///
    /// Material 자체는 여전히 <b>공유</b>다 - 이 컴포넌트가 들고 있는 outlineMaterial 하나를 모든
    /// 액터가 sharedMaterial로 함께 쓰고, 값 차이는 프로퍼티 블록으로만 넘긴다.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public class ActorOutlineSettings : MonoBehaviour
    {
        /// <summary>설정이 바뀌었으니 다시 읽어가라는 신호. 인자는 없다 - 구독자가
        /// <see cref="Active"/>에서 현재 값을 직접 읽는다.</summary>
        public static event Action Changed;

        private static ActorOutlineSettings instance;

        /// <summary>지금 유효한 전역 설정. 씬에 없거나 컴포넌트가 꺼져 있으면 null이고, 그때 각
        /// 컨트롤러는 외곽선을 적용하지 않은 원래 상태로 되돌아간다.</summary>
        public static ActorOutlineSettings Active
        {
            get
            {
                if (instance != null && instance.isActiveAndEnabled) return instance;
#if UNITY_EDITOR
                // Edit 모드에서는 Awake가 돌지 않아 instance가 비어 있다 - Inspector 편집 반영을 위해
                // 그때만 씬에서 한 번 찾아 캐시한다(런타임 매 프레임 경로가 아니라 비용은 무시할 수준).
                if (!Application.isPlaying)
                {
                    instance = FindObjectOfType<ActorOutlineSettings>();
                    if (instance != null && instance.isActiveAndEnabled) return instance;
                }
#endif
                return null;
            }
        }

        [Tooltip("모든 캐릭터/몬스터 본체가 공유할 외곽선 Material. 비워두면 외곽선을 적용하지 않고 " +
                 "기존 Sprite Material 그대로 표시된다.")]
        [SerializeField] private Material outlineMaterial;

        [Tooltip("끄면 모든 액터가 원래 Material로 돌아가 외곽선 적용 전과 완전히 동일하게 표시된다.")]
        [SerializeField] private bool outlineEnabled = true;

        [Tooltip("외곽선 색. 알파는 외곽선 자체의 진하기이며, 각 SpriteRenderer의 전체 알파(처치/리젠 Fade)가 " +
                 "여기에 곱해진다. 밝은 회백색~옅은 하늘색 권장.")]
        [SerializeField] private Color outlineColor = new Color(0.86f, 0.93f, 1f, 0.85f);

        [Tooltip("외곽선 두께(텍스처 픽셀). 1은 3x3, 2는 5x5 주변을 검사한다. Stage 50%에서 1픽셀이 " +
                 "보이지 않으면 2와 비교한다.")]
        [Range(1f, 2f)]
        [SerializeField] private float outlineWidth = 1f;

        public Material OutlineMaterial => outlineMaterial;
        public bool OutlineEnabled => outlineEnabled;
        public Color OutlineColor => outlineColor;
        public float OutlineWidth => Mathf.Clamp(outlineWidth, 1f, 2f);

        private void Awake()
        {
            instance = this;
        }

        private void OnEnable()
        {
            instance = this;
            // 실행 순서상 이미 Apply를 마친 컨트롤러가 있을 수 있으므로(비활성 캐릭터가 먼저 켜지는 등)
            // 켜지는 시점에 한 번 다시 알린다.
            RaiseChanged();
        }

        private void OnDisable()
        {
            RaiseChanged(); // Active가 null이 되므로 각 컨트롤러가 원래 Material로 되돌아간다.
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            RaiseChanged();
        }

        /// <summary>런타임에 외곽선을 켜고 끈다(향후 사용자 설정 UI의 진입점). 색상/두께도 같은 방식으로
        /// 바꿀 수 있으며, 모든 활성 액터에 즉시 반영된다.</summary>
        public void SetOutlineEnabled(bool enabled)
        {
            if (outlineEnabled == enabled) return;
            outlineEnabled = enabled;
            RaiseChanged();
        }

        public void SetOutlineColor(Color color)
        {
            outlineColor = color;
            RaiseChanged();
        }

        public void SetOutlineWidth(float width)
        {
            outlineWidth = Mathf.Clamp(width, 1f, 2f);
            RaiseChanged();
        }

        private static void RaiseChanged()
        {
            Changed?.Invoke();
        }

#if UNITY_EDITOR
        /// <summary>Inspector에서 값을 만지는 즉시 반영한다. OnValidate 안에서 곧바로 렌더러를 건드리면
        /// Unity의 직렬화 재진입 경고가 날 수 있어 delayCall로 한 틱 미룬다(TargetCombatController와 같은
        /// 패턴) - 그 사이 오브젝트가 파괴됐을 수 있어 실행 시점에 다시 확인한다.</summary>
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += NotifyFromEditor;
        }

        private void NotifyFromEditor()
        {
            if (this == null) return;

            RaiseChanged();
            if (Application.isPlaying) return;

            // Edit 모드에서는 컨트롤러의 OnEnable이 돌지 않아 위 이벤트에 구독자가 없다 - 씬의 활성
            // 컨트롤러를 직접 찾아 갱신해야 Scene/Game 뷰에 바로 보인다.
            foreach (ActorOutlineController controller in FindObjectsOfType<ActorOutlineController>())
            {
                controller.Refresh();
            }
        }
#endif
    }
}
