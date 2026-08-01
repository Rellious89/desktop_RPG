using UnityEngine;
using UnityEngine.UI;

namespace Dungeon
{
    /// <summary>
    /// 등장 몬스터 미리보기 한 칸(item_monster). 아이콘 한 장을 그리는 것이 전부다 - 툴팁도, 수량도,
    /// 등장 확률도, 전투 데이터와의 연결도 이 단계의 범위가 아니다.
    ///
    /// 표시할 이미지는 <see cref="DungeonMonsterPreviewEntry"/>가 그대로 들고 있는 Sprite이며,
    /// MonsterMotionProfile이나 전투 쪽 타입은 참조하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class DungeonMonsterPreviewView : MonoBehaviour
    {
        [Header("References (에디터에서 직접 연결한다 - 이름으로 찾지 않는다)")]
        [Tooltip("몬스터 이미지를 표시할 Image(sp_portrait). 이 칸에는 Image가 여럿 있으므로 " +
                 "반드시 직접 연결한다.")]
        [SerializeField] private Image iconImage;

        private bool missingIconWarned;

        /// <summary>지금 표시 중인 이미지. 검증/디버깅용 읽기 전용 값이다.</summary>
        public Sprite CurrentSprite => iconImage != null ? iconImage.sprite : null;

        /// <summary>미리보기 항목 하나를 표시한다. 항목이 없거나 이미지가 비어 있으면 빈 칸이 된다.</summary>
        public void Bind(DungeonMonsterPreviewEntry entry)
        {
            ApplySprite(entry != null ? entry.PreviewSprite : null);
        }

        /// <summary>표시를 비운다.</summary>
        public void Clear()
        {
            ApplySprite(null);
        }

        private void ApplySprite(Sprite sprite)
        {
            if (iconImage == null)
            {
                if (!missingIconWarned)
                {
                    missingIconWarned = true;
                    Debug.LogWarning($"[DungeonMonsterPreviewView] '{name}': 아이콘 Image가 연결되지 않아 " +
                                     "몬스터 미리보기를 표시할 수 없습니다 - sp_portrait의 Image를 연결하세요.", this);
                }
                return;
            }

            iconImage.sprite = sprite;

            // 프리팹에 따라 아이콘 오브젝트 자체가 꺼진 채 저장되어 있을 수 있어서, 보여줄 때는
            // GameObject를 먼저 켠다(컴포넌트만 켜면 화면에 나오지 않는다).
            if (sprite != null && !iconImage.gameObject.activeSelf) iconImage.gameObject.SetActive(true);

            // 스프라이트가 없는 Image가 흰 사각형으로 남지 않게 컴포넌트를 끈다.
            iconImage.enabled = sprite != null;
        }
    }
}
