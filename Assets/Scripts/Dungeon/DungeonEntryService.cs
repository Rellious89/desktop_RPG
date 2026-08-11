using System;
using Character;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// 던전 <b>입장 요청</b>의 단일 통로. UI(<see cref="DungeonPanel"/>)는 "이 던전에 들어가겠다"는
    /// 사실만 여기에 알리고, 그 뒤에 무슨 일이 일어나는지는 알지 못한다 - 필드 모드 전환, 전투 시작,
    /// 몬스터 큐 구성 같은 처리는 나중에 이 이벤트를 구독하는 쪽(FieldModeManager 등)이 담당한다.
    /// 그래서 이 클래스는 전투/필드 쪽 타입을 하나도 참조하지 않으며, 반대로 UI도 그쪽을 참조하지 않는다.
    ///
    /// <b>구독자가 하나도 없어도 정상이다.</b> 지금은 실제로 입장을 처리하는 쪽이 없으므로 요청은
    /// 로그만 남기고 끝난다 - 그래도 패널은 요청이 받아들여진 것으로 보고 닫힌다. 이 동작이 나중에
    /// 구독자가 생겼을 때와 같은 경로를 지나게 하기 위함이다(구독자 유무로 UI 흐름이 갈라지지 않는다).
    ///
    /// <b>검증은 여기서 끝낸다.</b> 던전이 없거나 식별자가 없는 요청, 그리고 필요 레벨에 못 미치는
    /// 요청은 이벤트를 발행하지 않고 false를 돌려주므로, 구독자는 "언제나 유효하고 입장 가능한 던전"만
    /// 받는다.
    /// </summary>
    public static class DungeonEntryService
    {
        /// <summary>입장 요청이 받아들여졌을 때 <b>요청당 정확히 한 번</b> 발행된다. 인자는 항상 유효한
        /// (식별자가 있는) 던전 정의다.</summary>
        public static event Action<DungeonDefinition> DungeonEnterRequested;

        /// <summary>가장 최근에 받아들여진 요청의 던전. 검증/디버깅과 테스트용 읽기 전용 상태이며,
        /// 이 값으로 게임 로직을 분기하지 않는다.</summary>
        public static DungeonDefinition LastRequestedDungeon { get; private set; }

        /// <summary>가장 최근에 받아들여진 요청의 던전 식별자. 요청이 없었으면 빈 문자열이다.</summary>
        public static string LastRequestedDungeonId { get; private set; } = string.Empty;

        /// <summary>받아들여진 요청의 누적 횟수. "한 번의 클릭에 요청이 한 번만 발행되는가"를 확인하는
        /// 데 쓴다 - 거부된 요청은 세지 않는다.</summary>
        public static int AcceptedRequestCount { get; private set; }

        private static DungeonAccessService accessOverride;

        /// <summary>
        /// 던전 입장을 요청한다. 받아들여지면 <see cref="DungeonEnterRequested"/>를 한 번 발행하고
        /// true를, 요청이 유효하지 않으면 아무것도 발행하지 않고 false를 돌려준다.
        ///
        /// <b>중복 클릭 억제는 호출하는 쪽이 한다.</b> 이 통로는 "요청이 왔다"는 사실만 처리하고,
        /// 같은 클릭이 두 번 도달하지 않게 막는 것은 버튼을 소유한 UI의 몫이다(패널은 요청이
        /// 받아들여지면 즉시 버튼을 끄고 닫는다).
        ///
        /// <b>레벨 판정은 요청 직전에 현재 상태로 한다.</b> UI가 미리 계산한 값은 이 통로가 보지
        /// 않으며, 우회할 수 없다.
        /// </summary>
        public static bool RequestEnterDungeon(DungeonDefinition dungeon)
        {
            if (dungeon == null)
            {
                Debug.LogError("[DungeonEntryService] 입장 요청에 던전이 없습니다 - 요청을 무시합니다.");
                return false;
            }

            if (!dungeon.IsValid)
            {
                Debug.LogError($"[DungeonEntryService] 던전 '{dungeon.name}'에 Dungeon Id가 없어 입장 요청을 " +
                               "무시합니다 - 에셋에서 식별자를 지정하세요.", dungeon);
                return false;
            }

            DungeonAccessResult access = EvaluateAccess(dungeon);
            if (!access.Allowed)
            {
                Debug.Log($"[DungeonEntryService] 던전 '{dungeon.DungeonId}' 입장 거부: {access.FailureReason} " +
                          $"(필요 {access.DungeonRequiredLevel}, 최고 보유 {access.HighestOwnedLevel}).");
                return false;
            }

            LastRequestedDungeon = dungeon;
            LastRequestedDungeonId = dungeon.DungeonId;
            AcceptedRequestCount++;

            Debug.Log($"[DungeonEntryService] 던전 입장 요청: '{dungeon.DungeonId}'.", dungeon);

            DungeonEnterRequested?.Invoke(dungeon);
            return true;
        }

        /// <summary>기록해 둔 읽기 전용 상태를 지운다. 테스트에서 요청 횟수를 초기화할 때만 쓰며,
        /// 구독자 목록은 건드리지 않는다.</summary>
        public static void ResetRequestState()
        {
            LastRequestedDungeon = null;
            LastRequestedDungeonId = string.Empty;
            AcceptedRequestCount = 0;
            accessOverride = null;
        }

        internal static void SetAccessServiceForTests(DungeonAccessService service)
        {
            accessOverride = service;
        }

        private static DungeonAccessResult EvaluateAccess(DungeonDefinition dungeon)
        {
            DungeonAccessService service = accessOverride ?? DefaultAccessService();
            if (service == null)
                return DungeonAccessResult.Deny(DungeonAccessFailureReason.MissingRosterOrProgression);

            return service.Evaluate(dungeon);
        }

        private static DungeonAccessService DefaultAccessService()
        {
            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null) return null;
            return new DungeonAccessService(roster);
        }
    }
}
