using System;
using System.Collections.Generic;
using Enemy;
using UnityEngine;

namespace TableDataEditor
{
    /// <summary>
    /// 해석이 끝난 Localization 참조. <b>Table GUID + Entry Key ID만</b> 들고 있다 - 카테고리 코드와
    /// 숫자 키는 사람이 CSV에 적는 표기이고, 에셋에 기록되는 것은 언제나 GUID와 Key ID다.
    /// <see cref="Resolved"/>가 false면 참조를 비운다.
    /// </summary>
    public struct LocalizedEntryRef
    {
        public bool Resolved;
        public Guid TableGuid;
        public long KeyId;

        public static LocalizedEntryRef None => default;
    }

    /// <summary>World.csv 한 행. 검증을 통과한 값만 채워지며, 통과하지 못한 칸은 기본값으로 남는다.</summary>
    public sealed class WorldRow
    {
        public int Line;
        public string Id = string.Empty;
        public LocalizedEntryRef Name;
        public int DisplayOrder;
        public bool Enabled;
    }

    /// <summary>
    /// Currency.csv 한 행. <b>currency_id는 저장 파일과 밸런스 테이블이 공유하는 키</b>라
    /// <see cref="ItemRow.Id"/>와 같은 무게를 가진다 - 값을 다듬거나 대체하는 경로는 어디에도 없고,
    /// 형식이 어긋나면 그 행 자체가 스냅샷에 들어가지 않는다.
    ///
    /// <b>보유 잔액은 여기에 없다.</b> 표가 말하는 것은 "이 게임에 어떤 재화가 있는가"이고, 얼마를
    /// 갖고 있는지는 저장 데이터의 몫이다.
    /// </summary>
    public sealed class CurrencyRow
    {
        public int Line;
        public string Id = string.Empty;
        public LocalizedEntryRef Name;

        /// <summary>재화를 표시할 때 쓰는 아이콘. 비어 있어도 된다(경고만 남는다).</summary>
        public Sprite Icon;

        public int DisplayOrder;
        public bool Enabled;
    }

    /// <summary>
    /// Item.csv 한 행. <b>item_id는 저장 파일의 키</b>라 다른 표의 id보다 무겁다 - 값을 다듬거나
    /// 대체하는 경로는 어디에도 없고, 형식이 어긋나면 그 행 자체가 스냅샷에 들어가지 않는다.
    /// </summary>
    public sealed class ItemRow
    {
        public int Line;
        public string Id = string.Empty;
        public LocalizedEntryRef Name;

        /// <summary>인벤토리 슬롯에 그릴 아이콘. 비어 있어도 된다(수량만 표시된다).</summary>
        public Sprite Icon;

        public int DisplayOrder;
        public bool Enabled;
    }

    /// <summary>
    /// 드롭 슬롯 하나가 실제로 채워진 경우의 값. <b>빈 슬롯은 아예 만들지 않는다</b> - "비었음"을
    /// 나타내는 특별한 값을 두면 읽는 쪽마다 그 규칙을 다시 알아야 한다.
    ///
    /// 아이템은 에셋이 아니라 <see cref="ItemId"/> 문자열로 들고 있다. Rebuild가 Item을 Monster보다
    /// 먼저 만들므로, 에셋 연결은 그때 만들어 둔 ItemDefinition으로 한 번에 한다(던전의 보상 목록과
    /// 같은 방식이다).
    /// </summary>
    public struct MonsterDropRow
    {
        /// <summary>CSV에 적힌 그대로의 item_id. Item.csv에 있는 활성 행만 여기까지 온다.</summary>
        public string ItemId;

        /// <summary>만분율 확률. 10000이 100%, 1이 0.01%다. <b>1 이상 10000 이하만 여기까지 온다</b> -
        /// 0(떨어지지 않는 칸)은 경고를 남기고 슬롯 자체를 만들지 않는다.</summary>
        public int ChanceBasisPoints;

        /// <summary>한 번 드롭될 때의 개수. 1 이상만 여기까지 온다.</summary>
        public int Count;
    }

    /// <summary>Monster.csv 한 행.</summary>
    public sealed class MonsterRow
    {
        public int Line;
        public string Id = string.Empty;
        public LocalizedEntryRef Name;

        /// <summary>
        /// 묶음(분류)용 참조. 비어 있을 수 있으며, 값이 있으면 Monster.csv에 실제로 있는 다른 행의
        /// monster_id다. <b>여기서 무엇도 상속하지 않는다</b> - base가 가리키는 행의 값이 이 행에
        /// 자동으로 채워지는 일은 없고, 모든 칸은 각자의 행이 스스로 적는다.
        /// </summary>
        public string BaseMonsterId = string.Empty;

        /// <summary>채워진 드롭 슬롯만 CSV에 적힌 슬롯 순서 그대로. 최대
        /// <see cref="TableDataColumns.MonsterDropSlotCount"/>개다.</summary>
        public readonly List<MonsterDropRow> Drops = new List<MonsterDropRow>();

        /// <summary>
        /// 처치 재화 보상의 재화 id. 비어 있으면 <b>이 몬스터는 표에서 재화를 지정하지 않은 것</b>이며,
        /// 그때 <see cref="CurrencyAmountMin"/>/<see cref="CurrencyAmountMax"/>는 둘 다 0이다(CSV의 금액
        /// 칸도 비어 있어야 한다 - 0을 적어 두는 것도 오류다).
        ///
        /// 값이 있으면 <b>Currency.csv에 실제로 있는 활성 행</b>을 가리켜야 한다 - 비교는 다듬지 않은
        /// 값끼리의 Ordinal 완전 일치이고, 없는 재화나 enabled=0인 재화를 가리키면 오류다
        /// (<see cref="TableDataSnapshot.CurrenciesById"/>로 조회한다).
        /// </summary>
        public string CurrencyId = string.Empty;

        /// <summary>재화 보상의 최소 금액. 0 이상이며 <see cref="CurrencyAmountMax"/> 이하만 여기까지 온다.</summary>
        public int CurrencyAmountMin;

        /// <summary>재화 보상의 최대 금액. <see cref="CurrencyAmountMin"/> 이상만 여기까지 온다(같아도 된다).</summary>
        public int CurrencyAmountMax;

        /// <summary>참조에 쓰는 world_id(앞뒤 공백을 다듬은 값). 비어 있을 수 있다.</summary>
        public string WorldId = string.Empty;

        public MonsterMotionProfile MotionProfile;

        /// <summary>직접 지정한 미리보기 Sprite. 비어 있으면 런타임이 Base Idle 첫 프레임으로 대신한다.</summary>
        public Sprite PreviewSprite;

        public int MaxDurability = 1;
        public int DisplayOrder;
        public bool Enabled;
    }

    /// <summary>Dungeon.csv 한 행.</summary>
    public sealed class DungeonRow
    {
        public int Line;
        public string Id = string.Empty;
        public LocalizedEntryRef Name;
        public string WorldId = string.Empty;
        public Sprite RepresentativeSprite;

        /// <summary>등장 몬스터 id를 CSV에 적힌 순서 그대로. 공백은 다듬은 값이다.</summary>
        public readonly List<string> MonsterIds = new List<string>();

        /// <summary>보상 아이템 id를 CSV에 적힌 순서 그대로. monster_ids와 마찬가지로 <b>Item.csv의
        /// 행</b>을 가리키며, 실제 에셋 연결은 Rebuild가 그때 만든 ItemDefinition으로 한다 - 수동
        /// 아이템 에셋은 임포터의 출력 대상이 아니다.</summary>
        public readonly List<string> RewardItemIds = new List<string>();

        public int DisplayOrder;
        public bool Enabled;
    }

    /// <summary>
    /// 파싱과 검증을 마친 다섯 표. Rebuild는 이 스냅샷만 보고 에셋을 만든다 - CSV를 다시 읽지 않으므로
    /// "검증한 내용"과 "쓰는 내용"이 어긋날 수 없다.
    ///
    /// 목록의 순서는 <b>World → Currency → Item → Monster → Dungeon</b>이고, 이것이 검증과 생성
    /// 순서이기도 하다. 뒤의 표가 앞의 표를 참조하므로(Monster는 Currency와 Item을, Dungeon은 Item과
    /// Monster를 가리킨다), 가리켜지는 표가 언제나 먼저 온다. Currency를 Item보다 앞에 둔 것은 재화가
    /// 무엇도 참조하지 않는 가장 바깥 표라서다 - 아이템/몬스터가 재화를 가리키게 되어도 순서를 다시
    /// 뒤집을 필요가 없다.
    ///
    /// <b>모든 id 사전은 <see cref="StringComparer.Ordinal"/>이다.</b> 대소문자도 문화권 규칙도
    /// 끼어들지 않는 정확한 문자열 비교여야, CSV에 적힌 id와 조회에 쓰는 id가 같다는 것이 보장된다.
    /// </summary>
    public sealed class TableDataSnapshot
    {
        public readonly List<WorldRow> Worlds = new List<WorldRow>();
        public readonly List<CurrencyRow> Currencies = new List<CurrencyRow>();
        public readonly List<ItemRow> Items = new List<ItemRow>();
        public readonly List<MonsterRow> Monsters = new List<MonsterRow>();
        public readonly List<DungeonRow> Dungeons = new List<DungeonRow>();

        public readonly Dictionary<string, WorldRow> WorldsById = new Dictionary<string, WorldRow>(StringComparer.Ordinal);
        public readonly Dictionary<string, CurrencyRow> CurrenciesById = new Dictionary<string, CurrencyRow>(StringComparer.Ordinal);
        public readonly Dictionary<string, ItemRow> ItemsById = new Dictionary<string, ItemRow>(StringComparer.Ordinal);
        public readonly Dictionary<string, MonsterRow> MonstersById = new Dictionary<string, MonsterRow>(StringComparer.Ordinal);
        public readonly Dictionary<string, DungeonRow> DungeonsById = new Dictionary<string, DungeonRow>(StringComparer.Ordinal);
    }
}
