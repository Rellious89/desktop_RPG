using System;
using System.Collections;
using Character;
using UnityEngine;

namespace Field
{
    /// <summary>
    /// 필드에 도착한 플레이어 캐릭터가 <b>머리 위에서 뚝 떨어지며</b> 등장하는 연출. 캐릭터는 슬롯에
    /// 고정돼 있어서 그냥 나타나면 어색한데, 낙하 한 번이면 "다른 곳에서 이리로 왔다"가 성립한다.
    ///
    /// <b>Transform 소유권</b>: 평소 캐릭터의 위치는 <see cref="AttackMovement"/>가 소유한다(매 프레임
    /// basePosition + 공격 오프셋으로 덮어쓴다). 그래서 이 연출은 반드시 전투가 꺼진 상태에서만
    /// 돌아야 한다 - <see cref="PlayerCharacterAnimator.SetCombatEnabled"/>(false)가 진행 중이던 이동을
    /// 취소하면 AttackMovement는 mode가 None이 되어 Update에서 즉시 빠져나가므로 위치를 건드리지 않는다.
    /// 이 순서는 <see cref="FieldTransitionSequencer"/>가 보장한다.
    ///
    /// 착지 지점은 따로 계산하지 않고 <b>연출 시작 시점의 현재 위치</b>를 그대로 쓴다. 전투를 끄면
    /// AttackMovement가 기준점으로 되돌려놓기 때문에, 그 순간의 localPosition이 곧 이 캐릭터의 정위치다 -
    /// Stage Layout이나 Actor Offset 계산식을 여기서 다시 구현하지 않아도 되고, 그래서 배치 규칙이
    /// 바뀌어도 이 파일은 따라 고칠 것이 없다.
    ///
    /// 낙하가 끝나면 <see cref="AttackMovement.SetPresentationBasePosition"/>으로 착지 위치를 기준점으로
    /// 확정한다 - 그러지 않으면 다음 공격이 낙하 중 위치를 기준으로 삼아 튈 수 있다.
    ///
    /// 포탈 이펙트는 아직 없다. 나중에 붙일 때는 낙하 시작 지점(착지 위치 + dropHeight)에 이펙트를
    /// 하나 재생하고 이 연출을 그대로 이어서 돌리면 되므로, 이 파일의 구조를 바꿀 필요가 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterDropIn : MonoBehaviour
    {
        [Tooltip("이 연출이 움직일 캐릭터의 이동 컨트롤러. 낙하가 끝나면 착지 위치를 기준점으로 확정하는 데도 쓴다. " +
                 "비어 있으면 연출을 건너뛴다(전환 자체는 막지 않는다).")]
        [SerializeField] private AttackMovement attackMovement;

        [Tooltip("착지 지점에서 얼마나 위에서 떨어질지(로컬 유닛). 캐릭터 키를 기준으로 1~2배 정도가 무난하다.")]
        [Min(0f)]
        [SerializeField] private float dropHeight = 2.5f;

        [Tooltip("낙하에 걸리는 시간(초). 길면 등장이 굼떠 보이고, 던전을 자주 들락거리는 앱이라 짧게 잡는다.")]
        [Min(0.01f)]
        [SerializeField] private float dropDuration = 0.28f;

        [Tooltip("낙하 가속 곡선. 위로 볼록할수록 마지막에 '뚝' 떨어지는 느낌이 강해진다 - 중력처럼 " +
                 "가속시키려면 아래로 볼록한 In 계열(기본값)을 쓴다.")]
        [SerializeField] private AnimationCurve dropCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(1f, 1f, 2f, 0f));

        [Tooltip("착지 직후 살짝 눌렸다 펴지는 시간(초). 0이면 스쿼시 없이 곧바로 끝난다.")]
        [Min(0f)]
        [SerializeField] private float squashDuration = 0.09f;

        [Tooltip("착지 순간 세로로 얼마나 눌릴지(1 = 안 눌림, 0.8 = 20% 납작).")]
        [Range(0.5f, 1f)]
        [SerializeField] private float squashScaleY = 0.82f;

        private Coroutine playRoutine;

        /// <summary>이 연출에 걸리는 전체 시간(초).</summary>
        public float TotalDuration => dropDuration + squashDuration;

        private void OnDisable()
        {
            // 코루틴은 비활성화와 함께 멈춘다 - 완료 콜백이 오지 않으므로 상태만 정리한다.
            playRoutine = null;
        }

        /// <summary>
        /// 낙하 등장을 재생한다. 연결이 없거나 이 컴포넌트가 꺼져 있으면 <b>같은 프레임에</b> onComplete를
        /// 불러서 전환이 멈추지 않게 한다 - 연출은 있으면 좋은 것이지 전환의 전제 조건이 아니다.
        ///
        /// <b>전제</b>: 호출 시점에 전투/입력이 꺼져 있어야 한다(AttackMovement가 위치를 덮어쓰지 않는 상태).
        /// </summary>
        public void Play(Action onComplete)
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }

            if (attackMovement == null || !isActiveAndEnabled)
            {
                onComplete?.Invoke();
                return;
            }

            playRoutine = StartCoroutine(DropRoutine(onComplete));
        }

        private IEnumerator DropRoutine(Action onComplete)
        {
            Transform actor = attackMovement.transform;

            // 지금 위치가 곧 착지 지점이다(전투가 꺼지면서 기준점으로 되돌아온 상태).
            Vector3 landing = actor.localPosition;
            Vector3 start = landing + Vector3.up * dropHeight;
            Vector3 baseScale = actor.localScale;

            actor.localPosition = start;

            float elapsed = 0f;
            while (elapsed < dropDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / dropDuration);
                actor.localPosition = Vector3.LerpUnclamped(start, landing, dropCurve.Evaluate(t));
                yield return null;
            }

            actor.localPosition = landing;

            // 착지 스쿼시 - 발이 바닥에 닿는 순간의 무게감만 담당한다. 위치는 이미 착지 지점에 있으므로
            // 여기서부터는 스케일만 움직인다(Transform 소유권 다툼이 생길 여지를 남기지 않는다).
            if (squashDuration > 0f && squashScaleY < 1f)
            {
                float squashElapsed = 0f;
                while (squashElapsed < squashDuration)
                {
                    squashElapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(squashElapsed / squashDuration);
                    // 0 -> 1 -> 0으로 한 번 눌렸다 펴진다.
                    float pulse = Mathf.Sin(t * Mathf.PI);
                    float scaleY = Mathf.Lerp(baseScale.y, baseScale.y * squashScaleY, pulse);
                    actor.localScale = new Vector3(baseScale.x, scaleY, baseScale.z);
                    yield return null;
                }

                actor.localScale = baseScale;
            }

            // 착지 위치를 기준점으로 확정한다 - 진행 중이던 이동도 여기서 안전하게 정리된다.
            attackMovement.SetPresentationBasePosition(landing);

            playRoutine = null;
            onComplete?.Invoke();
        }
    }
}
