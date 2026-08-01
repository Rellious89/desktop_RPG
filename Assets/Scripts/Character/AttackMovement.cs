using Common;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 이 캐릭터의 Transform 위치를 <b>단독으로</b> 제어하는 이동 컨트롤러. Attack Movement(실제 타격
    /// 연출)와 Charge Movement(누적 입력 충전 자세)를 하나의 상태 기계로 합쳐서, 두 연출이 같은
    /// 프레임에 서로의 위치를 덮어쓰는 일이 없도록 한다. Idle 프레임 재생과는 독립적으로 위치만 움직인다.
    ///
    /// <b>키 입력에는 반응하지 않는다.</b> 예전에는 GlobalKeyboardHook.AnyKeyDownThisFrame을 직접 보고
    /// 모든 입력에 전진/복귀(타격 연출)를 재생했다. 이제 진입점은 PlayerCharacterAnimator가 직접
    /// 호출하는 세 가지뿐이다.
    ///   - <see cref="PlayAttackMove"/>: 실제 타격(Hit Frame)에서 1회
    ///   - <see cref="RequestChargeMove"/>: 누적 입력이 <b>1회 쌓일 때마다</b> 1회(작은 움찔)
    ///   - <see cref="EndChargeMove"/>: 충전이 끝날 때(발사/취소) 1회
    /// 정적 이벤트가 아니라 같은 GameObject에서의 직접 호출인 이유는, 씬에 다른 캐릭터가 함께 켜져
    /// 있어도 남의 타격에 따라 움직이지 않게 하기 위함이다.
    ///
    /// <b>이동 수치는 이 컴포넌트가 소유하지 않는다.</b> 전부 같은 GameObject의 PlayerCharacterAnimator에
    /// 연결된 CharacterMotionProfile의 AttackMovement/ChargeMovement에서 읽어오고(Motion Editor가
    /// 편집하는 값과 100% 동일), 이 컴포넌트는 실제 Transform 이동 실행과 Stage Layout 기준 배치만
    /// 담당한다. 프로필이 없으면 임시 수치로 조용히 움직이는 대신 오류를 남기고 스스로 비활성화된다.
    ///
    /// 프로필이 런타임에 교체되면(하나의 액터 오브젝트가 여러 캐릭터를 연기하는 경로)
    /// <see cref="RefreshFromCurrentProfile"/>로 기준점/배율/이동 수치를 새 프로필 기준으로 다시 잡는다.
    /// </summary>
    [RequireComponent(typeof(PlayerCharacterAnimator))]
    public class AttackMovement : MonoBehaviour
    {
        /// <summary>지금 Transform을 움직이고 있는 주체. 동시에 둘이 될 수 없다는 것 자체가 이
        /// 컨트롤러의 핵심 규칙이라 상태를 하나의 필드로 둔다.</summary>
        private enum MoveMode
        {
            None,
            AttackOut,     // 타격 연출: 현재 위치 -> 전진
            AttackBack,    // 타격 연출: 전진 -> 기준점
            ChargeIn,      // 충전 움찔: 현재 위치 -> 충전 위치
            ChargeReturn,  // 충전 움찔: 현재 위치 -> 기준점(입력 1회분 마무리 또는 취소 복귀)
        }

        [Header("Combat Stage Layout")]
        [Tooltip("시작 위치를 Character Slot Position + 이 캐릭터 프로필의 Actor Offset으로 계산하고, " +
                 "Actor Scale도 함께 적용한다(Motion Editor Preview와 동일한 공식). 비어 있으면 씬에 " +
                 "배치된 현재 Transform 위치/스케일을 그대로 쓴다 - 캐릭터마다 배치가 어긋날 수 있으므로 " +
                 "연결하는 것을 권장한다.")]
        [SerializeField] private CombatStageLayout stageLayout;

        private Vector3 basePosition;
        private float timer;
        private MoveMode mode = MoveMode.None;

        // 지금 구간의 시작/목표 오프셋(기준점 기준 X 변위). 충전 자세에서 곧바로 타격 연출로 넘어가도
        // 위치가 튀지 않도록, 모든 구간은 "현재 오프셋에서 목표 오프셋으로" 보간한다.
        private float startOffset;
        private float targetOffset;
        private float currentOffset;
        private float segmentDuration = 0.001f;

        private float activeMoveDistance;
        private float activeMoveOutDuration;
        private float activeMoveBackDuration;
        private PlayerCharacterAnimator characterAnimator;

        // 지금까지 실제로 재생을 시작한 충전 움찔 횟수. 런타임 로직은 쓰지 않고 진단/검증에서만 읽는다
        // ("입력 1회 = 움찔 1회"가 실제로 지켜졌는지 확인하는 용도).
        private int chargeMoveCount;

        // characterAnimator 캐시를 이미 잡았는지. Awake와 RefreshFromCurrentProfile 중 어느 쪽이 먼저
        // 오더라도(비활성 오브젝트에 Awake 전에 프로필을 적용하는 경로 포함) 같은 초기화를 공유한다.
        private bool initialized;

        // basePosition이 실제 기준점으로 채워졌는지. 아직 한 번도 잡지 않았다면 basePosition은 그냥
        // 0이라, 그 값으로 오프셋을 "되돌리면" 캐릭터가 원점으로 순간이동한다 - 되돌릴 옛 오프셋이
        // 있는지 판단하는 유일한 근거다.
        private bool layoutApplied;

        private void Awake()
        {
            EnsureInitialized();
            // PlayerCharacterAnimator와 완전히 같은 판정(CharacterMotionProfile.IsPlayable)을 쓴다 -
            // 각자 다른 기준을 쓰면 "프로필은 있지만 Base Idle이 비어 있는" 캐릭터가 애니메이션 없이
            // 이동만 하는 상태로 남는다. 컴포넌트 Awake 순서에 의존하지 않도록 상대 컴포넌트의
            // enabled를 보지 않고 같은 데이터를 각자 직접 검사한다.
            if (!HasPlayableProfile())
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

            ApplyProfileLayout();
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;

            characterAnimator = GetComponent<PlayerCharacterAnimator>();
        }

        private bool HasPlayableProfile()
        {
            return characterAnimator != null && CharacterMotionProfile.IsPlayable(characterAnimator.MotionProfile);
        }

        /// <summary>같은 GameObject의 PlayerCharacterAnimator가 <b>지금</b> 들고 있는 프로필 기준으로
        /// 배치와 이동 수치를 다시 잡는다 - 하나의 액터 오브젝트가 프로필만 갈아 끼워 다른 캐릭터를
        /// 연기할 때(CharacterRuntimeActor), 애니메이터가 프로필을 바꾼 <b>직후</b> 호출한다.
        ///
        /// 진행 중이던 타격/충전 이동을 먼저 취소하고 옛 오프셋을 0으로 되돌린 뒤에 기준점을 다시
        /// 계산한다 - 순서를 지켜야 Stage Layout이 없는 폴백 경로(현재 Transform 위치를 기준점으로
        /// 쓰는 경로)에서 이전 캐릭터의 전진 오프셋이 새 기준점에 눌러앉지 않는다.
        ///
        /// 오브젝트가 비활성이거나 아직 Awake가 불리기 전이어도, 몇 번을 다시 불러도 안전하다.</summary>
        /// <returns>새 프로필 기준으로 배치를 다시 잡았으면 true. 프로필이 재생 불가능하면 아무것도
        /// 바꾸지 않고 false다.</returns>
        public bool RefreshFromCurrentProfile()
        {
            EnsureInitialized();
            if (!HasPlayableProfile())
            {
                Debug.LogError($"[AttackMovement] '{name}': PlayerCharacterAnimator의 Character Motion Profile이 " +
                               "없거나 Base Idle 프레임이 비어 있어 배치를 갱신할 수 없습니다 - 현재 상태를 그대로 둡니다.", this);
                return false;
            }

            // 진행 중이던 구간을 끊고 옛 기준점 기준 오프셋을 0으로 되돌린다(기준점을 다시 읽기 전에).
            // 아직 기준점을 한 번도 잡지 않았다면(Awake 전에 프로필을 적용하는 경로) 되돌릴 옛 오프셋
            // 자체가 없다 - 씬에 배치된 현재 위치를 그대로 둬야 Stage Layout이 없는 폴백이 그 위치를
            // 기준점으로 쓸 수 있다.
            if (layoutApplied) ApplyOffset(0f);
            ResetToBase();
            ApplyProfileLayout();

            // Awake가 프로필 문제로 스스로 껐던 컴포넌트라면 여기서 되살린다.
            if (!enabled) enabled = true;
            return true;
        }

        /// <summary>기준점(Character Slot + Actor Offset), Actor Scale, 이동 수치를 현재 프로필에서
        /// 한꺼번에 다시 읽어 적용한다. Stage Layout이 비어 있으면 기존 폴백(씬에 배치된 현재 Transform
        /// 위치/스케일 유지)이 그대로 살아 있다.</summary>
        private void ApplyProfileLayout()
        {
            basePosition = ResolveInitialBasePosition();
            layoutApplied = true;
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
            layoutApplied = true;
            transform.localPosition = localPosition;
            ResetToBase();
        }

        /// <summary>실제 타격(Hit Frame)에서 PlayerCharacterAnimator가 호출한다 - 타격 1회당 정확히
        /// 1회 전진/복귀. 충전 자세에 있었더라도 그 위치에서 이어서 전진하고, 복귀는 기준점으로
        /// 돌아온다(충전 복귀와 겹치지 않는다).</summary>
        public void PlayAttackMove()
        {
            ResolveActiveSettings();
            BeginSegment(MoveMode.AttackOut, activeMoveDistance, activeMoveOutDuration);
        }

        /// <summary>누적 입력이 1회 쌓일 때마다 호출된다 - 호출 1회 = 움찔 1회(충전 위치로 갔다가
        /// 기준점으로 돌아오는 한 번의 펄스). 발사를 일으키는 마지막 입력에서는 호출되지 않으므로,
        /// Required Inputs가 5면 이 호출은 4회까지만 일어난다.
        ///
        /// 이전 움찔이 아직 끝나지 않았는데 다음 입력이 들어오면 <b>요청을 버리지 않고</b> 지금 있는
        /// 위치에서 움찔을 다시 시작한다 - 요청을 큐에 쌓아 순서대로 재생하면 빠른 타이핑에서 이동이
        /// 입력보다 한참 뒤처지고, 발사 이후까지 남은 움찔이 계속 재생된다.</summary>
        public void RequestChargeMove()
        {
            CharacterMotionProfile.ChargeMovementSettings settings = characterAnimator.MotionProfile.ChargeMovement;
            if (!settings.EnableChargeMovement || Mathf.Approximately(settings.ChargeMoveDistance, 0f))
            {
                // 충전 이동을 쓰지 않는 캐릭터 - 진행 중인 타격 연출은 그대로 두고 아무 것도 하지 않는다.
                return;
            }

            chargeMoveCount++;
            BeginSegment(MoveMode.ChargeIn, settings.ChargeMoveDistance, settings.ChargeMoveInDuration);
        }

        /// <summary>충전이 끝날 때(발사/취소 모두) 1회 호출된다. 진행 중이던 움찔을 여기서 끊고 기준점으로
        /// 돌려보내므로, 발사 이후에 남은 움찔이 뒤늦게 재생되는 일이 없다. 발사인 경우에는 곧바로
        /// <see cref="PlayAttackMove"/>가 이어서 호출되어 이 복귀 구간을 현재 위치에서 덮어쓴다.</summary>
        public void EndChargeMove()
        {
            if (mode != MoveMode.ChargeIn && mode != MoveMode.ChargeReturn) return;

            CharacterMotionProfile.ChargeMovementSettings settings = characterAnimator.MotionProfile.ChargeMovement;
            BeginSegment(MoveMode.ChargeReturn, 0f, settings.ChargeMoveReturnDuration);
        }

        private void BeginSegment(MoveMode next, float offset, float duration)
        {
            mode = next;
            startOffset = currentOffset;
            targetOffset = offset;
            segmentDuration = Mathf.Max(0.001f, duration);
            timer = 0f;
        }

        private void Update()
        {
            if (mode == MoveMode.None) return;

            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / segmentDuration);
            ApplyOffset(Mathf.Lerp(startOffset, targetOffset, t));
            if (t < 1f) return;

            switch (mode)
            {
                case MoveMode.AttackOut:
                    BeginSegment(MoveMode.AttackBack, 0f, activeMoveBackDuration);
                    break;
                case MoveMode.ChargeIn:
                    // 움찔 한 번은 "나갔다가 돌아오기"까지가 한 세트다 - 충전 위치에서 멈춰 있지 않으므로
                    // 다음 입력이 오면 항상 기준점 근처에서 새 움찔이 시작된다.
                    BeginSegment(MoveMode.ChargeReturn, 0f, characterAnimator.MotionProfile.ChargeMovement.ChargeMoveReturnDuration);
                    break;
                default: // AttackBack / ChargeReturn - 기준점으로 돌아왔다.
                    ApplyOffset(0f);
                    mode = MoveMode.None;
                    break;
            }
        }

        private void ApplyOffset(float offset)
        {
            currentOffset = offset;
            transform.localPosition = basePosition + Vector3.right * offset;
        }

        /// <summary>이동 상태를 남기지 않고 기준점으로 되돌린다(비활성화/기준점 재설정 시).</summary>
        private void ResetToBase()
        {
            mode = MoveMode.None;
            timer = 0f;
            currentOffset = 0f;
            startOffset = 0f;
            targetOffset = 0f;
        }

        /// <summary>공격 중 캐릭터가 꺼지면(교체/파괴) 밀려 있던 이동 상태가 남지 않게 기준점으로 되돌린다.
        /// 기준점을 아직 한 번도 잡지 않았으면 위치는 건드리지 않는다(0으로 되돌릴 옛 오프셋이 없다).</summary>
        private void OnDisable()
        {
            if (layoutApplied) ApplyOffset(0f);
            ResetToBase();
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
