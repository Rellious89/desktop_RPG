using UnityEngine;

namespace DesktopWindow
{
    /// <summary>
    /// 데스크톱 상주 앱은 화면 대부분이 정지해 있으므로 프레임레이트를 낮춰 CPU/GPU 점유율을 줄인다.
    /// </summary>
    public class FpsLimiter : MonoBehaviour
    {
        [SerializeField] private int targetFrameRate = 30;

        private void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
        }
    }
}
