using System;
using UnityEngine;
using UnityEngine.Events;

namespace Common
{
    /// <summary>
    /// 옵션 패널의 배율 버튼 중 하나를 선택한다. 실제 배율을 저장하므로 버튼 값이 바뀌어도
    /// 다음에 패널을 열 때 저장값과 가장 가까운 선택지를 사용할 수 있다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SizeSelectionGroup : MonoBehaviour
    {
        [Serializable]
        private sealed class ScaleOption
        {
            public UnityEngine.UI.Button button;
            [Min(0.01f)] public float scale = 1f;
        }

        [Header("배율 선택지 (1 = 100%)")]
        [SerializeField] private ScaleOption[] scaleOptions = Array.Empty<ScaleOption>();

        [Header("선택 상태")]
        [Tooltip("선택된 버튼의 기본/호버/포커스 이미지. 선택되지 않은 버튼은 씬에 설정된 원래 스프라이트를 사용한다.")]
        [SerializeField] private Sprite selectedSprite;

        private UnityEngine.UI.Image[] buttonImages;
        private Sprite[] normalSprites;
        private UnityEngine.UI.SpriteState[] normalStates;
        private UnityAction[] clickCallbacks;
        private int selectedIndex = -1;

        private void Awake()
        {
            int count = scaleOptions?.Length ?? 0;
            buttonImages = new UnityEngine.UI.Image[count];
            normalSprites = new Sprite[count];
            normalStates = new UnityEngine.UI.SpriteState[count];
            clickCallbacks = new UnityAction[count];

            for (int i = 0; i < count; i++)
            {
                UnityEngine.UI.Button button = scaleOptions[i]?.button;
                if (button == null) continue;

                int index = i;
                clickCallbacks[i] = () => Select(index);
                buttonImages[i] = button.targetGraphic as UnityEngine.UI.Image;
                normalSprites[i] = buttonImages[i] != null ? buttonImages[i].sprite : null;
                normalStates[i] = button.spriteState;
            }
        }

        private void OnEnable()
        {
            for (int i = 0; i < clickCallbacks.Length; i++)
            {
                if (clickCallbacks[i] != null)
                    scaleOptions[i].button.onClick.AddListener(clickCallbacks[i]);
            }

            RefreshFromSavedScale();
        }

        private void OnDisable()
        {
            for (int i = 0; i < clickCallbacks.Length; i++)
            {
                if (clickCallbacks[i] != null)
                    scaleOptions[i].button.onClick.RemoveListener(clickCallbacks[i]);
            }
        }

        private void RefreshFromSavedScale()
        {
            float savedScale = UiSettingsSaveSystem.Load()?.sizeScale ?? 1f;
            if (savedScale <= 0f || float.IsNaN(savedScale) || float.IsInfinity(savedScale))
                savedScale = 1f;

            selectedIndex = FindClosestIndex(savedScale);
            if (selectedIndex < 0) return;

            float scale = scaleOptions[selectedIndex].scale;
            StageVisualRootController.Instance?.SetUserScale(scale);

            // Inspector에서 배율값이 바뀌었다면 과거 저장값을 현재 선택지로 맞춘다.
            if (!Mathf.Approximately(savedScale, scale))
                UiSettingsSaveSystem.SaveSizeScale(scale);

            RefreshVisuals();
        }

        private void Select(int index)
        {
            if (index < 0 || index >= scaleOptions.Length || !IsValid(scaleOptions[index])) return;

            selectedIndex = index;
            float scale = scaleOptions[index].scale;
            StageVisualRootController.Instance?.SetUserScale(scale);
            UiSettingsSaveSystem.SaveSizeScale(scale);
            RefreshVisuals();
        }

        private int FindClosestIndex(float scale)
        {
            int closest = -1;
            float difference = float.PositiveInfinity;

            for (int i = 0; i < scaleOptions.Length; i++)
            {
                if (!IsValid(scaleOptions[i])) continue;

                float candidateDifference = Mathf.Abs(scaleOptions[i].scale - scale);
                if (candidateDifference < difference)
                {
                    closest = i;
                    difference = candidateDifference;
                }
            }

            return closest;
        }

        private static bool IsValid(ScaleOption option)
        {
            return option != null && option.button != null && option.scale > 0f &&
                   !float.IsNaN(option.scale) && !float.IsInfinity(option.scale);
        }

        private void RefreshVisuals()
        {
            if (selectedSprite == null) return;

            for (int i = 0; i < scaleOptions.Length; i++)
            {
                UnityEngine.UI.Button button = scaleOptions[i]?.button;
                UnityEngine.UI.Image image = buttonImages[i];
                if (button == null || image == null) continue;

                bool selected = i == selectedIndex;
                // Button의 SpriteSwap이 소유하는 overrideSprite는 건드리지 않는다.
                // 기본 Sprite와 선택 중의 hover/focus Sprite만 바꿔 눌림 애니메이션을 유지한다.
                image.sprite = selected ? selectedSprite : normalSprites[i];
                UnityEngine.UI.SpriteState state = normalStates[i];
                if (selected)
                {
                    state.highlightedSprite = selectedSprite;
                    state.selectedSprite = selectedSprite;
                }
                button.spriteState = state;
            }
        }
    }
}
