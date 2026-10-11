using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Grimoire.Tools.Wiki
{
    public static class WikiScraper
    {
        public static event Action<string> Log;
        private static void Logd(string msg) => Log?.Invoke(msg);

        private const string BaseUrl = "http://aqwwiki.wikidot.com";
        private static readonly HashSet<string> GenderedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Armor", "Class"
        };

        private static readonly Dictionary<string, string> FlexibleListLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Locations"] = "Locations",
            ["Location"] = "Locations",
            ["Sellback"] = "Sellback",
            ["Notes"] = "Notes",
            ["Note"] = "Notes",
            ["Special Effects"] = "Special Effects",
        };

        private static readonly HashSet<string> RichLinkListLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Locations", "Price", "Notes"
        };

        private static readonly HashSet<string> InlineLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Price", "Rarity", "Description", "Level", "Health", "Race", "Category", "Type",
            "Weapon Damage", "Stat Model",
        };

        private static readonly Dictionary<string, string> LocationListLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Monsters"] = "Monsters",
            ["Monster"] = "Monsters",
            ["NPCs"] = "NPCs",
            ["NPC"] = "NPCs",
            ["Quests"] = "Quests",
            ["Quest"] = "Quests",
            ["Shops"] = "Shops",
            ["Shop"] = "Shops",
            ["Access Points"] = "Access Points",
            ["Access Point"] = "Access Points",
        };

        private static readonly HashSet<string> LocationInlineLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Map Name", "Room Limit",
        };

        private static readonly Dictionary<string, string> LocationFlexibleLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Note"] = "Notes",
            ["Notes"] = "Notes",
        };

        private static readonly HashSet<string> QuestGroupLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Quest Location", "Quests Begun From", "Requirements", "Note", "Notes"
        };

        private static bool _diagnosticDumped = false;

        private static HtmlWeb CreateWebClient()
        {
            var web = new HtmlWeb
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            };

            web.PreRequest = request =>
            {
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.Add("Accept-Language", "en-US,en;q=0.5");
                return true;
            };

            return web;
        }

        private static HtmlNode FindPageContent(HtmlDocument doc, string contextLabel)
        {
            var byClass = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'page-content')]");
            if (byClass != null) return byClass;

            var byId = doc.DocumentNode.SelectSingleNode("//div[@id='page-content']");
            if (byId != null) return byId;

            var byContentPanel = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'content-panel')]");
            if (byContentPanel != null) return byContentPanel;

            if (!_diagnosticDumped)
            {
                _diagnosticDumped = true;
                try
                {
                    string raw = doc.DocumentNode.OuterHtml;
                    File.WriteAllText("debug_wiki_response.html", raw);
                    string preview = raw.Length > 400 ? raw.Substring(0, 400) : raw;
                    Logd($"DIAGNOSTIC [{contextLabel}]: no content div found. Received {raw.Length} chars, saved to debug_wiki_response.html. Preview: {preview.Replace("\n", " ").Replace("\r", "")}");
                }
                catch (Exception ex)
                {
                    Logd($"DIAGNOSTIC dump failed: {ex.Message}");
                }
            }

            return null;
        }

        public static Task<List<WikiEntry>> ScrapeArmorListAsync()
        {
            return ScrapeCategoryListAsync("armors", WikiEntryType.Item);
        }

        public static async Task<List<WikiEntry>> ScrapeCategoryListAsync(string categorySlug, WikiEntryType type)
        {
            var results = new List<WikiEntry>();
            var seenSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HtmlWeb web = CreateWebClient();

            Logd($"Scraping category list: {categorySlug}");

            string firstUrl = $"{BaseUrl}/{categorySlug}";
            HtmlDocument firstDoc;
            try
            {
                firstDoc = await web.LoadFromWebAsync(firstUrl);
                Logd($"Fetch completed — StatusCode: {web.StatusCode}, ResponseUri: {web.ResponseUri}, NodeCount: {firstDoc.DocumentNode.ChildNodes.Count}");
            }
            catch (Exception ex)
            {
                Logd($"EXCEPTION loading '{categorySlug}': {ex.GetType().Name}: {ex.Message}");
                return results;
            }

            AddCategoryItems(firstDoc, type, results, seenSlugs);
            int maxPage = GetMaxPage(firstDoc);

            Logd($"'{categorySlug}' has {maxPage} page(s) — page 1 yielded {results.Count} so far");

            if (maxPage > 1)
            {
                using (var semaphore = new SemaphoreSlim(4))
                {
                    var tasks = new List<Task<HtmlDocument>>();
                    for (int page = 2; page <= maxPage; page++)
                    {
                        int pageNum = page;
                        tasks.Add(FetchPageThrottled(web, $"{BaseUrl}/{categorySlug}/p/{pageNum}", semaphore));
                    }

                    HtmlDocument[] pageDocs = await Task.WhenAll(tasks);
                    foreach (var doc in pageDocs)
                    {
                        if (doc != null)
                            AddCategoryItems(doc, type, results, seenSlugs);
                    }
                }
            }

            Logd($"Total '{categorySlug}' entries found: {results.Count}");
            return results;
        }

        private static async Task<HtmlDocument> FetchPageThrottled(HtmlWeb web, string url, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                await Task.Delay(150);
                var doc = await web.LoadFromWebAsync(url);
                Logd($"Fetched page: {url}");
                return doc;
            }
            catch (Exception ex)
            {
                Logd($"Failed to load page '{url}': {ex.Message}");
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        }

        private static int GetMaxPage(HtmlDocument doc)
        {
            var pagerLinks = doc.DocumentNode.SelectNodes("//div[contains(@class,'pager')]//a");
            if (pagerLinks == null || pagerLinks.Count == 0)
            {
                Logd("No pager found on page — treating as single-page category");
                return 1;
            }

            int max = 1;
            foreach (var link in pagerLinks)
            {
                string text = link.InnerText.Trim();
                if (int.TryParse(text, out int pageNum) && pageNum > max)
                    max = pageNum;

                string href = link.GetAttributeValue("href", "");
                var match = Regex.Match(href, @"/p/(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int hrefPage) && hrefPage > max)
                    max = hrefPage;
            }

            Logd($"Pager detected — highest page number found: {max}");
            return max;
        }

        private static void AddCategoryItems(HtmlDocument doc, WikiEntryType type, List<WikiEntry> results, HashSet<string> seenSlugs)
        {
            var pageContent = FindPageContent(doc, "category list");

            if (pageContent == null)
            {
                Logd("WARNING: no page-content div found on this page (see diagnostic above)");
                return;
            }

            var itemBlocks = pageContent.SelectNodes(".//div[contains(@class,'list-pages-item')]");

            int added = 0;

            if (itemBlocks != null && itemBlocks.Count > 0)
            {
                foreach (var item in itemBlocks)
                {
                    var link = item.SelectSingleNode(".//a[starts-with(@href,'/')]");
                    if (link == null) continue;

                    string href = link.GetAttributeValue("href", "");
                    string name = link.InnerText.Trim();

                    if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(name)) continue;
                    if (href.Contains("system:") || href.Contains("search:") || href.Contains("/tag/") || href.Contains("/p/")) continue;

                    string slug = href.TrimStart('/');
                    if (string.IsNullOrEmpty(slug) || !seenSlugs.Add(slug)) continue;

                    results.Add(new WikiEntry
                    {
                        Name = name,
                        Type = type,
                        WikiUrl = $"{BaseUrl}/{slug}"
                    });
                    added++;
                }
            }
            else
            {
                var plainLinks = pageContent.SelectNodes(".//ul//a[starts-with(@href,'/')]");
                if (plainLinks != null)
                {
                    foreach (var link in plainLinks)
                    {
                        string href = link.GetAttributeValue("href", "");
                        string name = link.InnerText.Trim();

                        if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(name)) continue;
                        if (href.Contains("system:") || href.Contains("search:") || href.Contains("/tag/") || href.Contains("/p/")) continue;

                        string slug = href.TrimStart('/');
                        if (string.IsNullOrEmpty(slug) || !seenSlugs.Add(slug)) continue;

                        results.Add(new WikiEntry
                        {
                            Name = name,
                            Type = type,
                            WikiUrl = $"{BaseUrl}/{slug}"
                        });
                        added++;
                    }

                    if (added > 0)
                        Logd("  (used plain <ul><li><a> fallback — no list-pages-item blocks found on this page)");
                }
            }

            Logd($"  +{added} new entries from this page (running total: {results.Count})");
        }

        public static Task<WikiEntry> ScrapeArmorDetailAsync(WikiEntry entry)
        {
            return ScrapeDetailIntoEntryAsync(entry, isGendered: true);
        }

        public static Task<WikiEntry> ScrapeDetailAsync(WikiEntry entry)
        {
            return ScrapeDetailIntoEntryAsync(entry, isGendered: false);
        }

        private static async Task<WikiEntry> ScrapeDetailIntoEntryAsync(WikiEntry entry, bool isGendered)
        {
            if (entry == null || string.IsNullOrEmpty(entry.WikiUrl))
                return entry;

            try
            {
                Logd($"Scraping detail: {entry.WikiUrl}");

                HtmlWeb web = CreateWebClient();
                HtmlDocument doc = await web.LoadFromWebAsync(entry.WikiUrl);

                string pageText = doc.DocumentNode.InnerText;
                if (pageText.Contains("usually refers to") || pageText.Contains("disambiguation"))
                {
                    doc = await ResolveDisambiguationAsync(web, doc, entry.Name) ?? doc;
                }

                var pageContent = FindPageContent(doc, $"detail: {entry.Name}");

                if (pageContent == null)
                {
                    Logd($"No page content found for {entry.WikiUrl}");
                    return entry;
                }

                if (entry.Type == WikiEntryType.Location)
                {
                    entry.MonsterLines = new List<WikiLinkItem>();
                    entry.NpcLines = new List<WikiLinkItem>();
                    entry.QuestLines = new List<WikiLinkItem>();
                    entry.ShopLines = new List<WikiLinkItem>();
                    entry.AccessPointLines = new List<WikiLinkItem>();
                    entry.Screens = new List<ScreenInfo>();
                    entry.NotesLines = new List<LinkedLine>();
                    entry.NotesIconUrl = null;

                    entry.EventIconUrls = new List<string>();
                    entry.TopIconUrls = new List<string>();
                    ExtractTopIcons(pageContent, entry);

                    ParseLocationContent(pageContent, entry);
                    ParseScreens(pageContent, entry);

                    Logd($"  Top icons: {entry.TopIconUrls.Count}, Monsters: {entry.MonsterLines.Count}, NPCs: {entry.NpcLines.Count}, Quests: {entry.QuestLines.Count}, Shops: {entry.ShopLines.Count}, Access Points: {entry.AccessPointLines.Count}, Notes: {entry.NotesLines.Count}, NotesIcon: {(entry.NotesIconUrl ?? "none")}");
                    Logd($"Successfully scraped location detail: {entry.Name}");
                    return entry;
                }

                if (entry.Type == WikiEntryType.Monster)
                {
                    entry.MonsterLocationLink = null;
                    entry.AttackLines = new List<string>();
                    entry.SkillBlocks = new List<SkillBlock>();
                    entry.TempItemsDroppedLines = new List<WikiLinkItem>();
                    entry.ItemsDroppedLines = new List<WikiLinkItem>();
                    entry.NotesLines = new List<LinkedLine>();
                    entry.NotesIconUrl = null;
                    entry.Stats.Clear();
                    entry.ImageUrl = null;
                    entry.BaseStatsLines = new List<string>();
                    entry.AbilitiesLines = new List<string>();
                    entry.PhaseGroups = new List<PhaseGroup>();

                    entry.EventIconUrls = new List<string>();
                    entry.TopIconUrls = new List<string>();
                    ExtractTopIcons(pageContent, entry);

                    ParseMonsterContent(pageContent, entry);

                    Logd($"  Top icons: {entry.TopIconUrls.Count}, Level: {(GetStatInternal(entry, "Level") ?? "?")}, Attacks: {entry.AttackLines.Count}, Skill Blocks: {entry.SkillBlocks.Count}, Temp Items Dropped: {entry.TempItemsDroppedLines.Count}, Items Dropped: {entry.ItemsDroppedLines.Count}, Notes: {entry.NotesLines.Count}, NotesIcon: {(entry.NotesIconUrl ?? "none")}, Image: {(entry.ImageUrl ?? "none")}");
                    Logd($"Successfully scraped monster detail: {entry.Name}");
                    return entry;
                }

                if (entry.Type == WikiEntryType.Shop)
                {
                    entry.ShopLocationLinks = new List<WikiLinkItem>();
                    entry.ShopTabs = new List<ShopTab>();

                    entry.EventIconUrls = new List<string>();
                    entry.TopIconUrls = new List<string>();
                    ExtractTopIcons(pageContent, entry);

                    ParseShopContent(pageContent, entry);

                    Logd($"  Shop location links: {entry.ShopLocationLinks.Count}, Tabs: {entry.ShopTabs.Count}, Total rows: {entry.ShopTabs.Sum(t => t.Rows.Count)}");
                    Logd($"Successfully scraped shop detail: {entry.Name}");
                    return entry;
                }

                if (entry.Type == WikiEntryType.Quest)
                {
                    entry.QuestJumpLinks = new List<string>();
                    entry.QuestGroups = new List<QuestGroup>();
                    entry.EventIconUrls = new List<string>();
                    entry.TopIconUrls = new List<string>();
                    ExtractTopIcons(pageContent, entry);

                    ParseQuestContent(pageContent, entry);

                    Logd($"  Top icons: {entry.TopIconUrls.Count}, Jump links: {entry.QuestJumpLinks.Count}, Quest groups: {entry.QuestGroups.Count}, total tabs: {entry.QuestGroups.Sum(g => g.Tabs.Count)}");
                    Logd($"Successfully scraped quest detail: {entry.Name}");
                    return entry;
                }

                if (entry.Type == WikiEntryType.NPC)
                {
                    entry.NpcDialogueTabs = new List<NpcDialogueTab>();
                    entry.NpcLocationLinks = new List<WikiLinkItem>();
                    entry.Screens = new List<ScreenInfo>();
                    entry.NotesLines = new List<LinkedLine>();
                    entry.NotesIconUrl = null;
                    entry.ThanksLine = null;
                    entry.ImageUrl = null;

                    entry.EventIconUrls = new List<string>();
                    entry.TopIconUrls = new List<string>();
                    ExtractTopIcons(pageContent, entry);

                    ParseNpcContent(pageContent, entry);

                    Logd($"  Dialogue tabs: {entry.NpcDialogueTabs.Count}, Locations: {entry.NpcLocationLinks.Count}, Notes: {entry.NotesLines.Count}, Screens: {entry.Screens.Count}, Image: {(entry.ImageUrl ?? "none")}");
                    Logd($"Successfully scraped NPC detail: {entry.Name}");
                    return entry;
                }

                if (entry.Type == WikiEntryType.Enhancement)
                {
                    entry.EnhancementLocationLinks = new List<WikiLinkItem>();
                    entry.EnhancementGroups = new List<EnhancementGroup>();

                    entry.EventIconUrls = new List<string>();
                    entry.TopIconUrls = new List<string>();
                    ExtractTopIcons(pageContent, entry);

                    ParseEnhancementContent(pageContent, entry);

                    Logd($"  Location links: {entry.EnhancementLocationLinks.Count}, Enhancement groups: {entry.EnhancementGroups.Count}");
                    Logd($"Successfully scraped enhancement detail: {entry.Name}");
                    return entry;
                }

                var breadcrumbs = doc.DocumentNode.SelectSingleNode("//div[@id='breadcrumbs']");
                if (breadcrumbs != null)
                {
                    var links = breadcrumbs.SelectNodes(".//a");
                    if (links != null)
                    {
                        var itemLinks = links
                            .Select(a => a.GetAttributeValue("href", "").Trim('/'))
                            .Where(h => !string.IsNullOrEmpty(h) && !h.Equals("main", StringComparison.OrdinalIgnoreCase) && !h.Equals("items", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        if (itemLinks.Count > 0)
                            entry.CategorySlug = itemLinks.Last();
                    }
                }

                entry.ItemImageTabs = new List<MonsterImageTab>();
                var navsetDiv = pageContent.SelectSingleNode(".//div[contains(@class,'yui-navset')]");

                if (isGendered && navsetDiv != null)
                {
                    var navLabels = navsetDiv.SelectNodes(".//ul[contains(@class,'yui-nav')]/li")?
                        .Select(li =>
                        {
                            var em = li.SelectSingleNode(".//em");
                            string t = em != null ? em.InnerText : li.InnerText;
                            return Regex.Replace(WebUtility.HtmlDecode(t).Trim(), @"\s+", " ");
                        })
                        .ToList() ?? new List<string>();

                    var contentDiv = navsetDiv.SelectSingleNode(".//div[contains(@class,'yui-content')]");
                    var tabPanels = contentDiv?.SelectNodes("./div");

                    bool looksLikeGender = navLabels.Count >= 2
                        && navLabels[0].Equals("Male", StringComparison.OrdinalIgnoreCase)
                        && navLabels[1].Equals("Female", StringComparison.OrdinalIgnoreCase);

                    if (looksLikeGender && tabPanels != null && tabPanels.Count >= 2)
                    {
                        entry.ImageUrl = GetFirstImage(tabPanels[0]);
                        entry.ImageUrlFemale = GetFirstImage(tabPanels[1]);

                        Logd($"  Tabbed Male/Female layout detected — Male: {entry.ImageUrl ?? "none"}, Female: {entry.ImageUrlFemale ?? "none"}");
                    }
                    else if (tabPanels != null && tabPanels.Count >= 1)
                    {
                        for (int i = 0; i < tabPanels.Count; i++)
                        {
                            string src = GetFirstImage(tabPanels[i]);
                            if (string.IsNullOrEmpty(src)) continue;

                            string label = i < navLabels.Count && !string.IsNullOrEmpty(navLabels[i])
                                ? navLabels[i] : $"Tab {i + 1}";
                            entry.ItemImageTabs.Add(new MonsterImageTab { Label = label, ImageUrl = src });
                        }

                        if (entry.ItemImageTabs.Count > 0)
                            entry.ImageUrl = entry.ItemImageTabs[0].ImageUrl;

                        Logd($"  Generic labeled tabview detected ({entry.ItemImageTabs.Count} tab(s)): {string.Join(", ", entry.ItemImageTabs.Select(t => t.Label))}");
                    }
                    else
                    {
                        Logd("  yui-navset found but no usable tab panels — falling back to page images");
                        var validImageUrls = (pageContent.SelectNodes(".//img") ?? Enumerable.Empty<HtmlNode>())
                            .Select(img => img.GetAttributeValue("src", ""))
                            .Where(src => !string.IsNullOrEmpty(src) && !src.Contains("/image-tags/"))
                            .Select(NormalizeImageUrl)
                            .Distinct()
                            .ToList();

                        if (validImageUrls.Count > 0) entry.ImageUrl = validImageUrls[0];
                    }
                }
                else
                {
                    var validImageUrls = (pageContent.SelectNodes(".//img") ?? Enumerable.Empty<HtmlNode>())
                        .Select(img => img.GetAttributeValue("src", ""))
                        .Where(src => !string.IsNullOrEmpty(src) && !src.Contains("/image-tags/"))
                        .Select(NormalizeImageUrl)
                        .Distinct()
                        .ToList();

                    if (validImageUrls.Count > 0) entry.ImageUrl = validImageUrls[0];

                    Logd($"  Found {validImageUrls.Count} image(s) (no tabbed layout)");
                }

                entry.EventIconUrls = new List<string>();
                entry.TopIconUrls = new List<string>();
                ExtractTopIcons(pageContent, entry);
                Logd($"  Event icons: {entry.EventIconUrls.Count}, top icons (currency/rarity/tags): {entry.TopIconUrls.Count}");

                entry.Stats.Clear();
                entry.LocationLines = new List<LinkedLine>();
                entry.PriceLines = new List<LinkedLine>();
                entry.SellbackLines = new List<string>();
                entry.NotesLines = new List<LinkedLine>();
                entry.NotesIconUrl = null;
                entry.Description = null;
                entry.SpecialEffectsLines = new List<string>();
                entry.SkillBlocks = new List<SkillBlock>();

                ParseLabeledContent(pageContent, entry);
                ParseClassSkills(pageContent, entry);

                if (string.IsNullOrEmpty(entry.Description))
                {
                    var paragraphs = pageContent.SelectNodes(".//p");
                    if (paragraphs != null)
                    {
                        var fallback = paragraphs
                            .Select(p => p.InnerText.Trim())
                            .FirstOrDefault(t => t.Length > 40 && !t.Contains(":"));
                        if (fallback != null)
                            entry.Description = WebUtility.HtmlDecode(fallback);
                    }
                }

                Logd($"  Stats found: {string.Join(", ", entry.Stats.Keys)}");
                Logd($"  Locations: {entry.LocationLines.Count} line(s), Sellback: {entry.SellbackLines.Count} line(s), Notes: {entry.NotesLines.Count} line(s), NotesIcon: {(entry.NotesIconUrl ?? "none")}");
                Logd($"Successfully scraped detail: {entry.Name}");
            }
            catch (Exception ex)
            {
                Logd($"ERROR scraping detail for {entry.WikiUrl}: {ex.Message}");
            }

            return entry;
        }

        private static string GetStatInternal(WikiEntry entry, string key)
        {
            if (entry.Stats == null) return null;
            foreach (var kv in entry.Stats)
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            return null;
        }

        private static string GetFirstImage(HtmlNode scope)
        {
            var src = scope.SelectNodes(".//img")?
                .Select(i => i.GetAttributeValue("src", ""))
                .FirstOrDefault(s => !string.IsNullOrEmpty(s) && !s.Contains("/image-tags/"));

            return src != null ? NormalizeImageUrl(src) : null;
        }

        private static void ExtractTopIcons(HtmlNode pageContent, WikiEntry entry)
        {
            var imageTagImgs = pageContent.SelectNodes(".//img[contains(@src,'/image-tags/')]");
            if (imageTagImgs == null) return;

            var firstLabel = pageContent.SelectSingleNode(".//strong | .//b");
            int firstLabelPos = firstLabel?.StreamPosition ?? int.MaxValue;

            foreach (var img in imageTagImgs)
            {
                string src = img.GetAttributeValue("src", "");
                if (string.IsNullOrEmpty(src)) continue;
                src = NormalizeImageUrl(src);

                bool wrappedInLink = img.Ancestors("a").Any();
                if (wrappedInLink)
                {
                    entry.EventIconUrls.Add(src);
                    continue;
                }

                if (img.StreamPosition >= firstLabelPos) continue;

                entry.TopIconUrls.Add(src);
            }
        }

        private static void ParseLabeledContent(HtmlNode pageContent, WikiEntry entry)
        {
            string currentLabel = null;
            var inlineBuffers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var listFieldFilledByUl = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<(string Text, int Depth)> locationsRaw = null;

            foreach (var (kind, node) in FlattenContent(pageContent))
            {
                if (kind == "strong")
                {
                    string raw = WebUtility.HtmlDecode(node.InnerText).Trim().TrimEnd(':').Trim();
                    currentLabel = FlexibleListLabels.TryGetValue(raw, out var canonical) ? canonical : raw;
                    continue;
                }

                if (kind == "ul")
                {
                    if (currentLabel != null && RichLinkListLabels.Contains(currentLabel))
                    {
                        var parsed = ParseRichNestedList(node);

                        if (currentLabel.Equals("Locations", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.LocationLines = parsed;
                            entry.Location = parsed.FirstOrDefault()?.Runs.FirstOrDefault()?.Text;
                        }
                        else if (currentLabel.Equals("Price", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.PriceLines = parsed;
                        }
                        else if (currentLabel.Equals("Notes", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.NotesLines = parsed;
                        }

                        listFieldFilledByUl.Add(currentLabel);
                        continue;
                    }

                    if (currentLabel != null && FlexibleListLabels.ContainsValue(currentLabel))
                    {
                        var parsed = ParseNestedList(node);

                        if (currentLabel.Equals("Locations", StringComparison.OrdinalIgnoreCase))
                        {
                            locationsRaw = parsed;
                            entry.LocationLines = FormatNestedList(parsed).Select(s => new LinkedLine
                            {
                                Depth = 0,
                                Runs = new List<TextRun> { new TextRun { Text = s } }
                            }).ToList();
                        }
                        else if (currentLabel.Equals("Sellback", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.SellbackLines = FormatNestedList(parsed);
                        }
                        else if (currentLabel.Equals("Special Effects", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.SpecialEffectsLines = FormatNestedList(parsed);
                        }

                        listFieldFilledByUl.Add(currentLabel);
                    }
                    continue;
                }

                if (kind == "img")
                {
                    if (currentLabel != null && currentLabel.Equals("Notes", StringComparison.OrdinalIgnoreCase))
                    {
                        string src = node.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(src) && src.Contains("/image-tags/"))
                            entry.NotesIconUrl = NormalizeImageUrl(src);
                    }
                    continue;
                }

                if (kind == "br")
                {
                    currentLabel = null;
                    continue;
                }

                bool isFlexible = currentLabel != null && FlexibleListLabels.ContainsValue(currentLabel);
                if (currentLabel == null || !(InlineLabels.Contains(currentLabel) || isFlexible)) continue;

                string text = WebUtility.HtmlDecode(node.InnerText).Trim();
                if (text.Length == 0) continue;

                if (!inlineBuffers.TryGetValue(currentLabel, out var buf))
                    inlineBuffers[currentLabel] = buf = new List<string>();
                buf.Add(text);
            }

            foreach (var kv in inlineBuffers)
            {
                string value = Regex.Replace(string.Join(" ", kv.Value), @"\s+", " ").Trim();

                if (kv.Key.Equals("Rarity", StringComparison.OrdinalIgnoreCase))
                    value = Regex.Replace(value, @"\s+Rarity$", "", RegexOptions.IgnoreCase);

                if (kv.Key.Equals("Description", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Description = value;
                    continue;
                }

                if (FlexibleListLabels.ContainsValue(kv.Key) || RichLinkListLabels.Contains(kv.Key))
                {
                    if (listFieldFilledByUl.Contains(kv.Key)) continue;

                    if (RichLinkListLabels.Contains(kv.Key))
                    {
                        var line = new LinkedLine
                        {
                            Depth = 0,
                            Runs = new List<TextRun> { new TextRun { Text = value } }
                        };

                        if (kv.Key.Equals("Locations", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.LocationLines = new List<LinkedLine> { line };
                            entry.Location = value;
                        }
                        else if (kv.Key.Equals("Price", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.PriceLines = new List<LinkedLine> { line };
                        }
                        else if (kv.Key.Equals("Notes", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.NotesLines = new List<LinkedLine> { line };
                        }
                        continue;
                    }

                    var singleLine = new List<string> { $"• {value}" };

                    if (kv.Key.Equals("Sellback", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.SellbackLines = singleLine;
                    }
                    else if (kv.Key.Equals("Special Effects", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.SpecialEffectsLines = singleLine;
                    }
                    continue;
                }

                entry.Stats[kv.Key] = value;
            }

            if (locationsRaw != null && locationsRaw.Count > 0)
                entry.Location = locationsRaw[0].Text;
        }

        private static void ParseLocationContent(HtmlNode pageContent, WikiEntry entry)
        {
            string currentLabel = null;
            var inlineBuffers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            bool notesFilledByUl = false;

            foreach (var (kind, node) in FlattenContent(pageContent))
            {
                if (kind == "strong")
                {
                    string raw = WebUtility.HtmlDecode(node.InnerText).Trim().TrimEnd(':').Trim();
                    if (LocationListLabels.TryGetValue(raw, out var canonicalList))
                        currentLabel = canonicalList;
                    else if (LocationFlexibleLabels.TryGetValue(raw, out var canonicalFlex))
                        currentLabel = canonicalFlex;
                    else
                        currentLabel = raw;
                    continue;
                }

                if (kind == "ul")
                {
                    if (currentLabel != null && LocationListLabels.ContainsValue(currentLabel))
                    {
                        var items = ParseLinkList(node);

                        switch (currentLabel)
                        {
                            case "Monsters": entry.MonsterLines = items; break;
                            case "NPCs": entry.NpcLines = items; break;
                            case "Quests": entry.QuestLines = items; break;
                            case "Shops": entry.ShopLines = items; break;
                            case "Access Points": entry.AccessPointLines = items; break;
                        }

                        currentLabel = null;
                    }
                    else if (currentLabel != null && currentLabel.Equals("Notes", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.NotesLines = ParseRichNestedList(node);
                        notesFilledByUl = true;
                    }
                    continue;
                }

                if (kind == "img")
                {
                    if (currentLabel != null && currentLabel.Equals("Notes", StringComparison.OrdinalIgnoreCase))
                    {
                        string src = node.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(src) && src.Contains("/image-tags/"))
                            entry.NotesIconUrl = NormalizeImageUrl(src);
                    }
                    continue;
                }

                if (kind == "br")
                {
                    currentLabel = null;
                    continue;
                }

                bool isNotesFlexible = currentLabel != null && currentLabel.Equals("Notes", StringComparison.OrdinalIgnoreCase);
                if (currentLabel == null || !(LocationInlineLabels.Contains(currentLabel) || isNotesFlexible)) continue;

                string text = WebUtility.HtmlDecode(node.InnerText).Trim();
                if (text.Length == 0) continue;

                if (!inlineBuffers.TryGetValue(currentLabel, out var buf))
                    inlineBuffers[currentLabel] = buf = new List<string>();
                buf.Add(text);
            }

            foreach (var kv in inlineBuffers)
            {
                string value = Regex.Replace(string.Join(" ", kv.Value), @"\s+", " ").Trim();

                if (kv.Key.Equals("Map Name", StringComparison.OrdinalIgnoreCase))
                    entry.MapName = value;
                else if (kv.Key.Equals("Room Limit", StringComparison.OrdinalIgnoreCase))
                    entry.RoomLimit = value;
                else if (kv.Key.Equals("Notes", StringComparison.OrdinalIgnoreCase))
                {
                    if (notesFilledByUl) continue;
                    entry.NotesLines = new List<LinkedLine>
                    {
                        new LinkedLine { Depth = 0, Runs = new List<TextRun> { new TextRun { Text = value } } }
                    };
                }
            }
        }

        private static void ParseMonsterContent(HtmlNode pageContent, WikiEntry entry)
        {
            string currentLabel = null;
            bool inAttacksSection = false;
            var inlineBuffers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            WikiLinkItem locationLink = null;

            var currentPhaseBlocks = new List<SkillBlock>();
            int phaseIndex = 0;

            void FlushPhaseGroup()
            {
                if (currentPhaseBlocks.Count == 0) return;
                string label = phaseIndex == 0 ? null : $"Phase {phaseIndex}";
                foreach (var b in currentPhaseBlocks) b.PhaseLabel = label;
                entry.PhaseGroups.Add(new PhaseGroup { Label = label, Blocks = new List<SkillBlock>(currentPhaseBlocks) });
                entry.SkillBlocks.AddRange(currentPhaseBlocks);
                currentPhaseBlocks.Clear();
            }

            void HandleAttacksItem(string kind, HtmlNode node)
            {
                if (kind == "phase_marker")
                {
                    FlushPhaseGroup();
                    phaseIndex++;
                }
                else
                {
                    currentPhaseBlocks.Add(ParseSingleCollapsibleBlock(node));
                }
            }

            void FlattenAttacksNode(HtmlNode node)
            {
                foreach (var child in node.ChildNodes)
                {
                    if (child.NodeType == HtmlNodeType.Comment) continue;
                    if (child.Name == "script" || child.Name == "style") continue;

                    if (child.Name == "p" && child.SelectSingleNode(".//iframe[contains(@class,'html-block-iframe')]") != null)
                    {
                        HandleAttacksItem("phase_marker", child);
                        continue;
                    }

                    var classes = child.GetAttributeValue("class", "")
                        .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (child.Name == "div" && classes.Contains("collapsible-block"))
                    {
                        HandleAttacksItem("skill_block", child);
                        continue;
                    }

                    FlattenAttacksNode(child);
                }
            }

            foreach (var child in pageContent.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Comment) continue;
                if (child.Name == "script" || child.Name == "style") continue;

                if (child.Name == "br")
                {
                    currentLabel = null;
                    continue;
                }

                if (child.Name == "strong" || child.Name == "b")
                {
                    string bareLabel = WebUtility.HtmlDecode(child.InnerText).Trim().TrimEnd(':').Trim();
                    currentLabel = bareLabel;
                    inAttacksSection = bareLabel.Equals("Attacks", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (child.Name == "p")
                {
                    bool isPhaseMarker = inAttacksSection
                        && child.SelectSingleNode(".//iframe[contains(@class,'html-block-iframe')]") != null;

                    if (isPhaseMarker)
                    {
                        HandleAttacksItem("phase_marker", child);
                        continue;
                    }

                    foreach (var (kind, node) in WalkParagraph(child))
                    {
                        if (kind == "strong")
                        {
                            string raw = WebUtility.HtmlDecode(node.InnerText).Trim().TrimEnd(':').Trim();
                            currentLabel = raw;
                            inAttacksSection = raw.Equals("Attacks", StringComparison.OrdinalIgnoreCase);
                            continue;
                        }

                        if (kind == "br") { currentLabel = null; continue; }

                        if (kind == "a")
                        {
                            if (currentLabel != null && currentLabel.Equals("Location", StringComparison.OrdinalIgnoreCase))
                            {
                                string href = node.GetAttributeValue("href", "");
                                string text = WebUtility.HtmlDecode(node.InnerText).Trim();
                                if (!string.IsNullOrEmpty(text))
                                {
                                    locationLink = new WikiLinkItem
                                    {
                                        Text = text,
                                        Url = string.IsNullOrEmpty(href) ? null : NormalizeWikiUrl(href)
                                    };
                                }
                            }
                            continue;
                        }

                        if (currentLabel == null) continue;
                        string t = WebUtility.HtmlDecode(node.InnerText).Trim();
                        if (t.Length == 0) continue;

                        if (!inlineBuffers.TryGetValue(currentLabel, out var buf))
                            inlineBuffers[currentLabel] = buf = new List<string>();
                        buf.Add(t);
                    }
                    continue;
                }

                if (child.Name == "ul")
                {
                    if (currentLabel == null) continue;

                    if (currentLabel.Equals("Attacks", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.AttackLines = child.ChildNodes.Where(n => n.Name == "li")
                            .Select(li => Regex.Replace(WebUtility.HtmlDecode(li.InnerText).Trim(), @"\s+", " "))
                            .Where(t => t.Length > 0)
                            .ToList();
                    }
                    else if (currentLabel.Equals("Base Stats", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.BaseStatsLines = child.ChildNodes.Where(n => n.Name == "li")
                            .Select(li => Regex.Replace(WebUtility.HtmlDecode(li.InnerText).Trim(), @"\s+", " "))
                            .Where(t => t.Length > 0)
                            .ToList();
                    }
                    else if (currentLabel.Equals("Abilities", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.AbilitiesLines = child.ChildNodes.Where(n => n.Name == "li")
                            .Select(li => Regex.Replace(WebUtility.HtmlDecode(li.InnerText).Trim(), @"\s+", " "))
                            .Where(t => t.Length > 0)
                            .ToList();
                    }
                    else if (currentLabel.Equals("Temporary Items Dropped", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.TempItemsDroppedLines = ParseLinkList(child);
                    }
                    else if (currentLabel.Equals("Items Dropped", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ItemsDroppedLines = ParseDroppedItemsList(child);
                    }
                    else if (currentLabel.Equals("Notes", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.NotesLines = ParseRichNestedList(child);
                    }

                    if (!currentLabel.Equals("Attacks", StringComparison.OrdinalIgnoreCase))
                        currentLabel = null;

                    continue;
                }

                if (child.Name == "div")
                {
                    string divClass = child.GetAttributeValue("class", "");
                    var classList = divClass.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (divClass.Contains("yui-navset"))
                    {
                        var tabLabels = child.SelectNodes(".//ul[contains(@class,'yui-nav')]/li/a")?
                            .Select(a => {
                                var em = a.SelectSingleNode(".//em");
                                if (em != null)
                                    return Regex.Replace(WebUtility.HtmlDecode(em.InnerText).Trim(), @"\s+", " ");
                                return Regex.Replace(WebUtility.HtmlDecode(a.InnerText).Trim(), @"\s+", " ");
                            })
                            .ToList() ?? new List<string>();

                        var contentDiv = child.SelectSingleNode(".//div[contains(@class,'yui-content')]");
                        if (contentDiv != null)
                        {
                            var panels = contentDiv.SelectNodes("./div");
                            if (panels != null)
                            {
                                for (int i = 0; i < panels.Count; i++)
                                {
                                    var panel = panels[i];
                                    var img = panel.SelectSingleNode(".//img");
                                    if (img != null)
                                    {
                                        string src = img.GetAttributeValue("src", "");
                                        if (!string.IsNullOrEmpty(src) && !src.Contains("/image-tags/"))
                                        {
                                            string label = i < tabLabels.Count && !string.IsNullOrEmpty(tabLabels[i])
                                                ? tabLabels[i]
                                                : $"Variant {i + 1}";
                                            entry.MonsterImageTabs.Add(new MonsterImageTab { Label = label, ImageUrl = NormalizeImageUrl(src) });

                                            if (string.IsNullOrEmpty(entry.ImageUrl))
                                                entry.ImageUrl = NormalizeImageUrl(src);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (inAttacksSection)
                    {
                        if (classList.Contains("collapsible-block"))
                        {
                            HandleAttacksItem("skill_block", child);
                        }
                        else if (divClass.Contains("skills-container"))
                        {
                            FlattenAttacksNode(child);
                        }
                    }
                    continue;
                }

                if (child.Name == "img")
                {
                    string src = child.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(src) && !src.Contains("/image-tags/") && string.IsNullOrEmpty(entry.ImageUrl))
                        entry.ImageUrl = NormalizeImageUrl(src);
                    continue;
                }
            }

            FlushPhaseGroup();

            foreach (var kv in inlineBuffers)
            {
                string value = Regex.Replace(string.Join(" ", kv.Value), @"\s+", " ").Trim();
                entry.Stats[kv.Key] = value;
            }

            entry.MonsterLocationLink = locationLink;

            Logd($"  Phase groups: {string.Join(", ", entry.PhaseGroups.Select(g => $"[{g.Label ?? "none"}]={g.Blocks.Count}"))}");
        }

        private static SkillBlock ParseSingleCollapsibleBlock(HtmlNode block)
        {
            string headerRaw = block.SelectSingleNode(".//a[contains(@class,'collapsible-block-link')]")?.InnerText;
            string header = !string.IsNullOrEmpty(headerRaw)
                ? Regex.Replace(WebUtility.HtmlDecode(headerRaw).Trim().TrimStart('+', '-').Trim().Replace('\u00A0', ' '), @"\s+", " ").Trim()
                : "Skill";

            var skillBlock = new SkillBlock { HeaderTitle = header };

            var content = block.SelectSingleNode(".//div[contains(@class,'collapsible-block-content')]");
            if (content != null)
            {
                foreach (var child in content.ChildNodes)
                {
                    if (child.NodeType != HtmlNodeType.Element) continue;
                    string cls = child.GetAttributeValue("class", "");

                    if (cls.Contains("skill-container"))
                    {
                        var table = child.SelectSingleNode(".//table[contains(@class,'skill-info')]");
                        if (table != null)
                            skillBlock.Rows.Add(new SkillRowItem { IsLink = false, Skill = ParseSkillTable(table) });
                    }
                    else if (cls.Contains("skill-link"))
                    {
                        string linkText = child.SelectSingleNode(".//span[contains(@class,'link-text')]")?.InnerText;
                        skillBlock.Rows.Add(new SkillRowItem
                        {
                            IsLink = true,
                            LinkText = !string.IsNullOrEmpty(linkText) ? WebUtility.HtmlDecode(linkText).Trim() : ""
                        });
                    }
                }
            }

            return skillBlock;
        }

        private static void ParseShopContent(HtmlNode pageContent, WikiEntry entry)
        {
            var locationP = pageContent.SelectNodes(".//p")?.FirstOrDefault(p =>
            {
                string label = (p.SelectSingleNode("./strong") ?? p.SelectSingleNode("./b"))
                    ?.InnerText.Trim().TrimEnd(':').Trim();
                return label != null && label.Equals("Location", StringComparison.OrdinalIgnoreCase);
            });

            if (locationP != null)
            {
                var links = locationP.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>();
                bool first = true;
                foreach (var a in links)
                {
                    string href = a.GetAttributeValue("href", "");
                    string text = WebUtility.HtmlDecode(a.InnerText).Trim();
                    if (string.IsNullOrEmpty(text)) continue;

                    if (!first)
                        entry.ShopLocationLinks.Add(new WikiLinkItem { Text = "-", Url = null });

                    entry.ShopLocationLinks.Add(new WikiLinkItem
                    {
                        Text = text,
                        Url = !string.IsNullOrEmpty(href) ? NormalizeWikiUrl(href) : null
                    });
                    first = false;
                }
            }

            var navset = pageContent.SelectSingleNode(".//div[contains(@class,'yui-navset')]");
            if (navset != null)
            {
                var tabLabels = navset.SelectNodes(".//ul[contains(@class,'yui-nav')]/li")?
                    .Select(li =>
                    {
                        var em = li.SelectSingleNode(".//em");
                        string t = em != null ? em.InnerText : li.InnerText;
                        return Regex.Replace(WebUtility.HtmlDecode(t).Trim(), @"\s+", " ");
                    })
                    .ToList() ?? new List<string>();

                var panels = navset.SelectNodes("./div[contains(@class,'yui-content')]/div");
                if (panels != null)
                {
                    for (int i = 0; i < panels.Count; i++)
                    {
                        var table = panels[i].SelectSingleNode(".//table");
                        var rows = table != null ? ParseShopTable(table) : new List<ShopRow>();
                        string label = i < tabLabels.Count && !string.IsNullOrEmpty(tabLabels[i])
                            ? tabLabels[i] : $"Tab {i + 1}";
                        entry.ShopTabs.Add(new ShopTab { Label = label, Rows = rows });
                    }
                }
            }
            else
            {
                var table = pageContent.SelectSingleNode(".//table");
                if (table != null)
                    entry.ShopTabs.Add(new ShopTab { Label = "Items", Rows = ParseShopTable(table) });
            }
        }

        private static List<ShopRow> ParseShopTable(HtmlNode table)
        {
            var rows = new List<ShopRow>();
            var trs = table.SelectNodes("./tr");
            if (trs == null) return rows;

            foreach (var tr in trs)
            {
                if (tr.SelectSingleNode("./th") != null) continue;

                var tds = tr.SelectNodes("./td");
                if (tds == null || tds.Count < 3) continue;

                string iconUrl = null;
                var iconImg = tds[0].SelectSingleNode(".//img");
                if (iconImg != null)
                {
                    string src = iconImg.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(src))
                        iconUrl = NormalizeImageUrl(src);
                }

                rows.Add(new ShopRow
                {
                    IconUrl = iconUrl,
                    NameLink = ParseShopNameCell(tds[1]),
                    PriceItems = ParseShopPriceCell(tds[2])
                });
            }

            return rows;
        }

        private static WikiLinkItem ParseShopNameCell(HtmlNode td)
        {
            string text = null, url = null;
            var icons = new List<string>();

            foreach (var child in td.ChildNodes)
            {
                if (child.Name == "a")
                {
                    text = WebUtility.HtmlDecode(child.InnerText).Trim();
                    string href = child.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(href))
                        url = NormalizeWikiUrl(href);
                }
                else if (child.Name == "img")
                {
                    string src = child.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(src))
                        icons.Add(NormalizeImageUrl(src));
                }
            }

            return new WikiLinkItem { Text = text ?? "", Url = url, IconUrls = icons };
        }

        private static List<WikiLinkItem> ParseShopPriceCell(HtmlNode td)
        {
            var result = new List<WikiLinkItem>();
            string linkText = null, linkUrl = null;
            var trailing = new List<string>();

            void FlushLine()
            {
                string trail = string.Join(" ", trailing).Trim().TrimEnd(',').Trim();
                string text = !string.IsNullOrEmpty(linkText)
                    ? (string.IsNullOrEmpty(trail) ? linkText : $"{linkText} {trail}")
                    : trail;

                if (!string.IsNullOrEmpty(text))
                    result.Add(new WikiLinkItem { Text = text, Url = linkUrl });

                linkText = null;
                linkUrl = null;
                trailing.Clear();
            }

            foreach (var child in td.ChildNodes)
            {
                if (child.Name == "br")
                {
                    FlushLine();
                    continue;
                }
                if (child.Name == "a")
                {
                    linkText = WebUtility.HtmlDecode(child.InnerText).Trim();
                    string href = child.GetAttributeValue("href", "");
                    linkUrl = !string.IsNullOrEmpty(href) ? NormalizeWikiUrl(href) : null;
                    continue;
                }
                if (child.Name == "img") continue;
                if (child.NodeType == HtmlNodeType.Text)
                {
                    string t = WebUtility.HtmlDecode(child.InnerText).Trim();
                    if (!string.IsNullOrEmpty(t))
                        trailing.Add(t);
                }
            }
            FlushLine();

            return result;
        }

        // ===================== QUEST PARSING =====================

        private static void ParseQuestContent(HtmlNode pageContent, WikiEntry entry)
        {
            entry.QuestJumpLinks = new List<string>();
            entry.QuestGroups = new List<QuestGroup>();

            var currentGroup = new QuestGroup();
            string currentLabel = null;
            bool groupHasContent = false;

            void FlushGroup()
            {
                if (groupHasContent)
                    entry.QuestGroups.Add(currentGroup);
                currentGroup = new QuestGroup();
                groupHasContent = false;
                currentLabel = null;
            }

            foreach (var child in pageContent.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Comment) continue;
                if (child.Name == "script" || child.Name == "style") continue;

                if (child.Name == "hr")
                {
                    FlushGroup();
                    continue;
                }

                if (child.Name == "img") continue;

                if (child.Name == "p")
                {
                    var subNode = child.SelectSingleNode("./sub");
                    if (subNode != null && subNode.InnerText.TrimStart().StartsWith("Jump to", StringComparison.OrdinalIgnoreCase))
                    {
                        var jumpLinks = subNode.SelectNodes(".//a");
                        if (jumpLinks != null)
                            foreach (var a in jumpLinks)
                                entry.QuestJumpLinks.Add(WebUtility.HtmlDecode(a.InnerText).Trim());
                        continue;
                    }

                    var anchor = child.SelectSingleNode("./a[@name]");
                    if (anchor != null && child.ChildNodes.Count <= 1)
                    {
                        string name = anchor.GetAttributeValue("name", "");
                        if (!string.Equals(name, "space", StringComparison.OrdinalIgnoreCase))
                            currentGroup.AnchorName = name;
                        continue;
                    }

                    var buffer = new List<string>();
                    string bufferedLabel = null;
                    string bufferedLinkUrl = null;

                    void FlushBuffer()
                    {
                        if (bufferedLabel == null) return;
                        string text = Regex.Replace(string.Join(" ", buffer), @"\s+", " ").Trim();
                        buffer.Clear();

                        if (text.Length == 0 && bufferedLinkUrl == null) { bufferedLabel = null; return; }
                        groupHasContent = true;

                        if (bufferedLabel.Equals("Quest Location", StringComparison.OrdinalIgnoreCase))
                            currentGroup.LocationLines.Add(new QuestSubLine { Text = text, Url = bufferedLinkUrl });
                        else if (bufferedLabel.Equals("Quests Begun From", StringComparison.OrdinalIgnoreCase))
                            currentGroup.BegunFromLink = new QuestSubLine { Text = text, Url = bufferedLinkUrl };
                        else if (bufferedLabel.Equals("Requirements", StringComparison.OrdinalIgnoreCase))
                        {
                            if (text.Length > 0) currentGroup.RequirementLines.Add(text);
                        }
                        else if (bufferedLabel.Equals("Note", StringComparison.OrdinalIgnoreCase) || bufferedLabel.Equals("Notes", StringComparison.OrdinalIgnoreCase))
                        {
                            if (text.Length > 0) currentGroup.NoteLines.Add(text);
                        }

                        bufferedLinkUrl = null;
                    }

                    foreach (var (kind, node) in WalkParagraph(child))
                    {
                        if (kind == "strong")
                        {
                            FlushBuffer();
                            string raw = WebUtility.HtmlDecode(node.InnerText).Trim().TrimEnd(':').Trim();
                            bufferedLabel = QuestGroupLabels.Contains(raw) ? raw : null;
                            currentLabel = bufferedLabel;
                            continue;
                        }
                        if (kind == "br") { FlushBuffer(); bufferedLabel = null; currentLabel = null; continue; }
                        if (bufferedLabel == null) continue;

                        if (kind == "a")
                        {
                            string href = node.GetAttributeValue("href", "");
                            string text = WebUtility.HtmlDecode(node.InnerText).Trim();
                            if (!string.IsNullOrEmpty(text)) buffer.Add(text);
                            if (bufferedLinkUrl == null && !string.IsNullOrEmpty(href))
                                bufferedLinkUrl = NormalizeWikiUrl(href);
                            continue;
                        }

                        string t = WebUtility.HtmlDecode(node.InnerText).Trim();
                        if (!string.IsNullOrEmpty(t)) buffer.Add(t);
                    }
                    FlushBuffer();
                    continue;
                }

                if (child.Name == "ul")
                {
                    if (currentLabel != null && currentLabel.Equals("Quest Location", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var li in child.ChildNodes.Where(n => n.Name == "li"))
                        {
                            var a = li.SelectSingleNode(".//a");
                            if (a != null)
                            {
                                string href = a.GetAttributeValue("href", "");
                                currentGroup.LocationLines.Add(new QuestSubLine
                                {
                                    Text = WebUtility.HtmlDecode(a.InnerText).Trim(),
                                    Url = string.IsNullOrEmpty(href) ? null : NormalizeWikiUrl(href)
                                });
                            }
                            else
                            {
                                string t = WebUtility.HtmlDecode(li.InnerText).Trim();
                                if (!string.IsNullOrEmpty(t))
                                    currentGroup.LocationLines.Add(new QuestSubLine { Text = t });
                            }
                        }
                        groupHasContent = true;
                    }
                    else if (currentLabel != null && currentLabel.Equals("Requirements", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var li in child.ChildNodes.Where(n => n.Name == "li"))
                        {
                            string t = Regex.Replace(WebUtility.HtmlDecode(li.InnerText).Trim(), @"\s+", " ");
                            if (!string.IsNullOrEmpty(t)) currentGroup.RequirementLines.Add(t);
                        }
                        groupHasContent = true;
                    }
                    continue;
                }

                if (child.Name == "div")
                {
                    string divClass = child.GetAttributeValue("class", "");
                    if (divClass.Contains("yui-navset"))
                    {
                        currentGroup.Tabs = ParseQuestTabs(child);
                        groupHasContent = true;
                    }
                    continue;
                }
            }

            FlushGroup();
        }

        private static List<QuestTabInfo> ParseQuestTabs(HtmlNode navset)
        {
            var result = new List<QuestTabInfo>();

            var tabLabels = navset.SelectNodes(".//ul[contains(@class,'yui-nav')]/li")?
                .Select(li =>
                {
                    var em = li.SelectSingleNode(".//em");
                    string t = em != null ? em.InnerText : li.InnerText;
                    return Regex.Replace(WebUtility.HtmlDecode(t).Trim(), @"\s+", " ");
                })
                .ToList() ?? new List<string>();

            var contentDiv = navset.SelectSingleNode(".//div[contains(@class,'yui-content')]");
            var panels = contentDiv?.SelectNodes("./div");
            if (panels == null) return result;

            for (int i = 0; i < panels.Count; i++)
            {
                var tab = new QuestTabInfo
                {
                    Label = i < tabLabels.Count && !string.IsNullOrEmpty(tabLabels[i]) ? tabLabels[i] : $"Quest {i + 1}"
                };
                ParseQuestTabPanel(panels[i], tab);
                result.Add(tab);
            }

            return result;
        }

        private static void ParseQuestTabPanel(HtmlNode panel, QuestTabInfo tab)
        {
            string currentLabel = null;
            bool descriptionSet = false;

            foreach (var child in panel.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Comment) continue;
                if (child.Name == "script" || child.Name == "style") continue;

                if (child.Name == "img")
                {
                    string src = child.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(src) && src.Contains("/image-tags/"))
                        tab.TopIconUrls.Add(NormalizeImageUrl(src));
                    continue;
                }

                if (child.Name == "div" && child.GetAttributeValue("class", "").Contains("collapsible-block"))
                {
                    string headerRaw = child.SelectSingleNode(".//a[contains(@class,'collapsible-block-link')]")?.InnerText;
                    string header = !string.IsNullOrEmpty(headerRaw)
                        ? Regex.Replace(WebUtility.HtmlDecode(headerRaw).Trim().TrimStart('+', '-').Trim().Replace('\u00A0', ' '), @"\s+", " ").Trim()
                        : "";

                    if (header.IndexOf("When Completed", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var contentP = child.SelectSingleNode(".//div[contains(@class,'collapsible-block-content')]//p");
                        if (contentP != null)
                            tab.CompletionText = WebUtility.HtmlDecode(contentP.InnerText).Trim();
                    }
                    continue;
                }

                if (child.Name == "p")
                {
                    var strong = child.SelectSingleNode("./strong") ?? child.SelectSingleNode("./b");
                    if (strong == null)
                    {
                        string text = WebUtility.HtmlDecode(child.InnerText).Trim();
                        if (text.Length == 0) continue;

                        bool looksLikeThanks = child.SelectSingleNode(".//em") != null
                            && text.StartsWith("Thanks to", StringComparison.OrdinalIgnoreCase);

                        if (looksLikeThanks) tab.ThanksLine = text;
                        else if (!descriptionSet) { tab.Description = text; descriptionSet = true; }

                        currentLabel = null;
                        continue;
                    }

                    string raw = WebUtility.HtmlDecode(strong.InnerText).Trim().TrimEnd(':').Trim();
                    currentLabel = raw;

                    var afterStrong = new List<string>();
                    bool passedStrong = false;
                    foreach (var c in child.ChildNodes)
                    {
                        if (c == strong) { passedStrong = true; continue; }
                        if (!passedStrong) continue;
                        if (c.Name == "br") break;
                        string t = WebUtility.HtmlDecode(c.InnerText).Trim();
                        if (!string.IsNullOrEmpty(t)) afterStrong.Add(t);
                    }
                    string inlineValue = Regex.Replace(string.Join(" ", afterStrong), @"\s+", " ").Trim();
                    if (!string.IsNullOrEmpty(inlineValue) && raw.Equals("Requirements", StringComparison.OrdinalIgnoreCase))
                        tab.RequirementLines.Add(inlineValue);

                    continue;
                }

                if (child.Name == "ul")
                {
                    if (currentLabel == null) continue;

                    if (currentLabel.Equals("Requirements", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var li in child.ChildNodes.Where(n => n.Name == "li"))
                        {
                            string t = Regex.Replace(WebUtility.HtmlDecode(li.InnerText).Trim(), @"\s+", " ");
                            if (!string.IsNullOrEmpty(t)) tab.RequirementLines.Add(t);
                        }
                    }
                    else if (currentLabel.Equals("Items Required", StringComparison.OrdinalIgnoreCase))
                    {
                        tab.ItemsRequired = ParseQuestItemsRequired(child);
                    }
                    else if (currentLabel.Equals("Rewards", StringComparison.OrdinalIgnoreCase))
                    {
                        tab.RewardLines = ParseQuestRewardLines(child);
                    }
                    else if (currentLabel.Equals("Items", StringComparison.OrdinalIgnoreCase)
                          || currentLabel.IndexOf("also receive", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        tab.ItemRewardsLabel = currentLabel;
                        tab.ItemRewards = ParseQuestItemRewardLines(child);
                    }

                    continue;
                }
            }
        }

        private static List<QuestRequiredItem> ParseQuestItemsRequired(HtmlNode ul)
        {
            var result = new List<QuestRequiredItem>();
            if (ul == null) return result;

            foreach (var li in ul.ChildNodes.Where(n => n.Name == "li"))
            {
                var item = new QuestRequiredItem();
                var ownParts = new List<string>();
                HtmlNode nestedUl = null;

                foreach (var child in li.ChildNodes)
                {
                    if (child.Name == "ul") { nestedUl = child; continue; }
                    if (child.Name == "img") continue;

                    if (child.Name == "a" && item.Url == null)
                    {
                        string href = child.GetAttributeValue("href", "");
                        if (!string.IsNullOrEmpty(href)) item.Url = NormalizeWikiUrl(href);
                    }

                    string t = WebUtility.HtmlDecode(child.InnerText).Trim();
                    if (!string.IsNullOrEmpty(t)) ownParts.Add(t);
                }

                item.Text = Regex.Replace(string.Join(" ", ownParts), @"\s+", " ").Trim();

                if (nestedUl != null)
                {
                    foreach (var subLi in nestedUl.ChildNodes.Where(n => n.Name == "li"))
                    {
                        var innerNestedUl = subLi.SelectSingleNode("./ul");
                        if (innerNestedUl != null)
                        {
                            string label = Regex.Replace(WebUtility.HtmlDecode(
                                string.Join(" ", subLi.ChildNodes.Where(n => n.Name != "ul")
                                    .Select(n => WebUtility.HtmlDecode(n.InnerText).Trim()))).Trim(), @"\s+", " ");
                            if (!string.IsNullOrEmpty(label))
                                item.SubLines.Add(new QuestSubLine { Text = label });

                            foreach (var deepLi in innerNestedUl.ChildNodes.Where(n => n.Name == "li"))
                            {
                                var a = deepLi.SelectSingleNode(".//a");
                                if (a != null)
                                {
                                    string href = a.GetAttributeValue("href", "");
                                    item.SubLines.Add(new QuestSubLine
                                    {
                                        Text = "• " + WebUtility.HtmlDecode(a.InnerText).Trim(),
                                        Url = string.IsNullOrEmpty(href) ? null : NormalizeWikiUrl(href)
                                    });
                                }
                            }
                        }
                        else
                        {
                            var a = subLi.SelectSingleNode(".//a");
                            string subText = Regex.Replace(WebUtility.HtmlDecode(subLi.InnerText).Trim(), @"\s+", " ");
                            if (a != null)
                            {
                                string href = a.GetAttributeValue("href", "");
                                item.SubLines.Add(new QuestSubLine { Text = subText, Url = string.IsNullOrEmpty(href) ? null : NormalizeWikiUrl(href) });
                            }
                            else if (!string.IsNullOrEmpty(subText))
                            {
                                item.SubLines.Add(new QuestSubLine { Text = subText });
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(item.Text) || item.SubLines.Count > 0)
                    result.Add(item);
            }

            return result;
        }

        private static List<QuestRewardLine> ParseQuestRewardLines(HtmlNode ul)
        {
            var result = new List<QuestRewardLine>();
            if (ul == null) return result;

            foreach (var li in ul.ChildNodes.Where(n => n.Name == "li"))
            {
                string url = null;
                var parts = new List<string>();

                foreach (var child in li.ChildNodes)
                {
                    if (child.Name == "a" && url == null)
                    {
                        string href = child.GetAttributeValue("href", "");
                        if (!string.IsNullOrEmpty(href)) url = NormalizeWikiUrl(href);
                    }
                    string t = WebUtility.HtmlDecode(child.InnerText).Trim();
                    if (!string.IsNullOrEmpty(t)) parts.Add(t);
                }

                string text = Regex.Replace(string.Join(" ", parts), @"\s+", " ").Trim();
                if (!string.IsNullOrEmpty(text))
                    result.Add(new QuestRewardLine { Text = text, Url = url });
            }

            return result;
        }

        private static List<QuestItemRewardLine> ParseQuestItemRewardLines(HtmlNode ul)
        {
            var result = new List<QuestItemRewardLine>();
            if (ul == null) return result;

            foreach (var li in ul.ChildNodes.Where(n => n.Name == "li"))
            {
                string text = null, url = null;
                var icons = new List<string>();

                foreach (var child in li.ChildNodes)
                {
                    if (child.Name == "a")
                    {
                        string t = WebUtility.HtmlDecode(child.InnerText).Trim();
                        if (!string.IsNullOrEmpty(t)) text = t;
                        string href = child.GetAttributeValue("href", "");
                        if (!string.IsNullOrEmpty(href)) url = NormalizeWikiUrl(href);
                    }
                    else if (child.Name == "img")
                    {
                        string src = child.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(src)) icons.Add(NormalizeImageUrl(src));
                    }
                    else if (child.NodeType == HtmlNodeType.Text)
                    {
                        string t = WebUtility.HtmlDecode(child.InnerText).Trim();
                        if (!string.IsNullOrEmpty(t))
                            text = string.IsNullOrEmpty(text) ? t : $"{text} {t}";
                    }
                }

                if (!string.IsNullOrEmpty(text))
                    result.Add(new QuestItemRewardLine { Text = text, Url = url, IconUrls = icons });
            }

            return result;
        }

        // ===================== NPC PARSING =====================

        private static List<HtmlNode> FlattenNpcWrapperDivs(HtmlNode container)
        {
            var result = new List<HtmlNode>();
            foreach (var child in container.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Comment) continue;
                if (child.Name == "script" || child.Name == "style") continue;

                if (child.Name == "div")
                {
                    string cls = child.GetAttributeValue("class", "");
                    bool isSpecial = cls.Contains("yui-navset") || cls.Contains("collapsible-block");
                    if (!isSpecial)
                    {
                        result.AddRange(FlattenNpcWrapperDivs(child));
                        continue;
                    }
                }

                result.Add(child);
            }
            return result;
        }

        private static void ParseClassSkills(HtmlNode pageContent, WikiEntry entry)
        {
            var skillsDiv = pageContent.SelectSingleNode(".//div[contains(@class,'skills')]");
            if (skillsDiv == null) return;

            var blocks = skillsDiv.SelectNodes("./div[contains(@class,'collapsible-block')]");
            if (blocks == null || blocks.Count == 0) return;

            entry.SkillBlocks = new List<SkillBlock>();

            foreach (var block in blocks)
            {
                var contentDiv = block.SelectSingleNode(".//div[contains(@class,'collapsible-block-content')]");
                if (contentDiv == null) continue;

                string headerRaw = block.SelectSingleNode(".//a[contains(@class,'collapsible-block-link')]")?.InnerText;
                string header = !string.IsNullOrEmpty(headerRaw)
                    ? Regex.Replace(WebUtility.HtmlDecode(headerRaw).Trim().TrimStart('+', '-').Trim().Replace('\u00A0', ' '), @"\s+", " ").Trim()
                    : "Skill";

                string imageUrl = null;
                var img = contentDiv.SelectSingleNode(".//img");
                if (img != null)
                {
                    string src = img.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(src))
                        imageUrl = NormalizeImageUrl(src);
                }

                var skill = new SkillInfo { Name = header, ImageUrl = imageUrl };

                var p = contentDiv.SelectSingleNode(".//p");
                if (p != null)
                {
                    string currentFieldLabel = null;
                    var buffer = new List<string>();

                    void FlushField()
                    {
                        if (currentFieldLabel == null) return;
                        string val = Regex.Replace(string.Join(" ", buffer), @"\s+", " ").Trim();
                        buffer.Clear();
                        if (val.Length > 0)
                            skill.Fields.Add(new SkillField { Label = currentFieldLabel, Value = val });
                        currentFieldLabel = null;
                    }

                    foreach (var (kind, node) in WalkParagraph(p))
                    {
                        if (kind == "strong")
                        {
                            bool isNameHeader = node.SelectSingleNode(".//span[contains(@style,'underline')]") != null;
                            if (isNameHeader) { FlushField(); continue; }

                            FlushField();
                            string raw = WebUtility.HtmlDecode(node.InnerText).Trim().TrimEnd(':').Trim();
                            currentFieldLabel = raw;
                            continue;
                        }
                        if (kind == "br") { FlushField(); continue; }
                        if (currentFieldLabel == null) continue;

                        string t = WebUtility.HtmlDecode(node.InnerText).Trim();
                        if (!string.IsNullOrEmpty(t)) buffer.Add(t);
                    }
                    FlushField();
                }

                var ul = contentDiv.SelectSingleNode(".//ul");
                if (ul != null)
                {
                    var lines = ul.ChildNodes.Where(n => n.Name == "li")
                        .Select(li => Regex.Replace(WebUtility.HtmlDecode(li.InnerText).Trim(), @"\s+", " "))
                        .Where(t => t.Length > 0)
                        .ToList();

                    var noteField = skill.Fields.FirstOrDefault(f =>
                        f.Label.Equals("Note", StringComparison.OrdinalIgnoreCase) ||
                        f.Label.Equals("Notes", StringComparison.OrdinalIgnoreCase));

                    if (noteField != null)
                    {
                        noteField.ListValues = lines;
                        noteField.Value = null;
                    }
                    else if (lines.Count > 0)
                    {
                        skill.Fields.Add(new SkillField { Label = "Notes", ListValues = lines });
                    }
                }

                entry.SkillBlocks.Add(new SkillBlock
                {
                    HeaderTitle = header,
                    Rows = new List<SkillRowItem> { new SkillRowItem { IsLink = false, Skill = skill } }
                });
            }

            Logd($"  Class skills parsed: {entry.SkillBlocks.Count}");
        }

        private static void ParseNpcContent(HtmlNode pageContent, WikiEntry entry)
        {
            var allChildren = FlattenNpcWrapperDivs(pageContent);
            int hrIndex = allChildren.FindIndex(n => n.Name == "hr");

            var navsets = allChildren
                .Where(n => n.Name == "div" && n.GetAttributeValue("class", "").Contains("yui-navset"))
                .ToList();

            HtmlNode dialogueNavset = null, imageNavset = null;
            foreach (var nv in navsets)
            {
                int idx = allChildren.IndexOf(nv);
                if (hrIndex < 0 || idx < hrIndex)
                {
                    if (dialogueNavset == null) dialogueNavset = nv;
                }
                else
                {
                    if (imageNavset == null) imageNavset = nv;
                }
            }

            if (dialogueNavset != null)
            {
                ParseNpcDialogueTabs(dialogueNavset, entry);
            }
            else
            {
                var preHr = hrIndex >= 0 ? allChildren.Take(hrIndex) : allChildren;
                var tab = new NpcDialogueTab { Label = null };
                ParseNpcNodesIntoTab(preHr, tab);
                if (tab.Entries.Count > 0 || !string.IsNullOrEmpty(tab.IconUrl))
                    entry.NpcDialogueTabs.Add(tab);
            }

            var postHrNodes = hrIndex >= 0 ? allChildren.Skip(hrIndex + 1).ToList() : new List<HtmlNode>();
            ParseNpcLocationAndNotes(postHrNodes, entry);

            if (imageNavset != null)
                ParseScreensFromNavset(imageNavset, entry);
        }

        private static void ParseNpcDialogueTabs(HtmlNode navset, WikiEntry entry)
        {
            var tabLabels = navset.SelectNodes(".//ul[contains(@class,'yui-nav')]/li")?
                .Select(li =>
                {
                    var em = li.SelectSingleNode(".//em");
                    string t = em != null ? em.InnerText : li.InnerText;
                    return Regex.Replace(WebUtility.HtmlDecode(t).Trim(), @"\s+", " ");
                })
                .ToList() ?? new List<string>();

            var contentDiv = navset.SelectSingleNode(".//div[contains(@class,'yui-content')]");
            var panels = contentDiv?.SelectNodes("./div");
            if (panels == null) return;

            for (int i = 0; i < panels.Count; i++)
            {
                var tab = new NpcDialogueTab
                {
                    Label = i < tabLabels.Count && !string.IsNullOrEmpty(tabLabels[i]) ? tabLabels[i] : $"Tab {i + 1}"
                };
                ParseNpcNodesIntoTab(panels[i].ChildNodes.Cast<HtmlNode>(), tab);
                entry.NpcDialogueTabs.Add(tab);
            }
        }

        private static void ParseNpcNodesIntoTab(IEnumerable<HtmlNode> nodes, NpcDialogueTab tab)
        {
            NpcDialogueEntry pending = null;
            NpcDialogueEntry current = null;

            void AddEntry(NpcDialogueEntry e)
            {
                tab.Entries.Add(e);
                current = e;
            }

            void WalkNodes(IEnumerable<HtmlNode> ns, string conditionPrefix)
            {
                foreach (var child in ns.ToList())
                {
                    if (child.NodeType == HtmlNodeType.Comment) continue;
                    if (child.Name == "script" || child.Name == "style") continue;

                    if (child.Name == "img")
                    {
                        string src = child.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(src) && src.Contains("/image-tags/") && string.IsNullOrEmpty(tab.IconUrl))
                            tab.IconUrl = NormalizeImageUrl(src);
                        continue;
                    }

                    if (child.Name == "p")
                    {
                        var strongOnly = child.SelectSingleNode("./strong") ?? child.SelectSingleNode("./b");
                        int elementChildCount = child.ChildNodes.Count(n => n.NodeType == HtmlNodeType.Element);
                        string fullText = WebUtility.HtmlDecode(child.InnerText).Trim();

                        bool isConditionMarker = strongOnly != null && elementChildCount == 1 && fullText.EndsWith(":");

                        if (isConditionMarker)
                        {
                            string condText = Regex.Replace(fullText, @"\s+", " ");
                            var linkNode = strongOnly.SelectSingleNode(".//a");
                            string href = linkNode?.GetAttributeValue("href", "");
                            pending = new NpcDialogueEntry
                            {
                                ConditionText = conditionPrefix != null ? $"{conditionPrefix} — {condText}" : condText,
                                ConditionLinkUrl = !string.IsNullOrEmpty(href) ? NormalizeWikiUrl(href) : null
                            };
                            continue;
                        }

                        bool looksLikeTrailingLinks = fullText.StartsWith("-") && child.SelectSingleNode(".//strong") != null;
                        if (looksLikeTrailingLinks && current != null)
                        {
                            foreach (var seg in SplitParagraphByBr(child))
                            {
                                string segText = seg.text.TrimStart('-', ' ').Trim();
                                if (segText.Length == 0) continue;
                                current.Links.Add(new WikiLinkItem { Text = segText, Url = seg.url });
                            }
                            continue;
                        }

                        var speakerNode = child.SelectSingleNode(".//strong") ?? child.SelectSingleNode("./b");
                        string speaker = speakerNode != null
                            ? Regex.Replace(WebUtility.HtmlDecode(speakerNode.InnerText).Trim(), @"\s+", " ")
                            : null;

                        string dialogue = fullText;
                        if (!string.IsNullOrEmpty(speaker) && dialogue.StartsWith(speaker))
                            dialogue = dialogue.Substring(speaker.Length).Trim();

                        if (string.IsNullOrEmpty(dialogue) && string.IsNullOrEmpty(speaker)) continue;

                        var entryToUse = pending ?? new NpcDialogueEntry { ConditionText = conditionPrefix };
                        entryToUse.Speaker = speaker;
                        entryToUse.DialogueText = dialogue;
                        AddEntry(entryToUse);
                        pending = null;
                        continue;
                    }

                    if (child.Name == "blockquote")
                    {
                        var innerP = child.SelectSingleNode(".//p");
                        if (innerP != null && current != null)
                        {
                            var underline = innerP.SelectSingleNode(".//span[contains(@style,'underline')]");
                            string title = underline != null ? WebUtility.HtmlDecode(underline.InnerText).Trim() : null;
                            string body;

                            if (underline != null)
                            {
                                var sb = new List<string>();
                                bool passed = false;
                                foreach (var c in innerP.ChildNodes)
                                {
                                    if (c == underline) { passed = true; continue; }
                                    if (!passed) continue;
                                    if (c.Name == "br") continue;
                                    string t = WebUtility.HtmlDecode(c.InnerText).Trim();
                                    if (!string.IsNullOrEmpty(t)) sb.Add(t);
                                }
                                body = Regex.Replace(string.Join(" ", sb), @"\s+", " ").Trim();
                            }
                            else
                            {
                                body = Regex.Replace(WebUtility.HtmlDecode(innerP.InnerText).Trim(), @"\s+", " ");
                            }

                            current.TalkBlocks.Add(new NpcTalkBlock { Title = title, Text = body });
                        }
                        continue;
                    }

                    if (child.Name == "div" && child.GetAttributeValue("class", "").Contains("collapsible-block"))
                    {
                        string headerRaw = child.SelectSingleNode(".//a[contains(@class,'collapsible-block-link')]")?.InnerText;
                        string header = !string.IsNullOrEmpty(headerRaw)
                            ? Regex.Replace(WebUtility.HtmlDecode(headerRaw).Trim().TrimStart('+', '-').Trim().Replace('\u00A0', ' '), @"\s+", " ").Trim()
                            : null;

                        var contentDiv2 = child.SelectSingleNode(".//div[contains(@class,'collapsible-block-content')]");
                        if (contentDiv2 != null)
                            WalkNodes(contentDiv2.ChildNodes.Cast<HtmlNode>(), header);
                        continue;
                    }
                }
            }

            WalkNodes(nodes, null);
        }

        private static List<(string text, string url)> SplitParagraphByBr(HtmlNode p)
        {
            var result = new List<(string, string)>();
            var textParts = new List<string>();
            string url = null;

            void Flush()
            {
                string t = string.Join(" ", textParts).Trim();
                if (t.Length > 0 || url != null) result.Add((t, url));
                textParts.Clear();
                url = null;
            }

            foreach (var child in p.ChildNodes)
            {
                if (child.Name == "br") { Flush(); continue; }
                if (child.Name == "a")
                {
                    string t = WebUtility.HtmlDecode(child.InnerText).Trim();
                    if (!string.IsNullOrEmpty(t)) textParts.Add(t);
                    string href = child.GetAttributeValue("href", "");
                    if (url == null && !string.IsNullOrEmpty(href)) url = NormalizeWikiUrl(href);
                    continue;
                }
                string txt = WebUtility.HtmlDecode(child.InnerText).Trim();
                if (!string.IsNullOrEmpty(txt)) textParts.Add(txt);
            }
            Flush();
            return result;
        }

        private static void ParseNpcLocationAndNotes(List<HtmlNode> nodes, WikiEntry entry)
        {
            string currentLabel = null;
            var locationLinks = new List<WikiLinkItem>();
            var inlineNotesParts = new List<string>();
            bool notesFilledByUl = false;

            foreach (var node in nodes)
            {
                if (node.NodeType == HtmlNodeType.Comment) continue;
                if (node.Name == "script" || node.Name == "style") continue;

                if (node.Name == "img")
                {
                    string src = node.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(src) && !src.Contains("/image-tags/") && string.IsNullOrEmpty(entry.ImageUrl))
                        entry.ImageUrl = NormalizeImageUrl(src);
                    continue;
                }

                if (node.Name == "p")
                {
                    var emCheck = node.SelectSingleNode(".//em");
                    string fullTextCheck = WebUtility.HtmlDecode(node.InnerText).Trim();
                    if (emCheck != null && fullTextCheck.StartsWith("Thanks to", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ThanksLine = fullTextCheck;
                        currentLabel = null;
                        continue;
                    }

                    currentLabel = null;

                    foreach (var (kind, n) in WalkParagraph(node))
                    {
                        if (kind == "strong")
                        {
                            string raw = WebUtility.HtmlDecode(n.InnerText).Trim().TrimEnd(':').Trim();
                            if (raw.Equals("Location", StringComparison.OrdinalIgnoreCase) || raw.Equals("Locations", StringComparison.OrdinalIgnoreCase))
                                currentLabel = "Location";
                            else if (raw.Equals("Note", StringComparison.OrdinalIgnoreCase) || raw.Equals("Notes", StringComparison.OrdinalIgnoreCase))
                                currentLabel = "Notes";
                            else
                                currentLabel = null;
                            continue;
                        }
                        if (kind == "br") continue;
                        if (currentLabel == null) continue;

                        if (kind == "a")
                        {
                            string href = n.GetAttributeValue("href", "");
                            string text = WebUtility.HtmlDecode(n.InnerText).Trim();
                            if (currentLabel == "Location" && !string.IsNullOrEmpty(text))
                                locationLinks.Add(new WikiLinkItem { Text = text, Url = string.IsNullOrEmpty(href) ? null : NormalizeWikiUrl(href) });
                            else if (currentLabel == "Notes" && !string.IsNullOrEmpty(text))
                                inlineNotesParts.Add(text);
                            continue;
                        }

                        string t = WebUtility.HtmlDecode(n.InnerText).Trim();
                        if (t.Length == 0) continue;
                        if (currentLabel == "Notes")
                            inlineNotesParts.Add(t);
                    }
                    continue;
                }

                if (node.Name == "ul")
                {
                    if (currentLabel == "Location")
                    {
                        locationLinks.AddRange(ParseLinkList(node));
                    }
                    else if (currentLabel == "Notes")
                    {
                        entry.NotesLines = ParseRichNestedList(node);
                        notesFilledByUl = true;
                    }
                    continue;
                }
            }

            entry.NpcLocationLinks = locationLinks;

            if (!notesFilledByUl && inlineNotesParts.Count > 0)
            {
                string value = Regex.Replace(string.Join(" ", inlineNotesParts), @"\s+", " ").Trim();
                entry.NotesLines = new List<LinkedLine>
                {
                    new LinkedLine { Depth = 0, Runs = new List<TextRun> { new TextRun { Text = value } } }
                };
            }
        }

        private static SkillInfo ParseSkillTable(HtmlNode table)
        {
            var skill = new SkillInfo();

            var nameRow = table.SelectSingleNode(".//tr[contains(@class,'skill-name')]");
            if (nameRow != null)
                skill.Name = Regex.Replace(WebUtility.HtmlDecode(nameRow.InnerText).Trim(), @"\s+", " ");

            var infoRows = table.SelectNodes(".//tr[contains(@class,'info-row')]");
            if (infoRows == null) return skill;

            foreach (var row in infoRows)
            {
                string rowClass = row.GetAttributeValue("class", "");
                if (!rowClass.Contains("show-yes")) continue;

                string label = row.SelectSingleNode(".//th[contains(@class,'row-heading')]")?.InnerText;
                var valueCell = row.SelectSingleNode(".//td[contains(@class,'row-info')]");
                if (string.IsNullOrEmpty(label) || valueCell == null) continue;

                label = WebUtility.HtmlDecode(label).Trim();

                var ul = valueCell.SelectSingleNode(".//ul");
                if (ul != null)
                {
                    var lines = ul.ChildNodes.Where(n => n.Name == "li")
                        .Select(li => Regex.Replace(WebUtility.HtmlDecode(li.InnerText).Trim(), @"\s+", " "))
                        .Where(t => t.Length > 0)
                        .ToList();
                    skill.Fields.Add(new SkillField { Label = label, ListValues = lines });
                }
                else
                {
                    string value = Regex.Replace(WebUtility.HtmlDecode(valueCell.InnerText).Trim(), @"\s+", " ");
                    skill.Fields.Add(new SkillField { Label = label, Value = value });
                }
            }

            return skill;
        }

        private static IEnumerable<(string Kind, HtmlNode Node)> WalkParagraph(HtmlNode p)
        {
            foreach (var child in p.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Comment) continue;

                if (child.Name == "br")
                {
                    yield return ("br", child);
                    continue;
                }

                if (child.Name == "strong" || child.Name == "b")
                {
                    yield return ("strong", child);
                    continue;
                }

                if (child.Name == "a")
                {
                    yield return ("a", child);
                    continue;
                }

                if (child.NodeType == HtmlNodeType.Text)
                {
                    yield return ("text", child);
                    continue;
                }

                foreach (var item in WalkParagraph(child))
                    yield return item;
            }
        }

        private static List<WikiLinkItem> ParseDroppedItemsList(HtmlNode ul)
        {
            var result = new List<WikiLinkItem>();
            if (ul == null) return result;

            foreach (var li in ul.ChildNodes.Where(n => n.Name == "li"))
            {
                string text = null;
                string url = null;
                var icons = new List<string>();

                foreach (var child in li.ChildNodes)
                {
                    if (child.Name == "a")
                    {
                        string linkText = WebUtility.HtmlDecode(child.InnerText).Trim();
                        if (!string.IsNullOrEmpty(linkText))
                            text = linkText;

                        string href = child.GetAttributeValue("href", "");
                        if (!string.IsNullOrEmpty(href) && !href.Contains("system:") && !href.Contains("search:") && !href.Contains("/tag/"))
                            url = NormalizeWikiUrl(href);
                    }
                    else if (child.Name == "img")
                    {
                        string src = child.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(src))
                            icons.Add(NormalizeImageUrl(src));
                    }
                    else if (child.NodeType == HtmlNodeType.Text)
                    {
                        if (text == null)
                        {
                            string t = WebUtility.HtmlDecode(child.InnerText).Trim();
                            if (!string.IsNullOrEmpty(t))
                                text = t;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(text))
                    result.Add(new WikiLinkItem { Text = text, Url = url, IconUrls = icons });
            }

            return result;
        }

        private static List<WikiLinkItem> ParseLinkList(HtmlNode ul, int depth = 0)
        {
            var result = new List<WikiLinkItem>();
            if (ul == null) return result;

            foreach (var li in ul.ChildNodes.Where(n => n.Name == "li"))
            {
                var ownTextParts = new List<string>();
                HtmlNode nestedUl = null;
                string url = null;

                foreach (var child in li.ChildNodes)
                {
                    if (child.Name == "ul") { nestedUl = child; continue; }
                    if (child.Name == "img" || child.Name == "script") continue;

                    string t = WebUtility.HtmlDecode(child.InnerText).Trim();
                    if (!string.IsNullOrEmpty(t)) ownTextParts.Add(t);

                    if (url == null)
                    {
                        var innerLink = child.Name == "a" ? child : child.SelectSingleNode(".//a[starts-with(@href,'/')]");
                        if (innerLink != null)
                        {
                            string href = innerLink.GetAttributeValue("href", "");
                            if (!string.IsNullOrEmpty(href) && !href.Contains("system:") && !href.Contains("search:") && !href.Contains("/tag/"))
                                url = NormalizeWikiUrl(href);
                        }
                    }
                }

                string ownText = Regex.Replace(string.Join(" ", ownTextParts), @"\s+", " ").Trim();

                if (!string.IsNullOrEmpty(ownText))
                    result.Add(new WikiLinkItem { Text = ownText, Url = url, Depth = depth });

                if (nestedUl != null)
                    result.AddRange(ParseLinkList(nestedUl, depth + 1));
            }

            return result;
        }

        private static void ParseScreens(HtmlNode pageContent, WikiEntry entry)
        {
            var navset = pageContent.SelectSingleNode(".//div[contains(@class,'yui-navset')]");
            if (navset == null)
            {
                ParseUntabbedScreen(pageContent, entry);
                return;
            }
            ParseScreensFromNavset(navset, entry);
        }

        private static void ParseScreensFromNavset(HtmlNode navset, WikiEntry entry)
        {
            var tabLabels = navset.SelectNodes(".//ul[contains(@class,'yui-nav')]/li")?
                .Select(li => Regex.Replace(WebUtility.HtmlDecode(li.InnerText).Trim(), @"\s+", " "))
                .ToList() ?? new List<string>();

            var panels = navset.SelectNodes("./div[contains(@class,'yui-content')]/div");
            if (panels == null) return;

            for (int i = 0; i < panels.Count; i++)
            {
                var panel = panels[i];
                var screen = new ScreenInfo
                {
                    Label = i < tabLabels.Count ? tabLabels[i] : $"Screen {i + 1}"
                };

                var noteP = panel.SelectNodes("./p")?
                    .FirstOrDefault(p =>
                    {
                        string label = (p.SelectSingleNode("./strong") ?? p.SelectSingleNode("./b"))
                            ?.InnerText.Trim().TrimEnd(':').Trim();
                        return label != null &&
                            (label.Equals("Note", StringComparison.OrdinalIgnoreCase) ||
                             label.Equals("Notes", StringComparison.OrdinalIgnoreCase));
                    });

                if (noteP != null)
                {
                    string inlineText = WebUtility.HtmlDecode(noteP.InnerText).Trim();
                    inlineText = Regex.Replace(inlineText, @"^Notes?:\s*", "", RegexOptions.IgnoreCase).Trim();

                    if (!string.IsNullOrEmpty(inlineText))
                    {
                        screen.NoteLines.Add(inlineText);
                    }
                    else
                    {
                        HtmlNode sib = noteP.NextSibling;
                        while (sib != null && sib.NodeType != HtmlNodeType.Element)
                            sib = sib.NextSibling;

                        if (sib != null && sib.Name == "ul")
                        {
                            foreach (var li in sib.ChildNodes.Where(n => n.Name == "li"))
                            {
                                string liText = WebUtility.HtmlDecode(li.InnerText).Trim();
                                if (!string.IsNullOrEmpty(liText))
                                    screen.NoteLines.Add(liText);
                            }
                        }
                    }
                }

                var collapsibles = panel.SelectNodes("./div[contains(@class,'collapsible-block')]");
                if (collapsibles != null && collapsibles.Count > 0)
                {
                    foreach (var block in collapsibles)
                    {
                        string label = block.SelectSingleNode(".//a[contains(@class,'collapsible-block-link')]")?
                            .InnerText;
                        if (!string.IsNullOrEmpty(label))
                            label = WebUtility.HtmlDecode(label).Trim().TrimStart('+', '-').Trim().Replace('\u00A0', ' ');
                        label = string.IsNullOrEmpty(label) ? null : Regex.Replace(label, @"\s+", " ").Trim();

                        var img = block.SelectSingleNode(".//div[contains(@class,'collapsible-block-content')]//img");
                        string src = img?.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(src))
                            screen.Images.Add(new ScreenSubImage { Label = label, ImageUrl = NormalizeImageUrl(src) });
                    }
                }
                else
                {
                    var imgs = panel.SelectNodes(".//img");
                    if (imgs != null)
                    {
                        foreach (var img in imgs)
                        {
                            string src = img.GetAttributeValue("src", "");
                            if (!string.IsNullOrEmpty(src))
                                screen.Images.Add(new ScreenSubImage { Label = null, ImageUrl = NormalizeImageUrl(src) });
                        }
                    }
                }

                entry.Screens.Add(screen);
            }

            Logd($"  Screens parsed: {entry.Screens.Count}, total images: {entry.Screens.Sum(s => s.Images.Count)}, screens with notes: {entry.Screens.Count(s => s.NoteLines.Count > 0)}");
        }

        private static void ParseUntabbedScreen(HtmlNode pageContent, WikiEntry entry)
        {
            var imgs = pageContent.SelectNodes(".//img[not(contains(@src,'/image-tags/'))]");
            if (imgs == null || imgs.Count == 0)
            {
                Logd("  No tabview and no untabbed screenshot found on this location page");
                return;
            }

            var screen = new ScreenInfo { Label = "Screen 1" };
            foreach (var img in imgs)
            {
                string src = img.GetAttributeValue("src", "");
                if (!string.IsNullOrEmpty(src))
                    screen.Images.Add(new ScreenSubImage { Label = null, ImageUrl = NormalizeImageUrl(src) });
            }

            if (screen.Images.Count > 0)
            {
                entry.Screens.Add(screen);
                Logd($"  No tabview found — using fallback single-screen image(s): {screen.Images.Count}");
            }
        }

        private static IEnumerable<(string Kind, HtmlNode Node)> FlattenContent(HtmlNode root)
        {
            foreach (var child in root.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Comment) continue;
                if (child.Name == "script" || child.Name == "style")
                    continue;

                if (child.Name == "br")
                {
                    yield return ("br", child);
                    continue;
                }

                string cls = child.GetAttributeValue("class", "");
                if (cls.Contains("yui-navset") || cls.Contains("skills")) continue;

                if (child.Name == "img")
                {
                    yield return ("img", child);
                    continue;
                }

                if (child.NodeType == HtmlNodeType.Text)
                {
                    yield return ("text", child);
                    continue;
                }

                if (child.Name == "strong" || child.Name == "b")
                {
                    yield return ("strong", child);
                    continue;
                }

                if (child.Name == "ul")
                {
                    yield return ("ul", child);
                    continue;
                }

                foreach (var item in FlattenContent(child))
                    yield return item;
            }
        }

        private static List<LinkedLine> ParseRichNestedList(HtmlNode ul, int depth = 0)
        {
            var result = new List<LinkedLine>();
            if (ul == null) return result;

            foreach (var li in ul.ChildNodes.Where(n => n.Name == "li"))
            {
                var line = new LinkedLine { Depth = depth };
                HtmlNode nestedUl = null;

                foreach (var child in li.ChildNodes)
                {
                    if (child.Name == "ul") { nestedUl = child; continue; }
                    if (child.Name == "img" || child.Name == "script") continue;

                    if (child.Name == "a")
                    {
                        string text = WebUtility.HtmlDecode(child.InnerText).Trim();
                        string href = child.GetAttributeValue("href", "");
                        if (!string.IsNullOrEmpty(text))
                            line.Runs.Add(new TextRun { Text = text, Url = string.IsNullOrEmpty(href) ? null : NormalizeWikiUrl(href) });
                    }
                    else
                    {
                        string t = WebUtility.HtmlDecode(child.InnerText);
                        if (!string.IsNullOrWhiteSpace(t))
                            line.Runs.Add(new TextRun { Text = Regex.Replace(t, @"\s+", " ") });
                    }
                }

                if (line.Runs.Count > 0) result.Add(line);
                if (nestedUl != null) result.AddRange(ParseRichNestedList(nestedUl, depth + 1));
            }
            return result;
        }

        private static List<(string Text, int Depth)> ParseNestedList(HtmlNode ul, int depth = 0)
        {
            var result = new List<(string, int)>();
            if (ul == null) return result;

            foreach (var li in ul.ChildNodes.Where(n => n.Name == "li"))
            {
                var ownTextParts = new List<string>();
                HtmlNode nestedUl = null;

                foreach (var child in li.ChildNodes)
                {
                    if (child.Name == "ul") { nestedUl = child; continue; }
                    if (child.Name == "img" || child.Name == "script") continue;

                    string t = WebUtility.HtmlDecode(child.InnerText).Trim();
                    if (!string.IsNullOrEmpty(t)) ownTextParts.Add(t);
                }

                string ownText = Regex.Replace(string.Join(" ", ownTextParts), @"\s+", " ").Trim();
                ownText = Regex.Replace(ownText, @"\s+([.,;:!?])", "$1");

                if (!string.IsNullOrEmpty(ownText))
                    result.Add((ownText, depth));

                if (nestedUl != null)
                    result.AddRange(ParseNestedList(nestedUl, depth + 1));
            }

            return result;
        }

        private static List<string> FormatNestedList(List<(string Text, int Depth)> items)
        {
            var lines = new List<string>();
            foreach (var (text, depth) in items)
            {
                string indent = new string(' ', depth * 3);
                string bullet = depth == 0 ? "•" : "–";
                lines.Add($"{indent}{bullet} {text}");
            }
            return lines;
        }

        private static async Task<HtmlDocument> ResolveDisambiguationAsync(HtmlWeb web, HtmlDocument doc, string expectedName)
        {
            Logd("Detected disambiguation page, resolving...");

            var links = doc.DocumentNode.SelectNodes("//div[contains(@class,'page-content')]//a[starts-with(@href,'/')]")
                ?? doc.DocumentNode.SelectNodes("//a[starts-with(@href,'/')]");

            if (links == null) return null;

            foreach (var link in links)
            {
                string href = link.GetAttributeValue("href", "");
                string linkText = link.InnerText.Trim();
                if (href.Contains("system:") || href.Contains("search:") || href.Contains("/tag/")) continue;
                if (!linkText.StartsWith(expectedName, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    var candidate = await web.LoadFromWebAsync($"{BaseUrl}{href}");
                    if (candidate.DocumentNode.SelectSingleNode("//div[contains(@class,'page-content')]") != null)
                    {
                        Logd($"Disambiguation resolved to: {BaseUrl}{href}");
                        return candidate;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static string NormalizeImageUrl(string url)
        {
            if (url.StartsWith("//")) return "https:" + url;
            if (url.StartsWith("/")) return BaseUrl + url;
            return url;
        }

        private static string NormalizeWikiUrl(string href)
        {
            return BaseUrl + href;
        }

        private static void ParseEnhancementContent(HtmlNode pageContent, WikiEntry entry)
        {
            Logd("Parsing enhancement content...");

            var locationDiv = pageContent.SelectSingleNode(".//div[contains(@class,'location')]");
            if (locationDiv != null)
            {
                var locLinks = locationDiv.SelectNodes(".//a");
                if (locLinks != null)
                {
                    foreach (var a in locLinks)
                    {
                        string href = a.GetAttributeValue("href", "");
                        string text = WebUtility.HtmlDecode(a.InnerText).Trim();
                        if (!string.IsNullOrEmpty(text))
                            entry.EnhancementLocationLinks.Add(new WikiLinkItem { Text = text, Url = NormalizeWikiUrl(href) });
                    }
                }
            }

            var collapsibleBlocks = pageContent.SelectNodes(".//div[@class='collapsible-block']");
            int blockIndex = 0;
            if (collapsibleBlocks != null)
            {
                foreach (var block in collapsibleBlocks)
                {
                    blockIndex++;

                    var headerAnchor = block.SelectSingleNode(".//a[contains(@class,'collapsible-block-link')]")
                                     ?? block.SelectSingleNode(".//a[contains(@class,'collapsible-block-toggle')]");
                    var headerSpan = block.SelectSingleNode(".//span[contains(@class,'collapsible-block-toggle')]");

                    string headerText = null;
                    if (headerAnchor != null)
                        headerText = WebUtility.HtmlDecode(headerAnchor.InnerText).Trim();
                    else if (headerSpan != null)
                        headerText = WebUtility.HtmlDecode(headerSpan.InnerText).Trim();

                    if (!string.IsNullOrEmpty(headerText))
                    {
                        headerText = Regex.Replace(
                            headerText.TrimStart('+', '-').Trim().Replace('\u00A0', ' '),
                            @"\s+", " ").Trim();
                    }

                    if (string.IsNullOrEmpty(headerText))
                    {
                        var preceding = block.PreviousSibling;
                        while (preceding != null && (preceding.NodeType != HtmlNodeType.Text || string.IsNullOrWhiteSpace(preceding.InnerText)))
                            preceding = preceding.PreviousSibling;
                        if (preceding != null)
                            headerText = WebUtility.HtmlDecode(preceding.InnerText).Trim();
                    }

                    if (string.IsNullOrEmpty(headerText))
                    {
                        var anyHeader = block.SelectSingleNode(".//h3|.//h4");
                        if (anyHeader != null)
                            headerText = WebUtility.HtmlDecode(anyHeader.InnerText).Trim();
                    }

                    if (string.IsNullOrEmpty(headerText))
                        headerText = $"Enhancement {blockIndex}";

                    var group = new EnhancementGroup { HeaderTitle = headerText };
                    ParseEnhancementGroup(block, group);
                    entry.EnhancementGroups.Add(group);
                }
            }
            else
            {
                var tables = pageContent.SelectNodes(".//table");
                if (tables != null && tables.Count > 0)
                {
                    var group = new EnhancementGroup { HeaderTitle = entry.Name };
                    PopulateEnhancementGroupBody(pageContent, group);
                    entry.EnhancementGroups.Add(group);
                }
                else
                {
                    ParseEnhancementFlatContent(pageContent, entry);
                }
            }

            Logd($"  Parsed {entry.EnhancementGroups.Count} enhancement groups");
        }

        private static void ParseEnhancementGroup(HtmlNode block, EnhancementGroup group)
        {
            var contentDiv = block.SelectSingleNode(".//div[contains(@class,'collapsible-block-content')]");
            if (contentDiv == null)
            {
                PopulateEnhancementGroupBody(block, group);
                return;
            }

            PopulateEnhancementGroupBody(contentDiv, group);
        }

        private static void PopulateEnhancementGroupBody(HtmlNode container, EnhancementGroup group)
        {
            string pendingListLabel = null;

            foreach (var child in container.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Comment) continue;
                if (child.Name == "script" || child.Name == "style") continue;

                if (child.Name == "p")
                {
                    var strongNode = child.SelectSingleNode("./strong") ?? child.SelectSingleNode("./b");
                    string label = strongNode != null
                        ? WebUtility.HtmlDecode(strongNode.InnerText).Trim().TrimEnd(':').Trim()
                        : null;

                    bool isNoteLabel = label != null &&
                        (label.Equals("Note", StringComparison.OrdinalIgnoreCase) ||
                         label.Equals("Notes", StringComparison.OrdinalIgnoreCase));

                    if (isNoteLabel)
                    {
                        var afterParts = new List<string>();
                        bool passedStrong = false;
                        foreach (var c in child.ChildNodes)
                        {
                            if (c == strongNode) { passedStrong = true; continue; }
                            if (!passedStrong) continue;
                            string t = WebUtility.HtmlDecode(c.InnerText).Trim();
                            if (!string.IsNullOrEmpty(t)) afterParts.Add(t);
                        }

                        string inlineNote = Regex.Replace(string.Join(" ", afterParts), @"\s+", " ").Trim();
                        if (!string.IsNullOrEmpty(inlineNote))
                            group.NoteLines.Add(inlineNote);
                        else
                            pendingListLabel = "Notes";

                        continue;
                    }

                    pendingListLabel = null;
                    continue;
                }

                if (child.Name == "table")
                {
                    pendingListLabel = null;
                    if (group.Rows.Count == 0)
                        ParseEnhancementTable(child, group);
                    continue;
                }

                if (child.Name == "ul" || child.Name == "ol")
                {
                    if (pendingListLabel == "Notes")
                    {
                        foreach (var li in child.SelectNodes(".//li") ?? Enumerable.Empty<HtmlNode>())
                        {
                            string text = Regex.Replace(WebUtility.HtmlDecode(li.InnerText).Trim(), @"\s+", " ");
                            if (!string.IsNullOrEmpty(text))
                                group.NoteLines.Add(text);
                        }
                        pendingListLabel = null;
                        continue;
                    }

                    var items = child.SelectNodes(".//li");
                    if (items != null)
                    {
                        foreach (var li in items)
                        {
                            string text = WebUtility.HtmlDecode(li.InnerText).Trim();
                            text = Regex.Replace(text, @"\s+", " ");
                            if (!string.IsNullOrEmpty(text))
                            {
                                if (child.Name == "ul" && text.StartsWith("Level "))
                                    group.TableHeader = text;
                                else
                                    group.SkillDescriptionLines.Add(text);
                            }
                        }
                    }
                    continue;
                }

                pendingListLabel = null;
            }
        }

        private static void ParseEnhancementTable(HtmlNode table, EnhancementGroup group)
        {
            var rows = table.SelectNodes("./tr");
            if (rows == null) return;

            foreach (var row in rows)
            {
                var ths = row.SelectNodes("./th");
                var tds = row.SelectNodes("./td");

                if (ths != null && ths.Count == 1 && tds == null)
                {
                    string t = Regex.Replace(WebUtility.HtmlDecode(ths[0].InnerText).Trim(), @"\s+", " ");
                    if (!string.IsNullOrEmpty(t) && string.IsNullOrEmpty(group.TableHeader))
                        group.TableHeader = t;
                    continue;
                }

                if (ths != null && ths.Count > 1)
                {
                    foreach (var th in ths)
                    {
                        string text = Regex.Replace(WebUtility.HtmlDecode(th.InnerText).Trim(), @"\s+", " ");
                        if (!string.IsNullOrEmpty(text))
                            group.ColumnHeaders.Add(text);
                    }
                    continue;
                }

                if (tds != null && tds.Count == 1 && !string.IsNullOrEmpty(tds[0].GetAttributeValue("colspan", "")))
                {
                    ParseEnhancementDescriptionCell(tds[0], group);
                    continue;
                }

                if (tds != null && tds.Count >= 2)
                {
                    var statRow = new EnhancementStatRow();
                    string[] props = { "Level", "STR", "INT", "DEX", "WIS", "END", "LUK", "Cost" };
                    for (int i = 0; i < tds.Count && i < props.Length; i++)
                    {
                        string val = Regex.Replace(WebUtility.HtmlDecode(tds[i].InnerText).Trim(), @"\s+", " ");
                        if (string.IsNullOrEmpty(val)) val = "—";

                        switch (i)
                        {
                            case 0: statRow.Level = val; break;
                            case 1: statRow.STR = val; break;
                            case 2: statRow.INT = val; break;
                            case 3: statRow.DEX = val; break;
                            case 4: statRow.WIS = val; break;
                            case 5: statRow.END = val; break;
                            case 6: statRow.LUK = val; break;
                            case 7: statRow.Cost = val; break;
                        }
                    }
                    group.Rows.Add(statRow);
                }
            }

            Logd($"  Enhancement group '{group.HeaderTitle}': {group.ColumnHeaders.Count} columns, {group.Rows.Count} rows, {group.NoteLines.Count} notes, {group.SkillDescriptionLines.Count} skill descriptions");
        }

        private static void ParseEnhancementDescriptionCell(HtmlNode td, EnhancementGroup group)
        {
            foreach (var child in td.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Comment) continue;
                if (child.Name == "script" || child.Name == "style") continue;

                if (child.Name == "p")
                {
                    string text = Regex.Replace(WebUtility.HtmlDecode(child.InnerText).Trim(), @"\s+", " ");
                    if (!string.IsNullOrEmpty(text))
                        group.SkillDescriptionLines.Add(text);
                    continue;
                }

                if (child.Name == "ul" || child.Name == "ol")
                {
                    var parsed = ParseNestedList(child);
                    group.SkillDescriptionLines.AddRange(FormatNestedList(parsed));
                }
            }
        }

        private static void ParseEnhancementFlatContent(HtmlNode pageContent, WikiEntry entry)
        {
            var group = new EnhancementGroup { HeaderTitle = entry.Name, IsExpanded = true };

            string currentLabel = null;
            var buffer = new List<string>();
            string bufferedLinkUrl = null;
            string bufferedLinkText = null;

            void FlushField()
            {
                if (currentLabel == null) { buffer.Clear(); bufferedLinkUrl = null; bufferedLinkText = null; return; }

                string value = Regex.Replace(string.Join(" ", buffer), @"\s+", " ").Trim();
                buffer.Clear();

                if (currentLabel.Equals("Location", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(bufferedLinkText))
                        entry.EnhancementLocationLinks.Add(new WikiLinkItem { Text = bufferedLinkText, Url = bufferedLinkUrl });
                    else if (!string.IsNullOrEmpty(value))
                        entry.EnhancementLocationLinks.Add(new WikiLinkItem { Text = value });
                }
                else if (!string.IsNullOrEmpty(value))
                {
                    group.SkillDescriptionLines.Add($"{currentLabel}: {value}");
                }

                bufferedLinkUrl = null;
                bufferedLinkText = null;
                currentLabel = null;
            }

            foreach (var p in pageContent.SelectNodes(".//p") ?? Enumerable.Empty<HtmlNode>())
            {
                var em = p.SelectSingleNode(".//em");
                string fullText = WebUtility.HtmlDecode(p.InnerText).Trim();
                if (em != null && fullText.StartsWith("Thanks to", StringComparison.OrdinalIgnoreCase))
                {
                    group.NoteLines.Add(fullText);
                    continue;
                }

                foreach (var (kind, node) in WalkParagraph(p))
                {
                    if (kind == "strong")
                    {
                        FlushField();
                        currentLabel = WebUtility.HtmlDecode(node.InnerText).Trim().TrimEnd(':').Trim();
                        continue;
                    }
                    if (kind == "br") { FlushField(); continue; }
                    if (currentLabel == null) continue;

                    if (kind == "a")
                    {
                        string href = node.GetAttributeValue("href", "");
                        string text = WebUtility.HtmlDecode(node.InnerText).Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            buffer.Add(text);
                            if (bufferedLinkText == null) bufferedLinkText = text;
                            if (bufferedLinkUrl == null && !string.IsNullOrEmpty(href))
                                bufferedLinkUrl = NormalizeWikiUrl(href);
                        }
                        continue;
                    }

                    string t = WebUtility.HtmlDecode(node.InnerText).Trim();
                    if (!string.IsNullOrEmpty(t)) buffer.Add(t);
                }
                FlushField();
            }

            if (group.SkillDescriptionLines.Count > 0 || group.NoteLines.Count > 0)
                entry.EnhancementGroups.Add(group);
        }
    }
}
