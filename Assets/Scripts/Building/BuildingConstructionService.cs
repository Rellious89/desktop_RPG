using System;
using System.Collections.Generic;
using Common;
using Inventory;
using UnityEngine;

namespace Building
{
    /// <summary>건설 시작 요청이 어떻게 끝났는지의 갈래.</summary>
    public enum BuildingConstructionStartCode
    {
        /// <summary>비용을 냈고 건설 기록이 파일에 남았다.</summary>
        Success,

        /// <summary>건물 정의가 없거나 Building Id가 비어 있다 - 무엇을 짓는지 알 수 없다.</summary>
        InvalidBuilding,

        /// <summary>저장 문서를 얻지 못했다. 기록할 곳이 없으므로 비용도 건드리지 않는다.</summary>
        NoSaveData,

        /// <summary>같은 Building Id의 기록이 이미 있다(완성 시각이 지났어도 마찬가지다).</summary>
        AlreadyStarted,

        /// <summary>이미 이 서비스가 시작 처리를 도는 중이다(같은 프레임의 두 번째 클릭 등).</summary>
        Reentrant,

        /// <summary>비용을 낼 수 없었다. 자세한 이유는 <see cref="BuildingConstructionStartResult.Cost"/>에
        /// 그대로 있다.</summary>
        CostRejected,

        /// <summary>저장에 실패했다 - 비용도 건설 기록도 <b>전부 되돌렸다</b>.</summary>
        SaveFailed,
    }

    /// <summary>
    /// 건설 시작 요청의 불변 결과. 성공/실패만이 아니라 <b>무엇 때문에 못 했는지</b>까지 담는다 -
    /// 화면이 실패를 다시 계산하려고 보유량을 또 읽지 않게 하기 위함이다.
    /// </summary>
    public readonly struct BuildingConstructionStartResult
    {
        private BuildingConstructionStartResult(
            BuildingConstructionStartCode code, InventoryCostResult cost, BuildingConstructionSaveState state)
        {
            Code = code;
            Cost = cost;
            State = state;
        }

        /// <summary>어떻게 끝났는가.</summary>
        public BuildingConstructionStartCode Code { get; }

        /// <summary>비용 판정/지불의 결과. 비용까지 가 보지도 못한 갈래에서는 null이다.</summary>
        public InventoryCostResult Cost { get; }

        /// <summary>파일에 남은 건설 기록. 성공이 아니면 null이다.</summary>
        public BuildingConstructionSaveState State { get; }

        public bool Success => Code == BuildingConstructionStartCode.Success;

        internal static BuildingConstructionStartResult Started(BuildingConstructionSaveState state)
        {
            return new BuildingConstructionStartResult(
                BuildingConstructionStartCode.Success, InventoryCostResult.Payable, state);
        }

        internal static BuildingConstructionStartResult Rejected(BuildingConstructionStartCode code)
        {
            return new BuildingConstructionStartResult(code, null, null);
        }

        internal static BuildingConstructionStartResult CostRejected(InventoryCostResult cost)
        {
            return new BuildingConstructionStartResult(BuildingConstructionStartCode.CostRejected, cost, null);
        }

        internal static BuildingConstructionStartResult SaveFailed(InventoryCostResult cost)
        {
            return new BuildingConstructionStartResult(BuildingConstructionStartCode.SaveFailed, cost, null);
        }
    }

    /// <summary>
    /// 건설을 <b>시작하는 규칙 하나</b>를 소유한다. 씬도 UI도 모르는 순수 C# 경계이며(MonoBehaviour가
    /// 아니다), 그래서 시험이 엔진 수명주기 없이 이 규칙 전체를 그대로 돌려 볼 수 있다 -
    /// <see cref="Recovery.RecoveryStation"/>과 같은 구조다.
    ///
    /// <b>이 클래스가 정하는 것은 딱 하나다 - "지금 이 건물의 건설을 시작해도 되는가, 되면 무엇을
    /// 남기는가".</b> 비용의 판정과 차감은 <see cref="InventoryManager"/>가, 파일 쓰기는 넘겨받은
    /// 저장 함수가, 무엇을 지을지는 <see cref="BuildingDefinition"/>이 여전히 소유한다.
    ///
    /// <b>비용과 건설 기록은 한 번의 저장으로 함께 기록된다.</b> 비용을 먼저 <see cref="Save"/>하고
    /// 건설 기록을 나중에 저장하면 그 사이에 앱이 죽었을 때 <b>낸 것만 남고 지은 것은 사라진다</b>.
    /// 그래서 차감은 저장하지 않는 경로(<see cref="InventoryManager.TrySpendCostWithoutSave"/>)로
    /// 메모리에서만 끝내고, 건설 기록을 같은 문서에 얹은 <b>뒤에</b> 저장을 한 번 한다.
    ///
    /// <b>저장이 실패하면 아무 일도 없었던 것이 된다.</b> 방금 얹은 건설 기록을 도로 걷어 내고 비용을
    /// 되돌리며, 저장 성공을 뜻하는 알림(<see cref="InventoryManager.InventoryChanged"/>)도
    /// <see cref="ConstructionStarted"/>도 보내지 않는다 - 실패한 시작을 본 화면이 하나도 없어야 한다.
    ///
    /// <b>시각은 주입받은 시계로만 읽는다.</b> <see cref="DateTime.UtcNow"/>를 직접 부르지 않으므로
    /// 시험이 고정된 순간을 넣어 기록된 문자열을 글자 그대로 확인할 수 있고, 실제 게임은 UTC 시계를
    /// 그대로 넘긴다(로컬 시각을 넘겨도 UTC로 바꿔 적는다).
    /// </summary>
    public sealed class BuildingConstructionService
    {
        private readonly InventoryManager inventory;
        private readonly Func<SaveData> dataProvider;
        private readonly Func<bool> saveAction;
        private readonly Func<DateTime> utcNowProvider;

        /// <summary>지금 시작 처리를 도는 중인가. 같은 프레임에 확인 버튼이 두 번 눌리거나
        /// <see cref="ConstructionStarted"/> 처리기가 다시 시작을 부르는 경우를 막는다 - 그 경로가
        /// 열려 있으면 <b>기록은 하나인데 비용은 두 번</b> 빠지는 순간이 생긴다.</summary>
        private bool starting;

        /// <summary>
        /// 건설이 실제로 시작되어 <b>파일에 남은 뒤</b>에만 발생한다. 인자는 (건물 정의, 방금 남은 기록).
        /// 실패한 시작에서는 발생하지 않으며, 한 번의 시작에 정확히 한 번 발생한다.
        /// </summary>
        public event Action<BuildingDefinition, BuildingConstructionSaveState> ConstructionStarted;

        /// <param name="inventory">비용의 소유자. 이 서비스는 재화도 아이템도 직접 읽거나 쓰지 않는다.</param>
        /// <param name="dataProvider">저장 문서를 가져오는 함수(보통 <c>() =&gt; SaveSystem.Data</c>).</param>
        /// <param name="saveAction">파일 쓰기 함수(보통 <c>SaveSystem.Save</c>). 성공 여부를 돌려준다.</param>
        /// <param name="utcNowProvider">지금 시각(UTC)을 돌려주는 시계.</param>
        public BuildingConstructionService(
            InventoryManager inventory,
            Func<SaveData> dataProvider,
            Func<bool> saveAction,
            Func<DateTime> utcNowProvider)
        {
            this.inventory = inventory != null
                ? inventory
                : throw new ArgumentNullException(nameof(inventory));
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            this.saveAction = saveAction ?? throw new ArgumentNullException(nameof(saveAction));
            this.utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
        }

        /// <summary>
        /// 이 건물의 기록이 저장 문서에 있는지. <b>완성 시각이 지났는지는 보지 않는다</b> - 기록이
        /// 있다는 것 자체가 "이 건물은 이미 시작됐다"이고, 그것이 건설 버튼을 감추는 근거다.
        ///
        /// <b>없으면 만들지 않는다.</b> 조회는 문서를 한 글자도 바꾸지 않는다(목록이 null이어도
        /// 만들지 않는다) - 물어보기만 했는데 저장 항목이 생기면 "기록의 존재 = 시작됨"이라는 규칙이
        /// 무너진다.
        /// </summary>
        public bool HasConstruction(string buildingId)
        {
            return FindConstruction(buildingId) != null;
        }

        /// <summary>같은 뜻의 편의 진입점. 정의가 null이면 false다.</summary>
        public bool HasConstruction(BuildingDefinition building)
        {
            return building != null && HasConstruction(building.BuildingId);
        }

        /// <summary>
        /// 이 건물의 기록을 찾아 돌려준다(없으면 null). 대조는 <see cref="StringComparison.Ordinal"/>
        /// 완전 일치이므로 '1'과 ' 1 ', 'inn'과 'Inn'은 서로 다른 건물이다 - 저장 키를 다듬는 곳은
        /// 어디에도 없다.
        ///
        /// 같은 id가 두 줄 있는 손상된 문서에서는 <b>처음 것</b>을 돌려준다(<see cref="InventoryManager"/>가
        /// 같은 Id의 아이템 항목을 다루는 방식과 같다).
        /// </summary>
        public BuildingConstructionSaveState FindConstruction(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return null;

            SaveData data = dataProvider();
            List<BuildingConstructionSaveState> states = data?.buildingConstructions;
            if (states == null) return null;

            for (int i = 0; i < states.Count; i++)
            {
                BuildingConstructionSaveState state = states[i];
                if (state == null) continue;
                if (string.Equals(state.buildingId, buildingId, StringComparison.Ordinal)) return state;
            }

            return null;
        }

        /// <summary>
        /// 이 건물의 비용을 지금 낼 수 있는지 <b>판정만</b> 한다 - 값을 바꾸지도, 저장하지도, 알림을
        /// 보내지도 않는다(<see cref="InventoryManager.EvaluateCost"/>가 그런 경로다). 정의가 없으면
        /// 판정할 근거가 없으므로 null이다.
        /// </summary>
        public InventoryCostResult EvaluateCost(BuildingDefinition building)
        {
            if (building == null) return null;
            return inventory.EvaluateCost(building.ToCostRequest());
        }

        /// <summary>
        /// 이 건물의 건설을 시작한다. 순서가 곧 규칙이다.
        ///
        /// <list type="number">
        ///   <item>무엇을 짓는지 확인한다(정의 / Building Id).</item>
        ///   <item>이미 도는 시작 처리가 있으면 그대로 돌아간다(중복 클릭).</item>
        ///   <item>같은 id의 기록이 이미 있으면 시작하지 않는다 - <b>비용을 건드리기 전에</b> 본다.</item>
        ///   <item>비용을 <b>다시</b> 판정한다. 화면에 보이던 판정을 믿지 않는다 - 팝업이 열려 있는
        ///         동안 다른 경로가 재화를 썼을 수 있다.</item>
        ///   <item>통과했으면 <b>저장하지 않는</b> 경로로 차감한다(메모리만).</item>
        ///   <item>건설 기록을 같은 문서에 얹는다.</item>
        ///   <item>저장을 <b>한 번</b> 한다. 실패하면 기록을 걷어 내고 비용을 되돌린 뒤 실패를 알린다.</item>
        ///   <item>성공했을 때만 인벤토리 표시 갱신을 알리고 <see cref="ConstructionStarted"/>를 보낸다.</item>
        /// </list>
        ///
        /// <b>비용이 0인 건물도 저장은 한 번 한다.</b> 낼 것이 없어도 남길 기록은 있기 때문이며,
        /// 그때도 갱신 알림은 정확히 한 번이다 - 성공했다는 사실을 화면이 한 방법으로만 알게 한다.
        /// </summary>
        public BuildingConstructionStartResult TryStartConstruction(BuildingDefinition building)
        {
            if (building == null || string.IsNullOrEmpty(building.BuildingId))
            {
                return BuildingConstructionStartResult.Rejected(BuildingConstructionStartCode.InvalidBuilding);
            }

            if (starting)
            {
                Debug.LogWarning($"[BuildingConstructionService] '{building.BuildingId}' 건설 시작이 이미 " +
                                 "처리 중입니다 - 이번 요청은 무시합니다(비용은 한 번만 빠집니다).");
                return BuildingConstructionStartResult.Rejected(BuildingConstructionStartCode.Reentrant);
            }

            starting = true;
            try
            {
                return StartInternal(building);
            }
            finally
            {
                starting = false;
            }
        }

        private BuildingConstructionStartResult StartInternal(BuildingDefinition building)
        {
            string buildingId = building.BuildingId;

            SaveData data = dataProvider();
            if (data == null)
            {
                Debug.LogError($"[BuildingConstructionService] 저장 문서를 얻지 못해 '{buildingId}' 건설을 " +
                               "시작하지 않았습니다 - 비용도 건드리지 않았습니다.");
                return BuildingConstructionStartResult.Rejected(BuildingConstructionStartCode.NoSaveData);
            }

            // 비용보다 <b>먼저</b> 본다 - 이미 지은 건물에 값을 치른 뒤 되돌리는 경로를 만들지 않는다.
            if (FindConstruction(buildingId) != null)
            {
                return BuildingConstructionStartResult.Rejected(BuildingConstructionStartCode.AlreadyStarted);
            }

            InventoryCostRequest request = building.ToCostRequest();

            // 판정을 한 번 더 하는 이유는 실패를 <b>값을 건드리기 전에</b> 확정하기 위해서다. 차감
            // 경로도 스스로 판정하지만, 그쪽의 실패는 이미 되돌릴 것이 생긴 뒤일 수 있다.
            InventoryCostResult evaluation = inventory.EvaluateCost(request);
            if (evaluation == null || !evaluation.Success)
            {
                return BuildingConstructionStartResult.CostRejected(evaluation);
            }

            InventoryCostResult spend = inventory.TrySpendCostWithoutSave(
                request, out InventoryCostReceipt receipt);
            if (spend == null || !spend.Success)
            {
                return BuildingConstructionStartResult.CostRejected(spend);
            }

            BuildingConstructionSaveState state = CreateState(buildingId, building.BuildTimeSeconds);

            if (data.buildingConstructions == null)
            {
                data.buildingConstructions = new List<BuildingConstructionSaveState>();
            }
            data.buildingConstructions.Add(state);

            if (!saveAction())
            {
                // 얹은 기록을 <b>그 항목만</b> 걷어 낸다 - 목록을 다시 만들거나 다른 항목의 순서를
                // 건드리지 않는다.
                data.buildingConstructions.Remove(state);
                inventory.RefundCostWithoutSave(receipt);

                Debug.LogError($"[BuildingConstructionService] '{buildingId}' 건설 시작을 저장하지 못해 " +
                               "요청을 취소했습니다 - 재화와 아이템, 건설 기록 모두 시작 전 그대로입니다.");
                return BuildingConstructionStartResult.SaveFailed(spend);
            }

            // 저장이 끝난 <b>뒤에만</b> 알린다. 비용이 0이어도 여기를 지나므로 성공의 신호는 언제나
            // 한 벌이다(갱신 알림 한 번 + 시작 이벤트 한 번).
            inventory.NotifyChangedAfterExternalSave();
            ConstructionStarted?.Invoke(building, state);

            return BuildingConstructionStartResult.Started(state);
        }

        /// <summary>
        /// 기록 한 줄을 만든다. 두 시각 모두 <see cref="SaveData.FormatTimestamp"/>를 지나므로 저장
        /// 파일 안의 시각 서식은 여전히 하나뿐이며, 건설 시간이 0초면 두 값이 같다(= 이미 완성).
        /// </summary>
        private BuildingConstructionSaveState CreateState(string buildingId, int buildTimeSeconds)
        {
            DateTime startedAtUtc = ToUtc(utcNowProvider());

            return new BuildingConstructionSaveState
            {
                buildingId = buildingId,
                startedAtUtc = SaveData.FormatTimestamp(startedAtUtc),
                completeAtUtc = SaveData.FormatTimestamp(AddSeconds(startedAtUtc, buildTimeSeconds)),
            };
        }

        /// <summary>시계가 로컬 시각을 돌려줘도 UTC로 맞춘다 - 기록되는 값이 시계의 Kind에 따라
        /// 달라지면 안 된다(지정되지 않았으면 이미 UTC인 것으로 본다).</summary>
        private static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            if (value.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return value;
        }

        /// <summary>완성 시각을 더한다. 표가 아무리 긴 시간을 적어도 <see cref="DateTime.MaxValue"/>를
        /// 넘기지 않는다 - 넘기면 예외가 나서 <b>낼 수 있었던 건설이 시작되지 않는다</b>.</summary>
        private static DateTime AddSeconds(DateTime startedAtUtc, int seconds)
        {
            if (seconds <= 0) return startedAtUtc;

            double remaining = (DateTime.MaxValue - startedAtUtc).TotalSeconds;
            return seconds >= remaining ? DateTime.MaxValue : startedAtUtc.AddSeconds(seconds);
        }
    }
}
