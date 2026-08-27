using Common;
using UnityEngine;

namespace Shop
{
    [CreateAssetMenu(fileName = "ShopDefinition", menuName = "Shop/Shop Definition")]
    public sealed class ShopDefinition : ScriptableObject
    {
        [SerializeField] private string shopId;
        [SerializeField] private LocalizedTextReference localizedName = new LocalizedTextReference();
        [SerializeField] private int requiredBuildingId;
        [SerializeField] private bool acceptItemSales;
        [SerializeField] private int displayOrder;
        [SerializeField] private bool enabled;
        public string ShopId => shopId ?? string.Empty;
        public LocalizedTextReference LocalizedName => localizedName ??= new LocalizedTextReference();
        public int RequiredBuildingId => requiredBuildingId;
        public bool AcceptItemSales => acceptItemSales;
        public int DisplayOrder => displayOrder;
        public bool Enabled => enabled;
        public bool IsValid => !string.IsNullOrWhiteSpace(shopId);
    }
}
