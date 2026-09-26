using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CommonEditor.Tests
{
    public sealed class MouseOnlySliderTests
    {
        [TestCase(MoveDirection.Left)]
        [TestCase(MoveDirection.Right)]
        public void NavigationMove_DoesNotChangeValue(MoveDirection direction)
        {
            var eventSystemObject = new GameObject("Test EventSystem", typeof(EventSystem));
            var sliderObject = new GameObject("Test Slider", typeof(RectTransform), typeof(MouseOnlySlider));
            try
            {
                var slider = sliderObject.GetComponent<MouseOnlySlider>();
                slider.minValue = 0f;
                slider.maxValue = 1f;
                slider.value = 0.5f;

                var eventData = new AxisEventData(eventSystemObject.GetComponent<EventSystem>())
                {
                    moveDir = direction
                };
                eventSystemObject.GetComponent<EventSystem>().SetSelectedGameObject(sliderObject);
                slider.OnMove(eventData);

                Assert.That(slider.value, Is.EqualTo(0.5f));

                // Direct value changes remain available to pointer handling.
                slider.value = 0.75f;
                Assert.That(slider.value, Is.EqualTo(0.75f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sliderObject);
                UnityEngine.Object.DestroyImmediate(eventSystemObject);
            }
        }
    }
}
