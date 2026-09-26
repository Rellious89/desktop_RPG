using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Plays a one-shot sprite animation above a uGUI Button without taking over its Sprite Swap state.
/// </summary>
public sealed class ButtonPressSpriteAnimation : MonoBehaviour, IPointerDownHandler, ISubmitHandler
{
    [SerializeField] private UnityEngine.UI.Button button;
    [SerializeField] private UnityEngine.UI.Image pressOverlay;
    [SerializeField] private Animator pressAnimator;
    [SerializeField] private AnimationClip pressClip;

    private Coroutine hideRoutine;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            PlayOnce();
        }
    }

    public void OnSubmit(BaseEventData eventData)
    {
        PlayOnce();
    }

    private void PlayOnce()
    {
        if (button == null || !button.IsActive() || !button.IsInteractable() ||
            pressOverlay == null || pressAnimator == null || pressClip == null)
        {
            return;
        }

        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
        }

        pressAnimator.Play("Base Layer." + pressClip.name, 0, 0f);
        pressAnimator.Update(0f);
        pressOverlay.enabled = true;
        hideRoutine = StartCoroutine(HideAfterPlayback());
    }

    private IEnumerator HideAfterPlayback()
    {
        yield return new WaitForSecondsRealtime(pressClip.length);
        pressOverlay.enabled = false;
        hideRoutine = null;
    }

    private void OnDisable()
    {
        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        if (pressOverlay != null)
        {
            pressOverlay.enabled = false;
        }
    }
}
