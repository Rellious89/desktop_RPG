using DesktopWindow;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 어떤 키보드 입력이든 감지하면 캐릭터가 앞으로 살짝 이동했다가 원래 위치로 돌아온다.
    /// SpriteFlipbook의 Idle 루프와는 독립적으로 Transform 위치만 움직인다.
    /// GlobalKeyboardHook을 통해 이 앱이 비활성 상태여도(다른 앱 사용 중이어도) 반응한다.
    /// </summary>
    public class AttackMovement : MonoBehaviour
    {
        [SerializeField] private float moveDistance = 0.15f;
        [SerializeField] private float moveOutDuration = 0.05f;
        [SerializeField] private float moveBackDuration = 0.12f;

        private Vector3 basePosition;
        private float timer;
        private bool isMoving;
        private bool returning;

        private void Awake()
        {
            basePosition = transform.localPosition;
        }

        private void Update()
        {
            if (GlobalKeyboardHook.AnyKeyDownThisFrame)
            {
                StartMove();
            }

            if (isMoving)
            {
                UpdateMove();
            }
        }

        private void StartMove()
        {
            isMoving = true;
            returning = false;
            timer = 0f;
        }

        private void UpdateMove()
        {
            timer += Time.deltaTime;

            if (!returning)
            {
                float t = Mathf.Clamp01(timer / moveOutDuration);
                transform.localPosition = basePosition + Vector3.right * (moveDistance * t);

                if (t >= 1f)
                {
                    returning = true;
                    timer = 0f;
                }
            }
            else
            {
                float t = Mathf.Clamp01(timer / moveBackDuration);
                transform.localPosition = Vector3.Lerp(basePosition + Vector3.right * moveDistance, basePosition, t);

                if (t >= 1f)
                {
                    transform.localPosition = basePosition;
                    isMoving = false;
                }
            }
        }
    }
}
