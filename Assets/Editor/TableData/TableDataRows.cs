using System;
using System.Collections.Generic;
using Enemy;
using Inventory;
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

    /// <summary>Monster.csv 한 행.</summary>
    public sealed class MonsterRow
    {
        public int Line;
        public string Id = string.Empty;
        public LocalizedEntryRef Name;

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

        /// <summary>보상 아이템 id를 CSV에 적힌 순서 그대로.</summary>
        public readonly List<string> RewardItemIds = new List<string>();

        /// <summary><see cref="RewardItemIds"/>와 같은 순서로 찾아 둔 기존 ItemDefinition.
        /// <b>읽기만 한다</b> - 임포터는 수동 아이템 에셋을 절대 고치지 않는다.</summary>
        public readonly List<ItemDefinition> RewardItems = new List<ItemDefinition>();

        public int DisplayOrder;
        public bool Enabled;
    }

    /// <summary>
    /// 파싱과 검증을 마친 세 표. Rebuild는 이 스냅샷만 보고 에셋을 만든다 - CSV를 다시 읽지 않으므로
    /// "검증한 내용"과 "쓰는 내용"이 어긋날 수 없다.
    /// </summary>
    public sealed class TableDataSnapshot
    {
        public readonly List<WorldRow> Worlds = new List<WorldRow>();
        public readonly List<MonsterRow> Monsters = new List<MonsterRow>();
        public readonly List<DungeonRow> Dungeons = new List<DungeonRow>();

        public readonly Dictionary<string, WorldRow> WorldsById = new Dictionary<string, WorldRow>(StringComparer.Ordinal);
        public readonly Dictionary<string, MonsterRow> MonstersById = new Dictionary<string, MonsterRow>(StringComparer.Ordinal);
        public readonly Dictionary<string, DungeonRow> DungeonsById = new Dictionary<string, DungeonRow>(StringComparer.Ordinal);
    }
}
