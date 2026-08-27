using System;
using System.Collections.Generic;
using Inventory;

namespace Shop
{
    /// <summary>
    /// 상점 판매 화면이 닫힐 때까지의 임시 선택 목록이다. 저장이나 인벤토리는 바꾸지 않으며,
    /// 실제 거래 직전에도 현재 보유 상태로 다시 검증한다.
    /// </summary>
    public sealed class ShopSellSession
    {
        public readonly struct Entry
        {
            public Entry(ItemDefinition item, int unitPrice)
            {
                Item = item;
                UnitPrice = unitPrice;
            }

            public ItemDefinition Item { get; }
            public int UnitPrice { get; }
        }

        private readonly ItemCatalog itemCatalog;
        private readonly InventoryManager inventory;
        private readonly List<Entry> entries = new List<Entry>();

        public ShopSellSession(ItemCatalog itemCatalog, InventoryManager inventory)
        {
            this.itemCatalog = itemCatalog;
            this.inventory = inventory;
        }

        public IReadOnlyList<Entry> Entries => entries;

        public bool CanAdd(ItemDefinition item)
        {
            return TryGetCurrentSale(item, out _, out _) && !Contains(item.ItemId) && !WouldOverflow(item);
        }

        public bool TryAdd(ItemDefinition item)
        {
            if (!CanAdd(item)) return false;
            TryGetCurrentSale(item, out ItemDefinition current, out int unitPrice);
            entries.Add(new Entry(current, unitPrice));
            return true;
        }

        public bool Remove(string itemId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.Equals(entries[i].Item.ItemId, itemId, StringComparison.Ordinal)) continue;
                entries.RemoveAt(i);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            entries.Clear();
        }

        /// <summary>외부 변경으로 더는 팔 수 없는 줄을 제거하고, 최신 정의와 가격을 반영한다.</summary>
        public bool Revalidate()
        {
            bool changed = false;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];
                if (!TryGetCurrentSale(entry.Item, out ItemDefinition current, out int unitPrice))
                {
                    entries.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (entry.Item != current || entry.UnitPrice != unitPrice)
                {
                    entries[i] = new Entry(current, unitPrice);
                    changed = true;
                }
            }

            if (HasOverflow())
            {
                // 가장 최근에 추가한 항목부터 빼, 이전에 표시했던 안전한 선택을 유지한다.
                while (HasOverflow())
                {
                    entries.RemoveAt(entries.Count - 1);
                    changed = true;
                }
            }
            return changed;
        }

        public bool TryCreateSnapshot(out ShopSellLine[] snapshot, out int totalPrice)
        {
            Revalidate();
            if (entries.Count == 0)
            {
                snapshot = Array.Empty<ShopSellLine>();
                totalPrice = 0;
                return false;
            }

            snapshot = new ShopSellLine[entries.Count];
            long total = 0L;
            for (int i = 0; i < entries.Count; i++)
            {
                snapshot[i] = new ShopSellLine(entries[i].Item.ItemId, 1);
                total += entries[i].UnitPrice;
            }
            if (total > int.MaxValue)
            {
                snapshot = Array.Empty<ShopSellLine>();
                totalPrice = 0;
                return false;
            }

            totalPrice = (int)total;
            return true;
        }

        public int TotalPrice
        {
            get
            {
                long total = 0L;
                for (int i = 0; i < entries.Count; i++) total += entries[i].UnitPrice;
                return total >= int.MaxValue ? int.MaxValue : (int)total;
            }
        }

        private bool TryGetCurrentSale(ItemDefinition item, out ItemDefinition current, out int unitPrice)
        {
            current = null;
            unitPrice = 0;
            if (item == null || !item.IsValid || itemCatalog == null || inventory == null) return false;

            current = itemCatalog.Find(item.ItemId);
            if (current == null || inventory.GetItemCount(current.ItemId) <= 0) return false;
            return ShopTradeService.TryGetSellUnitPrice(current, out unitPrice);
        }

        private bool Contains(string itemId)
        {
            for (int i = 0; i < entries.Count; i++)
                if (string.Equals(entries[i].Item.ItemId, itemId, StringComparison.Ordinal)) return true;
            return false;
        }

        private bool WouldOverflow(ItemDefinition item)
        {
            if (!TryGetCurrentSale(item, out _, out int price)) return true;
            return (long)TotalPrice + price > int.MaxValue;
        }

        private bool HasOverflow()
        {
            long total = 0L;
            for (int i = 0; i < entries.Count; i++) total += entries[i].UnitPrice;
            return total > int.MaxValue;
        }
    }
}
