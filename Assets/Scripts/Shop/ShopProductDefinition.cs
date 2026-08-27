using UnityEngine;

namespace Shop
{
    [CreateAssetMenu(fileName = "ShopProductDefinition", menuName = "Shop/Shop Product Definition")]
    public sealed class ShopProductDefinition : ScriptableObject
    {
        [SerializeField] private string shopId;
        [SerializeField] private string itemId;
        [SerializeField] private string buyCurrencyId;
        [SerializeField] private int buyPrice;
        [SerializeField] private int displayOrder;
        [SerializeField] private bool enabled;
        public string ShopId => shopId ?? string.Empty;
        public string ItemId => itemId ?? string.Empty;
        public string BuyCurrencyId => buyCurrencyId ?? string.Empty;
        public int BuyPrice => buyPrice;
        public int DisplayOrder => displayOrder;
        public bool Enabled => enabled;
        public bool IsValid => !string.IsNullOrWhiteSpace(shopId) && !string.IsNullOrWhiteSpace(itemId);
    }
}
