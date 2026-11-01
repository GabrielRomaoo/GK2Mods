using System;
using System.Collections.Generic;
using System.Linq;
using LazyBearTechnology;

namespace GK2Mods.StorageNetwork
{
    /// <summary>
    /// Connectors double as drop boxes: once a day (when a new game day starts) whatever was put in them goes to
    /// the Central Storage, or to another network chest when it is full. Deposit only; nothing comes back out.
    /// Items with their own state (bags, tools with durability, ...) stay in the box, since moving them by id
    /// would lose that state.
    /// </summary>
    internal static class Courier
    {
        internal const string NoticeKey = "gk2sn_courier_notice";

        internal static void Init()
        {
            EnvironmentEngine.OnNewDayStarted += OnNewDay;
        }

        private static void OnNewDay(int day)
        {
            try
            {
                int moved = Deliver();
                Plugin.Debug($"Courier, day {day}: {moved} item(s) delivered");
                if (moved > 0)
                {
                    LazySingleton<UINotificator>.Instance.ShowSimpleTextWithIconNotification(NoticeKey, Icons.Connector);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Courier failed: {e}");
            }
        }

        /// <summary>Empties every Connector box into the network. Returns how many items moved.</summary>
        internal static int Deliver()
        {
            Network.Invalidate();
            Inventory central = Network.CentralInventory;
            if (central == null)
            {
                return 0;
            }
            List<Inventory> targets = new[] { central }.Concat(Network.Pool.Where(i => i != central)).ToList();
            int total = 0;
            foreach (Inventory box in Network.ConnectorBoxes)
            {
                foreach (Item item in box.Data.Inventory.ToList())
                {
                    if (item == null || item.IsEmpty || item.IsBag || (item.properties != null && item.properties.Count > 0))
                    {
                        continue;
                    }
                    int moved = AddTo(targets, item.id, item.Count);
                    if (moved > 0)
                    {
                        box.RemoveItemById(item.id, moved, null, null, true);
                        total += moved;
                    }
                }
            }
            return total;
        }

        private static int AddTo(List<Inventory> targets, string itemId, int count)
        {
            int moved = 0;
            foreach (Inventory target in targets)
            {
                if (moved >= count)
                {
                    break;
                }
                if (target.AddItemToInventory(new Item(itemId, count - moved), out List<Item> added, null, true))
                {
                    moved += added.Sum(i => i.Count);
                }
            }
            return moved;
        }
    }
}
