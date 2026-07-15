using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// SoundToggleButton/HudToggleButton이 공유하는 On/Off 표시 로직. 색상은 건드리지 않는다 -
    /// onGraphic/offGraphic 각각에 에디터에서 미리 설정해둔 이미지 소스(스프라이트/색)를 그대로 쓰고,
    /// 이 클래스는 상태에 맞는 쪽만 활성화하고 반대쪽은 비활성화하는 역할만 한다.
    /// </summary>
    public static class ToggleButtonVisual
    {
        public static void Apply(bool isOn, Image onGraphic, Image offGraphic)
        {
            if (onGraphic != null) onGraphic.gameObject.SetActive(isOn);
            if (offGraphic != null) offGraphic.gameObject.SetActive(!isOn);
        }
    }
}
