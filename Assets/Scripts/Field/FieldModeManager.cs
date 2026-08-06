using System;
using Dungeon;
using UnityEngine;

namespace Field
{
    /// <summary>
    /// 필드 모드(마을/던전)의 <b>단일 소유자</b>. "지금 어디에 있는가"(<see cref="CurrentMode"/>),
    /// "어느 던전인가"(<see cref="CurrentDungeon"/>), "전투가 성립하는가"(<see cref="CanCombat"/>)를
    /// 이 컴포넌트 하나만 결정하고, 나머지는 모두 <see cref="FieldModeChanged"/>를 구독해서 따라온다.
    ///
    /// <b>여기서 하는 일은 상태 전환뿐이다.</b> 씬 로드, 저장, 전투/입력/몬스터/회복/UI 동작은 하나도
    /// 하지 않는다 - 그런 처리는 전부 이벤트를 받는 쪽의 몫이며, 이 클래스가 그쪽 타입을 참조하지
    /// 않아야 나중에 처리를 붙이거나 떼는 일이 이 파일을 건드리지 않는다.
    ///
    /// <b>요청은 전부 Try 형태이며, 거부는 아무것도 바꾸지 않는다.</b> 거부된 요청은 상태를 건드리지도
    /// 않고 <see cref="FieldModeChanged"/>를 발행하지도 않는다 - 호출하는 쪽은 false 하나로
    /// "아무 일도 일어나지 않았다"를 확신할 수 있고, 왜 거부됐는지는 로그가 남긴다.
    ///
    /// <b>전환은 한 프레임에 한 번뿐이다.</b> 연타/중복 구독/동기 재진입 어느 쪽이든 같은
    /// <see cref="Time.frameCount"/> 안에서는 두 번째 전환이 막힌다. 이벤트 콜백 도중에 들어오는
    /// 재진입 요청도 마찬가지로 막히는데(<see cref="IsTransitioning"/>), 이 둘을 모두 두는 이유는
    /// 막아야 하는 경로가 서로 다르기 때문이다 - 재진입은 콜백 <b>안</b>에서, 프레임 잠금은 콜백이
    /// 끝난 <b>뒤</b> 같은 프레임에 들어오는 요청을 막는다.
    ///
    /// <b>상태는 알리기 전에 확정한다.</b> <see cref="FieldModeChanged"/>가 불릴 때
    /// <see cref="CurrentMode"/>와 <see cref="CurrentDungeon"/>은 이미 새 값으로 짝이 맞춰져 있으므로,
    /// 구독자는 이벤트 인자와 프로퍼티 중 무엇을 읽어도 같은 답을 얻는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class FieldModeManager : MonoBehaviour
    {
        /// <summary>아직 한 번도 전환하지 않았음을 뜻하는 프레임 값. <see cref="Time.frameCount"/>는
        /// 음수가 되지 않으므로 첫 프레임(0)의 요청과 절대 겹치지 않는다.</summary>
        private const int NoTransitionFrame = -1;

        /// <summary>마지막으로 <b>받아들여진</b> 전환이 일어난 프레임. 거부된 요청은 여기에 남지 않는다.</summary>
        private int lastTransitionFrame = NoTransitionFrame;

        /// <summary>
        /// 필드 모드가 실제로 바뀌었을 때 <b>전환 한 번당 정확히 한 번</b> 발행된다. 거부된 요청은
        /// 발행하지 않으므로, 이 이벤트가 왔다는 것은 상태가 이미 바뀌었다는 뜻이다.
        ///
        /// 인자는 <c>(새 모드, 그 모드의 던전)</c>이다 - <see cref="FieldMode.Dungeon"/>이면 던전은
        /// 항상 유효한(<see cref="DungeonDefinition.IsValid"/>) 정의이고,
        /// <see cref="FieldMode.Town"/>이면 <b>항상 null</b>이다.
        ///
        /// 콜백이 도는 동안 <see cref="IsTransitioning"/>은 true이고 따라서
        /// <see cref="CanCombat"/>은 false다 - 전환 도중에는 전투가 성립하지 않는다. 콜백 안에서
        /// 다시 전환을 요청하면 거부된다(false를 받는다).
        /// </summary>
        public event Action<FieldMode, DungeonDefinition> FieldModeChanged;

        /// <summary>지금의 필드 모드. 시작값은 <see cref="FieldMode.Town"/>이다.</summary>
        public FieldMode CurrentMode { get; private set; } = FieldMode.Town;

        /// <summary>지금 들어와 있는 던전. <see cref="FieldMode.Town"/>일 때는 <b>항상 null</b>이며,
        /// <see cref="FieldMode.Dungeon"/>일 때만 유효한 정의가 들어 있다.</summary>
        public DungeonDefinition CurrentDungeon { get; private set; }

        /// <summary>전환 이벤트를 알리는 중인지 여부. <see cref="FieldModeChanged"/> 콜백이 도는
        /// 동안에만 true이며, 콜백이 예외를 던져도 반드시 false로 되돌아온다.</summary>
        public bool IsTransitioning { get; private set; }

        /// <summary>전투가 성립하는 상태인지 여부. 마을에서도, 전환 도중에도 false이고, <b>던전 모드가
        /// 던전과 짝이 맞은 채로 안정된 뒤</b>에만 true다.</summary>
        public bool CanCombat => !IsTransitioning
                                 && CurrentMode == FieldMode.Dungeon
                                 && CurrentDungeon != null;

        /// <summary>
        /// UI의 입장 요청을 받는다. <see cref="DungeonEntryService"/>는 "요청이 왔다"는 사실만
        /// 전달하므로, 받아들일지 말지는 여기서 결정한다 - 거부해도 요청 통로와 UI는 아무 영향을 받지
        /// 않는다(패널은 이미 닫힌 뒤다).
        ///
        /// <b>지웠다 다시 구독한다.</b> 정적 이벤트라 구독이 컴포넌트보다 오래 살 수 있어서, 같은
        /// 핸들러가 두 번 붙는 경로를 아예 만들지 않는다.
        /// </summary>
        private void OnEnable()
        {
            DungeonEntryService.DungeonEnterRequested -= HandleDungeonEnterRequested;
            DungeonEntryService.DungeonEnterRequested += HandleDungeonEnterRequested;
        }

        private void OnDisable()
        {
            DungeonEntryService.DungeonEnterRequested -= HandleDungeonEnterRequested;
        }

        /// <summary>
        /// 던전으로 들어간다. 받아들여지면 상태를 바꾸고 <see cref="FieldModeChanged"/>를 한 번 발행한
        /// 뒤 true를, 거부되면 <b>아무것도 바꾸지 않고</b> false를 돌려준다.
        ///
        /// 거부하는 경우는 넷이다 - 던전이 없거나 식별자가 없는 경우, 이미 던전에 들어와 있는 경우
        /// (마을을 거치지 않고 던전에서 던전으로 건너뛰지 않는다), 전환 이벤트 도중의 재진입,
        /// 그리고 같은 프레임의 두 번째 전환 요청.
        /// </summary>
        public bool TryEnterDungeon(DungeonDefinition dungeon)
        {
            if (dungeon == null)
            {
                Debug.LogError("[FieldModeManager] 입장 요청에 던전이 없습니다 - 요청을 무시합니다.", this);
                return false;
            }

            if (!dungeon.IsValid)
            {
                Debug.LogError($"[FieldModeManager] 던전 '{dungeon.name}'에 Dungeon Id가 없어 입장 요청을 " +
                               "무시합니다 - 에셋에서 식별자를 지정하세요.", dungeon);
                return false;
            }

            if (CurrentMode == FieldMode.Dungeon)
            {
                Debug.LogWarning($"[FieldModeManager] 이미 던전 '{CurrentDungeonId()}'에 있어 " +
                                 $"'{dungeon.DungeonId}' 입장 요청을 무시합니다 - 먼저 마을로 돌아가세요.", this);
                return false;
            }

            if (!CanBeginTransition($"던전 '{dungeon.DungeonId}' 입장")) return false;

            ApplyMode(FieldMode.Dungeon, dungeon);
            return true;
        }

        /// <summary>
        /// 마을로 돌아간다. 받아들여지면 상태를 바꾸고 <see cref="FieldModeChanged"/>를 한 번 발행한
        /// 뒤 true를, 거부되면 <b>아무것도 바꾸지 않고</b> false를 돌려준다.
        ///
        /// 거부하는 경우는 셋이다 - 이미 마을인 경우, 전환 이벤트 도중의 재진입, 같은 프레임의 두 번째
        /// 전환 요청.
        /// </summary>
        public bool TryReturnToTown()
        {
            if (CurrentMode == FieldMode.Town)
            {
                Debug.LogWarning("[FieldModeManager] 이미 마을에 있어 복귀 요청을 무시합니다.", this);
                return false;
            }

            if (!CanBeginTransition("마을 복귀")) return false;

            ApplyMode(FieldMode.Town, null);
            return true;
        }

        private void HandleDungeonEnterRequested(DungeonDefinition dungeon)
        {
            // 전환 연출이 있으면 <b>언제</b> 전환할지는 그쪽이 정한다 - 연출이 끝난 뒤 이 클래스의
            // TryEnterDungeon을 그대로 부르므로, 전환 규칙(프레임 잠금/재진입 금지/거부 시 무변경)은
            // 어느 경로로 오든 똑같이 적용된다. 연출이 없으면(씬에 시퀀서가 없거나 꺼져 있으면)
            // 예전처럼 이 자리에서 곧바로 전환한다 - 연출은 이동의 전제 조건이 아니다.
            if (FieldTransitionSequencer.Instance != null
                && FieldTransitionSequencer.Instance.TryPlayEnterDungeon(dungeon))
            {
                return;
            }

            // 거부는 이 아래에서 로그로 남으므로 여기서는 결과를 다시 해석하지 않는다.
            TryEnterDungeon(dungeon);
        }

        /// <summary>전환을 시작해도 되는지만 판정한다 - <b>상태를 건드리지 않는다</b>. 재진입과 프레임
        /// 잠금은 막는 경로가 다르므로 둘 다 본다.</summary>
        private bool CanBeginTransition(string requestLabel)
        {
            if (IsTransitioning)
            {
                Debug.LogWarning($"[FieldModeManager] 전환 알림 도중에 들어온 {requestLabel} 요청을 " +
                                 "무시합니다 - 전환 콜백 안에서 모드를 다시 바꿀 수 없습니다.", this);
                return false;
            }

            if (lastTransitionFrame == Time.frameCount)
            {
                Debug.LogWarning($"[FieldModeManager] 같은 프레임에 이미 전환이 있었으므로 {requestLabel} " +
                                 "요청을 무시합니다 - 한 프레임에 전환은 한 번뿐입니다.", this);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 전환을 확정하고 알린다. <b>모드와 던전을 먼저 짝지어 확정한 뒤에</b> 이벤트를 발행하므로,
        /// 구독자가 보는 상태는 언제나 앞뒤가 맞는다.
        ///
        /// <see cref="IsTransitioning"/>은 try/finally로 반드시 되돌린다 - 구독자 하나가 예외를
        /// 던졌다고 해서 전환 중 상태로 굳어 <see cref="CanCombat"/>이 영영 false가 되는 경로를
        /// 만들지 않는다.
        /// </summary>
        private void ApplyMode(FieldMode mode, DungeonDefinition dungeon)
        {
            lastTransitionFrame = Time.frameCount;

            CurrentMode = mode;
            CurrentDungeon = dungeon;

            IsTransitioning = true;
            try
            {
                FieldModeChanged?.Invoke(mode, dungeon);
            }
            finally
            {
                IsTransitioning = false;
            }
        }

        /// <summary>로그에 쓸 현재 던전 식별자. 던전이 없으면 빈 문자열이다.</summary>
        private string CurrentDungeonId() =>
            CurrentDungeon != null ? CurrentDungeon.DungeonId : string.Empty;
    }
}
