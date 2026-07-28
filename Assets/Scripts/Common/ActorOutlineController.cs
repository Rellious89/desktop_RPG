using UnityEngine;

namespace Common
{
    /// <summary>
    /// 캐릭터/몬스터 "본체" SpriteRenderer에 외곽선 Material을 적용하는 컴포넌트. 본체에만 붙인다 -
    /// AttackFrameOverlay(캐스팅/잔상), 발사체, 피격 이펙트, 데미지 숫자, UI에는 붙이지 않는다.
    ///
    /// On/Off·색상·두께 값은 이 컴포넌트가 갖지 않는다 - 씬에 하나뿐인
    /// <see cref="ActorOutlineSettings"/>에서 읽어온다. 캐릭터와 몬스터가 전부 같은 설정을 쓰므로
    /// 액터별로 값을 맞출 일이 없고, 전역 설정을 바꾸면 <see cref="ActorOutlineSettings.Changed"/>를
    /// 통해 활성 상태인 모든 액터에 즉시 반영된다. 액터별 Override는 두지 않는다.
    ///
    /// Material 인스턴스를 늘리지 않는 것이 이 컴포넌트의 핵심 제약이다.
    /// - Material 자체는 <b>공유</b>한다(sharedMaterial 대입). renderer.material은 절대 쓰지 않는다 -
    ///   그 프로퍼티를 읽는 순간 캐릭터마다 Material 사본이 생기기 때문이다.
    /// - 색상/두께/On-Off는 <see cref="MaterialPropertyBlock"/>으로 덮어쓴다. 프로퍼티 블록은 렌더러에
    ///   붙는 값이라 Material 사본을 만들지 않는다.
    /// - SpriteRenderer가 내부적으로 넘기는 _RendererColor/_Flip은 엔진 전용 상수 버퍼
    ///   (UnityPerDrawSprite)에 있어서 사용자 프로퍼티 블록과 충돌하지 않는다 - 즉 이 컴포넌트가
    ///   블록을 설정해도 SpriteRenderer.color(Flash/Fade)와 flipX/flipY는 그대로 동작한다.
    ///
    /// 전역 설정이 없거나 꺼져 있거나 Material이 비어 있으면 기록해둔 원래 sharedMaterial로 되돌린다 -
    /// 외곽선을 적용하지 않은 기존 표시와 완전히 동일해진다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [DisallowMultipleComponent]
    public class ActorOutlineController : MonoBehaviour
    {
        // 셰이더 프로퍼티 이름 -> ID. 문자열 해싱을 매번 하지 않도록 정적으로 한 번만 만든다.
        private static readonly int OutlineEnabledId = Shader.PropertyToID("_OutlineEnabled");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

        // 외곽선을 끄거나 전역 설정이 없을 때 되돌아갈 원래 Material(보통 Sprites/Default).
        // 직렬화해서 보관한다 - Edit Mode에서 외곽선을 켜 둔 채 씬을 저장하면 SpriteRenderer에는
        // 외곽선 Material이 저장되므로, 다음 로드 때 "원래 Material"을 다시 알아낼 방법이 없어진다.
        [HideInInspector]
        [SerializeField] private Material capturedOriginalMaterial;

        // 원래 Material을 끝내 알 수 없을 때만 쓰는 최후의 fallback. 프로세스 전체에서 딱 하나만
        // 만들어 공유하므로 캐릭터 수만큼 늘어나지 않는다.
        private static Material sharedDefaultSpriteMaterial;

        private SpriteRenderer spriteRenderer;
        private MaterialPropertyBlock propertyBlock;
        private bool capturedOriginalChecked;

        /// <summary>
        /// 캐시된 참조를 항상 사용 가능한 상태로 맞춘다. 에디터 재컴파일/도메인 리로드 뒤에는 직렬화되지
        /// 않는 필드(spriteRenderer, propertyBlock)가 전부 비워진 채로 OnValidate -> ApplyFromEditor가
        /// 먼저 불릴 수 있으므로, "한 번 초기화했다"는 플래그와 무관하게 매번 null을 확인한다 -
        /// 그러지 않으면 GetPropertyBlock(null)로 예외가 난다.
        /// </summary>
        private void Initialize()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

            // 원본 Material 기록은 딱 한 번만 시도한다(이미 외곽선이 적용된 뒤에 기록하면 안 되므로).
            if (capturedOriginalChecked) return;
            capturedOriginalChecked = true;

            if (capturedOriginalMaterial != null || spriteRenderer == null) return;

            // sharedMaterial을 읽는 것은 사본을 만들지 않는다(material과 다른 점). 지금 붙어 있는 것이
            // 이미 외곽선 Material이면 그것은 "원래" 값이 아니므로 기록하지 않는다.
            Material current = spriteRenderer.sharedMaterial;
            if (current != null && current != ResolveOutlineMaterial())
            {
                capturedOriginalMaterial = current;
            }
        }

        private static Material ResolveOutlineMaterial()
        {
            ActorOutlineSettings settings = ActorOutlineSettings.Active;
            return settings != null ? settings.OutlineMaterial : null;
        }

        /// <summary>외곽선을 껐을 때 되돌릴 Material. 기록해둔 원본이 있으면 그것을 쓰고, 없으면
        /// (외곽선 Material이 이미 저장된 씬을 원본 기록 없이 연 경우) 공용 Sprites/Default 하나를
        /// 만들어 재사용한다.</summary>
        private static Material ResolveRestoreMaterial(Material captured)
        {
            if (captured != null) return captured;

            if (sharedDefaultSpriteMaterial == null)
            {
                Shader spriteShader = Shader.Find("Sprites/Default");
                if (spriteShader == null) return null;
                sharedDefaultSpriteMaterial = new Material(spriteShader) { name = "Sprites/Default (ActorOutline restore)" };
            }
            return sharedDefaultSpriteMaterial;
        }

        private void OnEnable()
        {
            Initialize();
            ActorOutlineSettings.Changed += Apply;
            Apply();
        }

        /// <summary>컴포넌트가 꺼지거나 오브젝트가 비활성화되면(캐릭터 교체 등) 외곽선 적용 이전 상태로
        /// 되돌린다 - 원래 sharedMaterial을 복원하고 덮어써 둔 프로퍼티 블록도 지운다. 다시 켜질 때는
        /// OnEnable의 Apply()가 처음부터 다시 적용하므로 상태가 남지 않는다.
        ///
        /// 여기서는 fallback Material을 새로 만들지 않는다 - OnDisable은 씬 언로드나 도메인 리로드
        /// 중에도 불리는데 그 시점의 오브젝트 생성은 Unity가 정리 누락 경고를 낸다. 기록해둔 원본이
        /// 있을 때만 Material을 되돌리고, 없으면 프로퍼티 블록만 정리한다.</summary>
        private void OnDisable()
        {
            ActorOutlineSettings.Changed -= Apply;

            if (spriteRenderer == null) return;

            if (capturedOriginalMaterial != null && spriteRenderer.sharedMaterial != capturedOriginalMaterial)
            {
                spriteRenderer.sharedMaterial = capturedOriginalMaterial;
            }
            spriteRenderer.SetPropertyBlock(null);
        }

        /// <summary>전역 설정을 다시 읽어 적용한다. Edit 모드에서 <see cref="ActorOutlineSettings"/>가
        /// 씬의 컨트롤러들을 직접 갱신할 때 쓰는 진입점이다(그때는 정적 이벤트 구독자가 없다).</summary>
        public void Refresh()
        {
            Apply();
        }

        /// <summary>전역 설정 값을 렌더러에 반영한다. Material 교체는 sharedMaterial 대입 한 번뿐이고,
        /// 나머지 값은 전부 프로퍼티 블록으로 넘어가므로 액터가 몇이든 Material 사본은 생기지 않는다.</summary>
        private void Apply()
        {
            Initialize();
            if (spriteRenderer == null) return;

            ActorOutlineSettings settings = ActorOutlineSettings.Active;
            Material outlineMaterial = settings != null ? settings.OutlineMaterial : null;
            bool useOutline = settings != null && settings.OutlineEnabled && outlineMaterial != null;

            Material desired = useOutline ? outlineMaterial : ResolveRestoreMaterial(capturedOriginalMaterial);
            if (desired != null && spriteRenderer.sharedMaterial != desired)
            {
                spriteRenderer.sharedMaterial = desired;
            }

            if (!useOutline)
            {
                // 원래 Material로 돌아갈 때는 덮어써 둔 프로퍼티도 같이 지운다 - 기존 표시와
                // 완전히 동일한 상태로 되돌리기 위함이다.
                spriteRenderer.SetPropertyBlock(null);
                return;
            }

            spriteRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(OutlineEnabledId, 1f);
            propertyBlock.SetColor(OutlineColorId, settings.OutlineColor);
            propertyBlock.SetFloat(OutlineWidthId, settings.OutlineWidth);
            spriteRenderer.SetPropertyBlock(propertyBlock);
        }

#if UNITY_EDITOR
        /// <summary>컴포넌트를 붙이거나 되돌린 직후에도 Scene/Game 뷰가 전역 설정을 따라가게 한다.
        /// OnValidate 안에서 곧바로 렌더러를 건드리면 Unity의 직렬화 재진입 경고가 날 수 있어
        /// delayCall로 한 틱 미룬다(TargetCombatController.OnValidate와 같은 패턴) - 그 사이
        /// 오브젝트가 파괴됐을 수 있으므로 실행 시점에 다시 확인한다.</summary>
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += ApplyFromEditor;
        }

        private void ApplyFromEditor()
        {
            if (this == null) return;
            if (!isActiveAndEnabled) return;
            Apply();
        }
#endif
    }
}
