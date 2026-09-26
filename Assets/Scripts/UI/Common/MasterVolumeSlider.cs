using Common;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Keeps a uGUI slider in sync with the global master volume. Dragging applies immediately;
/// persistence happens when the pointer is released or after a brief delay for non-pointer changes.
/// </summary>
[RequireComponent(typeof(UnityEngine.UI.Slider))]
public sealed class MasterVolumeSlider : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IEndDragHandler
{
    private const float DeferredSaveDelay = 0.4f;

    private UnityEngine.UI.Slider slider;
    private bool pointerHeld;
    private bool savePending;
    private float lastChangeTime;

    private void Awake()
    {
        slider = GetComponent<UnityEngine.UI.Slider>();
    }

    private void OnEnable()
    {
        slider.onValueChanged.AddListener(HandleValueChanged);
        AudioManager.OnMasterVolumeChanged += Refresh;
        Refresh(AudioManager.Instance != null ? AudioManager.Instance.MasterVolume : 1f);
    }

    private void OnDisable()
    {
        FlushSave();
        slider.onValueChanged.RemoveListener(HandleValueChanged);
        AudioManager.OnMasterVolumeChanged -= Refresh;
        pointerHeld = false;
    }

    private void Update()
    {
        if (savePending && !pointerHeld && Time.unscaledTime - lastChangeTime >= DeferredSaveDelay)
        {
            FlushSave();
        }
    }

    private void HandleValueChanged(float value)
    {
        if (AudioManager.Instance == null) return;
        AudioManager.Instance.SetMasterVolume(value);
        savePending = true;
        lastChangeTime = Time.unscaledTime;
    }

    private void Refresh(float value)
    {
        slider.SetValueWithoutNotify(value);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) pointerHeld = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        pointerHeld = false;
        FlushSave();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        pointerHeld = false;
        FlushSave();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) FlushSave();
    }

    private void FlushSave()
    {
        if (!savePending || AudioManager.Instance == null) return;
        AudioManager.Instance.SaveMasterVolume();
        savePending = false;
    }
}
