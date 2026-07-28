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
    ///
    /// <b>이동 수치(거리/나가는 시간/돌아오는 시간)는 이 컴포넌트가 소유하지 않는다.</b> 전부 같은
    /// GameObject의 PlayerCharacterAnimator에 연결된 CharacterMotionProfile.AttackMovement에서
    /// 읽어오고(Motion Editor가 편집하는 값과 100% 동일), 이 컴포넌트는 실제 Transform 이동 실행과
    /// Stage Layout 기준 배치만 담당한다. 프로필이 없으면 임시 수치로 조용히 움직이는 대신 오류를
    /// 남기고 스스로 비활성화된다.
    /// </summary>
    [RequireComponent(typeof(PlayerCharacterAnimator))]
    public class AttackMovement : MonoBehaviour
    {
        [Header("Combat Stage Layout")]
        [Tooltip("시작 위치를 Character Slot Position + 이 캐릭터 프로필의 Actor Offset으로 계산하고, " +
                 "Actor Scale도 함께 적용한다(Motion Editor Preview와 동일한 공식). 비어 있으면 씬에 " +
                 "배치된 현재 Transform 위치/스케일을 그대로 쓴다 - 캐릭터마다 배치가 어긋날 수 있으므로 " +
                 "연결하는 것을 권장한다.")]
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
            // PlayerCharacterAnimator와 완전히 같은 판정(CharacterMotionProfile.IsPlayable)을 쓴다 -
            // 각자 다른 기준을 쓰면 "프로필은 있지만 Base Idle이 비어 있는" 캐릭터가 애니메이션 없이
            // 이동만 하는 상태로 남는다. 컴포넌트 Awake 순서에 의존하지 않도록 상대 컴포넌트의
            // enabled를 보지 않고 같은 데이터를 각자 직접 검사한다.
            if (characterAnimator == null || !CharacterMotionProfile.IsPlayable(characterAnimator.MotionProfile))
            {
                // 이동 수치의 유일한 원천이 사라진 상태 - 임시 수치로 움직이면 캐릭터마다 다른 연출이
                // 조용히 뭉개지므로, 오류를 남기고 이동만 끈다(다른 컴포넌트에는 영향을 주지 않는다).
                Debug.LogError($"[AttackMovement] '{name}': PlayerCharacterAnimator의 Character Motion Profile이 " +
                               "없거나 Base Idle 프레임이 비어 있어 Attack Movement 값을 가져올 수 없습니다. 이동을 비활성화합니다.", this);
                enabled = false;
                return;
            }
            if (stageLayout == null)
            {
                Debug.LogWarning($"[AttackMovement] '{name}': Combat Stage Layout이 비어 있어 씬에 배치된 현재 " +
                                 "Transform 위치/스케일을 그대로 사용합니다(Motion Editor Preview와 어긋날 수 있습니다).", this);
            }

            basePosition = ResolveInitialBasePosition();
            transform.localPosition = basePosition;
            ApplyActorScale();
            ResolveActiveSettings();
        }

        /// <summary>Preview(DrawPairedStage)와 같은 공식: Slot + Actor Offset. stageLayout이 없으면
        /// 지금 Transform 위치를 그대로 기준점으로 쓴다(Awake에서 경고를 남긴 뒤의 안전한 동작).</summary>
        private Vector3 ResolveInitialBasePosition()
        {
            if (stageLayout == null) return transform.localPosition;

            Vector2 offset = characterAnimator.MotionProfile.Preview.ActorOffset;
            Vector2 slot = stageLayout.CharacterSlotPosition;
            return new Vector3(slot.x + offset.x, slot.y + offset.y, transform.localPosition.z);
        }

        private void ApplyActorScale()
        {
            if (stageLayout == null) return;

            float scale = characterAnimator.MotionProfile.Preview.ActorScale;
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

        /// <summary>이동 수치를 매 이동 시작 시점에 프로필에서 다시 읽는다 - Play Mode 중 Motion
        /// Editor에서 값을 바꿔도 다음 이동부터 곧바로 반영된다(진행 중인 이동은 끊지 않는다).</summary>
        private void ResolveActiveSettings()
        {
            CharacterMotionProfile.AttackMovementSettings settings = characterAnimator.MotionProfile.AttackMovement;
            activeMoveDistance = settings.MoveDistance;
            activeMoveOutDuration = settings.MoveOutDuration;
            activeMoveBackDuration = settings.MoveBackDuration;
        }

#if UNITY_EDITOR
        /// <summary>필수 연결이 빠진 상태를 Edit Mode Inspector에서 바로 알 수 있게 경고만 남긴다
        /// (런타임 판정은 Awake가 담당한다).</summary>
        private void OnValidate()
        {
            if (Application.isPlaying) return;
            if (stageLayout == null)
            {
                Debug.LogWarning($"[AttackMovement] '{name}': Combat Stage Layout이 비어 있습니다 - 다른 캐릭터와 " +
                                 "시작 위치/스케일 계산 방식이 달라집니다.", this);
            }
        }
#endif
    }
}
