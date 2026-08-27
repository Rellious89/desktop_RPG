using System.Collections.Generic;
using UnityEngine;

namespace Shop
{
    [CreateAssetMenu(fileName = "ShopProductCatalog", menuName = "Shop/Shop Product Catalog")]
    public sealed class ShopProductCatalog : ScriptableObject
    {
        [SerializeField] private List<ShopProductDefinition> products = new List<ShopProductDefinition>();
        private readonly List<ShopProductDefinition> active = new List<ShopProductDefinition>();
        private bool built;
        public IReadOnlyList<ShopProductDefinition> GetActiveProducts(string shopId) { Build(); return active.FindAll(p => p.ShopId == shopId); }
        public ShopProductDefinition Find(string shopId, string itemId) { Build(); return active.Find(p => p.ShopId == shopId && p.ItemId == itemId); }
        public void MarkDirty() => built = false;
        private void OnEnable() => built = false;
        private void Build() { if (built) return; built=true; active.Clear(); var seen=new HashSet<string>(); foreach(var p in products) if(p != null && p.IsValid && p.Enabled && seen.Add(p.ShopId+"\n"+p.ItemId)) active.Add(p); active.Sort((a,b)=>{ int c=a.DisplayOrder.CompareTo(b.DisplayOrder); return c != 0 ? c : string.CompareOrdinal(a.ItemId,b.ItemId);}); }
    }
}
