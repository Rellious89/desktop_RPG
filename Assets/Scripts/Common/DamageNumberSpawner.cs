using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 이 오브젝트 위치(ReceivePoint 기준)에 데미지 숫자를 띄운다.
    /// Target과 마찬가지로 어떤 대상에도 붙여 재사용할 수 있다.
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
        [Tooltip("이 오브젝트 기준 오프셋. 피격체 머리 위쪽에 오도록 y값을 잡는다.")]
        [SerializeField] private Vector2 spawnOffset = new Vector2(0f, 0.5f);
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

        public void Spawn(int amount)
        {
            if (Time.time - lastSpawnTime < minSpawnInterval) return;
            lastSpawnTime = Time.time;

            // 풀이 비어 있으면(동시에 떠 있는 숫자가 poolSize를 넘어서면) 그때만 예외적으로 새로 만든다 -
            // 정상적인 연타 빈도에서는 poolSize만으로 충분해서 이 경로를 거의 타지 않는다.
            DamageNumberPopup popup = pool.Count > 0 ? pool.Dequeue() : CreatePooledInstance();

            Transform t = popup.transform;
            t.SetParent(null, true);
            t.position = transform.position + (Vector3)spawnOffset +
                new Vector3(Random.Range(-randomHorizontalJitter, randomHorizontalJitter), 0f, 0f);

            popup.gameObject.SetActive(true);
            popup.Initialize(amount.ToString(), textColor, fontSize, riseDistance, duration, ReturnToPool);
        }

        private void ReturnToPool(DamageNumberPopup popup)
        {
            if (popup == null) return;

            popup.gameObject.SetActive(false);
            popup.transform.SetParent(transform, false);
            pool.Enqueue(popup);
        }
    }
}
