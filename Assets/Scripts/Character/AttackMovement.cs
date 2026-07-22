using Common;
using DesktopWindow;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 어떤 키보드 입력이든 감지하면 캐릭터가 앞으로 살짝 이동했다가 원래 위치로 돌아온다.
    /// SpriteFlipbook의 Idle 루프와는 독립적으로 Transform 위치만 움직인다.
    /// GlobalKeyboardHook을 통해 이 앱이 비활성 상태여도(다른 앱 사용 중이어도) 반응한다.
    ///
    /// 공격 가능한 Target이 없으면(Target.HasAttackableTarget == false) 새 입력으로 이동을 시작하지
    /// 않는다 - PlayerCharacterAnimator/ComboManager와 같은 기준으로 "허공 공격" 중 캐릭터만 움직이는
    /// 것을 막는다. 이미 진행 중인 이동은 끊지 않고 기존 방식대로 끝까지 재생한다.
    /// </summary>
    public class AttackMovement : MonoBehaviour
    {
        [SerializeField] private float moveDistance = 0.15f;
        [SerializeField] private float moveOutDuration = 0.05f;
        [SerializeField] private float moveBackDuration = 0.12f;

        private Vector3 basePosition;
        private float timer;
        private bool isMoving;
        private bool returning;
        private float activeMoveDistance;
        private float activeMoveOutDuration;
        private float activeMoveBackDuration;
        private PlayerCharacterAnimator characterAnimator;

        private void Awake()
        {
            basePosition = transform.localPosition;
            characterAnimator = GetComponent<PlayerCharacterAnimator>();
            ResolveActiveSettings();
        }

        private void Update()
        {
            if (GlobalKeyboardHook.AnyKeyDownThisFrame && Target.HasAttackableTarget)
            {
                StartMove();
            }

            if (isMoving)
            {
                UpdateMove();
            }
        }

        private void StartMove()
        {
            ResolveActiveSettings();
            isMoving = true;
            returning = false;
            timer = 0f;
        }

        private void UpdateMove()
        {
            timer += Time.deltaTime;

            if (!returning)
            {
                float t = Mathf.Clamp01(timer / activeMoveOutDuration);
                transform.localPosition = basePosition + Vector3.right * (activeMoveDistance * t);

                if (t >= 1f)
                {
                    returning = true;
                    timer = 0f;
                }
            }
            else
            {
                float t = Mathf.Clamp01(timer / activeMoveBackDuration);
                transform.localPosition = Vector3.Lerp(basePosition + Vector3.right * activeMoveDistance, basePosition, t);

                if (t >= 1f)
                {
                    transform.localPosition = basePosition;
                    isMoving = false;
                }
            }
        }

        private void ResolveActiveSettings()
        {
            CharacterMotionProfile profile = characterAnimator != null ? characterAnimator.MotionProfile : null;
            CharacterMotionProfile.AttackMovementSettings profileSettings = profile != null ? profile.AttackMovement : null;

            if (profileSettings != null && profileSettings.OverrideComponentValues)
            {
                activeMoveDistance = profileSettings.MoveDistance;
                activeMoveOutDuration = profileSettings.MoveOutDuration;
                activeMoveBackDuration = profileSettings.MoveBackDuration;
                return;
            }

            activeMoveDistance = Mathf.Max(0f, moveDistance);
            activeMoveOutDuration = Mathf.Max(0.001f, moveOutDuration);
            activeMoveBackDuration = Mathf.Max(0.001f, moveBackDuration);
        }
    }
}
