using System.Collections.Generic;
using UnityEngine;

namespace Shop
{
    [CreateAssetMenu(fileName = "ShopCatalog", menuName = "Shop/Shop Catalog")]
    public sealed class ShopCatalog : ScriptableObject
    {
        [SerializeField] private List<ShopDefinition> shops = new List<ShopDefinition>();
        private readonly List<ShopDefinition> active = new List<ShopDefinition>();
        private bool built;
        public IReadOnlyList<ShopDefinition> ActiveShops { get { Build(); return active; } }
        public ShopDefinition Find(string id) { Build(); return active.Find(s => s.ShopId == id); }
        public void MarkDirty() => built = false;
        private void OnEnable() => built = false;
        private void Build() { if (built) return; built = true; active.Clear(); var seen = new HashSet<string>(); foreach (var shop in shops) if (shop != null && shop.IsValid && shop.Enabled && seen.Add(shop.ShopId)) active.Add(shop); active.Sort((a,b) => { int c=a.DisplayOrder.CompareTo(b.DisplayOrder); return c != 0 ? c : string.CompareOrdinal(a.ShopId,b.ShopId); }); }
    }
}
