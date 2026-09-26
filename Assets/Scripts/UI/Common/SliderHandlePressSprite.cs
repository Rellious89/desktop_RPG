using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Swaps a uGUI Slider handle sprite for the duration of a pointer press.
/// Attach this beside the Slider so dragging remains owned by the Slider.
/// </summary>
[RequireComponent(typeof(UnityEngine.UI.Slider))]
public sealed class SliderHandlePressSprite : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, ICancelHandler
{
    [SerializeField] private UnityEngine.UI.Image handleImage;
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite pressedSprite;

    private UnityEngine.UI.Slider slider;
    private int pressedPointerId = int.MinValue;

    private void Awake()
    {
        slider = GetComponent<UnityEngine.UI.Slider>();
    }

    private void OnEnable()
    {
        ShowNormal();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left ||
            slider == null || !slider.IsActive() || !slider.IsInteractable())
        {
            return;
        }

        pressedPointerId = eventData.pointerId;
        if (handleImage != null && pressedSprite != null)
        {
            handleImage.sprite = pressedSprite;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId == pressedPointerId)
        {
            ShowNormal();
        }
    }

    public void OnCancel(BaseEventData eventData)
    {
        ShowNormal();
    }

    private void OnDisable()
    {
        ShowNormal();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            ShowNormal();
        }
    }

    private void ShowNormal()
    {
        pressedPointerId = int.MinValue;
        if (handleImage != null && normalSprite != null)
        {
            handleImage.sprite = normalSprite;
        }
    }
}
