using DesktopWindow;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 어떤 키보드 입력이든 감지하면 캐릭터가 앞으로 살짝 튀었다가 원래 위치로 돌아온다.
    /// SpriteFlipbook의 Idle 루프와는 독립적으로 Transform 위치만 움직인다.
    /// GlobalKeyboardHook을 통해 이 앱이 비활성 상태여도(다른 앱 사용 중이어도) 반응한다.
    /// </summary>
    public class KeyPunchReaction : MonoBehaviour
    {
        [SerializeField] private float punchDistance = 0.15f;
        [SerializeField] private float punchOutDuration = 0.05f;
        [SerializeField] private float punchBackDuration = 0.12f;

        private Vector3 basePosition;
        private float timer;
        private bool isPunching;
        private bool returning;

        private void Awake()
        {
            basePosition = transform.localPosition;
        }

        private void Update()
        {
            if (GlobalKeyboardHook.AnyKeyDownThisFrame)
            {
                StartPunch();
            }

            if (isPunching)
            {
                UpdatePunch();
            }
        }

        private void StartPunch()
        {
            isPunching = true;
            returning = false;
            timer = 0f;
        }

        private void UpdatePunch()
        {
            timer += Time.deltaTime;

            if (!returning)
            {
                float t = Mathf.Clamp01(timer / punchOutDuration);
                transform.localPosition = basePosition + Vector3.right * (punchDistance * t);

                if (t >= 1f)
                {
                    returning = true;
                    timer = 0f;
                }
            }
            else
            {
                float t = Mathf.Clamp01(timer / punchBackDuration);
                transform.localPosition = Vector3.Lerp(basePosition + Vector3.right * punchDistance, basePosition, t);

                if (t >= 1f)
                {
                    transform.localPosition = basePosition;
                    isPunching = false;
                }
            }
        }
    }
}
