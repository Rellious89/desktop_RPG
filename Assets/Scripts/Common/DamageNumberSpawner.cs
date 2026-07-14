using TMPro;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 이 오브젝트 위치(ReceivePoint 기준)에 데미지 숫자를 띄운다.
    /// Target과 마찬가지로 어떤 대상에도 붙여 재사용할 수 있다.
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

        private float lastSpawnTime = -999f;

        public void Spawn(int amount)
        {
            if (Time.time - lastSpawnTime < minSpawnInterval) return;
            lastSpawnTime = Time.time;

            var go = new GameObject("DamageNumber");
            go.transform.position = transform.position + (Vector3)spawnOffset +
                new Vector3(Random.Range(-randomHorizontalJitter, randomHorizontalJitter), 0f, 0f);

            go.AddComponent<TextMeshPro>();
            go.GetComponent<MeshRenderer>().sortingOrder = sortingOrder;

            var popup = go.AddComponent<DamageNumberPopup>();
            popup.Initialize(amount.ToString(), textColor, fontSize, riseDistance, duration);
        }
    }
}
