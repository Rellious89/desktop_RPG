using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// HUD UI가 아니라 HitEffectSpawner와 같은 "월드 스페이스 전투 연출"로 취급한다. 화면 고정 좌표나
    /// 월드 스페이스 고정 오프셋을 쓰지 않고, 피격체가 지정한 anchor Transform(보통 그 자식인
    /// DamageAnchor)의 위치를 그대로 스폰 기준점으로 쓴다 - anchor가 StageVisualRoot 하위 계층에
    /// 있으므로 위치/배율 변화가 Transform 계층을 통해 자동으로, 정확히 한 번만 반영된다(이 스크립트가
    /// 별도로 스케일을 곱하지 않는다).
    ///
    /// anchor를 Inspector에서 비워두면 이름이 "DamageAnchor"인 자식을 찾고, 그것도 없으면 이 오브젝트
    /// 자신(기존에 스폰 기준으로 쓰던 "ReceivePoint" 역할)을 그대로 쓴다 - DamageAnchor가 없는 기존
    /// Target도 예전과 동일하게 동작한다. Target과 마찬가지로 어떤 대상에도 붙여 재사용할 수 있다.
    ///
    /// 인스턴스를 풀링한다 - 예전에는 Spawn()마다 new GameObject + AddComponent&lt;TextMeshPro&gt;를
    /// 새로 만들었는데, 연타 중 이 호출이 반복되면(초당 최대 1/minSpawnInterval회) GC 압박이 쌓여
    /// 메인 스레드가 순간적으로 멎을 수 있다 - 이게 전역 키보드 후크/WndProc 응답을 늦춰서 키 입력 중
    /// 마우스가 끊기는 것처럼 보이는 원인 중 하나였다. Awake에서 미리 poolSize만큼 만들어두고
    /// Spawn()은 그중 하나를 재활용한다.
    /// </summary>
    public class DamageNumberSpawner : MonoBehaviour
    {
        [Header("생성 위치")]
        [Tooltip("데미지 숫자의 기본 출력 위치. 비워두면 자식 중 이름이 \"DamageAnchor\"인 Transform을 찾고, 그것도 없으면 이 오브젝트 자신을 쓴다.")]
        [SerializeField] private Transform anchor;
        [Tooltip("anchor의 로컬 좌표 기준 좌우 랜덤 폭(anchor의 스케일이 그대로 반영되므로 화면 고정 픽셀/월드 오프셋이 아니다).")]
        [SerializeField] private float randomHorizontalJitter = 0.1f;

        [Header("움직임")]
        [SerializeField] private float riseDistance = 0.4f;
        [SerializeField] private float duration = 0.6f;

        [Header("모양")]
        [SerializeField] private Color textColor = Color.red;
        [Tooltip("TMP 폰트 크기. 값을 올리면 그대로 직접 커진다.")]
        [SerializeField] private float fontSize = 15f;
        [SerializeField] private int sortingOrder = 10;

        [Header("연타 제한")]
        [Tooltip("이 시간(초)보다 짧은 간격으로는 새 숫자를 생성하지 않는다. 빠른 연타 시 과도하게 겹치는 것을 막는다.")]
        [SerializeField] private float minSpawnInterval = 0.05f;

        [Header("풀")]
        [Tooltip("미리 만들어두고 재사용할 데미지 숫자 오브젝트 개수. 연타 중 동시에 떠 있을 수 있는 최대 개수보다 넉넉하게 잡는다.")]
        [SerializeField] private int poolSize = 8;

        private float lastSpawnTime = -999f;
        private readonly Queue<DamageNumberPopup> pool = new Queue<DamageNumberPopup>();

        private void Awake()
        {
            if (anchor == null)
            {
                Transform found = transform.Find("DamageAnchor");
                // "ReceivePoint" 역할 - anchor도 DamageAnchor 자식도 없으면 피격체 자신의 위치를
                // 그대로 스폰 기준점으로 쓴다(예전부터 이 컴포넌트가 있던 오브젝트 자체가 사실상
                // 그 역할이었다).
                anchor = found != null ? found : transform;
            }

            for (int i = 0; i < poolSize; i++)
            {
                pool.Enqueue(CreatePooledInstance());
            }
        }

        private DamageNumberPopup CreatePooledInstance()
        {
            var go = new GameObject("DamageNumber(Pooled)");
            go.transform.SetParent(transform, false);

            go.AddComponent<TextMeshPro>();
            go.GetComponent<MeshRenderer>().sortingOrder = sortingOrder;

            var popup = go.AddComponent<DamageNumberPopup>();
            go.SetActive(false);
            return popup;
        }

        /// <summary>
        /// centerOverride가 있으면(예: Monster Motion Profile의 Damage Number Offset으로 호출부가 이미
        /// 계산해둔 최종 월드 위치) anchor Transform 대신 그 지점을 기준점으로 쓴다 - anchor는 이 경우
        /// 완전히 무시되고, jitter 폭/방향만 anchor의 회전·스케일을 빌려 계산한다(스케일이 바뀌어도
        /// 지터 폭이 캐릭터 크기에 비례해서 따라가는 기존 동작을 유지하기 위함). null이면(기본값)
        /// 기존처럼 anchor Transform 자체를 기준점으로 쓴다 - Profile이 없는 몬스터는 완전히 기존 동작 그대로다.
        ///
        /// presentationOverride가 있으면(Monster Motion Profile의 Damage Number 연출값) Jitter/Rise
        /// Distance/Duration/Text Color/Font Size/Sorting Order를 이 컴포넌트의 Inspector 값 대신 쓴다.
        /// Min Spawn Interval/Pool Size는 몬스터별 연출값이 아니라 이 스포너의 성능 안전장치라 override
        /// 대상이 아니다 - 항상 이 컴포넌트 자신의 값을 쓴다.
        /// </summary>
        public void Spawn(int amount, Vector3? centerOverride = null, DamageNumberPresentation? presentationOverride = null)
        {
            if (Time.time - lastSpawnTime < minSpawnInterval) return;
            lastSpawnTime = Time.time;

            // 풀이 비어 있으면(동시에 떠 있는 숫자가 poolSize를 넘어서면) 그때만 예외적으로 새로 만든다 -
            // 정상적인 연타 빈도에서는 poolSize만으로 충분해서 이 경로를 거의 타지 않는다.
            DamageNumberPopup popup = pool.Count > 0 ? pool.Dequeue() : CreatePooledInstance();

            float activeJitter = presentationOverride?.RandomHorizontalJitter ?? randomHorizontalJitter;
            float activeRiseDistance = presentationOverride?.RiseDistance ?? riseDistance;
            float activeDuration = presentationOverride?.Duration ?? duration;
            Color activeTextColor = presentationOverride?.TextColor ?? textColor;
            float activeFontSize = presentationOverride?.FontSize ?? fontSize;
            int activeSortingOrder = presentationOverride?.SortingOrder ?? sortingOrder;

            Transform t = popup.transform;
            t.SetParent(null, true);

            // 지터는 anchor의 로컬 좌표로 잡고 TransformPoint/TransformVector로 변환한다 - anchor의
            // 현재 스케일이 그대로 반영되므로, StageVisualRoot 배율이 바뀌어도 지터 폭이 캐릭터 크기에
            // 비례해서 따라간다(화면/월드 고정 오프셋이 아니다).
            Vector3 localJitter = new Vector3(Random.Range(-activeJitter, activeJitter), 0f, 0f);
            t.position = centerOverride.HasValue
                ? centerOverride.Value + anchor.TransformVector(localJitter)
                : anchor.TransformPoint(localJitter);

            popup.gameObject.SetActive(true);
            // sortingOrder는 풀 생성 시점에 한 번만 굳어 있던 값이었는데, 몬스터마다 다른 값을 override로
            // 받을 수 있게 됐으니 Spawn 시점에 실제 Renderer에 매번 다시 적용한다.
            popup.GetComponent<MeshRenderer>().sortingOrder = activeSortingOrder;
            // riseDistance도 anchor의 스케일만큼 함께 줄어들어야 캐릭터가 작을 때 숫자만 과하게 크게
            // 떠오르는 어색함이 없다 - anchor가 StageVisualRoot 하위 계층이라 lossyScale에 이미 그
            // 배율이 정확히 한 번 반영돼 있다.
            float scaledRiseDistance = activeRiseDistance * anchor.lossyScale.y;
            popup.Initialize(amount.ToString(), activeTextColor, activeFontSize, scaledRiseDistance, activeDuration, ReturnToPool);
        }

        private void ReturnToPool(DamageNumberPopup popup)
        {
            if (popup == null) return;

            popup.gameObject.SetActive(false);
            popup.transform.SetParent(transform, false);
            // SetParent(worldPositionStays: false)는 localScale을 그대로 둔 채 부모만 바꾼다. Spawn()의
            // SetParent(null, true)는 반대로 "현재 월드 스케일"을 보존하도록 localScale을 역산한다 - 두
            // 호출이 짝을 이루지 않으면 재사용될 때마다 스포너(부모)의 lossyScale이 한 번씩 더 곱해져
            // 데미지 숫자가 기하급수적으로 작아진다(Stage 스케일이 1이 아닌 이상 누적됨). 여기서 매번
            // localScale을 1로 리셋해두면 다음 Spawn()의 world-preserving 언페어런트가 그 순간의 Stage
            // 스케일만 정확히 반영해 항상 같은 크기로 시작한다.
            popup.transform.localScale = Vector3.one;
            pool.Enqueue(popup);
        }
    }
}
