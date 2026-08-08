using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// 재화 목록의 <b>순서와 구성</b>을 소유하는 에셋. <see cref="ItemCatalog"/>와 같은 역할이며,
    /// 읽는 쪽은 프로젝트를 뒤져 재화를 모으지 않고(AssetDatabase 탐색도 하지 않는다) 이 에셋
    /// 하나만 읽는다.
    ///
    /// <b>이 목록이 "지금 갖고 있는 재화"는 아니다.</b> 잔액은 저장 데이터가 소유하고, 여기는
    /// "이 게임에 어떤 재화가 있는가"를 모아 두는 자리다.
    ///
    /// <b>걸러내기는 여기서 한 번만 한다.</b> 비어 있는 칸, 식별자가 없는 재화, 앞선 항목과 id가
    /// 겹치는 재화는 목록에서 제외하고 <see cref="Currencies"/>는 <b>남은 항목을 작성 순서 그대로</b>
    /// 돌려준다 - 정렬은 목록을 채우는 임포터의 몫이지 읽는 쪽의 몫이 아니다. 검사 결과는 캐시되며
    /// 로그도 그때 한 번만 남는다. id 비교는 <see cref="StringComparer.Ordinal"/>이라 <b>적힌 그대로</b>
    /// 본다 - 'jewel'과 'Jewel'은 겹치지 않는 별개의 재화이고, 공백을 떼거나 대소문자를 맞추는
    /// 정규화는 어디서도 하지 않는다. id가 겹칠 때 앞의 것을 남기는
    /// 것도 다른 카탈로그와 같은 이유다 - 나중에 실수로 복제한 항목이 먼저 작성한 재화를 밀어내지
    /// 않게 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "CurrencyCatalog", menuName = "Inventory/Currency Catalog")]
    public class CurrencyCatalog : ScriptableObject
    {
        [Tooltip("재화를 나올 순서대로 넣는다. 비어 있는 칸/식별자가 없는 재화/id가 겹치는 재화는 " +
                 "자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<CurrencyDefinition> currencies = new List<CurrencyDefinition>();

        /// <summary>검사를 통과한 항목만 작성 순서대로 담아 둔 캐시. 조회할 때마다 새로 만들지 않는다.</summary>
        private readonly List<CurrencyDefinition> validCurrencies = new List<CurrencyDefinition>();

        private bool built;

        /// <summary>쓸 수 있는 재화들을 <b>작성 순서 그대로</b> 돌려준다. 항목이 하나도 없으면 빈
        /// 목록이며 null이 아니다 - 비어 있는 카탈로그도 정상적인 상태로 다룬다.</summary>
        public IReadOnlyList<CurrencyDefinition> Currencies
        {
            get
            {
                EnsureBuilt();
                return validCurrencies;
            }
        }

        /// <summary>쓸 수 있는 재화 수.</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return validCurrencies.Count;
            }
        }

        /// <summary>식별자로 재화를 찾는다. 없으면 null이다 - 목록 크기가 작아 선형 탐색으로 충분하고,
        /// 별도 사전을 두어 캐시 무효화 경로를 하나 더 만들지 않는다. <b>넘어온 문자열을 손대지 않고
        /// 그대로 비교한다</b> - 대소문자를 구분하고('Jewel'로 찾으면 'jewel'은 나오지 않는다) 앞뒤
        /// 공백도 떼지 않는다('  gold  '로는 'gold'를 찾을 수 없다). 조회하는 쪽이 흘린 공백을 여기서
        /// 말없이 지워 주면, 저장 파일에 공백이 붙은 키가 들어가는 것을 아무도 눈치채지 못한다.</summary>
        public CurrencyDefinition Find(string currencyId)
        {
            if (string.IsNullOrWhiteSpace(currencyId)) return null;

            EnsureBuilt();

            for (int i = 0; i < validCurrencies.Count; i++)
            {
                if (string.Equals(validCurrencies[i].CurrencyId, currencyId, StringComparison.Ordinal))
                {
                    return validCurrencies[i];
                }
            }

            return null;
        }

        /// <summary>다음 조회 때 검사를 다시 하도록 표시한다. 에디터에서 목록을 고친 뒤나 임포터가
        /// 목록을 채운 뒤에 쓴다.</summary>
        public void MarkDirty()
        {
            built = false;
        }

        private void OnEnable()
        {
            // 에셋이 로드될 때마다 한 번은 다시 검사한다.
            built = false;
        }

        private void EnsureBuilt()
        {
            if (built) return;
            built = true;

            validCurrencies.Clear();
            if (currencies == null) return;

            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < currencies.Count; i++)
            {
                CurrencyDefinition currency = currencies[i];

                if (currency == null)
                {
                    Debug.LogWarning($"[CurrencyCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                if (!currency.IsValid)
                {
                    Debug.LogError($"[CurrencyCatalog] '{name}': {i}번 항목('{currency.name}')에 Currency Id가 " +
                                   "없어 목록에서 제외합니다 - 에셋에서 식별자를 직접 지정하세요.", currency);
                    continue;
                }

                if (!seenIds.Add(currency.CurrencyId))
                {
                    Debug.LogError($"[CurrencyCatalog] '{name}': {i}번 항목('{currency.name}')의 Currency Id " +
                                   $"'{currency.CurrencyId}'가 앞선 항목과 겹쳐 목록에서 제외합니다 - " +
                                   "먼저 작성된 재화가 남습니다(대소문자는 구분합니다).", currency);
                    continue;
                }

                validCurrencies.Add(currency);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 에디터에서 목록을 고치면 다음 조회 때 검사와 경고가 최신 내용 기준으로 한 번 다시 돈다.
            built = false;
        }
#endif
    }
}
