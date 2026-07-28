using System.Collections.Generic;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 발사체 인스턴스를 prefab별로 풀링해서 생성/반환하는 컴포넌트. 시전자(캐릭터)에 붙는다.
    ///
    /// 기본 공격 템포가 빠르기 때문에 공격마다 Instantiate/Destroy하지 않는다 - HitEffectSpawner와
    /// 같은 이유(연타 중 GC 압박과 엔진 오브젝트 생성/파괴 비용이 메인 스레드를 순간적으로 멎게 해
    /// 전역 키보드 후크 응답까지 늦어질 수 있다)로 풀링 패턴을 그대로 따르되, 피격 이펙트 풀과는
    /// 완전히 분리된 별도 책임으로 둔다 - 수명 규칙(도착 시 완료)과 상태 복원 대상(위치/회전/스케일/
    /// Sprite/알파/진행도)이 서로 다르기 때문이다.
    ///
    /// 인스턴스는 시전자의 자식이 아니라 StageVisualRootController.CombatFxRoot 아래에 만든다 - 캐릭터의
    /// flipX나 공격 이동(AttackMovement)을 따라가지 않는 중립 컨테이너여야 하고, 동시에 Stage의
    /// 위치/배율은 Transform 계층을 통해 정확히 한 번만 상속받아야 하기 때문이다(HitEffectSpawner와
    /// 동일한 규칙 - 배율 보정 코드를 따로 두지 않는다).
    /// </summary>
    [DisallowMultipleComponent]
    public class ProjectileSpawner : MonoBehaviour
    {
        [Tooltip("어떤 발사체 prefab이 처음 쓰일 때 미리 만들어둘 인스턴스 개수. 연타 중 동시에 날아갈 수 있는 " +
                 "최대 개수보다 넉넉하게 잡는다(부족하면 그때만 예외적으로 추가 생성한다).")]
        [Min(0)]
        [SerializeField] private int prewarmCount = 4;

        // HitEffectSpawner와 같은 컨테이너를 공유한다 - 별도 Inspector 와이어링이 필요 없다.
        private Transform combatFxRoot;

        private readonly Dictionary<GameObject, Queue<ProjectileMover>> poolsByPrefab = new Dictionary<GameObject, Queue<ProjectileMover>>();
        private readonly Dictionary<ProjectileMover, GameObject> prefabByInstance = new Dictionary<ProjectileMover, GameObject>();

        // 프리팹 고유의 기본 로컬 스케일 - Projectile Scale은 이 값에 곱해서 적용하고, 풀 반환 시 다시
        // 이 값으로 되돌린다(재사용 사이에 배율이 누적되지 않는다).
        private readonly Dictionary<ProjectileMover, Vector3> originalLocalScaleByInstance = new Dictionary<ProjectileMover, Vector3>();

        // ProjectileMover가 없어 발사할 수 없다고 이미 판정한 prefab - 연타마다 같은 경고가 쏟아지지 않도록
        // 한 번 걸러낸 뒤로는 조용히 무시한다.
        private readonly HashSet<GameObject> unusablePrefabs = new HashSet<GameObject>();

        // 발사할 때마다 1씩 늘어나는 일련번호. 호출자가 "내가 쏜 그 발사체가 맞는지" 대조하는 데 쓴다.
        private int nextLaunchId = 1;

        private void Awake()
        {
            combatFxRoot = StageVisualRootController.Instance != null ? StageVisualRootController.Instance.CombatFxRoot : null;
        }

        /// <summary>
        /// 발사체를 하나 발사한다. startWorld/targetWorld는 월드 좌표로 받아서 CombatFxRoot 로컬 좌표로
        /// 한 번만 변환한다 - 그 이후 Stage 위치/배율은 Transform 계층이 자동으로 반영하므로 여기서 별도
        /// 보정을 하지 않는다. flightDuration은 공격 모션의 (Hit Frame - Cast Frame) / FPS다.
        /// scaleMultiplier는 prefab 원본 로컬 스케일에 곱해진다(0 이하면 1로 보정).
        ///
        /// prefab이 없거나 ProjectileMover가 붙어 있지 않으면 null을 반환한다 - 호출자는 그저 발사체가
        /// 없는 것으로 취급하면 되고, 공격 자체는 아무 영향도 받지 않는다.
        /// </summary>
        public ProjectileMover Launch(GameObject prefab, Vector3 startWorld, Vector3 targetWorld, float flightDuration, float scaleMultiplier)
        {
            if (prefab == null || unusablePrefabs.Contains(prefab)) return null;

            Queue<ProjectileMover> pool = GetOrCreatePool(prefab);
            // 풀이 비어 있으면(동시에 날아가는 개수가 prewarmCount를 넘어서면) 그때만 예외적으로 새로 만든다.
            ProjectileMover mover = pool.Count > 0 ? pool.Dequeue() : CreatePooledInstance(prefab);
            if (mover == null) return null;

            Transform moverTransform = mover.transform;
            Vector3 startLocal = combatFxRoot != null ? combatFxRoot.InverseTransformPoint(startWorld) : startWorld;
            Vector3 targetLocal = combatFxRoot != null ? combatFxRoot.InverseTransformPoint(targetWorld) : targetWorld;

            float safeScale = scaleMultiplier > 0f && !float.IsNaN(scaleMultiplier) && !float.IsInfinity(scaleMultiplier)
                ? scaleMultiplier
                : 1f;
            if (originalLocalScaleByInstance.TryGetValue(mover, out Vector3 baseScale))
            {
                moverTransform.localScale = baseScale * safeScale;
            }

            moverTransform.localPosition = startLocal;
            mover.gameObject.SetActive(true);
            // Launch가 회전/진행도/시각 상태를 전부 초기화하므로, 활성화 직후 바로 호출해서 이전 비행의
            // 마지막 프레임이 한 프레임이라도 노출되지 않게 한다.
            mover.Launch(startLocal, targetLocal, flightDuration, nextLaunchId++, ReturnToPool);
            return mover;
        }

        private Queue<ProjectileMover> GetOrCreatePool(GameObject prefab)
        {
            if (poolsByPrefab.TryGetValue(prefab, out Queue<ProjectileMover> pool)) return pool;

            pool = new Queue<ProjectileMover>();
            poolsByPrefab[prefab] = pool;

            // 이 prefab을 처음 쓰는 순간에만 미리 채운다 - 어떤 발사체 prefab이 쓰일지는 공격 모션
            // 데이터에 달려 있어서 Awake 시점에는 알 수 없다.
            for (int i = 0; i < prewarmCount; i++)
            {
                ProjectileMover instance = CreatePooledInstance(prefab);
                if (instance == null) break; // ProjectileMover가 없는 prefab - 더 만들어봐야 소용없다.
                pool.Enqueue(instance);
            }
            return pool;
        }

        private ProjectileMover CreatePooledInstance(GameObject prefab)
        {
            GameObject instance = Instantiate(prefab, combatFxRoot);
            var mover = instance.GetComponent<ProjectileMover>();
            if (mover == null)
            {
                if (unusablePrefabs.Add(prefab))
                {
                    Debug.LogWarning($"[ProjectileSpawner] '{prefab.name}' 프리팹 루트에 ProjectileMover가 없어 발사할 수 없습니다.", prefab);
                }
                Destroy(instance);
                return null;
            }

            instance.SetActive(false);
            prefabByInstance[mover] = prefab;
            originalLocalScaleByInstance[mover] = instance.transform.localScale;
            return mover;
        }

        /// <summary>ProjectileMover가 완료/수명 종료 시 호출하는 반환 콜백. 위치/회전/스케일을 프리팹
        /// 기본값으로 되돌린다(Sprite/알파/재생 진행도는 Mover가 IProjectileVisual.ResetVisual로 이미
        /// 되돌린 뒤에 여기로 온다).</summary>
        private void ReturnToPool(ProjectileMover mover)
        {
            if (mover == null) return;

            mover.gameObject.SetActive(false);

            Transform moverTransform = mover.transform;
            moverTransform.localPosition = Vector3.zero;
            moverTransform.localRotation = Quaternion.identity;
            if (originalLocalScaleByInstance.TryGetValue(mover, out Vector3 originalScale))
            {
                moverTransform.localScale = originalScale;
            }

            if (prefabByInstance.TryGetValue(mover, out GameObject prefab))
            {
                GetOrCreatePool(prefab).Enqueue(mover);
            }
        }
    }
}
