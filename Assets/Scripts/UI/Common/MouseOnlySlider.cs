using UnityEngine.EventSystems;

/// <summary>
/// Keeps the standard uGUI pointer/drag behavior but ignores UI navigation moves.
/// Slider.OnMove changes its value even when Navigation is set to None, so the
/// volume option uses this component instead of the built-in Slider.
/// </summary>
public sealed class MouseOnlySlider : UnityEngine.UI.Slider
{
    public override void OnMove(AxisEventData eventData)
    {
        // Intentionally do not call base.OnMove: Left/Right (including A/D) must
        // never change the value, even after a pointer click has selected us.
    }
}
