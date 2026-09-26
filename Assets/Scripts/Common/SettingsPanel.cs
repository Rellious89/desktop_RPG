namespace Common
{
    /// <summary>설정 패널의 열기, 닫기, ESC 및 포커스 처리를 공통 모달 흐름에 연결한다.</summary>
    [UnityEngine.DisallowMultipleComponent]
    public sealed class SettingsPanel : ModalPanel
    {
        protected override void RefreshContents()
        {
            // 설정 컨트롤은 각 컴포넌트가 자신의 값을 표시한다.
        }
    }
}
