using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 경험치 바 프리팹의 <b>시각 구조만</b> 재사용하기 위한 범용 진행도 표시 컴포넌트.
    /// 값이 무엇을 뜻하는지(경험치인지 행동력인지), 어디서 오는지 전혀 알지 못한다 - 호출부가
    /// <see cref="SetValue"/>/<see cref="SetRatio"/>로 자기 값을 주입한다.
    ///
    /// <b>PlayerProgress를 구독하지 않는다.</b> 캐릭터 행동력 바가 경험치 이벤트에 반응하면 캐릭터를
    /// 처치할 때마다 모든 리스트 항목의 행동력 표시가 함께 움직인다 - 그래서 이 컴포넌트는 이벤트
    /// 구독 자체를 갖고 있지 않고, 값 변경도 호출된 시점에만 일어난다.
    ///
    /// 경험치 HUD는 지금까지대로 <see cref="PlayerProgressDisplay"/>가 그린다(레벨업 연출 큐를 갖고
    /// 있어 단순 대입과 동작이 다르다). 같은 GameObject에 둘을 함께 붙이면 같은 Slider를 서로
    /// 덮어쓰므로 OnEnable에서 오류로 막는다.
    ///
    /// Slider는 표시 전용으로 쓴다 - Fill Image가 Sliced 타입이면 fillAmount로 텍스처를 잘라내지 않고
    /// Fill Rect의 폭을 바꾸므로, 나인슬라이스 양끝 모양을 유지한 채 진행률을 표현할 수 있다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProgressBarView : MonoBehaviour
    {
        /// <summary>Preview Fill을 이름으로 자동 탐색할 때 쓰는 이름(경험치 바 프리팹의 기존 이름).</summary>
        private const string PreviewFillName = "sp_ExpBar_Preview";

        [Tooltip("표시 전용 Slider. Min Value 0, Max Value 1, Whole Numbers Off. 비워두면 같은 " +
                 "GameObject에서 자동으로 찾는다.")]
        [SerializeField] private Slider fillSlider;

        [Tooltip("Main Fill 뒤에 깔리는 연출용 Fill(경험치 바에서 가져온 구조). 진행도 표시에는 " +
                 "Main Fill과 같은 값을 그대로 적용한다. 비워두면 자식에서 '" + PreviewFillName + "'을 찾고, " +
                 "그것도 없으면 사용하지 않는다.")]
        [SerializeField] private RectTransform previewFill;

        private bool resolved;

        private void OnEnable()
        {
            ResolveReferences();
        }

        /// <summary>현재값/최대값을 그대로 넣는다. 최대값이 0 이하면 빈 바로 그린다(0으로 나누지 않는다).</summary>
        public void SetValue(int current, int max)
        {
            SetRatio(max <= 0 ? 0f : (float)current / max);
        }

        /// <summary>0~1 진행률을 즉시 적용한다(연출 없음).</summary>
        public void SetRatio(float ratio)
        {
            ResolveReferences();

            float clamped = Mathf.Clamp01(ratio);
            if (fillSlider != null) fillSlider.SetValueWithoutNotify(clamped);

            if (previewFill == null) return;
            Vector2 anchorMax = previewFill.anchorMax;
            anchorMax.x = clamped;
            previewFill.anchorMax = anchorMax;
        }

        private void ResolveReferences()
        {
            if (resolved) return;
            resolved = true;

            if (fillSlider == null) fillSlider = GetComponent<Slider>();
            if (fillSlider == null)
            {
                Debug.LogError($"[ProgressBarView] '{name}': Slider를 찾지 못했습니다 - 진행도 막대가 " +
                               "전혀 갱신되지 않습니다. Inspector에서 Fill Slider를 연결하세요.", this);
            }

            if (previewFill == null)
            {
                Transform found = FindDeepChild(transform, PreviewFillName);
                if (found != null) previewFill = found as RectTransform;
            }

            if (GetComponent<PlayerProgressDisplay>() != null)
            {
                Debug.LogError($"[ProgressBarView] '{name}': 같은 GameObject에 PlayerProgressDisplay가 함께 " +
                               "붙어 있습니다. 둘이 같은 Slider를 번갈아 덮어쓰고, 이 막대가 경험치 값으로 " +
                               "덮어씌워집니다 - 하나만 남기세요.", this);
            }
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName) return child;

                Transform found = FindDeepChild(child, childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
