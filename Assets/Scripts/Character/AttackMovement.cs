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
        [Tooltip("Positive: move forward. Negative: move backward. Zero: no movement.")]
        [SerializeField] private float moveDistance = 0.15f;
        [SerializeField] private float moveOutDuration = 0.05f;
        [SerializeField] private float moveBackDuration = 0.12f;

        [Header("Combat Stage Layout (optional)")]
        [Tooltip("연결하면 시작 위치를 Character Slot Position + 이 캐릭터 프로필의 Actor Offset으로 계산하고, " +
                 "Actor Scale도 함께 적용한다(Motion Editor Preview와 동일한 공식). 비어 있으면 기존처럼 씬에 " +
                 "배치된 현재 Transform 위치/스케일을 그대로 쓴다.")]
        [SerializeField] private CombatStageLayout stageLayout;

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
            characterAnimator = GetComponent<PlayerCharacterAnimator>();
            basePosition = ResolveInitialBasePosition();
            transform.localPosition = basePosition;
            ApplyActorScale();
            ResolveActiveSettings();
        }

        /// <summary>Preview(DrawPairedStage)와 같은 공식: Slot + Actor Offset. stageLayout이 없는
        /// 캐릭터(기존 프리팹/씬)는 지금 Transform 위치를 그대로 기준점으로 쓴다 - 아무것도 깨지지 않는다.</summary>
        private Vector3 ResolveInitialBasePosition()
        {
            if (stageLayout == null) return transform.localPosition;

            CharacterMotionProfile profile = characterAnimator != null ? characterAnimator.MotionProfile : null;
            Vector2 offset = profile != null ? profile.Preview.ActorOffset : Vector2.zero;
            Vector2 slot = stageLayout.CharacterSlotPosition;
            return new Vector3(slot.x + offset.x, slot.y + offset.y, transform.localPosition.z);
        }

        private void ApplyActorScale()
        {
            if (stageLayout == null) return;

            CharacterMotionProfile profile = characterAnimator != null ? characterAnimator.MotionProfile : null;
            float scale = profile != null ? profile.Preview.ActorScale : 1f;
            transform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>Motion Editor의 "Apply Preview Layout to Open Stage"나 향후 런타임 배치 갱신이
        /// 호출하는 진입점 - basePosition과 실제 Transform 위치를 함께 새 기준점으로 맞추고, 진행 중이던
        /// 이동은 안전하게 취소한다(그대로 두면 다음 프레임에 옛 basePosition 기준으로 튈 수 있다).
        /// 공격/이동이 진행 중이 아닐 때(선택/교체/초기화 시점)만 호출해야 한다.</summary>
        public void SetPresentationBasePosition(Vector3 localPosition)
        {
            basePosition = localPosition;
            transform.localPosition = localPosition;
            isMoving = false;
            returning = false;
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

            activeMoveDistance = moveDistance;
            activeMoveOutDuration = Mathf.Max(0.001f, moveOutDuration);
            activeMoveBackDuration = Mathf.Max(0.001f, moveBackDuration);
        }
    }
}
