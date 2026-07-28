using System;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 발사체 루트에 붙는 이동 컴포넌트. 시전 위치에서 도착 위치까지 직선으로 이동하고, 이미지의
    /// 오른쪽(+X)이 진행 방향을 향하도록 Z축으로 회전한다(왼쪽 -X가 꼬리). 시각 표현은 전혀 건드리지
    /// 않고 <see cref="IProjectileVisual"/> 구현체들에게 진행도만 넘긴다 - 루트에 SpriteRenderer가 없어도,
    /// 자식이 여러 장이어도 그대로 동작한다.
    ///
    /// 좌표는 전부 부모(보통 StageVisualRootController.CombatFxRoot) 로컬 기준이다 - 발사체는 캐릭터의
    /// 자식이 아니라 전투 이펙트용 중립 컨테이너 아래에서 움직이므로, Stage의 위치/배율은 Transform
    /// 계층을 통해 정확히 한 번만 상속된다(HitEffectSpawner와 같은 규칙).
    ///
    /// 수명: 공격 애니메이션이 Hit Frame에 도달하면 <see cref="ProjectileSpawner"/>를 통하지 않고
    /// 호출자(PlayerCharacterAnimator)가 <see cref="CompleteNow"/>를 불러 목표 위치로 스냅하고 즉시
    /// 완료시킨다 - 프레임 드롭으로 비행이 덜 끝났어도 시각과 피격 타이밍이 어긋나지 않는다. 반대로
    /// 프레임이 밀려 Strike가 늦게 오면 도착 위치에서 arrivalHoldSeconds 동안 기다리고, 그래도 아무도
    /// 완료시키지 않으면(캐릭터 교체 등) 스스로 풀로 돌아가 인스턴스가 새지 않게 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProjectileMover : MonoBehaviour
    {
        [Tooltip("도착 지점에 닿은 뒤 호출자의 완료 처리를 기다리는 최대 시간(초). 이 시간이 지나면 스스로 풀로 " +
                 "돌아간다 - 공격이 중간에 끊겨(캐릭터 비활성화/교체) 완료 신호가 오지 않는 경우의 안전장치다.")]
        [Min(0f)]
        [SerializeField] private float arrivalHoldSeconds = 0.35f;

        private IProjectileVisual[] visuals = Array.Empty<IProjectileVisual>();

        private Vector3 startLocalPosition;
        private Vector3 targetLocalPosition;
        private float flightDuration;
        private float elapsed;
        private Action<ProjectileMover> onRelease;

        /// <summary>발사할 때마다 스포너가 새로 발급하는 일련번호. 호출자는 이 값을 함께 들고 있다가
        /// 완료시킬 때 대조한다 - 그 사이 이 인스턴스가 풀로 돌아가 다른 공격에 재사용됐다면 번호가
        /// 달라져서, 남의 발사체를 실수로 완료시키는 일이 없다.</summary>
        public int LaunchId { get; private set; }

        /// <summary>비행 중이거나 도착 대기 중이면 true. 풀에 들어가 있으면 false.</summary>
        public bool IsLive { get; private set; }

        private void Awake()
        {
            // 루트/자식 어디에 붙어 있든, 비활성 상태여도 전부 찾는다 - 발사체는 풀에서 비활성으로 대기하므로
            // includeInactive가 필수다.
            visuals = GetComponentsInChildren<IProjectileVisual>(true);
        }

        /// <summary>스포너만 호출한다. 좌표는 부모(CombatFxRoot) 로컬 기준이고, flightDuration은 공격
        /// 모션의 (Hit Frame - Cast Frame) / FPS로 계산된 값이다. 0 이하면 즉시 도착으로 처리한다.</summary>
        public void Launch(Vector3 startLocal, Vector3 targetLocal, float duration, int launchId, Action<ProjectileMover> releaseCallback)
        {
            startLocalPosition = startLocal;
            targetLocalPosition = targetLocal;
            flightDuration = duration > 0f && !float.IsNaN(duration) && !float.IsInfinity(duration) ? duration : 0f;
            elapsed = 0f;
            LaunchId = launchId;
            onRelease = releaseCallback;
            IsLive = true;

            ApplyHeadingRotation();

            for (int i = 0; i < visuals.Length; i++)
            {
                visuals[i].BeginFlight(flightDuration);
            }

            ApplyProgress(0f);
        }

        /// <summary>이미지 +X가 진행 방향을 향하도록 Z 회전만 준다. 캐릭터 SpriteRenderer의 flipX는
        /// 상속하지 않는다 - 방향은 오직 "시전 위치 -> 도착 위치" 벡터로만 결정된다. 두 지점이 사실상
        /// 같으면(거리 0) 각도를 구할 수 없으므로 회전을 건드리지 않고 원본 상태를 유지한다.</summary>
        private void ApplyHeadingRotation()
        {
            Vector3 delta = targetLocalPosition - startLocalPosition;
            if (delta.sqrMagnitude <= Mathf.Epsilon)
            {
                transform.localRotation = Quaternion.identity;
                return;
            }

            float angleDegrees = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            transform.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
        }

        private void Update()
        {
            if (!IsLive) return;

            elapsed += Time.deltaTime;

            float progress = flightDuration > 0f ? Mathf.Clamp01(elapsed / flightDuration) : 1f;
            ApplyProgress(progress);

            // 도착했는데도 완료 신호(Strike)가 오지 않으면 잠시 그 자리에서 기다렸다가 스스로 회수된다.
            if (progress >= 1f && elapsed >= flightDuration + arrivalHoldSeconds)
            {
                Release();
            }
        }

        /// <summary>Hit Frame에 호출한다 - 남은 거리와 무관하게 목표 위치로 스냅해 마지막 프레임을 한 번
        /// 보여준 뒤 곧바로 풀로 돌아간다. 피해/피격 이펙트는 기존 HitPoint 흐름이 그대로 담당하므로
        /// 여기서는 아무 판정도 발생시키지 않는다(한 공격에서 피해가 두 번 들어갈 여지가 없다).</summary>
        public void CompleteNow()
        {
            if (!IsLive) return;

            ApplyProgress(1f);
            Release();
        }

        /// <summary>완료 처리 없이 즉시 풀로 반환한다(공격이 중간에 끊긴 경우).</summary>
        public void Release()
        {
            if (!IsLive) return;
            IsLive = false;

            for (int i = 0; i < visuals.Length; i++)
            {
                visuals[i].ResetVisual();
            }

            Action<ProjectileMover> callback = onRelease;
            onRelease = null;
            callback?.Invoke(this);
        }

        private void ApplyProgress(float progress)
        {
            transform.localPosition = Vector3.Lerp(startLocalPosition, targetLocalPosition, progress);

            for (int i = 0; i < visuals.Length; i++)
            {
                visuals[i].SetFlightProgress(progress);
            }
        }
    }
}
