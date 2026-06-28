using Grimoire.Game;
using Grimoire.Game.Data;
using Grimoire.UI;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grimoire.Botting;
//using BrowserForm = Grimoire.UI.BrowserForm;
using System.Diagnostics;
using System.Dynamic;

namespace Grimoire.Tools
{
    public static class Grabber
    {
        private static readonly HashSet<TreeNode> _loadedNodes = new HashSet<TreeNode>();
        private static List<Quest> _cachedQuests = null;
        private static OrderBy _cachedQuestOrder = OrderBy.Id;
        private static List<TreeNode> _cachedQuestNodes = null;
        private static OrderBy _cachedQuestNodeOrder = OrderBy.Id;

        private static HtmlAgilityPack.HtmlNode GetNextElementSibling(HtmlAgilityPack.HtmlNode node)
        {
            var sibling = node.NextSibling;
            while (sibling != null && sibling.NodeType != HtmlAgilityPack.HtmlNodeType.Element)
            {
                sibling = sibling.NextSibling;
            }
            return sibling;
        }

        public enum OrderBy
        {
            Name,
            Id
        }

        public static List<String> GetQuestRewards(int idQuest)
        {
            List<String> listItem = new List<string>();

            List<Quest> questTree = Player.Quests.QuestTree;
            foreach (Quest q in questTree)
            {
                if (q.Id == idQuest)
                {
                    foreach (InventoryItem item in q.Rewards)
                    {
                        listItem.Add(item.Name);
                    }
                }
            }

            return listItem;
        }

        public static List<String> GetQuestRequirment(int idQuest)
        {
            List<String> listItem = new List<string>();

            List<Quest> questTree = Player.Quests.QuestTree;
            foreach (Quest q in questTree)
            {
                if (q.Id == idQuest)
                {
                    foreach (InventoryItem item in q.RequiredItems)
                    {
                        listItem.Add(item.Name);
                    }
                }
            }

            return listItem;
        }

        public static async Task GrabQuests(TreeView tree, OrderBy orderBy)
        {
            List<Quest> list = Player.Quests.QuestTree?.OrderBy((Quest q) => q.Name).ToList();
            switch (orderBy)
            {
                case OrderBy.Name:
                    list = Player.Quests.QuestTree?.OrderBy((Quest q) => q.Name).ToList();
                    break;
                case OrderBy.Id:
                    list = Player.Quests.QuestTree?.OrderBy((Quest q) => q.Id).ToList();
                    break;
            }

            if (list != null && list.Count > 0)
            {
                // Save quests to questdata.json first
                QuestDataLoaderService loader = new QuestDataLoaderService();
                int addedCount = await loader.AppendMissingQuestsAsync(list);
                UI.LogForm.Instance?.AppendDebug($"[GrabQuests] Added {addedCount} new quests to questdata.json");
                
                // Clear both caches so next load from file uses fresh data
                _cachedQuests = null;
                _cachedQuestNodes = null;

                // Create all nodes first on a background thread to keep the UI responsive
                List<TreeNode> questNodes = await Task.Run(() =>
                {
                    var nodes = new List<TreeNode>();
                    foreach (Quest item in list)
                    {
                        TreeNode treeNode = new TreeNode($"{item.Id} - {item.Name}");
                        treeNode.Nodes.Add($"ID: {item.Id}");
                        if (item.ISlot > 0) treeNode.Nodes.Add($"iSlot: {item.ISlot}");
                        if (item.IValue > 0) treeNode.Nodes.Add($"iValue: {item.IValue}");
                        treeNode.Nodes.Add($"Description: {item.Description}");
                        treeNode.ContextMenuStrip = MenuQuest(item.Id);
                        List<InventoryItem> requiredItems = item.RequiredItems;
                        if (requiredItems != null && requiredItems.Count > 0)
                        {
                            TreeNode treeNode2 = treeNode.Nodes.Add("Required items");
                            treeNode2.ContextMenuStrip = MenuItems(requiredItems);
                            foreach (InventoryItem req in requiredItems)
                            {
                                TreeNode treeNode3 = treeNode2.Nodes.Add(req.Name);
                                treeNode3.ContextMenuStrip = MenuItem(req);
                                AddQuestRequirementIdNodes(treeNode3, req);
                                treeNode3.Nodes.Add($"Quantity: {req.Quantity}");
                                treeNode3.Nodes.Add("Temporary: " + (req.IsTemporary ? "Yes" : "No"));
                                treeNode3.Nodes.Add($"Description: {req.Description}");
                            }
                        }
                        List<InventoryItem> rewards = item.Rewards;
                        if (rewards != null && rewards.Count > 0)
                        {
                            TreeNode treeNode4 = treeNode.Nodes.Add("Rewards");
                            treeNode4.ContextMenuStrip = MenuItems(item.Rewards);
                            foreach (InventoryItem reward in item.Rewards)
                            {
                                TreeNode treeNode5 = treeNode4.Nodes.Add(reward.Name);
                                treeNode5.ContextMenuStrip = MenuItem(reward);
                                treeNode5.Nodes.Add($"ID: {reward.Id}");
                                treeNode5.Nodes.Add($"Quantity: {reward.Quantity}");
                                treeNode5.Nodes.Add(string.Concat($"Drop chance: ", reward.DropChance.Contains("100") ? "Guaranteed" : reward.DropChance + "%"));
                                ItemBase reward2 = item.oRewards?.Find(x => x.Name == reward.Name);
                                if (reward2 != null)
                                {
                                    treeNode5.Nodes.Add($"Category: {reward2.Category}");
                                    treeNode5.Nodes.Add($"Description: {reward2.Description}");
                                    if (!string.IsNullOrEmpty(reward2.File))
                                    {
                                        treeNode5.ContextMenuStrip = MenuItem(reward2);
                                        treeNode5.Nodes.Add($"sFile: {reward2.File}");
                                        treeNode5.Nodes.Add($"sLink: {reward2.Link}");
                                    }
                                }
                            }
                        }

                        nodes.Add(treeNode);
                    }
                    return nodes;
                });

                tree.BeginUpdate();
                tree.Nodes.AddRange(questNodes.ToArray());
                tree.EndUpdate();
            }
        }

        private static void AddQuestRequirementIdNodes(TreeNode node, InventoryItem item)
        {
            if (Player.TryGetMapItemId(item, out int mapItemId))
            {
                node.Nodes.Add($"ID: {mapItemId}");
                node.Nodes.Add($"Item ID: {item.Id}");
                return;
            }

            node.Nodes.Add($"ID: {item.Id}");
        }

        public static void GrabShopItems(TreeView tree)
        {
            List<ShopInfo> list = World.LoadedShops?.OrderBy((ShopInfo s) => s.Name).ToList();
            if (list != null && list.Count > 0)
            {
                foreach (ShopInfo item in list)
                {
                    TreeNode treeNode = tree.Nodes.Add(item.Name);
                    treeNode.ContextMenuStrip = Wiki(item);
                    treeNode.Nodes.Add($"ID: {item.Id}");
                    treeNode.Nodes.Add($"Location: {item.Location}");
                    List<InventoryItem> items = item.Items;
                    if (items != null && items.Count > 0)
                    {
                        TreeNode treeNode2 = treeNode.Nodes.Add("Items");
                        foreach (InventoryItem item2 in item.Items)
                        {
                            TreeNode treeNode3 = treeNode2.Nodes.Add(item2.Name);
                            treeNode3.ContextMenuStrip = Wiki(item2);
                            treeNode3.Nodes.Add($"Shop item ID: {item2.ShopItemId}");
                            treeNode3.Nodes.Add($"ID: {item2.Id}");
                            treeNode3.Nodes.Add(string.Format("Cost: {0} {1}", item2.Cost, item2.IsAcItem ? "AC" : "Gold"));
                            treeNode3.Nodes.Add($"Category: {item2.Category}");
                            treeNode3.Nodes.Add($"Level: {item2.Level}");
                            treeNode3.Nodes.Add($"Description: {item2.Description}");
                            if (item2.IsEquippableNonItem || item2.IsWeapon)
                            {
                                treeNode3.Nodes.Add($"sFile: {item2.File}");
                                treeNode3.Nodes.Add($"sLink: {item2.Link}");
                            }
                        }
                    }
                }
            }
        }

        public static async Task GrabQuestIds(TreeView tree, OrderBy orderBy)
        {
            List<Quest> list = Player.Quests.QuestTree?.OrderBy((Quest q) => q.Name).ToList();
            switch (orderBy)
            {
                case OrderBy.Name:
                    list = Player.Quests.QuestTree?.OrderBy((Quest q) => q.Name).ToList();
                    break;
                case OrderBy.Id:
                    list = Player.Quests.QuestTree?.OrderBy((Quest q) => q.Id).ToList();
                    break;
            }
            if (list != null && list.Count > 0)
            {
                QuestDataLoaderService loader = new QuestDataLoaderService();
                int addedCount = await loader.AppendMissingQuestsAsync(list);
                UI.LogForm.Instance?.AppendDebug($"[GrabQuestIds] Added {addedCount} new quests to questdata.json");
                
                // Clear both caches so next load from file uses fresh data
                _cachedQuests = null;
                _cachedQuestNodes = null;

                // Create all nodes first on a background thread to keep the UI responsive
                List<TreeNode> questNodes = await Task.Run(() =>
                {
                    var nodes = new List<TreeNode>();
                    foreach (Quest item in list)
                    {
                        TreeNode treeNode = new TreeNode($"{item.Id} - {item.Name}");
                        treeNode.ContextMenuStrip = MenuQuest(item.Id, item.RequiredItems);
                        nodes.Add(treeNode);
                    }
                    return nodes;
                });

                tree.BeginUpdate();
                tree.Nodes.AddRange(questNodes.ToArray());
                tree.EndUpdate();
            }
        }

        public static void GrabInventoryItems(TreeView tree)
        {
            GrabItems(tree, inventory: true);
        }

        public static void GrabBankItems(TreeView tree)
        {
            GrabItems(tree, inventory: false);
        }

        private static void GrabItems(TreeView tree, bool inventory)
        {
            List<InventoryItem> list = (inventory ? Player.Inventory.Items : Player.Bank.Items)?.OrderBy((InventoryItem i) => i.Name).ToList();
            if (list != null && list.Count > 0)
            {
                foreach (InventoryItem item in list)
                {
                    TreeNode treeNode = tree.Nodes.Add(item.Name);
                    treeNode.ContextMenuStrip = Wiki(item);
                    treeNode.Nodes.Add($"ID: {item.Id}");
                    treeNode.Nodes.Add($"Char item id: {item.CharItemId}");
                    treeNode.Nodes.Add($"Quantity: {item.Quantity}/{item.MaxStack}");
                    treeNode.Nodes.Add($"AC tagged: {item.IsAcItem}");
                    treeNode.Nodes.Add($"Category: {item.Category}");
                    treeNode.Nodes.Add($"Level: {item.Level}");
                    treeNode.Nodes.Add($"Description: {item.Description}");
                    if (item.IsEquippableNonItem || item.IsWeapon)
                    {
                        treeNode.Nodes.Add($"sFile: {item.File}");
                        treeNode.Nodes.Add($"sLink: {item.Link}");

                        // Show a friendly enhancement name if we know it.
                        string enhName = null;
                        if (InventoryItem.EnhancementNames.TryGetValue(item.Enhancement, out var named))
                            enhName = named;
                        else if (item.ForgeEnhancement.HasValue)
                            enhName = item.ForgeEnhancement.Value.ToString();

                        treeNode.Nodes.Add($"Enhancement: {enhName ?? "Unknown"}");
                        treeNode.Nodes.Add($"Enhancement ID: {item.Enhancement}");
                    }
                }
            }
        }

        public static void GrabTempItems(TreeView tree)
        {
            List<TempItem> list = Player.TempInventory.Items?.OrderBy((TempItem i) => i.Name).ToList();
            if (list != null && list.Count > 0)
            {
                foreach (TempItem item in list)
                {
                    TreeNode treeNode = tree.Nodes.Add(item.Name);
                    treeNode.ContextMenuStrip = Wiki(item.Name);
                    treeNode.Nodes.Add($"ID: {item.Id}");
                    treeNode.Nodes.Add($"Quantity: {item.Quantity}");
                }
            }
        }

        public static void GrabMonsters(TreeView tree)
        {
            List<Monster> list = null;
            try
            {
                list = (from x in World.AvailableMonsters?.GroupBy((Monster m) => m.MonMapID)
                        select x.First()).ToList();
                switch (Loaders.order)
                {
                    case OrderBy.Name:
                        list = list.OrderBy(m => m.Name).ToList();
                        break;
                    case OrderBy.Id:
                        list = list.OrderBy(m => m.MonMapID).ToList();
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Please wait for map loaded or Login first before grabbing\n\n{ex.Message}",
                    "GrabMonsters Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }

            if (list != null && list.Count > 0)
            {
                foreach (Monster item in list)
                {
                    TreeNode treeNode = tree.Nodes.Add(item.Name);
                    treeNode.Tag = item.Name;
                    treeNode.ContextMenuStrip = Wiki(item.Name);
                    treeNode.Nodes.Add("Loading...");
                }
            }
        }

        public static void GrabAllMonsters(TreeView tree)
        {
            List<Monster> list = World.GetAllMonsters();
			switch (Loaders.order)
            {
                case OrderBy.Name:
                    list = list.OrderBy(m => m.Name).ToList();
                    break;
                case OrderBy.Id:
                    list = list.OrderBy(m => m.MonMapID).ToList();
                    break;
            }
            //    (from x in World.AvailableMonsters?.GroupBy((Monster m) => m.MonMapID)
            //                      select x.First()).ToList();
            if (list != null && list.Count > 0)
            {
                foreach (Monster item in list)
                {
                    TreeNode treeNode = tree.Nodes.Add($"{item.Name} ({item.cell})");
                    treeNode.Tag = item.Name;
                    treeNode.ContextMenuStrip = Wiki(item.Name);
                    treeNode.Nodes.Add("Loading...");
                }
            }
        }

        internal static async Task Monster_Drops(TreeNode parentNode, string monsterName)
        {
            if (_loadedNodes.Contains(parentNode)) return;

            // Find the monster from AvailableMonsters or AllMonsters to get its details
            Monster monster = World.AvailableMonsters?.FirstOrDefault(m => m.Name == monsterName);
            if (monster == null)
            {
                monster = World.GetAllMonsters()?.FirstOrDefault(m => m.Name == monsterName);
            }

            try
            {
                if (parentNode.TreeView != null && parentNode.TreeView.IsHandleCreated)
                {
                    parentNode.TreeView.Invoke((MethodInvoker)delegate
                    {
                        parentNode.Nodes.Clear();

                        if (monster != null)
                        {
                            parentNode.Nodes.Add($"ID: {monster.Id}");
                            parentNode.Nodes.Add($"MonMapID: {monster.MonMapID}");
                            parentNode.Nodes.Add($"Race: {monster.Race}");
                            parentNode.Nodes.Add($"Level: {monster.Level}");
                            parentNode.Nodes.Add($"Health: {monster.Health}/{monster.MaxHealth}");
                        }

                        TreeNode dropsNode = parentNode.Nodes.Add("Drops");
                        dropsNode.Tag = "__drops__:" + monsterName;
                        dropsNode.Nodes.Add("Click to load...");
                    });
                }

                _loadedNodes.Add(parentNode);
            }
            catch (Exception ex)
            {
                UI.LogForm.Instance.AppendDebug($"[Monster Drops] Error: {ex.Message}");
                if (parentNode.TreeView != null && parentNode.TreeView.IsHandleCreated && monster != null)
                {
                    parentNode.TreeView.Invoke((MethodInvoker)delegate
                    {
                        parentNode.Nodes.Clear();
                        parentNode.Nodes.Add($"ID: {monster.Id}");
                        parentNode.Nodes.Add($"MonMapID: {monster.MonMapID}");
                        parentNode.Nodes.Add($"Race: {monster.Race}");
                        parentNode.Nodes.Add($"Level: {monster.Level}");
                        parentNode.Nodes.Add($"Health: {monster.Health}/{monster.MaxHealth}");
                        TreeNode dropsNode = parentNode.Nodes.Add("Drops");
                        dropsNode.Tag = "__drops__:" + monsterName;
                        dropsNode.Nodes.Add("Click to load...");
                    });
                }
                _loadedNodes.Add(parentNode);
            }
        }

        internal static async Task Monster_Drops_Wiki(TreeNode dropsNode, string monsterName)
        {
            if (_loadedNodes.Contains(dropsNode)) return;

            try
            {
                string slug = Regex.Replace(monsterName.ToLower(), @"[^a-z0-9\s-]", "");
                slug = Regex.Replace(slug, @"\s+", "-");
                slug = Regex.Replace(slug, @"-+", "-");
                slug = slug.Trim('-');
                string url = $"https://aqwwiki.wikidot.com/" + slug;

                UI.LogForm.Instance.AppendDebug($"[Monster Drops Wiki] Fetching wiki page for: {monsterName}");
                UI.LogForm.Instance.AppendDebug($"[Monster Drops Wiki] URL: {url}");

                HtmlWeb web = new HtmlWeb();
                web.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                HtmlAgilityPack.HtmlDocument doc = await web.LoadFromWebAsync(url);

                UI.LogForm.Instance.AppendDebug($"[Monster Drops Wiki] Successfully loaded wiki page");

                // Check if it's a disambiguation page
                string pageText = doc.DocumentNode.InnerText;
                if (pageText.Contains("usually refers to") || pageText.Contains("disambiguation"))
                {
                    UI.LogForm.Instance.AppendDebug($"[Monster Drops Wiki] Detected disambiguation page");
                    var links = doc.DocumentNode.SelectNodes("//div[contains(@class,'page-content')]//a[starts-with(@href,'/')]")
                                ?? doc.DocumentNode.SelectNodes("//a[starts-with(@href,'/')]");

                    if (links != null)
                    {
                        foreach (var link in links)
                        {
                            string href = link.GetAttributeValue("href", "");
                            string linkText = link.InnerText.Trim();
                            if (href.Contains("system:") || href.Contains("search:") || href.Contains("/tag/")) continue;
                            if (linkText.StartsWith(monsterName, StringComparison.OrdinalIgnoreCase))
                            {
                                var candidate = await web.LoadFromWebAsync("https://aqwwiki.wikidot.com" + href);
                                if (candidate.DocumentNode.InnerText.Contains("Items Dropped:"))
                                {
                                    UI.LogForm.Instance.AppendDebug($"[Monster Drops Wiki] Disambig: using https://aqwwiki.wikidot.com{href}");
                                    doc = candidate;
                                    break;
                                }
                            }
                        }
                    }
                }

                var dropsData = new Dictionary<string, List<Tuple<string, string>>>();
                var pageContentNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'page-content')]")
                                   ?? doc.DocumentNode.SelectSingleNode("//div[@id='page-content']")
                                   ?? doc.DocumentNode.SelectSingleNode("//body");

                if (pageContentNode != null)
                {
                    foreach (var sectionText in new[] { "Temporary Items Dropped:", "Items Dropped:" })
                    {
                        UI.LogForm.Instance.AppendDebug($"[Monster Drops Wiki] Looking for section: {sectionText}");
                        var sectionNodes = pageContentNode.SelectNodes(
                            $".//*[normalize-space(text())='{sectionText}' or normalize-space(.)='{sectionText}']");
                        if (sectionNodes == null || sectionNodes.Count == 0) continue;

                        UI.LogForm.Instance.AppendDebug($"[Monster Drops Wiki] Found section: {sectionText}");

                        var searchStart = sectionNodes[0];
                        while (searchStart.ParentNode != null &&
                               searchStart.ParentNode.Name != "div" &&
                               searchStart.ParentNode.Name != "body")
                            searchStart = searchStart.ParentNode;

                        var nextUl = GetNextElementSibling(searchStart);
                        int attempts = 0;
                        while (nextUl != null && nextUl.Name != "ul" && attempts++ < 5)
                        {
                            UI.LogForm.Instance.AppendDebug($"[Monster Drops Wiki] Next element: {nextUl.Name}");
                            nextUl = GetNextElementSibling(nextUl);
                        }

                        if (nextUl?.Name != "ul") continue;

                        var listItems = nextUl.SelectNodes(".//li");
                        if (listItems != null && listItems.Count > 0)
                        {
                            var items = new List<Tuple<string, string>>();
                            foreach (var li in listItems)
                            {
                                string itemText = li.InnerText.Trim();
                                if (itemText.Contains("Search") || itemText.Contains("▼")) continue;

                                string itemName = itemText, note = null;
                                var m = Regex.Match(itemText, @"(.*?)\s+\(Dropped during the '(.*?)' quest\)");
                                if (m.Success) { itemName = m.Groups[1].Value.Trim(); note = $"Dropped during the '{m.Groups[2].Value}' quest"; }

                                UI.LogForm.Instance.AppendDebug($"[Monster Drops Wiki] Found item: {itemName}");
                                items.Add(Tuple.Create(itemName, note));
                            }

                            if (items.Count > 0)
                                dropsData[sectionText.Replace(":", "")] = items;
                        }
                    }

                    if (dropsData.Count == 0)
                    {
                        UI.LogForm.Instance.AppendDebug($"[Monster Drops Wiki] No drops found with current method, trying alternative approach...");
                        var allUlNodes = pageContentNode.SelectNodes(".//ul");
                        if (allUlNodes != null)
                        {
                            foreach (var ulNode in allUlNodes)
                            {
                                var listItems = ulNode.SelectNodes(".//li");
                                if (listItems != null && listItems.Count > 0)
                                {
                                    var items = new List<Tuple<string, string>>();
                                    foreach (var li in listItems)
                                    {
                                        string itemText = li.InnerText.Trim();
                                        if (itemText.Contains("Search") || itemText.Contains("▼") || itemText.Contains("Tag"))
                                            continue;

                                        string itemName = itemText;
                                        string note = null;

                                        var match = Regex.Match(itemText, @"(.*?)\s+\(Dropped during the '(.*?)' quest\)");
                                        if (match.Success)
                                        {
                                            itemName = match.Groups[1].Value.Trim();
                                            note = $"Dropped during the '{match.Groups[2].Value}' quest";
                                        }

                                        items.Add(Tuple.Create(itemName, note));
                                    }

                                    if (items.Any(i => i.Item1.Contains(":") && Regex.IsMatch(i.Item1, @"\d+-\d+")))
                                        continue;

                                    if (items.Count > 0)
                                    {
                                        UI.LogForm.Instance.AppendDebug($"[Monster Drops Wiki] Found {items.Count} items in alternative list");
                                        dropsData["Drops"] = items;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (dropsNode.TreeView != null && dropsNode.TreeView.IsHandleCreated)
                {
                    dropsNode.TreeView.Invoke((MethodInvoker)delegate
                    {
                        dropsNode.Nodes.Clear();

                        if (dropsData.Count == 0)
                        {
                            dropsNode.Nodes.Add("No drops found");
                        }
                        else
                        {
                            foreach (var kvp in dropsData)
                            {
                                TreeNode sectionNode = dropsNode.Nodes.Add(kvp.Key);
                                foreach (var item in kvp.Value)
                                {
                                    TreeNode itemNode = sectionNode.Nodes.Add(item.Item1);
                                    itemNode.ContextMenuStrip = Wiki(item.Item1);
                                    if (!string.IsNullOrEmpty(item.Item2))
                                        itemNode.Nodes.Add(item.Item2);
                                }
                            }
                        }
                    });
                }

                _loadedNodes.Add(dropsNode);
            }
            catch (Exception ex)
            {
                UI.LogForm.Instance.AppendDebug($"[Monster Drops Wiki] Error: {ex.Message}");
                UI.LogForm.Instance.AppendDebug($"[Monster Drops Wiki] Stack trace: {ex.StackTrace}");
                if (dropsNode.TreeView != null && dropsNode.TreeView.IsHandleCreated)
                {
                    dropsNode.TreeView.Invoke((MethodInvoker)delegate
                    {
                        dropsNode.Nodes.Clear();
                        dropsNode.Nodes.Add("Failed to load drops");
                    });
                }
                _loadedNodes.Add(dropsNode);
            }
        }

        public static async Task GrabQuestsFromFile(TreeView tree)
        {
            try
            {
                // First check if we have cached tree nodes with the same order - instant load!
                if (_cachedQuestNodes != null && _cachedQuestNodeOrder == Loaders.order)
                {
                    tree.BeginUpdate();
                    tree.Nodes.AddRange(_cachedQuestNodes.ToArray());
                    tree.EndUpdate();
                    return;
                }

                // Load and cache quests from file if not already cached or order changed
                if (_cachedQuests == null || _cachedQuestOrder != Loaders.order)
                {
                    QuestDataLoaderService loader = new QuestDataLoaderService();
                    List<Quest> quests = await loader.GetFromFileAsync(null);

                    if (quests == null || quests.Count == 0)
                    {
                        MessageBox.Show(
                            "No quests found in questdata.json.",
                            "All Quests (JSON)",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }

                    // Cache the ordered quests
                    _cachedQuests = Loaders.order == OrderBy.Id
                        ? quests.OrderBy(q => q.Id).ToList()
                        : quests.OrderBy(q => q.Name).ToList();
                    _cachedQuestOrder = Loaders.order;
                }

                List<Quest> orderedQuests = _cachedQuests;

                // Create all nodes first on a background thread to keep the UI responsive
                List<TreeNode> questNodes = await Task.Run(() =>
                {
                    var nodes = new List<TreeNode>();
                    foreach (Quest item in orderedQuests)
                    {
                        TreeNode treeNode = new TreeNode($"{item.Id} - {item.Name}");
                        treeNode.Nodes.Add($"ID: {item.Id}");
                        if (item.ISlot > 0) treeNode.Nodes.Add($"iSlot: {item.ISlot}");
                        if (item.IValue > 0) treeNode.Nodes.Add($"iValue: {item.IValue}");
                        treeNode.Nodes.Add($"Description: {item.Description}");
                        treeNode.ContextMenuStrip = MenuQuest(item.Id);

                        List<InventoryItem> requiredItems = item.RequiredItems;
                        if (requiredItems != null && requiredItems.Count > 0)
                        {
                            TreeNode treeNode2 = treeNode.Nodes.Add("Required items");
                            treeNode2.ContextMenuStrip = MenuItems(requiredItems);
                            foreach (InventoryItem req in requiredItems)
                            {
                                TreeNode treeNode3 = treeNode2.Nodes.Add(req.Name);
                                treeNode3.ContextMenuStrip = MenuItem(req);
                                AddQuestRequirementIdNodes(treeNode3, req);
                                treeNode3.Nodes.Add($"Quantity: {req.Quantity}");
                                treeNode3.Nodes.Add("Temporary: " + (req.IsTemporary ? "Yes" : "No"));
                                treeNode3.Nodes.Add($"Description: {req.Description}");
                            }
                        }

                        List<InventoryItem> rewards = item.Rewards;
                        if (rewards != null && rewards.Count > 0)
                        {
                            TreeNode treeNode4 = treeNode.Nodes.Add("Rewards");
                            treeNode4.ContextMenuStrip = MenuItems(item.Rewards);
                            foreach (InventoryItem reward in item.Rewards)
                            {
                                TreeNode treeNode5 = treeNode4.Nodes.Add(reward.Name);
                                treeNode5.ContextMenuStrip = MenuItem(reward);
                                treeNode5.Nodes.Add($"ID: {reward.Id}");
                                treeNode5.Nodes.Add($"Quantity: {reward.Quantity}");
                                treeNode5.Nodes.Add(string.Concat($"Drop chance: ", reward.DropChance.Contains("100") ? "Guaranteed" : reward.DropChance + "%"));

                                ItemBase reward2 = item.oRewards?.Find(x => x.Name == reward.Name);
                                if (reward2 != null)
                                {
                                    treeNode5.Nodes.Add($"Category: {reward2.Category}");
                                    treeNode5.Nodes.Add($"Description: {reward2.Description}");
                                    if (!string.IsNullOrEmpty(reward2.File))
                                    {
                                        treeNode5.ContextMenuStrip = MenuItem(reward2);
                                        treeNode5.Nodes.Add($"sFile: {reward2.File}");
                                        treeNode5.Nodes.Add($"sLink: {reward2.Link}");
                                    }
                                }
                            }
                        }

                        nodes.Add(treeNode);
                    }
                    return nodes;
                });

                // Cache the generated tree nodes
                _cachedQuestNodes = questNodes;
                _cachedQuestNodeOrder = Loaders.order;

                // Add all nodes to the TreeView at once
                tree.BeginUpdate();
                tree.Nodes.AddRange(questNodes.ToArray());
                tree.EndUpdate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load quests from file: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                UI.LogForm.Instance?.AppendDebug($"[GrabQuestsFromFile] Error: {ex.Message}");
                UI.LogForm.Instance?.AppendDebug($"[GrabQuestsFromFile] Stack trace: {ex.StackTrace}");
            }
        }

        public static void GrabMapItems(TreeView tree, List<MapItem> mapItems)
        {
            if (mapItems == null || mapItems.Count == 0)
            {
                MessageBox.Show(
                    "No map items found for the current map.",
                    "Get Map Item IDs",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            IEnumerable<MapItem> list = Loaders.order == OrderBy.Id
                ? mapItems.OrderBy(item => item.Id)
                : mapItems.OrderBy(item => item.QuestId);

            foreach (MapItem item in list)
            {
                if (item.QuestId <= 0)
                    continue;

                string questName = "";
                if (item.QuestId > 0)
                {
                    Quest q = Player.Quests.QuestTree?.FirstOrDefault(x => x.Id == item.QuestId);
                    if (q != null && !string.IsNullOrEmpty(q.Name))
                        questName = $" ({q.Name})";
                }

                TreeNode treeNode = tree.Nodes.Add($"{item.Id} - Quest {item.QuestId}{questName}");
                treeNode.ContextMenuStrip = MenuMapItem(item);
                treeNode.Nodes.Add($"Map Item ID: {item.Id}");
                treeNode.Nodes.Add($"Quest ID: {item.QuestId}");
                if (!string.IsNullOrEmpty(questName))
                    treeNode.Nodes.Add($"Quest Name: {questName.Trim(' ', '(', ')')}");
                if (!string.IsNullOrWhiteSpace(item.MapName))
                    treeNode.Nodes.Add($"Map: {item.MapName}");
            }
        }

        private static ContextMenuStrip MenuMapItem(MapItem item)
        {
            ContextMenuStrip contextMenuStrip = new ContextMenuStrip();

            ToolStripMenuItem getMapItem = new ToolStripMenuItem { Text = "Get map item" };
            getMapItem.Click += delegate
            {
                Player.GetMapItem(item.Id);
            };

            ToolStripMenuItem addCommand = new ToolStripMenuItem { Text = "Add to bot commands" };
            addCommand.Click += delegate
            {
                BotManager.Instance.AddCommand(new Botting.Commands.Item.CmdMapItem { ItemId = item.Id });
            };

            ToolStripMenuItem loadQuest = new ToolStripMenuItem { Text = "Load quest" };
            loadQuest.Click += delegate
            {
                Player.Quests.Load(item.QuestId);
            };

            ToolStripMenuItem acceptQuest = new ToolStripMenuItem { Text = "Accept quest" };
            acceptQuest.Click += delegate
            {
                Player.Quests.Accept(item.QuestId);
            };

            ToolStripMenuItem copyId = new ToolStripMenuItem { Text = "Copy map item ID" };
            copyId.Click += delegate
            {
                Clipboard.SetText(item.Id.ToString());
            };

            contextMenuStrip.Items.Add(getMapItem);
            contextMenuStrip.Items.Add(addCommand);
            contextMenuStrip.Items.Add(loadQuest);
            contextMenuStrip.Items.Add(acceptQuest);
            contextMenuStrip.Items.Add(copyId);
            return contextMenuStrip;
        }

        private static ContextMenuStrip Wiki(string item)
        {
            ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
            ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem
            {
                Text = "Go to Wikipage"
            };
            ToolStripMenuItem toolStripMenuItem1 = new ToolStripMenuItem
            {
                Text = "Search on Wiki"
            };
            ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem
            {
                Text = "Copy To Clipboard"
            };
            toolStripMenuItem.Click += delegate (object S, EventArgs E)
            {
                //System.Diagnostics.Process.Start
                Search("https://aqwwiki.wikidot.com/" + item.Replace(" ", "+"));
            };
            toolStripMenuItem1.Click += delegate (object S, EventArgs E)
            {
                //System.Diagnostics.Process.Start
                Search("https://aqwwiki.wikidot.com/search:site/q/" + item.Replace(" ", "+"));
            };
            toolStripMenuItem2.Click += delegate (object S, EventArgs E)
            {
                Clipboard.SetText(item);
            };
            contextMenuStrip.Items.Add(toolStripMenuItem);
            contextMenuStrip.Items.Add(toolStripMenuItem1);
            contextMenuStrip.Items.Add(toolStripMenuItem2);
            return contextMenuStrip;
        }

        private static ContextMenuStrip Wiki(ShopInfo item)
        {
            ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
            ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem
            {
                Text = "Go to Wikipage"
            };
            ToolStripMenuItem toolStripMenuItem1 = new ToolStripMenuItem
            {
                Text = "Search on Wiki"
            };
            ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem
            {
                Text = "Load Shop"
            };
            toolStripMenuItem.Click += delegate (object S, EventArgs E)
            {
                //System.Diagnostics.Process.Start
                Search("https://aqwwiki.wikidot.com/" + item.Name.Replace(" ", "+"));
            };
            toolStripMenuItem1.Click += delegate (object S, EventArgs E)
            {
                //System.Diagnostics.Process.Start
                Search("https://aqwwiki.wikidot.com/search:site/q/" + item.Name.Replace(" ", "+"));
            };
            toolStripMenuItem2.Click += delegate (object S, EventArgs E)
            {
                Shop.Load(item.Id);
            };
            contextMenuStrip.Items.Add(toolStripMenuItem);
            contextMenuStrip.Items.Add(toolStripMenuItem1);
            contextMenuStrip.Items.Add(toolStripMenuItem2);
            return contextMenuStrip;
        }

        private static ContextMenuStrip Wiki(InventoryItem Item)
        {
            ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
            ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem
            {
                Text = "Go to Wikipage"
            };
            ToolStripMenuItem toolStripMenuItem1 = new ToolStripMenuItem
            {
                Text = "Search on Wiki"
            };
            ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem
            {
                Text = "Equip SWF"
            };
            toolStripMenuItem.Click += delegate (object S, EventArgs E)
            {
                //System.Diagnostics.Process.Start
                Search("https://aqwwiki.wikidot.com/" + Item.Name.Replace(" ", "+"));
            };
            toolStripMenuItem1.Click += delegate (object S, EventArgs E)
            {
                //System.Diagnostics.Process.Start
                Search("https://aqwwiki.wikidot.com/search:site/q/" + Item.Name.Replace(" ", "+"));
            };
            toolStripMenuItem2.Click += delegate
            {
                string txt = Item.Category;
                string slot;
                if (txt == "Cape")
                    slot = "ba";
                else if (txt == "Pet")
                    slot = "pe";
                else if (txt == "Armor" || txt == "Class")
                    slot = "co";
                else if (txt == "Helm")
                    slot = "he";
                else if (txt == "Misc")
                    slot = "mi";
                else
                    slot = "Weapon";
                dynamic equip = new ExpandoObject();
                equip.sFile = Item.File;
                equip.sLink = Item.Link;
                equip.sType = txt;
                Flash.Call("SetEquip", new object[2] { slot, equip });
            };
            contextMenuStrip.Items.Add(toolStripMenuItem);
            contextMenuStrip.Items.Add(toolStripMenuItem1);
            if (Item.IsWeapon || Item.IsEquippableNonItem)
                contextMenuStrip.Items.Add(toolStripMenuItem2);
            return contextMenuStrip;
        }

        private static ContextMenuStrip MenuQuest(int QuestID, List<InventoryItem> Items = null)
        {
            ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
            ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem
            {
                Text = "Add to quest list"
            };
            ToolStripMenuItem toolStripMenuItem1 = new ToolStripMenuItem
            {
                Text = "Accept Quest"
            };
            ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem
            {
                Text = "Complete Quest (Once)"
            };
            ToolStripMenuItem toolStripMenuItem3 = new ToolStripMenuItem
            {
                Text = "Load Quest"
            };
            toolStripMenuItem.Click += delegate (object S, EventArgs E)
            {
                AddQuest(S, E, QuestID);
            };
            toolStripMenuItem1.Click += delegate (object S, EventArgs E)
            {
                Player.Quests.Accept(QuestID);
            };
            toolStripMenuItem2.Click += delegate (object S, EventArgs E)
            {
                Player.Quests.Complete(QuestID);
            };
            toolStripMenuItem3.Click += delegate (object S, EventArgs E)
            {
                Player.Quests.Load(QuestID);
            };
            contextMenuStrip.Items.Add(toolStripMenuItem);
            contextMenuStrip.Items.Add(toolStripMenuItem1);
            contextMenuStrip.Items.Add(toolStripMenuItem2);
            contextMenuStrip.Items.Add(toolStripMenuItem3);
            ToolStripMenuItem toolStripMenuItem4 = new ToolStripMenuItem
            {
                Text = "Complete Quest (Maxed)"
            };
            toolStripMenuItem4.Click += delegate (object S, EventArgs E)
            {
                Player.Quests.Quest(QuestID).Complete(max:true);
            };
            contextMenuStrip.Items.Add(toolStripMenuItem4);

            if (Items != null)
            {
                ToolStripMenuItem toolStripMenuItem5 = new ToolStripMenuItem
                {
                    Text = "Copy all Reqs"
                };
                toolStripMenuItem5.Click += delegate (object S, EventArgs E)
                {
                    string longString =
                    $"\"ItemName\" : \"{string.Join(",", Items.Select(i => i.Name))}\",\n"+
                    $"\"Quantity\" : \"{string.Join(",", Items.Select(i => i.Quantity))}\"";
                    Clipboard.SetText(longString);
                };
                contextMenuStrip.Items.Add(toolStripMenuItem5);
            }
            return contextMenuStrip;
        }

        private static ContextMenuStrip MenuItems(List<InventoryItem> Items)
        {
            ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
            ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem
            {
                Text = "Add all to both"
            };
            ToolStripMenuItem toolStripMenuItem1 = new ToolStripMenuItem
            {
                Text = "Add all to whitelist"
            };
            ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem
            {
                Text = "Add all to unbank list"
            };
            ToolStripMenuItem toolStripMenuItem3 = new ToolStripMenuItem
            {
                Text = "Search all on Wiki"
            };
            toolStripMenuItem.Click += delegate (object S, EventArgs E)
            {
                AddDrops(S, E, Items);
                AddItems(S, E, Items);
            };
            toolStripMenuItem1.Click += delegate (object S, EventArgs E)
            {
                AddDrops(S, E, Items);
            };
            toolStripMenuItem2.Click += delegate (object S, EventArgs E)
            {
                AddDrops(S, E, Items);
            };
            toolStripMenuItem3.Click += delegate (object S, EventArgs E)
            {
                foreach (InventoryItem Item in Items)
                {
                    Process.Start("https://aqwwiki.wikidot.com/search:site/q/" + Item.Name.Replace(" ", "+"));
                }
            };
            ToolStripMenuItem toolStripMenuItem4 = new ToolStripMenuItem
            {
                Text = "Copy all"
            };
            toolStripMenuItem4.Click += delegate (object S, EventArgs E)
            {
                string longString;
                string name = $"\"ItemName\" : \" {string.Join(",", Items.Select(i => i.Name))} \"";
                string qty = $"\"Quantity\" : \" {string.Join(",", Items.Select(i => i.Quantity))} \"";
                longString = $"{name}\"\n{qty}\"";
                Clipboard.SetText(longString);
            };
            contextMenuStrip.Items.Add(toolStripMenuItem);
            contextMenuStrip.Items.Add(toolStripMenuItem1);
            contextMenuStrip.Items.Add(toolStripMenuItem2);
            contextMenuStrip.Items.Add(toolStripMenuItem3);
            contextMenuStrip.Items.Add(toolStripMenuItem4);
            return contextMenuStrip;
        }

        private static ContextMenuStrip MenuItem(InventoryItem Item)
        {
            ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
            ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem
            {
                Text = "Add to both"
            };
            ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem
            {
                Text = "Add to whitelist"
            };
            ToolStripMenuItem toolStripMenuItem3 = new ToolStripMenuItem
            {
                Text = "Add to unbank list"
            };
            ToolStripMenuItem toolStripMenuItem4 = new ToolStripMenuItem
            {
                Text = "Copy item name to clipboard"
            };
            ToolStripMenuItem toolStripMenuItem5 = new ToolStripMenuItem
            {
                Text = "Go to Wikipage"
            };
            ToolStripMenuItem toolStripMenuItem6 = new ToolStripMenuItem
            {
                Text = "Search on Wiki"
            };
            toolStripMenuItem.Click += delegate (object S, EventArgs E)
            {
                AddDrop(S, E, Item);
                AddItem(S, E, Item);
            };
            toolStripMenuItem2.Click += delegate (object S, EventArgs E)
            {
                AddDrop(S, E, Item);
            };
            toolStripMenuItem3.Click += delegate (object S, EventArgs E)
            {
                AddItem(S, E, Item);
            };
            toolStripMenuItem4.Click += delegate
            {
                Clipboard.SetText(Item.Name);
            };
            toolStripMenuItem5.Click += delegate (object S, EventArgs E)
            {
                //System.Diagnostics.Process.Start("https://aqwwiki.wikidot.com/" + Item.Name.Replace(" ", "+"));
                Search("https://aqwwiki.wikidot.com/" + Item.Name.Replace(" ", "+"));
            };
            toolStripMenuItem6.Click += delegate (object S, EventArgs E)
            {
                //System.Diagnostics.Process.Start("https://aqwwiki.wikidot.com/search:site/q/" + Item.Name.Replace(" ", "+"));
                Search("https://aqwwiki.wikidot.com/search:site/q/" + Item.Name.Replace(" ", "+"));
            };
            contextMenuStrip.Items.Add(toolStripMenuItem);
            contextMenuStrip.Items.Add(toolStripMenuItem2);
            contextMenuStrip.Items.Add(toolStripMenuItem3);
            contextMenuStrip.Items.Add(toolStripMenuItem4);
            contextMenuStrip.Items.Add(toolStripMenuItem5);
            contextMenuStrip.Items.Add(toolStripMenuItem6);
            return contextMenuStrip;
        }

        private static ContextMenuStrip MenuItem(ItemBase Item)
        {
            ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
            ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem
            {
                Text = "Add to both"
            };
            ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem
            {
                Text = "Add to whitelist"
            };
            ToolStripMenuItem toolStripMenuItem3 = new ToolStripMenuItem
            {
                Text = "Add to unbank list"
            };
            ToolStripMenuItem toolStripMenuItem4 = new ToolStripMenuItem
            {
                Text = "Copy item name to clipboard"
            };
            ToolStripMenuItem toolStripMenuItem5 = new ToolStripMenuItem
            {
                Text = "Go to Wikipage"
            };
            ToolStripMenuItem toolStripMenuItem6 = new ToolStripMenuItem
            {
                Text = "Search on Wiki"
            };
            ToolStripMenuItem toolStripMenuItem7 = new ToolStripMenuItem
            {
                Text = "Equip SWF"
            };
            toolStripMenuItem.Click += delegate (object S, EventArgs E)
            {
                AddDrop(S, E, Item);
                AddItem(S, E, Item);
            };
            toolStripMenuItem2.Click += delegate (object S, EventArgs E)
            {
                AddDrop(S, E, Item);
            };
            toolStripMenuItem3.Click += delegate (object S, EventArgs E)
            {
                AddItem(S, E, Item);
            };
            toolStripMenuItem4.Click += delegate
            {
                Clipboard.SetText(Item.Name);
            };
            toolStripMenuItem5.Click += delegate (object S, EventArgs E)
            {
                //System.Diagnostics.Process.Start("https://aqwwiki.wikidot.com/" + Item.Name.Replace(" ", "+"));
                Search("https://aqwwiki.wikidot.com/" + Item.Name.Replace(" ", "+"));
            };
            toolStripMenuItem6.Click += delegate (object S, EventArgs E)
            {
                //System.Diagnostics.Process.Start("https://aqwwiki.wikidot.com/search:site/q/" + Item.Name.Replace(" ", "+"));
                Search("https://aqwwiki.wikidot.com/search:site/q/" + Item.Name.Replace(" ", "+"));
            };
            toolStripMenuItem7.Click += delegate
            {
                string txt = Item.Category;
                string slot;
                if (txt == "Cape")
                    slot = "ba";
                else if (txt == "Pet")
                    slot = "pe";
                else if (txt == "Armor" || txt == "Class")
                    slot = "co";
                else if (txt == "Helm")
                    slot = "he";
                else if (txt == "Misc")
                    slot = "mi";
                else
                    slot = "Weapon";
                dynamic equip = new ExpandoObject();
                equip.sFile = Item.File;
                equip.sLink = Item.Link;
                equip.sType = txt;
                Flash.Call("SetEquip", new object[2] { slot, equip });
            };
            contextMenuStrip.Items.Add(toolStripMenuItem);
            contextMenuStrip.Items.Add(toolStripMenuItem2);
            contextMenuStrip.Items.Add(toolStripMenuItem3);
            contextMenuStrip.Items.Add(toolStripMenuItem4);
            contextMenuStrip.Items.Add(toolStripMenuItem5);
            contextMenuStrip.Items.Add(toolStripMenuItem6);
            contextMenuStrip.Items.Add(toolStripMenuItem7);
            return contextMenuStrip;
        }

        private static void Search(string Item)
        {
            //BrowserForm.Instance.LoadUrl(Item);
            Process.Start(Item);
        }

        private static void AddDrop(object s, EventArgs e, InventoryItem Item)
        {
            if (!Item.IsTemporary)
            {
                BotManager.Instance.AddDrop(Item.Name);
            }
        }

        private static void AddItem(object s, EventArgs e, InventoryItem Item)
        {
            if (!Item.IsTemporary)
            {
                BotManager.Instance.AddItem(Item.Name);
            }
        }

        private static void AddDrops(object s, EventArgs e, List<InventoryItem> Items)
        {
            foreach (InventoryItem Item in Items)
            {
                AddDrop(s, e, Item);
            }
        }

        private static void AddItems(object s, EventArgs e, List<InventoryItem> Items)
        {
            foreach (InventoryItem Item in Items)
            {
                AddItem(s, e, Item);
            }
        }

        private static void AddDrop(object s, EventArgs e, ItemBase Item)
        {
            if (!Item.Temp)
            {
                BotManager.Instance.AddDrop(Item.Name);
            }
        }

        private static void AddItem(object s, EventArgs e, ItemBase Item)
        {
            if (!Item.Temp)
            {
                BotManager.Instance.AddItem(Item.Name);
            }
        }

        private static void AddDrops(object s, EventArgs e, List<ItemBase> Items)
        {
            foreach (ItemBase Item in Items)
            {
                AddDrop(s, e, Item);
            }
        }

        private static void AddItems(object s, EventArgs e, List<ItemBase> Items)
        {
            foreach (ItemBase Item in Items)
            {
                AddItem(s, e, Item);
            }
        }


        private static void AddQuest(object s, EventArgs e, int ID)
        {
            BotManager.Instance.AddQuest(ID);
        }
    }
}
