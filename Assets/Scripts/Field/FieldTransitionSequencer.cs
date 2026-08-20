using System;
using System.Collections;
using System.Collections.Generic;
using Character;
using Dungeon;
using UnityEngine;

namespace Field
{
    /// <summary>
    /// 필드 전환 <b>연출의 순서</b>를 소유하는 단 하나의 컴포넌트.
    ///
    /// <b>연출은 상태 전환을 감싼다 - 안으로 들어가지 않는다.</b> <see cref="FieldModeManager"/>는
    /// "한 프레임에 전환은 한 번", "상태는 알리기 전에 확정한다"를 지키는 동기 전환기이고,
    /// <see cref="FieldModeRuntimeController"/>는 자기 문서에 <b>전환 연출은 여기서 하지 않는다</b>고
    /// 못박아 두었다. 그래서 이 컴포넌트는 그 둘의 규칙을 하나도 건드리지 않고 바깥에서 시간만 만든다:
    ///
    ///   1. 입력/전투를 먼저 닫는다 - 연출 중에는 연출이 최우선이다.
    ///   2. 지금 필드를 디졸브로 지운다(여러 프레임).
    ///   3. 다 지워진 뒤에야 기존 전환을 <b>그대로</b> 호출한다(동기 1프레임, 손대지 않음).
    ///   4. 새 필드에서 캐릭터를 낙하시킨다(여러 프레임).
    ///   5. 연출이 끝난 뒤에야 입력/전투를 되돌린다.
    ///
    /// <b>연출 중에는 어떤 전환 요청도 받지 않는다.</b> 매니저의 프레임 잠금은 3번의 한 순간만
    /// 보호하므로, 1~5 구간 전체를 여기서 따로 잠근다.
    ///
    /// <b>없어도 게임은 돌아간다.</b> 이 컴포넌트가 씬에 없거나 꺼져 있으면 기존 진입점들이 예전처럼
    /// 곧바로 전환한다(연출만 빠진다) - 연출은 있으면 좋은 것이지 이동의 전제 조건이 아니다.
    ///
    /// <b>확장 여지</b>: 지금 디졸브 대상은 필드 루트뿐이다. 나중에 플레이어가 사라지는 연출이나 UI를
    /// 함께 포함시키려면 그 오브젝트에 <see cref="PixelDissolveGroup"/>을 붙여
    /// <see cref="alwaysDissolveGroups"/>에 넣기만 하면 된다 - 이 파일의 순서 코드는 그대로다.
    /// </summary>
    [DisallowMultipleComponent]
    public class FieldTransitionSequencer : MonoBehaviour
    {
        /// <summary>씬에 하나만 두는 연출 소유자. 기존 진입점들이 "연출이 있으면 맡기고 없으면 직접 간다"를
        /// 판단하는 데 쓴다 - 없을 때 조용히 기존 동작으로 돌아가야 하므로 필수 참조로 두지 않는다.</summary>
        public static FieldTransitionSequencer Instance { get; private set; }

        /// <summary>전환 연출이 정리되고 입력 복구까지 끝난 뒤 한 번 발생한다.</summary>
        public event Action TransitionCompleted;

        [Header("연결")]
        [Tooltip("모드 전환의 단일 소유자. 연출이 끝난 뒤 이 매니저의 Try...를 그대로 호출한다.")]
        [SerializeField] private FieldModeManager fieldModeManager;

        [Tooltip("연출 중 입력/전투를 닫고 여는 대상. 비어 있으면 입력을 막지 못하므로 경고를 남기고 " +
                 "연출 없이 즉시 전환한다.")]
        [SerializeField] private PlayerCharacterAnimator playerAnimator;

        [Header("디졸브 대상")]
        [Tooltip("마을을 떠날 때 사라질 그룹(TownFieldRoot에 붙인 PixelDissolveGroup).")]
        [SerializeField] private PixelDissolveGroup townFieldDissolve;

        [Tooltip("던전을 떠날 때 사라질 그룹(DungeonFieldRoot에 붙인 PixelDissolveGroup).")]
        [SerializeField] private PixelDissolveGroup dungeonFieldDissolve;

        [Tooltip("어느 방향으로 전환하든 항상 함께 사라질 그룹. 지금은 비워둔다 - 플레이어가 사라지는 " +
                 "연출이나 UI를 나중에 연출에 포함시키기로 하면 여기에 추가하면 된다.")]
        [SerializeField] private List<PixelDissolveGroup> alwaysDissolveGroups = new List<PixelDissolveGroup>();

        [Header("등장 연출")]
        [Tooltip("새 필드에 도착한 캐릭터의 낙하 연출. 비어 있으면 낙하 없이 곧바로 등장한다.")]
        [SerializeField] private CharacterDropIn characterDropIn;

        [Tooltip("던전에 들어갈 때 낙하 연출을 재생할지.")]
        [SerializeField] private bool dropInOnEnterDungeon = true;

        [Tooltip("마을로 돌아올 때도 낙하 연출을 재생할지.")]
        [SerializeField] private bool dropInOnReturnTown = true;

        /// <summary>연출이 도는 동안 true. 이 구간에는 어떤 전환 요청도 받지 않는다.</summary>
        public bool IsPlaying { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError($"[FieldTransitionSequencer] '{name}': 씬에 이미 다른 시퀀서가 있습니다 - " +
                               "전환 연출의 소유자는 하나여야 하므로 이 컴포넌트는 동작하지 않습니다.", this);
                enabled = false;
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDisable()
        {
            // 연출 도중에 꺼지면 코루틴이 멈춰 (1) 입력이 닫힌 채로 굳고 (2) 지우던 필드가 반쯤 사라진
            // 채로 남는다. 둘 다 되돌려놓고 끝낸다 - 연출이 끊겼다고 게임이 못 쓰게 되면 안 된다.
            if (!IsPlaying) return;

            if (townFieldDissolve != null) townFieldDissolve.ResetImmediate();
            if (dungeonFieldDissolve != null) dungeonFieldDissolve.ResetImmediate();
            for (int i = 0; i < alwaysDissolveGroups.Count; i++)
            {
                if (alwaysDissolveGroups[i] != null) alwaysDissolveGroups[i].ResetImmediate();
            }

            FinishSequence();
        }

        /// <summary>
        /// 던전 입장을 연출과 함께 진행한다. 맡았으면 true - 호출한 쪽은 아무것도 더 하지 않는다.
        /// false면 이 컴포넌트가 맡지 않았다는 뜻이므로 호출한 쪽이 기존대로 곧바로 전환하면 된다.
        /// </summary>
        public bool TryPlayEnterDungeon(DungeonDefinition dungeon)
        {
            if (!CanHandleTransition()) return false;
            if (RejectWhilePlaying("던전 입장")) return true;

            StartCoroutine(SequenceRoutine(
                exitGroup: townFieldDissolve,
                incomingGroup: dungeonFieldDissolve,
                applyModeChange: () => fieldModeManager.TryEnterDungeon(dungeon),
                playDropIn: dropInOnEnterDungeon));
            return true;
        }

        /// <summary>마을 복귀를 연출과 함께 진행한다. 반환 규칙은 <see cref="TryPlayEnterDungeon"/>과 같다.</summary>
        public bool TryPlayReturnToTown()
        {
            if (!CanHandleTransition()) return false;
            if (RejectWhilePlaying("마을 복귀")) return true;

            StartCoroutine(SequenceRoutine(
                exitGroup: dungeonFieldDissolve,
                incomingGroup: townFieldDissolve,
                applyModeChange: () => fieldModeManager.TryReturnToTown(),
                playDropIn: dropInOnReturnTown));
            return true;
        }

        /// <summary>이 컴포넌트가 전환을 맡을 수 있는 상태인지만 본다 - <b>상태를 건드리지 않는다</b>.
        /// false면 호출한 쪽이 기존 즉시 전환으로 넘어간다(연출만 빠지고 이동은 그대로 된다).</summary>
        private bool CanHandleTransition()
        {
            if (!isActiveAndEnabled) return false;

            if (fieldModeManager == null || playerAnimator == null)
            {
                Debug.LogError($"[FieldTransitionSequencer] '{name}': Field Mode Manager 또는 Player Animator가 " +
                               "연결되지 않아 연출을 재생할 수 없습니다 - 연출 없이 즉시 전환합니다.", this);
                return false;
            }

            return true;
        }

        /// <summary>연출이 도는 중이면 요청을 버린다. true를 돌려주면 호출한 쪽은 "맡았다"로 보고 물러난다 -
        /// 여기서 맡지 않았다고 하면 호출한 쪽이 연출을 건너뛰고 곧바로 전환해버려서, 연출 도중에 필드가
        /// 바뀌는 정확히 그 상황이 벌어진다. 요청을 버리는 것과 맡지 않는 것은 다르다.</summary>
        private bool RejectWhilePlaying(string requestLabel)
        {
            if (!IsPlaying) return false;

            Debug.LogWarning($"[FieldTransitionSequencer] '{name}': 전환 연출이 재생 중이라 {requestLabel} " +
                             "요청을 무시합니다 - 연출이 끝난 뒤에 다시 시도하세요.", this);
            return true;
        }

        private IEnumerator SequenceRoutine(PixelDissolveGroup exitGroup, PixelDissolveGroup incomingGroup,
            Func<bool> applyModeChange, bool playDropIn)
        {
            IsPlaying = true;

            // 1. 입력과 진행 중이던 공격을 먼저 닫는다. 여기서 AttackMovement도 기준점으로 되돌아가므로
            //    이후 낙하 연출이 Transform을 단독으로 쓸 수 있다.
            playerAnimator.SetCombatEnabled(false);

            // 2. 지금 필드를 지운다. 그룹이 여럿이면 전부 끝날 때까지 기다린다.
            yield return DissolveAll(exitGroup);

            // 3. 여기서야 기존 전환을 호출한다 - 동기 1프레임이고, 이 안에서 루트가 교체된다.
            //    거부되면(이미 그 모드, 같은 프레임 중복 등) 연출만 하고 필드는 그대로 남으므로,
            //    지운 화면을 반드시 되돌려놓고 끝낸다.
            bool changed = applyModeChange();
            if (!changed)
            {
                if (exitGroup != null) exitGroup.ResetImmediate();
                FinishSequence();
                yield break;
            }

            // 4. 새 필드는 디졸브 인 없이 그대로 등장한다(설계상 들어오는 연출은 캐릭터 낙하가 담당한다).
            //    다만 그 루트가 이전에 사라진 상태로 꺼졌을 수 있으므로 확실히 되돌린다 - 루트가 다시
            //    켜질 때 PixelDissolveGroup.OnEnable이 이미 복구하지만, 그룹이 루트 바깥에 붙어 있는
            //    구성(플레이어/UI)에서는 OnEnable이 돌지 않으므로 여기서 한 번 더 명시적으로 지운다.
            if (incomingGroup != null) incomingGroup.ResetImmediate();
            for (int i = 0; i < alwaysDissolveGroups.Count; i++)
            {
                if (alwaysDissolveGroups[i] != null) alwaysDissolveGroups[i].ResetImmediate();
            }

            // 5. 전투를 다시 닫는다 - 3번에서 FieldModeRuntimeController가 던전 준비를 마치며 전투를
            //    열었을 수 있는데, 캐릭터가 아직 공중에 있는 동안 입력을 받으면 안 된다. 같은 프레임
            //    안이라 이 사이에 Update가 끼어들지 않으므로 입력이 새어 들어갈 틈은 없다.
            playerAnimator.SetCombatEnabled(false);

            if (playDropIn && characterDropIn != null)
            {
                bool dropDone = false;
                characterDropIn.Play(() => dropDone = true);
                while (!dropDone) yield return null;
            }

            FinishSequence();
        }

        /// <summary>이번 전환에서 사라져야 할 그룹을 전부 재생하고 마지막 하나가 끝날 때까지 기다린다.</summary>
        private IEnumerator DissolveAll(PixelDissolveGroup exitGroup)
        {
            int pending = 0;
            Action onOne = () => pending--;

            if (exitGroup != null)
            {
                pending++;
                exitGroup.PlayDissolveOut(onOne);
            }

            for (int i = 0; i < alwaysDissolveGroups.Count; i++)
            {
                PixelDissolveGroup group = alwaysDissolveGroups[i];
                if (group == null) continue;

                pending++;
                group.PlayDissolveOut(onOne);
            }

            while (pending > 0) yield return null;
        }

        /// <summary>연출을 끝내고 입력을 되돌린다. 전투를 다시 여는 판단은 하지 않고 매니저에게 묻는다 -
        /// 마을이면 닫힌 채로 두는 것이 맞고, 그 판정의 소유자는 언제나 FieldModeManager다.</summary>
        private void FinishSequence()
        {
            IsPlaying = false;

            if (playerAnimator != null && fieldModeManager != null && fieldModeManager.CanCombat)
                playerAnimator.SetCombatEnabled(true);

            TransitionCompleted?.Invoke();
        }
    }
}
