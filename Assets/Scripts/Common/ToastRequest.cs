using UnityEngine;

namespace Common
{
    /// <summary>
    /// 토스트 한 건의 요청 데이터. 업적/레벨업/아이템 획득/시스템 안내 등 향후 알림 종류가 늘어나도
    /// ToastManager.Show(ToastRequest)에 그대로 실어 보낼 수 있도록 필드를 넉넉히 잡아둔다.
    /// priority/mergeKey는 이번 작업에서는 저장만 하고 동작에 영향을 주지 않는다(향후 확장용 자리 표시).
    /// </summary>
    public struct ToastRequest
    {
        public string message;
        public Sprite icon;
        public Color color;
        public int priority;
        public string mergeKey;

        /// <summary>0 이하이면 ToastManager의 기본 visibleDuration을 사용한다.</summary>
        public float duration;

        public static ToastRequest FromMessage(string message)
        {
            return new ToastRequest
            {
                message = message,
                icon = null,
                color = Color.white,
                priority = 0,
                mergeKey = null,
                duration = 0f,
            };
        }
    }
}
