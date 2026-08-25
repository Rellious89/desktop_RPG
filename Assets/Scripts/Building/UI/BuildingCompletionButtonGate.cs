using System;
using Common;
using UnityEngine;
using UnityEngine.UI;

namespace Building
{
    /// <summary>완공 여부만 읽어 메뉴 버튼을 해금한다. 건설 진행·저장·알림은 전혀 소유하지 않는다.</summary>
    [DisallowMultipleComponent]
    public sealed class BuildingCompletionButtonGate : MonoBehaviour
    {
        [SerializeField] private string buildingId;
        [SerializeField] private Button targetButton;
        private bool lastCompleted;
        private bool initialized;

        private void Awake()
        {
            if (targetButton == null) targetButton = GetComponent<Button>();
            Refresh();
        }

        private void Update()
        {
            bool completed = IsCompleted(buildingId);
            if (!initialized || completed != lastCompleted) Apply(completed);
        }

        private void Refresh() { Apply(IsCompleted(buildingId)); }
        private void Apply(bool completed)
        {
            initialized = true;
            lastCompleted = completed;
            if (targetButton != null) targetButton.interactable = completed;
        }

        public static bool IsCompleted(string id)
        {
            return BuildingCompletionPolicy.IsConfirmedCompleted(SaveSystem.Data, id, DateTime.UtcNow);
        }
    }
}
