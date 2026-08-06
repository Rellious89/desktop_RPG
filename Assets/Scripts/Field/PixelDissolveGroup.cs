using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Field
{
    /// <summary>
    /// 자기 아래에 있는 <see cref="SpriteRenderer"/>들을 한 덩어리로 묶어 픽셀 디더 디졸브로 사라지게
    /// 하는 연출 단위. 필드 루트(TownFieldRoot/DungeonFieldRoot)에 하나씩 붙이는 것이 기본이고,
    /// 나중에 플레이어나 다른 묶음을 같은 연출에 포함시키고 싶으면 그 오브젝트에 이 컴포넌트를 하나 더
    /// 붙여 <see cref="FieldTransitionSequencer"/>에 연결하기만 하면 된다 - 이 컴포넌트는 자기가 무엇을
    /// 담고 있는지 알 필요가 없다.
    ///
    /// <b>대상은 재생할 때마다 새로 모은다.</b> 던전 몬스터처럼 런타임에 생겼다 사라지는 오브젝트가
    /// 있어서 Awake에 캐시해두면 실제 화면에 있는 것과 어긋난다. 모으는 비용은 전환 순간 한 번뿐이라
    /// 매 프레임 도는 경로가 아니다.
    ///
    /// <b>머티리얼은 재생 시점에 바꿔 끼우고 되돌린다.</b> 그래서 씬이나 프리팹의 SpriteRenderer를
    /// 하나도 손대지 않아도 된다(전부 Sprites-Default를 그대로 쓰면 된다). 진행도는 렌더러마다 달라야
    /// 하므로 머티리얼 인스턴스를 만들지 않고 <see cref="MaterialPropertyBlock"/>으로 먹인다 - 인스턴스를
    /// 만들면 렌더러 수만큼 머티리얼이 새고, 이 프로젝트는 그런 누수에 민감한 이력이 있다.
    ///
    /// <b>다시 켜지면 스스로 되돌린다.</b> 디졸브가 끝난 루트는 곧바로 SetActive(false)되는데, 나중에
    /// 그 루트가 다시 켜졌을 때 사라진 상태로 남아 있으면 필드가 통째로 보이지 않는다. OnEnable에서
    /// 무조건 원상복구하므로 이 사고가 구조적으로 일어나지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PixelDissolveGroup : MonoBehaviour
    {
        private static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");
        private static readonly int DitherTexelsId = Shader.PropertyToID("_DitherTexels");
        private static readonly int DitherCellTexelsId = Shader.PropertyToID("_DitherCellTexels");

        [Tooltip("픽셀 디더 디졸브 머티리얼(KeyBuddy/PixelDitherDissolve). 비어 있으면 이 그룹은 " +
                 "연출 없이 즉시 완료 처리된다 - 전환 자체가 막히지는 않는다.")]
        [SerializeField] private Material dissolveMaterial;

        [Tooltip("이 그룹이 완전히 사라지는 데 걸리는 시간(초). 오브젝트별 시차를 포함한 전체 길이가 아니라 " +
                 "오브젝트 하나가 사라지는 데 걸리는 시간이다.")]
        [Min(0.01f)]
        [SerializeField] private float dissolveDuration = 0.35f;

        [Tooltip("사라질 때 빠지는 픽셀 한 칸의 크기(월드 유닛). 스프라이트마다 PPU가 달라도 " +
                 "화면에서 같은 굵기로 보이도록 이 값에 각 스프라이트의 PPU를 곱해 텍셀 수로 환산한다 - " +
                 "PPU 200인 캐릭터와 PPU 32인 마을 프롭이 한 화면에 있어도 디더 굵기가 어긋나지 않는다.\n\n" +
                 "0으로 두면 환산하지 않고 각 스프라이트의 원본 도트 1픽셀을 그대로 한 칸으로 쓴다 " +
                 "(PPU가 높은 아트에서는 눈에 안 보일 만큼 잘아진다).")]
        [Min(0f)]
        [SerializeField] private float dissolvePixelWorldSize = 0.03f;

        [Tooltip("오브젝트마다 시작 시점을 어긋나게 하는 최대 시차(초). 0이면 전부 동시에 사라진다. " +
                 "값을 주면 하나씩 순서대로 흩어져 '세계가 걷힌다'는 인상이 생긴다.")]
        [Min(0f)]
        [SerializeField] private float perObjectStagger = 0.08f;

        [Tooltip("시차 순서를 화면 왼쪽부터 줄지, 오른쪽부터 줄지. 끄면 계층 순서를 그대로 쓴다.")]
        [SerializeField] private bool staggerByScreenX = true;

        [Tooltip("켜면 왼쪽 오브젝트부터 사라진다. staggerByScreenX가 꺼져 있으면 의미가 없다.")]
        [SerializeField] private bool staggerLeftToRight = true;

        // 재생 중에만 채워지는 작업 목록 - 머티리얼을 되돌리려면 원본을 들고 있어야 한다.
        private readonly List<SpriteRenderer> targets = new List<SpriteRenderer>();
        private readonly List<Material> originalMaterials = new List<Material>();
        private readonly List<float> startDelays = new List<float>();

        private MaterialPropertyBlock propertyBlock;
        private Coroutine playRoutine;

        /// <summary>이 그룹이 사라지는 데 실제로 걸리는 전체 시간(초) - 마지막으로 시작하는 오브젝트가
        /// 끝나는 시점까지다. 시퀀서가 전체 연출 길이를 가늠할 때 쓴다.</summary>
        public float TotalDuration => dissolveDuration + perObjectStagger;

        private void OnEnable()
        {
            // 사라진 채로 다시 등장하는 사고를 구조적으로 막는다.
            ResetImmediate();
        }

        private void OnDisable()
        {
            // 코루틴은 비활성화와 함께 멈추므로 완료 콜백이 오지 않는다 - 상태만 정리해둔다.
            playRoutine = null;
        }

        /// <summary>
        /// 이 그룹을 디졸브로 사라지게 한다. 재생이 끝나면 onComplete를 부른다 - 대상이 하나도 없거나
        /// 머티리얼이 비어 있으면 <b>같은 프레임에</b> 부른다(전환이 멈추지 않게 하기 위해서다).
        ///
        /// 재생이 끝난 뒤에도 머티리얼은 바뀐 채로 남는다 - 곧바로 루트가 꺼지고, 다시 켜질 때
        /// <see cref="OnEnable"/>이 원상복구하기 때문이다. 중간에 다시 부르면 이전 재생은 취소된다.
        /// </summary>
        public void PlayDissolveOut(Action onComplete)
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }

            CollectTargets();

            if (dissolveMaterial == null || targets.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            playRoutine = StartCoroutine(DissolveRoutine(onComplete));
        }

        /// <summary>디졸브 상태를 전부 지우고 원래 머티리얼로 되돌린다. 재생 중이었다면 그 자리에서 멈춘다
        /// (완료 콜백은 오지 않는다 - 취소는 완료가 아니다).</summary>
        public void ResetImmediate()
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }

            RestoreMaterials();
        }

        private IEnumerator DissolveRoutine(Action onComplete)
        {
            // 원본을 기억해두고 디졸브 머티리얼로 바꿔 끼운다.
            originalMaterials.Clear();
            for (int i = 0; i < targets.Count; i++)
            {
                originalMaterials.Add(targets[i].sharedMaterial);
                targets[i].sharedMaterial = dissolveMaterial;
                ApplyDitherGrid(targets[i]);
                SetDissolve(targets[i], 0f);
            }

            float elapsed = 0f;
            float total = TotalDuration;

            while (elapsed < total)
            {
                elapsed += Time.deltaTime;

                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] == null) continue;

                    float local = (elapsed - startDelays[i]) / dissolveDuration;
                    SetDissolve(targets[i], Mathf.Clamp01(local));
                }

                yield return null;
            }

            // 마지막 프레임에 확실히 전부 사라진 상태로 고정한다 - 누산 오차로 한 픽셀이 남지 않게.
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null) SetDissolve(targets[i], 1f);
            }

            playRoutine = null;
            onComplete?.Invoke();
        }

        /// <summary>이 렌더러가 지금 쓰는 Sprite에서 디더 격자 정보를 읽어 셰이더에 넣는다.
        ///
        /// 텍스처 크기를 <b>여기서</b> 넘기는 이유: 셰이더의 _MainTex는 [PerRendererData]라 텍스처를
        /// SpriteRenderer가 넣어주는데, 그 경로에서는 Unity가 _MainTex_TexelSize를 갱신하지 않는다.
        /// 셰이더에서 그 값을 믿으면 머티리얼에 꽂힌 텍스처(여기서는 없음) 기준의 엉뚱한 크기가
        /// 들어와서, 스프라이트 하나가 몇 개의 거대한 블록으로 쪼개진다.
        ///
        /// 칸 크기를 PPU로 환산하는 이유: 아트마다 PPU가 다르면(이 프로젝트는 캐릭터 200,
        /// 마을 프롭 32) "원본 도트 1픽셀"의 실제 크기가 6배 넘게 차이 나서, 같은 화면에서 한쪽은
        /// 보이지도 않고 다른 쪽은 뭉텅뭉텅 사라진다. 월드 유닛으로 칸 크기를 정하고 각자의 PPU를
        /// 곱해 텍셀 수로 바꾸면 화면에서 같은 굵기로 보인다.</summary>
        private void ApplyDitherGrid(SpriteRenderer renderer)
        {
            Sprite sprite = renderer.sprite;
            Texture texture = sprite != null ? sprite.texture : null;
            if (texture == null) return;

            propertyBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock);

            propertyBlock.SetVector(DitherTexelsId, new Vector4(texture.width, texture.height, 0f, 0f));

            // 0 = 환산하지 않음(원본 도트 1픽셀 = 한 칸).
            float cellTexels = dissolvePixelWorldSize > 0f
                ? Mathf.Max(1f, Mathf.Round(dissolvePixelWorldSize * sprite.pixelsPerUnit))
                : 1f;
            propertyBlock.SetFloat(DitherCellTexelsId, cellTexels);

            renderer.SetPropertyBlock(propertyBlock);
        }

        private void SetDissolve(SpriteRenderer renderer, float amount)
        {
            // OnEnable이 Awake보다 먼저 도는 경로는 없지만, 초기화 순서에 기대지 않고 여기서 만든다.
            propertyBlock ??= new MaterialPropertyBlock();

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(DissolveAmountId, amount);
            renderer.SetPropertyBlock(propertyBlock);
        }

        /// <summary>지금 이 루트 아래에 실제로 있는 SpriteRenderer를 모으고 오브젝트별 시작 시차를 정한다.
        /// 꺼져 있는 오브젝트는 화면에 보이지 않으므로 대상에서 뺀다(includeInactive를 쓰지 않는다).</summary>
        private void CollectTargets()
        {
            targets.Clear();
            startDelays.Clear();
            GetComponentsInChildren(false, targets);
            if (targets.Count == 0) return;

            if (perObjectStagger <= 0f)
            {
                for (int i = 0; i < targets.Count; i++) startDelays.Add(0f);
                return;
            }

            if (!staggerByScreenX)
            {
                // 계층 순서를 그대로 쓴다 - 오브젝트가 하나면 시차가 0이 되도록 나눈다.
                float step = targets.Count > 1 ? perObjectStagger / (targets.Count - 1) : 0f;
                for (int i = 0; i < targets.Count; i++) startDelays.Add(i * step);
                return;
            }

            // 화면 X 기준으로 한쪽 끝부터 순서대로 흩어지게 한다. 가장 왼쪽/오른쪽을 0과
            // perObjectStagger에 맞추므로, 오브젝트가 몇 개든 전체 시차 길이는 일정하다.
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            for (int i = 0; i < targets.Count; i++)
            {
                float x = targets[i].transform.position.x;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
            }

            float span = maxX - minX;
            for (int i = 0; i < targets.Count; i++)
            {
                float t = span > Mathf.Epsilon ? (targets[i].transform.position.x - minX) / span : 0f;
                if (!staggerLeftToRight) t = 1f - t;
                startDelays.Add(t * perObjectStagger);
            }
        }

        private void RestoreMaterials()
        {
            for (int i = 0; i < targets.Count && i < originalMaterials.Count; i++)
            {
                if (targets[i] == null) continue;

                targets[i].sharedMaterial = originalMaterials[i];
                // 디졸브 값도 지운다 - 원래 머티리얼은 이 프로퍼티를 무시하지만, 다음에 다시
                // 디졸브 머티리얼이 끼워졌을 때 옛 값이 한 프레임 보이는 경로를 없앤다.
                SetDissolve(targets[i], 0f);
            }

            targets.Clear();
            originalMaterials.Clear();
            startDelays.Clear();
        }
    }
}
