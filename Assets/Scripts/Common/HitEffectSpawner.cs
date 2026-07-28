using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Common
{
    /// <summary>
    /// 피격 지점에 이펙트 prefab을 생성하는 재사용 가능한 컴포넌트. Target/DamageNumberSpawner와
    /// 마찬가지로 피격받는 어떤 오브젝트에도 붙여 쓴다.
    ///
    /// <b>어떤 이펙트를 쓸지는 이 컴포넌트가 정하지 않는다.</b> 호출자가 <see cref="Spawn"/>에 prefab을
    /// 직접 넘기고(공격별 Hit Effect는 AttackMotionDefinition이, Cast Effect도 마찬가지다), prefab이
    /// null이면 "이 연출에는 이펙트가 없다"는 뜻이라 조용히 아무것도 만들지 않는다 - 대신 채워 넣는
    /// 기본 이펙트는 없다. 이 컴포넌트가 담당하는 것은 위치 계산·스케일 적용·풀링·수명 관리뿐이다.
    /// prewarmEffectPrefab은 "무엇을 재생할지"가 아니라 "무엇을 미리 만들어둘지"만 정한다.
    ///
    /// 생성된 인스턴스는 StageVisualRootController.CombatFxRoot(StageVisualRoot의 자식) 아래에
    /// 만든다 - 그래야 Transform 계층을 통해 StageVisualRoot의 위치/배율을 정확히 한 번만, 자동으로
    /// 상속받는다(50%에서 이펙트만 상대적으로 크게 보이던 문제의 원인이 바로 이 부모 연결이 없었던
    /// 것이었다). 다만 피격 대상 자신에게는 부모로 붙이지 않는다 - 피격 대상이 이후에 흔들리거나
    /// 이동해도 이펙트가 그 움직임을 따라가지 않도록, 생성 시점 anchor의 월드 위치를 CombatFxRoot
    /// 기준 로컬 좌표로 한 번만 변환해서 스냅샷으로 쓴다.
    ///
    /// 인스턴스를 prefab별로 풀링한다(DamageNumberSpawner와 같은 이유) - 원래 아무 제한 없이 매
    /// 타격마다 새로 Instantiate/Destroy했는데, 연타가 이어지면 그만큼 GC 압박과 엔진 쪽 오브젝트
    /// 생성/파괴 비용이 쌓여 메인 스레드가 순간적으로 멎을 수 있었다 - 이게 전역 키보드 후크/WndProc
    /// 응답을 늦춰 키 입력 중 마우스가 끊기는 것처럼 보이는 원인 중 하나였다. minSpawnInterval
    /// 만으로는 빈도만 줄일 뿐 Instantiate/Destroy 자체의 비용은 그대로 남아서, DamageNumberSpawner와
    /// 동일하게 풀링으로 바꾼다. 이펙트 prefab에 HitEffectPop이 있으면 그 컴포넌트의 재생 완료 콜백을
    /// 통해 풀로 반환하고, 없으면 스포너가 직접 코루틴으로 duration 후 반환한다. 풀로 돌아올 때마다
    /// 위치/회전/로컬 스케일/알파를 프리팹 고유 기본값으로 되돌려서, HitEffectPop이 없는 prefab을
    /// 나중에 붙이더라도 재사용 사이에 상태가 누적되지 않는다.
    /// </summary>
    public class HitEffectSpawner : MonoBehaviour
    {
        [Header("Prewarm (재생 대상이 아니라 미리 만들어둘 prefab)")]
        [Tooltip("Awake에서 Pool Size만큼 미리 Instantiate해둘 이펙트 prefab. 이 대상에게 실제로 가장 " +
                 "자주 날아오는 이펙트(보통 공격 모션들이 공유하는 기본 타격 이펙트)를 넣어두면 첫 타격에 " +
                 "생성 비용이 몰리지 않는다. 비어 있어도 동작에는 지장이 없고(필요할 때 그 자리에서 만든다) " +
                 "Spawn()이 이 prefab을 대신 재생하는 일은 절대 없다.")]
        [FormerlySerializedAs("defaultEffectPrefab")]
        [SerializeField] private GameObject prewarmEffectPrefab;

        [Header("생성 위치")]
        [Tooltip("이펙트가 생성될 실제 지점. 비워두면 이 오브젝트의 Transform 기준 fallbackOffset 위치를 대신 쓴다.")]
        [SerializeField] private Transform impactPoint;

        [Tooltip("impactPoint 로컬 좌표 기준 좌우 랜덤 범위(units) - impactPoint의 스케일이 그대로 반영되므로 화면 고정 오프셋이 아니다.")]
        [SerializeField] private float spawnJitterX = 0.08f;
        [Tooltip("impactPoint 로컬 좌표 기준 상하 랜덤 범위(units) - impactPoint의 스케일이 그대로 반영되므로 화면 고정 오프셋이 아니다.")]
        [SerializeField] private float spawnJitterY = 0.08f;

        [Tooltip("impactPoint가 비어 있을 때 이 오브젝트 기준으로 사용할 오프셋(월드 유닛).")]
        [SerializeField] private Vector2 fallbackOffset = new Vector2(0f, 0.3f);

        [Header("수명")]
        [Tooltip("이펙트 인스턴스를 재생 후 정리하기까지 걸리는 시간(초). 0.1~0.2 권장, 기본값 0.15.")]
        [SerializeField] private float defaultDuration = 0.15f;

        [Header("연타 제한")]
        [Tooltip("이 시간(초)보다 짧은 간격으로는 새 이펙트를 생성하지 않는다. 빠른 연타 시 Instantiate가 과도하게 쌓이는 것을 막는다.")]
        [SerializeField] private float minSpawnInterval = 0.05f;

        [Header("풀")]
        [Tooltip("prewarmEffectPrefab 기준으로 미리 만들어두고 재사용할 이펙트 인스턴스 개수. 연타 중 동시에 재생 중일 수 있는 최대 개수보다 넉넉하게 잡는다.")]
        [SerializeField] private int poolSize = 8;

        private const float FallbackDuration = 0.15f;

        private float lastSpawnTime = -999f;

        // StageVisualRootController.CombatFxRoot를 그대로 가져와서 쓴다 - 모든 HitEffectSpawner
        // 인스턴스(몬스터마다 하나씩)가 같은 컨테이너를 공유하므로 별도 Inspector 와이어링이 필요 없다.
        private Transform combatFxRoot;

        // prefab별로 별도 풀을 둔다 - 공격 모션마다 다른 Hit Effect prefab이 들어올 수 있어서다.
        // prewarm하지 않은 prefab도 첫 요청 때 풀이 자동으로 만들어진다.
        private readonly Dictionary<GameObject, Queue<GameObject>> poolsByPrefab = new Dictionary<GameObject, Queue<GameObject>>();
        private readonly Dictionary<GameObject, GameObject> prefabByInstance = new Dictionary<GameObject, GameObject>();

        // 프리팹 고유의 기본 로컬 스케일 - 풀 반환 시 이 값으로 되돌려서 HitEffectPop이 없는(자체적으로
        // localScale을 매 프레임 재설정하지 않는) prefab이 나중에 붙어도 재사용 사이에 스케일이 누적되지 않는다.
        private readonly Dictionary<GameObject, Vector3> originalLocalScaleByInstance = new Dictionary<GameObject, Vector3>();

        private void Awake()
        {
            combatFxRoot = StageVisualRootController.Instance != null ? StageVisualRootController.Instance.CombatFxRoot : null;

            if (prewarmEffectPrefab == null) return;

            Queue<GameObject> pool = GetOrCreatePool(prewarmEffectPrefab);
            for (int i = 0; i < poolSize; i++)
            {
                pool.Enqueue(CreatePooledInstance(prewarmEffectPrefab));
            }
        }

        private Queue<GameObject> GetOrCreatePool(GameObject prefab)
        {
            if (!poolsByPrefab.TryGetValue(prefab, out Queue<GameObject> pool))
            {
                pool = new Queue<GameObject>();
                poolsByPrefab[prefab] = pool;
            }
            return pool;
        }

        private GameObject CreatePooledInstance(GameObject prefab)
        {
            GameObject instance = Instantiate(prefab, combatFxRoot);
            instance.SetActive(false);
            prefabByInstance[instance] = prefab;
            originalLocalScaleByInstance[instance] = instance.transform.localScale;
            return instance;
        }

        /// <summary>
        /// prefab이 지정한 이펙트를 생성한다. prefab이 null이면 "이 연출에는 이펙트가 없다"는 뜻이라
        /// 조용히 아무것도 하지 않는다 - 대신 재생할 기본 이펙트는 없다. duration이 비정상(0 이하,
        /// NaN 등)이어도 예외 없이 안전하게 보정한다. minSpawnInterval 안에 들어오는 추가 요청은
        /// 조용히 무시한다(데미지/피격 반응 등 다른 처리에는 영향 없음).
        ///
        /// offsetOverride: impactPoint가 있으면 그 로컬 좌표계 기준(TransformPoint) 추가 오프셋, 없으면
        /// fallbackOffset에 그대로 더하는 월드 오프셋 - 공격 모션 데이터의 Effect Offset을 그대로 전달한다.
        /// scaleOverride: 0 이하면 "지정 안 함"으로 보고 prefab 원본 배율을 그대로 쓴다. 0보다 크면
        /// HitEffectPop이 있는 prefab은 그 재생 애니메이션의 배율에 곱해서 적용하고, 없는 prefab은
        /// 인스턴스 원본 로컬 스케일에 곱해서 즉시 적용한다(둘 다 풀 반환 시 원본 스케일로 복원된다).
        /// </summary>
        public void Spawn(GameObject prefab, float durationOverride = 0f, Vector2 offsetOverride = default, float scaleOverride = 0f)
        {
            if (prefab == null) return; // 이 연출에는 이펙트가 없다 - 쿨다운도 소모하지 않는다.
            if (Time.time - lastSpawnTime < minSpawnInterval) return;

            lastSpawnTime = Time.time;

            float duration = durationOverride > 0f ? durationOverride : defaultDuration;
            if (!(duration > 0f) || float.IsNaN(duration) || float.IsInfinity(duration))
            {
                duration = FallbackDuration; // 비정상 duration은 안전한 기본값으로 보정한다.
            }

            Vector3 baseOffset = new Vector3(offsetOverride.x, offsetOverride.y, 0f);
            Vector3 spawnPosition;
            if (impactPoint != null)
            {
                // 지터는 impactPoint의 로컬 좌표로 잡고 TransformPoint로 변환한다 - impactPoint의
                // 현재 스케일/위치가 그대로 반영되므로, Stage 배율이 바뀌거나 StageVisualRoot가
                // 이동해도 지터 범위가 피격체 크기/위치에 비례해서 자연스럽게 따라간다(화면/월드
                // 고정 오프셋이 아니다). offsetOverride도 같은 로컬 좌표계에서 더해진다.
                Vector3 localJitter = new Vector3(
                    Random.Range(-spawnJitterX, spawnJitterX),
                    Random.Range(-spawnJitterY, spawnJitterY),
                    0f);
                spawnPosition = impactPoint.TransformPoint(baseOffset + localJitter);
            }
            else
            {
                spawnPosition = transform.position + (Vector3)fallbackOffset + baseOffset;
            }

            Queue<GameObject> pool = GetOrCreatePool(prefab);
            // 풀이 비어 있으면(동시에 재생 중인 개수가 poolSize를 넘어서거나, prewarm하지 않은 prefab의
            // 첫 요청이면) 그때만 예외적으로 새로 만든다 - prewarm된 prefab은 정상적인 연타 빈도에서
            // poolSize만으로 충분해서 이 경로를 거의 타지 않는다.
            GameObject instance = pool.Count > 0 ? pool.Dequeue() : CreatePooledInstance(prefab);

            // anchor(impactPoint)의 월드 위치를 CombatFxRoot 기준 로컬 좌표로 한 번만 변환한다 -
            // instance가 CombatFxRoot의 자식이므로 그 이후 렌더링 시 StageVisualRoot의 위치/배율이
            // Transform 계층을 통해 자동으로(정확히 한 번) 반영된다. combatFxRoot가 없으면(연결
            // 누락) instance도 부모가 없는 상태이므로 월드 좌표를 그대로 쓴다.
            Transform instanceTransform = instance.transform;
            if (combatFxRoot != null)
            {
                instanceTransform.localPosition = combatFxRoot.InverseTransformPoint(spawnPosition);
                instanceTransform.localRotation = Quaternion.identity;
            }
            else
            {
                instanceTransform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
            }
            instance.SetActive(true);

            float effectiveScale = scaleOverride > 0f ? scaleOverride : 1f;
            var pop = instance.GetComponent<HitEffectPop>();
            if (pop != null)
            {
                // Play()가 SetActive(true) 직후 OnEnable이 시작한 기본(Destroy 모드) 재생을 즉시
                // 취소하고 풀 반환 모드로 바꿔치기한다.
                pop.Play(duration, ReturnToPool, effectiveScale);
            }
            else
            {
                // HitEffectPop이 없는 prefab은 재생 종료를 스스로 알릴 방법이 없으니 스포너가 직접 타이머로 회수한다.
                if (originalLocalScaleByInstance.TryGetValue(instance, out Vector3 baseScale))
                {
                    instanceTransform.localScale = baseScale * effectiveScale;
                }
                StartCoroutine(ReturnToPoolAfterDelay(instance, duration));
            }
        }

        /// <summary>
        /// 이펙트가 생성될 기준 월드 위치를 계산해서 돌려준다(읽기 전용 - 아무것도 생성하지 않고 스포너
        /// 상태도 바꾸지 않는다). 발사체의 도착 지점처럼 "피격 이펙트와 같은 기준점"이 필요한 쪽이
        /// impactPoint 같은 내부 필드에 직접 의존하지 않도록 열어둔 API다.
        ///
        /// offset은 <see cref="Spawn"/>의 offsetOverride와 완전히 같은 규칙으로 해석된다 - impactPoint가
        /// 있으면 그 로컬 좌표계 기준, 없으면 fallbackOffset에 더하는 월드 오프셋이다. 다만 Spawn이
        /// 매번 더하는 랜덤 지터는 반영하지 않는다 - 지터는 피격 이펙트 내부 표현일 뿐이라 발사체가
        /// 조준해야 할 기준점은 흔들리지 않아야 한다.
        /// </summary>
        public Vector3 GetImpactWorldPosition(Vector2 offset = default)
        {
            Vector3 baseOffset = new Vector3(offset.x, offset.y, 0f);
            return impactPoint != null
                ? impactPoint.TransformPoint(baseOffset)
                : transform.position + (Vector3)fallbackOffset + baseOffset;
        }

        private void ReturnToPool(HitEffectPop pop)
        {
            ReturnInstanceToPool(pop.gameObject);
        }

        private IEnumerator ReturnToPoolAfterDelay(GameObject instance, float duration)
        {
            yield return new WaitForSeconds(duration);
            ReturnInstanceToPool(instance);
        }

        private void ReturnInstanceToPool(GameObject instance)
        {
            if (instance == null) return;

            instance.SetActive(false);

            // 재사용 상태를 프리팹 고유 기본값으로 복원한다 - HitEffectPop이 있는 prefab은 재생 중
            // 매 프레임 스스로 되돌리지만(PlayRoutine이 t=0부터 다시 덮어씀), 앞으로 붙을 수 있는
            // HitEffectPop 없는 prefab(예: ParticleSystem)은 스스로 되돌린다는 보장이 없으므로 여기서
            // 한 번 더 명시적으로 리셋해 재사용 사이에 위치/회전/스케일/알파가 누적되지 않게 한다.
            Transform instanceTransform = instance.transform;
            instanceTransform.localPosition = Vector3.zero;
            instanceTransform.localRotation = Quaternion.identity;
            if (originalLocalScaleByInstance.TryGetValue(instance, out Vector3 originalScale))
            {
                instanceTransform.localScale = originalScale;
            }

            var spriteRenderer = instance.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                Color color = spriteRenderer.color;
                color.a = 1f;
                spriteRenderer.color = color;
            }

            if (prefabByInstance.TryGetValue(instance, out GameObject prefab))
            {
                GetOrCreatePool(prefab).Enqueue(instance);
            }
        }
    }
}
