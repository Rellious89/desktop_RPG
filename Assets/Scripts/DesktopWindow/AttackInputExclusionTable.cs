using System.Collections.Generic;
using UnityEngine;

namespace DesktopWindow
{
    /// <summary>
    /// <b>공격 입력으로 쓰지 않을 키 목록</b>. UI 단축키(ESC 등)는 눌러도 공격/콤보/누적 충전/행동력
    /// 소모가 일어나면 안 되는데, 그 판단을 코드에 `if (key == Escape)`처럼 박아 두지 않고 이 에셋
    /// 하나로 모아 관리한다.
    ///
    /// <b>제외는 공통 입력 분류 단계에서 일어난다.</b> <see cref="GlobalKeyboardHook"/>이 키를 식별하는
    /// 순간 이 표를 보고 걸러내므로, 제외 키는 애초에 `AnyKeyDownThisFrame`(공격/콤보/누적 입력이 보는
    /// 신호)에 포함되지 않는다 - 특정 캐릭터나 특정 모션에서만 예외 처리하는 경로가 없고, 모든 캐릭터와
    /// 콤보·누적 공격·행동력 시스템에 자동으로 같게 적용된다.
    ///
    /// 지원 키 범위는 글로벌 후크가 Virtual Key로 변환할 수 있는 A-Z / 0-9 / F1-F15 / Escape다
    /// (<see cref="GlobalKeyboardHook.KeyCodeToVirtualKey"/>). 그 밖의 키를 넣으면 Windows 빌드에서는
    /// 제외되지 않으므로 등록 시 경고를 남긴다.
    ///
    /// 이 표에 없는 키의 기존 전투 동작은 전혀 달라지지 않는다.
    /// </summary>
    [CreateAssetMenu(fileName = "AttackInputExclusionTable", menuName = "Input/Attack Input Exclusion Table")]
    public class AttackInputExclusionTable : ScriptableObject
    {
        [Tooltip("공격 입력에서 제외할 키. 여기 있는 키는 눌러도 공격/콤보/충전/행동력에 아무 영향이 없다. " +
                 "UI 단축키를 추가할 때 이 목록에만 넣으면 된다.")]
        [SerializeField] private List<KeyCode> excludedKeys = new List<KeyCode> { KeyCode.Escape };

        public IReadOnlyList<KeyCode> ExcludedKeys =>
            excludedKeys ?? (IReadOnlyList<KeyCode>)System.Array.Empty<KeyCode>();

        /// <summary>이 키가 공격 입력에서 제외되는지. 목록이 짧아(현재 1개) 선형 검색으로 충분하다.</summary>
        public bool IsExcludedFromAttack(KeyCode key)
        {
            if (key == KeyCode.None || excludedKeys == null) return false;

            for (int i = 0; i < excludedKeys.Count; i++)
            {
                if (excludedKeys[i] == key) return true;
            }
            return false;
        }
    }
}
