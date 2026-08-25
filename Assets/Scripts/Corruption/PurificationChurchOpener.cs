using System;
using Building;
using Common;
using UnityEngine;
using UnityEngine.UI;

namespace Corruption
{
    /// <summary>ChurchSlot의 기존 UIAnchor 투영 결과 위에 있는 Interaction_Church 버튼을 패널에 연결한다.</summary>
    public sealed class PurificationChurchOpener : MonoBehaviour
    {
        [SerializeField] private PurificationPanel panel;
        [SerializeField] private string buildingId = "2";
        private Button button;
        private void Awake() { button = GetComponentInChildren<Button>(true); if (panel == null) panel = FindPanel(); }
        private void OnEnable() { if (button != null) button.onClick.AddListener(Open); }
        private void OnDisable() { if (button != null) button.onClick.RemoveListener(Open); }
        private void Update()
        {
            // Interaction_Church 자체는 TownInteractionLayer가 마을 밖에서 숨긴다. 여기서는 건설 완료
            // 여부만 자식 버튼에 반영해 완성 전 교회 진입을 막는다.
            if (button != null && button.gameObject.activeSelf != IsComplete()) button.gameObject.SetActive(IsComplete());
        }
        private void Open() { if (IsComplete() && panel != null) panel.Open(); }
        private bool IsComplete()
        {
            return BuildingCompletionPolicy.IsConfirmedCompleted(SaveSystem.Data, buildingId, DateTime.UtcNow);
        }
        private static PurificationPanel FindPanel() { PurificationPanel[] all = Resources.FindObjectsOfTypeAll<PurificationPanel>(); return all != null && all.Length > 0 ? all[0] : null; }
    }
}
