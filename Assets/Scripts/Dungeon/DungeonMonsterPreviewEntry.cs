using System;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// 던전 상세의 <b>등장 몬스터 미리보기 한 칸</b>. 이름 그대로 "미리보기"이며, 전투에 실제로 어떤
    /// 몬스터가 나오는지를 정하는 데이터가 아니다.
    ///
    /// <b>MonsterMotionProfile을 참조하지 않는다.</b> 입장 UI는 아이콘 한 장만 필요하고, 모션 프로필을
    /// 여기서 들고 있으면 이 화면을 그리려고 전투 쪽 데이터를 끌어오게 된다 - 그래서 표시할 Sprite만
    /// 담고, 그 Sprite도 필드에 직접 노출하지 않고 읽기 전용 속성으로만 꺼내 쓴다. 몬스터 큐/전투와의
    /// 연결은 이 MVP의 범위가 아니며, 필요해지면 이 항목에 식별자를 추가하는 방향으로 확장한다.
    /// </summary>
    [Serializable]
    public class DungeonMonsterPreviewEntry
    {
        [Tooltip("몬스터 미리보기 칸(item_monster)의 sp_portrait에 표시할 이미지. 비어 있으면 그 칸은 " +
                 "이미지 없이 빈 칸으로 표시된다.")]
        [SerializeField] private Sprite previewSprite;

        public DungeonMonsterPreviewEntry()
        {
        }

        public DungeonMonsterPreviewEntry(Sprite previewSprite)
        {
            this.previewSprite = previewSprite;
        }

        /// <summary>미리보기 칸에 그릴 이미지. 없으면 null이며, 그리는 쪽이 Image를 비우고 끈다.</summary>
        public Sprite PreviewSprite => previewSprite;

        /// <summary>표시할 이미지가 지정되어 있는지 여부.</summary>
        public bool HasPreview => previewSprite != null;
    }
}
