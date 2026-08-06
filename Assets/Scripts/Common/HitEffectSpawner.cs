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
    /// 응답을 늦춰 키 입력 중 마우스가 끊기는 것처럼 보이는 원인 중 하나였다. 한때는 최소 생성 간격
    /// (minSpawnInterval)으로 빈도까지 줄였지만, 그건 빈도만 낮출 뿐 Instantiate/Destroy 자체의 비용은
    /// 그대로 남기면서 "빠른 두 번째 타격은 이펙트가 조용히 사라지는" 연출 손실만 만들었다 - 비용 문제는
    /// DamageNumberSpawner와 동일한 풀링으로 해결됐으므로 그 제한은 걷어냈다. 풀로 돌아올 때마다
    /// 위치/회전/로컬 스케일/알파를 프리팹 고유 기본값으로 되돌려서, 재생 컴포넌트가 없는 prefab을
    /// 나중에 붙이더라도 재사용 사이에 상태가 누적되지 않는다.
    ///
    /// <b>이펙트의 수명은 이 스포너가 정하지 않는다.</b> prefab에 <see cref="IHitEffectPlayback"/>
    /// 구현체가 있으면 그 이펙트가 마지막 프레임까지 재생한 뒤 보내오는 완료 통보를 기다렸다가 회수한다 -
    /// 그래서 6프레임짜리 폭발이든 20프레임짜리 폭발이든 중간에 잘리지 않는다. 예전에는 스포너의
    /// defaultDuration(0.15초)으로 일괄 회수했는데, 그 값은 원래 아트가 없던 시절 HitEffectPop 더미
    /// 연출의 재생 속도를 잡으라고 둔 것이라 실제 스프라이트시트 이펙트의 클립 길이와 맞을 이유가
    /// 없었다(0.5초짜리 폭발이 6프레임 중 2프레임만 나오고 사라지던 원인). 이제 defaultDuration은
    /// 재생 컴포넌트가 아예 없는 prefab에만 쓰는 폴백이다.
    ///
    /// 이전 이펙트가 아직 재생 중인데 다음 이펙트가 생성되는 것은 정상이며 막지 않는다 - 인스턴스가
    /// prefab별로 풀링되므로 겹쳐서 여러 개가 동시에 떠 있어도 서로 간섭하지 않고, 풀이 모자라면 그때만
    /// 추가로 만든다. 겹침이 연출상 어색한지는 작업자가 데이터로 조절할 부분이지 코드가 제한할 부분이 아니다.
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

        [Tooltip("이 대상이 맞을 때 이펙트가 흩어지는 좌우 기본 범위(units) - impactPoint의 스케일이 그대로 " +
                 "반영되므로 화면 고정 오프셋이 아니다. 공격 모션에서 Override Jitter를 켜면 그 공격에는 " +
                 "이 값 대신 공격이 정한 범위가 쓰인다.")]
        [SerializeField] private float spawnJitterX = 0.08f;
        [Tooltip("이 대상이 맞을 때 이펙트가 흩어지는 상하 기본 범위(units) - impactPoint의 스케일이 그대로 " +
                 "반영되므로 화면 고정 오프셋이 아니다. 공격 모션에서 Override Jitter를 켜면 그 공격에는 " +
                 "이 값 대신 공격이 정한 범위가 쓰인다.")]
        [SerializeField] private float spawnJitterY = 0.08f;

        [Tooltip("impactPoint가 비어 있을 때 이 오브젝트 기준으로 사용할 오프셋(월드 유닛).")]
        [SerializeField] private Vector2 fallbackOffset = new Vector2(0f, 0.3f);

        [Header("수명 (폴백 전용)")]
        [Tooltip("IHitEffectPlayback 구현체(HitEffectPop / HitEffectSpriteAnimation 등)가 붙어 있지 않아 " +
                 "재생 종료를 스스로 알리지 못하는 prefab에만 쓰는 회수 시간(초). 재생 컴포넌트가 있는 " +
                 "이펙트는 이 값을 무시하고 자기 클립 길이만큼 온전히 재생한 뒤 사라지므로, 이펙트가 " +
                 "중간에 잘린다면 이 값이 아니라 그 이펙트의 프레임 수/FPS를 확인해야 한다.")]
        [SerializeField] private float defaultDuration = 0.15f;

        [Header("풀")]
        [Tooltip("prewarmEffectPrefab 기준으로 미리 만들어두고 재사용할 이펙트 인스턴스 개수. 연타 중 동시에 재생 중일 수 있는 최대 개수보다 넉넉하게 잡는다.")]
        [SerializeField] private int poolSize = 8;

        private const float FallbackDuration = 0.15f;

        // StageVisualRootController.CombatFxRoot를 그대로 가져와서 쓴다 - 모든 HitEffectSpawner
        // 인스턴스(몬스터마다 하나씩)가 같은 컨테이너를 공유하므로 별도 Inspector 와이어링이 필요 없다.
        private Transform combatFxRoot;

        // prefab별로 별도 풀을 둔다 - 공격 모션마다 다른 Hit Effect prefab이 들어올 수 있어서다.
        // prewarm하지 않은 prefab도 첫 요청 때 풀이 자동으로 만들어진다.
        private readonly Dictionary<GameObject, Queue<GameObject>> poolsByPrefab = new Dictionary<GameObject, Queue<GameObject>>();
        private readonly Dictionary<GameObject, GameObject> prefabByInstance = new Dictionary<GameObject, GameObject>();

        // 프리팹 고유의 기본 로컬 스케일 - 풀 반환 시 이 값으로 되돌려서 재생 컴포넌트가 없는(자체적으로
        // localScale을 매 프레임 재설정하지 않는) prefab이 나중에 붙어도 재사용 사이에 스케일이 누적되지 않는다.
        private readonly Dictionary<GameObject, Vector3> originalLocalScaleByInstance = new Dictionary<GameObject, Vector3>();

        // 완료 통보로 받은 재생 컴포넌트에서 "풀에 등록된 인스턴스 루트"로 되짚기 위한 표. 재생 컴포넌트가
        // 자식에 붙어 있어도 회수 대상은 항상 풀이 알고 있는 루트여야 하므로, 생성 시점에 한 번만 맺어둔다.
        private readonly Dictionary<IHitEffectPlayback, GameObject> instanceByPlayback = new Dictionary<IHitEffectPlayback, GameObject>();

        // 매 Spawn마다 메서드 그룹을 넘기면 그때마다 델리게이트가 새로 할당된다 - 연타 중 GC 압박을
        // 피하려고 풀링까지 도입한 컴포넌트이므로 콜백도 한 번만 만들어 재사용한다.
        // (System을 using하면 이 파일의 Random이 UnityEngine.Random과 모호해지므로 정규화해서 쓴다.)
        private System.Action<IHitEffectPlayback> returnPlaybackToPool;

        private void Awake()
        {
            combatFxRoot = StageVisualRootController.Instance != null ? StageVisualRootController.Instance.CombatFxRoot : null;
            returnPlaybackToPool = ReturnPlaybackToPool;

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

            // 비활성 상태로 대기하므로 includeInactive가 필수다.
            var playback = instance.GetComponentInChildren<IHitEffectPlayback>(true);
            if (playback != null) instanceByPlayback[playback] = instance;

            return instance;
        }

        /// <summary>
        /// prefab이 지정한 이펙트를 생성한다. prefab이 null이면 "이 연출에는 이펙트가 없다"는 뜻이라
        /// 조용히 아무것도 하지 않는다 - 대신 재생할 기본 이펙트는 없다. duration이 비정상(0 이하,
        /// NaN 등)이어도 예외 없이 안전하게 보정한다. 요청은 아무리 촘촘히 들어와도 전부 생성한다 -
        /// 이전 이펙트가 아직 재생 중인지는 보지 않는다(겹치는 연출이 어색한지는 데이터로 조절할 부분이다).
        ///
        /// offsetOverride: impactPoint가 있으면 그 로컬 좌표계 기준(TransformPoint) 추가 오프셋, 없으면
        /// fallbackOffset에 그대로 더하는 월드 오프셋 - 공격 모션 데이터의 Effect Offset을 그대로 전달한다.
        /// scaleOverride: 0 이하면 "지정 안 함"으로 보고 prefab 원본 배율을 그대로 쓴다. 0보다 크면
        /// 재생 컴포넌트가 있는 prefab은 그 재생이 배율에 곱해서 적용하고, 없는 prefab은 인스턴스 원본
        /// 로컬 스케일에 곱해서 즉시 적용한다(둘 다 풀 반환 시 원본 스케일로 복원된다).
        ///
        /// jitterOverride: null이면 이 스포너에 설정된 Spawn Jitter(= 맞는 쪽 덩치에 맞춰 잡아둔 기본
        /// 범위)를 쓰고, 값이 있으면 그 공격이 직접 정한 범위를 쓴다. 0,0도 정당한 값(= 랜덤 없이 정확히
        /// 한 점)이라 "지정 안 함"을 0으로 표현할 수 없어서 Nullable로 받는다.
        ///
        /// durationOverride는 재생 컴포넌트가 없는 prefab에만 의미가 있다 - 있는 이펙트는 자기 길이를
        /// 알고 있으므로 여기서 넘긴 값이 그 재생을 자르거나 늘리지 않는다.
        /// </summary>
        public void Spawn(GameObject prefab, float durationOverride = 0f, Vector2 offsetOverride = default,
            float scaleOverride = 0f, Vector2? jitterOverride = null)
        {
            if (prefab == null) return; // 이 연출에는 이펙트가 없다.

            float duration = durationOverride > 0f ? durationOverride : defaultDuration;
            if (!(duration > 0f) || float.IsNaN(duration) || float.IsInfinity(duration))
            {
                duration = FallbackDuration; // 비정상 duration은 안전한 기본값으로 보정한다.
            }

            // 이 타격에 실제로 쓸 랜덤 범위 - 공격이 직접 정했으면 그 값, 아니면 이 스포너의 기본값.
            Vector2 jitterRange = ResolveJitterRange(jitterOverride);
            Vector3 jitter = new Vector3(
                Random.Range(-jitterRange.x, jitterRange.x),
                Random.Range(-jitterRange.y, jitterRange.y),
                0f);

            Vector3 baseOffset = new Vector3(offsetOverride.x, offsetOverride.y, 0f);
            Vector3 spawnPosition;
            if (impactPoint != null)
            {
                // 지터는 impactPoint의 로컬 좌표로 잡고 TransformPoint로 변환한다 - impactPoint의
                // 현재 스케일/위치가 그대로 반영되므로, Stage 배율이 바뀌거나 StageVisualRoot가
                // 이동해도 지터 범위가 피격체 크기/위치에 비례해서 자연스럽게 따라간다(화면/월드
                // 고정 오프셋이 아니다). offsetOverride도 같은 로컬 좌표계에서 더해진다.
                spawnPosition = impactPoint.TransformPoint(baseOffset + jitter);
            }
            else
            {
                // impactPoint가 없으면 기준이 될 로컬 좌표계 자체가 없으므로 지터도 월드 유닛으로
                // 그대로 더한다 - 이 경로에서는 Stage 배율에 비례하지 않는다.
                spawnPosition = transform.position + (Vector3)fallbackOffset + baseOffset + jitter;
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
            IHitEffectPlayback playback = instance.GetComponentInChildren<IHitEffectPlayback>(true);
            if (playback != null)
            {
                // 수명은 이펙트 자신이 안다 - 스포너는 재생을 시작시키고 완료 통보만 기다린다.
                // Play()가 SetActive(true) 직후 OnEnable이 시작한 기본(Destroy 모드) 재생을 즉시
                // 취소하고 풀 반환 모드로 바꿔치기한다.
                playback.Play(effectiveScale, returnPlaybackToPool);
            }
            else
            {
                // 재생 컴포넌트가 없는 prefab(예: ParticleSystem)은 종료를 스스로 알릴 방법이 없으니
                // 스포너가 직접 타이머로 회수한다 - defaultDuration이 쓰이는 유일한 경로다.
                if (originalLocalScaleByInstance.TryGetValue(instance, out Vector3 baseScale))
                {
                    instanceTransform.localScale = baseScale * effectiveScale;
                }
                StartCoroutine(ReturnToPoolAfterDelay(instance, duration));
            }
        }

        /// <summary>이 스포너에 설정된 기본 랜덤 출력 범위(X/Y 각각 ±값). 공격이 Jitter를 직접 정하지
        /// 않았을 때 쓰이는 값이며, 모션 에디터가 "기본값을 쓰면 이만큼 흩어진다"는 가이드를 그릴 때도
        /// 이 값을 읽는다.</summary>
        public Vector2 DefaultJitterRange => new Vector2(Mathf.Abs(spawnJitterX), Mathf.Abs(spawnJitterY));

        /// <summary>공격이 범위를 직접 정했으면 그 값을, 아니면 이 스포너의 기본값을 쓴다. 음수는
        /// Random.Range(-x, x)에서 의미가 없으므로 절댓값으로 보정한다.</summary>
        private Vector2 ResolveJitterRange(Vector2? jitterOverride)
        {
            if (!jitterOverride.HasValue) return DefaultJitterRange;

            Vector2 range = jitterOverride.Value;
            return new Vector2(Mathf.Abs(range.x), Mathf.Abs(range.y));
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

        /// <summary>이펙트가 "다 재생했다"고 알려올 때 호출된다. 재생 컴포넌트가 자식에 붙어 있을 수도
        /// 있으므로 그 컴포넌트의 gameObject가 아니라 풀에 등록된 인스턴스 루트를 회수한다.</summary>
        private void ReturnPlaybackToPool(IHitEffectPlayback playback)
        {
            if (playback == null) return;
            if (instanceByPlayback.TryGetValue(playback, out GameObject instance))
            {
                ReturnInstanceToPool(instance);
            }
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

            // 재사용 상태를 프리팹 고유 기본값으로 복원한다 - 재생 컴포넌트가 있는 prefab은 Play()가
            // 처음부터 다시 덮어쓰지만, 앞으로 붙을 수 있는 재생 컴포넌트 없는 prefab(예: ParticleSystem)은
            // 스스로 되돌린다는 보장이 없으므로 여기서 한 번 더 명시적으로 리셋해 재사용 사이에
            // 위치/회전/스케일/알파가 누적되지 않게 한다.
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
