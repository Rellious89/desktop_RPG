using System;
using Character;
using Recovery;

namespace Common
{
    /// <summary>
    /// 캐릭터 교체 패널과 HUD 확인창이 함께 쓰는 수동 교체 흐름.
    /// 일반 캐릭터는 기존 <see cref="CharacterRoster.TrySwitchToManual"/> 경로를 그대로 사용하고,
    /// 회복소에서 완료된 캐릭터만 기존 합류 트랜잭션을 먼저 거친 뒤 즉시 교체한다.
    /// </summary>
    public static class CharacterSwapFlow
    {
        private const string ReturnToastTable = "01_UI";
        private const string ReturnToastEntry = "125";

        /// <summary>
        /// UI가 선택/확인 버튼을 그릴 때 쓰는 판정. 회복 슬롯에 있다는 일반 차단 사유는
        /// <see cref="RecoveryStation.GetState"/>가 RecoveryComplete라고 확정한 경우에만 해제한다.
        /// 행동력 수치만 보고 완료를 추측하지 않는다.
        /// </summary>
        public static CharacterRoster.SwapBlockReason GetBlockReason(
            CharacterRoster roster, CharacterDefinition definition, RecoveryStation station = null)
        {
            if (roster == null) return CharacterRoster.SwapBlockReason.NotAvailable;

            CharacterRoster.SwapBlockReason reason = roster.GetSwapBlockReason(definition);
            if (reason != CharacterRoster.SwapBlockReason.InRecovery) return reason;

            RecoveryStation activeStation = station ?? RecoveryService.Station;
            return activeStation != null &&
                   activeStation.GetState(definition) == RecoveryCharacterState.RecoveryComplete
                ? CharacterRoster.SwapBlockReason.None
                : reason;
        }

        /// <summary>
        /// 확인 시점에 상태를 다시 검사하고, 완료된 회복 슬롯이면 기존 합류 API로 슬롯을 비운 뒤
        /// 기존 수동 교체 API를 실행한다. 합류 전 일반 교체 조건을 모두 먼저 확인하므로 이미 알려진
        /// 교체 실패 사유 때문에 슬롯만 빠지는 일을 막는다.
        /// </summary>
        public static bool TrySwitch(
            CharacterRoster roster,
            CharacterDefinition definition,
            out CharacterRoster.SwapBlockReason reason,
            out bool joinedFromRecovery,
            RecoveryStation station = null)
        {
            joinedFromRecovery = false;

            // TrySwitchToManual이 합류 뒤에야 검사하는 튜토리얼 권한을 먼저 확인한다. 이 검사를
            // 생략하면 교체는 막혔는데 회복 슬롯만 비워지는 부분 성공이 생길 수 있다.
            if (definition != null && !Quest.TutorialFlowPolicy.CanSwitchCharacter(definition.CharacterId))
            {
                reason = CharacterRoster.SwapBlockReason.TutorialLocked;
                return false;
            }

            reason = GetBlockReason(roster, definition, station);
            if (reason != CharacterRoster.SwapBlockReason.None) return false;

            RecoveryStation activeStation = station ?? RecoveryService.Station;
            bool recoveryComplete = activeStation != null &&
                                    activeStation.GetState(definition) == RecoveryCharacterState.RecoveryComplete;

            if (recoveryComplete)
            {
                if (!activeStation.TryJoinCompleted(definition, out CharacterDefinition joined) || joined == null)
                {
                    reason = CharacterRoster.SwapBlockReason.InRecovery;
                    return false;
                }

                joinedFromRecovery = true;
            }

            if (roster.TrySwitchToManual(definition, out reason)) return true;

            if (joinedFromRecovery)
            {
                // 위에서 동기적으로 사전 검증했으므로 정상 구성에서는 도달하지 않는다. Runtime Actor가
                // 적용 도중 실패한 경우 기존 TryApply 롤백 덕분에 Current는 유지되지만, 합류 저장은 이미
                // 완료됐으므로 이 사실을 숨기지 않는다.
                UnityEngine.Debug.LogError(
                    $"[CharacterSwapFlow] '{definition.CharacterId}'의 회복소 합류 뒤 캐릭터 적용에 실패했습니다" +
                    $"(사유: {reason}). 캐릭터는 파티에 복귀했지만 현재 캐릭터는 유지됩니다.");
            }

            return false;
        }

        /// <summary>회복 완료 캐릭터의 합류와 교체가 모두 성공한 뒤에만 복귀 토스트를 표시한다.</summary>
        public static void ShowRecoveryReturnToast(CharacterDefinition definition)
        {
            if (definition == null || ToastManager.Instance == null) return;

            var text = new LocalizedTextReference
            {
                TableReference = ReturnToastTable,
                TableEntryReference = ReturnToastEntry,
            };
            string localized = text.GetLocalizedString(CharacterNameBinding.GetCurrent(definition));
            if (string.IsNullOrEmpty(localized) ||
                localized.StartsWith("No translation found", StringComparison.Ordinal)) return;

            ToastManager.Instance.Show(localized);
        }
    }
}
