using UnityEngine;

namespace Common
{
    /// <summary>
    /// UI 이벤트와 Win32 폴링 양쪽에서 공유하는 롱프레스 판정 상태다. 대기 중 허용 거리보다 움직이거나
    /// 대상 영역을 벗어나면 그 누름은 취소되고, 활성화된 뒤에는 PointerUp까지 유지된다.
    /// </summary>
    public sealed class LongPressDragGesture
    {
        private Vector2 pressPosition;
        private float pressTime;

        public bool IsWaiting { get; private set; }
        public bool IsActive { get; private set; }

        public void Press(Vector2 position, float time)
        {
            pressPosition = position;
            pressTime = time;
            IsWaiting = true;
            IsActive = false;
        }

        public void Move(Vector2 position, float allowedMovementPixels)
        {
            if (!IsWaiting || IsActive) return;

            float distance = Mathf.Max(0f, allowedMovementPixels);
            if ((position - pressPosition).sqrMagnitude > distance * distance) Cancel();
        }

        public void Exit()
        {
            if (IsWaiting && !IsActive) Cancel();
        }

        public bool TryActivate(float time, float holdSeconds)
        {
            if (!IsWaiting || IsActive) return false;
            if (time - pressTime < Mathf.Max(0.05f, holdSeconds)) return false;

            IsWaiting = false;
            IsActive = true;
            return true;
        }

        public bool Release()
        {
            bool wasActive = IsActive;
            Cancel();
            return wasActive;
        }

        public void Cancel()
        {
            IsWaiting = false;
            IsActive = false;
        }
    }
}
