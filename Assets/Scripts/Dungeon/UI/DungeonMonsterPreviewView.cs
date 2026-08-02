using UnityEngine;
using UnityEngine.UI;

namespace Dungeon
{
    /// <summary>
    /// 등장 몬스터 미리보기 한 칸(item_monster). 아이콘 한 장을 그리는 것이 전부다 - 툴팁도, 이름도,
    /// 수량도, 등장 확률도, 전투 데이터와의 연결도 이 단계의 범위가 아니다.
    ///
    /// 표시할 이미지는 <see cref="MonsterDefinition.PreviewSprite"/>가 결정한다 - 이 뷰는 모션 프로필을
    /// 직접 읽지 않고, "무엇을 그릴지"의 판단(직접 지정한 이미지인지 Base Idle 첫 프레임인지)은 전부
    /// 몬스터 정의 쪽에 있다. Sprite를 직접 받는 오버로드는 몬스터 정의 없이 이미지 한 장만 보여줄 때
    /// 쓴다.
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

        /// <summary>몬스터 하나를 표시한다. 몬스터가 없거나 표시할 이미지를 구하지 못하면 빈 칸이 된다.</summary>
        public void Bind(MonsterDefinition monster)
        {
            ApplySprite(monster != null ? monster.PreviewSprite : null);
        }

        /// <summary>이미지 한 장을 직접 표시한다. null이면 빈 칸이 된다.</summary>
        public void Bind(Sprite sprite)
        {
            ApplySprite(sprite);
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
