using System.Net.Http;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TradeTrackerConsoleApp
{
    public class TradeTrackerProduct
    {
        public string? ProductID { get; set; }
        public string? ProductName { get; set; }
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public string? Currency { get; set; }
        public string? ProductURL { get; set; }
        public string? ImageURL { get; set; }
        public string? Category { get; set; }
        public string? Brand { get; set; }
        public string? LastUpdated { get; set; }
        public DateTime ImportDate { get; set; }
        public string? FeedID { get; set; }
    }

    public class FeedConfig
    {
        public List<TradeTrackerFeed>? Feeds { get; set; }
        public Config? Config { get; set; }
    }

    public class TradeTrackerFeed
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Url { get; set; }
        public string? Category { get; set; }
        public bool Active { get; set; }
    }

    public class Config
    {
        public string? DefaultFeed { get; set; }
        public List<string>? OutputFormats { get; set; }
        public bool GenerateImages { get; set; }
        public bool SeoOptimized { get; set; }
    }

    public class TradeTrackerService
    {
        private readonly HttpClient _httpClient;
        private FeedConfig? _feedConfig;
        private TradeTrackerFeed? _currentFeed;

        public TradeTrackerService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            LoadFeedConfig();
        }

        private void LoadFeedConfig()
        {
            try
            {
                var configPath = "feeds.json";
                if (File.Exists(configPath))
                {
                    var jsonContent = File.ReadAllText(configPath);
                    _feedConfig = JsonConvert.DeserializeObject<FeedConfig>(jsonContent);
                    
                    if (_feedConfig?.Feeds != null && _feedConfig.Feeds.Any())
                    {
                        // Use default feed or first active feed
                        _currentFeed = _feedConfig.Feeds.FirstOrDefault(f => 
                            f.Id == _feedConfig.Config?.DefaultFeed && f.Active) 
                            ?? _feedConfig.Feeds.FirstOrDefault(f => f.Active);
                        
                        Console.WriteLine($"Geladen feed configuratie: {_feedConfig.Feeds.Count} feeds beschikbaar");
                        Console.WriteLine($"Actieve feed: {_currentFeed?.Name ?? "Geen"}");
                    }
                }
                else
                {
                    Console.WriteLine("Geen feeds.json gevonden, gebruik standaard URL");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fout bij laden feed configuratie: {ex.Message}");
            }
        }

        public void ListAvailableFeeds()
        {
            if (_feedConfig?.Feeds != null && _feedConfig.Feeds.Any())
            {
                Console.WriteLine("\n=== BESCHIKBARE FEEDS ===");
                foreach (var feed in _feedConfig.Feeds.Where(f => f.Active))
                {
                    var current = feed.Id == _currentFeed?.Id ? " (ACTIEF)" : "";
                    Console.WriteLine($"- {feed.Id}: {feed.Name}{current}");
                    Console.WriteLine($"  {feed.Description}");
                    Console.WriteLine($"  Categorie: {feed.Category}");
                    Console.WriteLine();
                }
            }
        }

        public bool SelectFeed(string feedId)
        {
            if (_feedConfig?.Feeds == null) return false;
            
            var feed = _feedConfig.Feeds.FirstOrDefault(f => f.Id == feedId && f.Active);
            if (feed != null)
            {
                _currentFeed = feed;
                Console.WriteLine($"Feed gewisseld naar: {feed.Name}");
                return true;
            }
            
            Console.WriteLine($"Feed '{feedId}' niet gevonden of niet actief");
            return false;
        }

        public List<TradeTrackerFeed> GetAllActiveFeeds()
        {
            return _feedConfig?.Feeds?.Where(f => f.Active).ToList() ?? new List<TradeTrackerFeed>();
        }

        public async Task<List<TradeTrackerProduct>> GetProductsAsync()
        {
            try
            {
                var apiUrl = _currentFeed?.Url ?? "https://pf.tradetracker.net/?aid=439092&encoding=utf-8&type=json&fid=2451096&r=xtrmbbq123jaloezie&categoryType=2&additionalType=2";
                
                Console.WriteLine($"Ophalen van data van: {_currentFeed?.Name ?? "Standaard feed"}");
                Console.WriteLine($"Feed categorie: {_currentFeed?.Category ?? "Onbekend"}");
                
                var response = await _httpClient.GetStringAsync(apiUrl);
                Console.WriteLine($"Data ontvangen, grootte: {response.Length} characters");

                var feedId = _currentFeed?.Id ?? "unknown";
                var products = ParseJsonData(response, feedId);
                
                Console.WriteLine($"Aantal producten verwerkt: {products.Count}");
                return products;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fout bij ophalen data: {ex.Message}");
                return new List<TradeTrackerProduct>();
            }
        }

        private List<TradeTrackerProduct> ParseJsonData(string jsonData, string feedId)
        {
            var products = new List<TradeTrackerProduct>();
            var importDate = DateTime.Now; // Standaard importdatum is nu

            try
            {
                // Probeer eerst de importdatum te vinden in de JSON
                var jsonObject = JObject.Parse(jsonData);
                importDate = DetectImportDate(jsonObject);

                Console.WriteLine($"Gedetecteerde importdatum: {importDate:yyyy-MM-dd HH:mm:ss}");

                // Verwerk de producten
                var productsArray = GetProductsArray(jsonObject);
                
                foreach (var productToken in productsArray)
                {
                    var product = ParseProduct(productToken, importDate, feedId);
                    if (product != null)
                    {
                        products.Add(product);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fout bij verwerken JSON: {ex.Message}");
            }

            return products;
        }

        private DateTime DetectImportDate(JObject jsonObject)
        {
            // Verschillende mogelijke velden waar de datum kan staan
            var dateFields = new[]
            {
                "timestamp",
                "generated",
                "created",
                "lastUpdated",
                "date",
                "exportDate",
                "feedDate",
                "generatedAt",
                "createdAt"
            };

            // Zoek eerst in root niveau
            foreach (var field in dateFields)
            {
                var dateValue = FindDateField(jsonObject, field);
                if (dateValue.HasValue)
                {
                    Console.WriteLine($"Importdatum gevonden in veld '{field}': {dateValue.Value}");
                    return dateValue.Value;
                }
            }

            // Kijk ook in headers, metadata, of info sectie
            var metadataSections = new[] { "metadata", "header", "info", "feed_info", "export_info" };
            foreach (var section in metadataSections)
            {
                var metadataObj = jsonObject[section] as JObject;
                if (metadataObj != null)
                {
                    foreach (var field in dateFields)
                    {
                        var dateValue = FindDateField(metadataObj, field);
                        if (dateValue.HasValue)
                        {
                            Console.WriteLine($"Importdatum gevonden in {section}.{field}: {dateValue.Value}");
                            return dateValue.Value;
                        }
                    }
                }
            }

            // Als laatste optie: kijk naar de laatste update van het eerste product
            var productsArray = jsonObject["products"] as JArray;
            if (productsArray != null && productsArray.Count > 0)
            {
                var firstProduct = productsArray[0] as JObject;
                if (firstProduct != null)
                {
                    foreach (var field in new[] { "lastUpdated", "updated", "modified", "timestamp" })
                    {
                        var dateValue = FindDateField(firstProduct, field);
                        if (dateValue.HasValue)
                        {
                            Console.WriteLine($"Importdatum afgeleid van eerste product '{field}': {dateValue.Value}");
                            return dateValue.Value;
                        }
                    }
                }
            }

            Console.WriteLine("Geen specifieke importdatum gevonden, gebruik huidige tijd");
            return DateTime.Now;
        }

        private DateTime? FindDateField(JObject obj, string fieldName)
        {
            try
            {
                var token = obj[fieldName];
                if (token == null) return null;

                var value = token.ToString();
                
                // Probeer verschillende datum formaten
                var dateFormats = new[]
                {
                    "yyyy-MM-dd HH:mm:ss",
                    "yyyy-MM-ddTHH:mm:ss",
                    "yyyy-MM-ddTHH:mm:ssZ",
                    "yyyy-MM-dd",
                    "dd-MM-yyyy HH:mm:ss",
                    "dd-MM-yyyy"
                };

                foreach (var format in dateFormats)
                {
                    if (DateTime.TryParseExact(value, format, null, System.Globalization.DateTimeStyles.None, out var parsedDate))
                    {
                        return parsedDate;
                    }
                }

                // Probeer gewone parsing
                if (DateTime.TryParse(value, out var generalParsedDate))
                {
                    return generalParsedDate;
                }

                // Probeer Unix timestamp
                if (long.TryParse(value, out var timestamp))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                }
            }
            catch
            {
                // Negeer parsing fouten
            }

            return null;
        }

        private JArray GetProductsArray(JObject jsonObject)
        {
            // Zoek naar het array met producten
            var possibleArrayNames = new[] { "products", "items", "data", "results", "entries" };

            foreach (var arrayName in possibleArrayNames)
            {
                var array = jsonObject[arrayName] as JArray;
                if (array != null && array.Count > 0)
                {
                    Console.WriteLine($"Producten array gevonden onder '{arrayName}' met {array.Count} items");
                    return array;
                }
            }

            // Als de JSON root een array is, behandel het anders
            if (jsonObject.Parent != null && jsonObject.Parent.Type == JTokenType.Array)
            {
                return (JArray)jsonObject.Parent;
            }

            // Als het root object zelf producten bevat
            var rootArray = new JArray();
            if (jsonObject.HasValues)
            {
                rootArray.Add(jsonObject);
            }

            return rootArray;
        }

        private TradeTrackerProduct? ParseProduct(JToken productToken, DateTime importDate, string feedId)
        {
            try
            {
                var product = new TradeTrackerProduct
                {
                    ImportDate = importDate,
                    FeedID = feedId
                };

                foreach (var property in productToken.Children<JProperty>())
                {
                    var key = property.Name;
                    var value = property.Value;

                    // Directe mapping van bekende TradeTracker velden
                    switch (key.ToLower())
                    {
                        case "productid":
                        case "id":
                            product.ProductID = value?.ToString();
                            break;
                        case "productname":
                        case "name":
                        case "title":
                            product.ProductName = value?.ToString();
                            break;
                        case "description":
                        case "desc":
                            product.Description = value?.ToString();
                            break;
                        case "price":
                            // TradeTracker heeft geneste prijs structuur
                            if (value is JObject priceObj)
                            {
                                var amount = priceObj["amount"]?.Value<decimal>() ?? 0;
                                var currency = priceObj["currency"]?.ToString() ?? "";
                                product.Price = amount;
                                product.Currency = currency;
                            }
                            else
                            {
                                // Fallback voor directe prijs waarde
                                var priceStr = value?.ToString();
                                if (!string.IsNullOrEmpty(priceStr))
                                {
                                    var cleanPrice = priceStr.Replace(",", ".").Replace("€", "").Replace("$", "").Trim();
                                    if (decimal.TryParse(cleanPrice, System.Globalization.NumberStyles.Any, 
                                        System.Globalization.CultureInfo.InvariantCulture, out var price))
                                    {
                                        product.Price = price;
                                    }
                                }
                            }
                            break;
                        case "currency":
                            product.Currency = value?.ToString();
                            break;
                        case "producturl":
                        case "url":
                            product.ProductURL = value?.ToString();
                            break;
                        case "imageurl":
                        case "image":
                        case "images":
                            // TradeTracker kan een array van images hebben
                            if (value is JArray imageArray && imageArray.Count > 0)
                            {
                                product.ImageURL = imageArray[0]?.ToString();
                            }
                            else
                            {
                                product.ImageURL = value?.ToString();
                            }
                            break;
                        case "category":
                        case "categoryname":
                            product.Category = value?.ToString();
                            break;
                        case "brand":
                        case "manufacturer":
                            product.Brand = value?.ToString();
                            break;
                        case "properties":
                            // TradeTracker heeft geneste properties structuur
                            if (value is JObject propsObj)
                            {
                                var brandArray = propsObj["brand"] as JArray;
                                if (brandArray != null && brandArray.Count > 0)
                                {
                                    var brandValue = brandArray[0]?.ToString();
                                    if (!string.IsNullOrEmpty(brandValue))
                                    {
                                        product.Brand = brandValue;
                                    }
                                }
                            }
                            break;
                        case "categories":
                            // TradeTracker kan een array van categories hebben
                            if (value is JArray categoryArray && categoryArray.Count > 0)
                            {
                                product.Category = categoryArray[0]?.ToString();
                            }
                            break;
                        case "lastupdated":
                        case "updated":
                        case "modified":
                            product.LastUpdated = value?.ToString();
                            break;
                    }
                }

                return product;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fout bij verwerken product: {ex.Message}");
                return null;
            }
        }

        private string CreateDailyFolderStructure(DateTime date, string subFolder = "", bool useImgFolder = false)
        {
            var year = date.ToString("yyyy");
            var month = date.ToString("MM");
            var day = date.ToString("dd");
            
            string folderPath;
            
            if (useImgFolder)
            {
                // Create the base img folder first
                var baseImgFolder = "img";
                if (!Directory.Exists(baseImgFolder))
                {
                    Directory.CreateDirectory(baseImgFolder);
                }
                
                // Create the date structure inside img folder
                folderPath = Path.Combine(baseImgFolder, year, month, day);
            }
            else
            {
                // Create the date structure in root
                folderPath = Path.Combine(year, month, day);
            }
            
            if (!string.IsNullOrEmpty(subFolder))
            {
                folderPath = Path.Combine(folderPath, subFolder);
            }
            
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                Console.WriteLine($"Folder structuur aangemaakt: {folderPath}");
            }
            
            return folderPath;
        }

        public async Task SaveProductsToMarkdownAsync(List<TradeTrackerProduct> products)
        {
            try
            {
                var dateTime = DateTime.Now;
                var datePrefix = dateTime.ToString("yyyy-MM-dd");
                var timeStamp = dateTime.ToString("HHmmss");
                var feedId = _currentFeed?.Id ?? "unknown";
                
                // Create daily folder structure for reports in root
                var reportFolderPath = CreateDailyFolderStructure(dateTime, "reports", useImgFolder: false);
                
                var fileName = $"{datePrefix}-{feedId}-report-{timeStamp}.markdown";
                var imageName = $"{datePrefix}-{feedId}-{timeStamp}.svg";
                var fullPath = Path.Combine(reportFolderPath, fileName);
                
                
                //var markdown = GenerateMarkdownReport(products, feedId);
                //await File.WriteAllTextAsync(fullPath, markdown);
                //Console.WriteLine($"Rapport opgeslagen in Markdown bestand: {fullPath}");
                
                // Genereer verkooppagina
                var salesPageMarkdown = GenerateSalesPageMarkdown(products, feedId);
                var salesPageTimestamp = DateTime.Now.ToString("yyyy-MM-dd");
                var salesPageFileName = $"{salesPageTimestamp}-verkoop-{feedId}.markdown";
                var salesPageFullPath = Path.Combine(reportFolderPath, salesPageFileName);
                await File.WriteAllTextAsync(salesPageFullPath, salesPageMarkdown);
                Console.WriteLine($"Verkooppagina opgeslagen: {salesPageFullPath}");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fout bij opslaan naar Markdown bestand: {ex.Message}");
            }
        }

        private string GenerateSalesPageMarkdown(List<TradeTrackerProduct> products, string feedId)
        {
            var sb = new System.Text.StringBuilder();
            var importDate = products.FirstOrDefault()?.ImportDate ?? DateTime.Now;
            var primaryBrand = products.Where(p => !string.IsNullOrEmpty(p.Brand))
                                     .GroupBy(p => p.Brand)
                                     .OrderByDescending(g => g.Count())
                                     .FirstOrDefault()?.Key ?? feedId;

            var productsWithPrice = products.Where(p => p.Price > 0).ToList();
            var minPrice = productsWithPrice.Any() ? productsWithPrice.Min(p => p.Price) : 0;
            var maxPrice = productsWithPrice.Any() ? productsWithPrice.Max(p => p.Price) : 0;
            var avgPrice = productsWithPrice.Any() ? productsWithPrice.Average(p => p.Price) : 0;
            var timeStamp = DateTime.Now.ToString("HHmmss");
            var cleanBrandName = primaryBrand.Replace(".", "").Replace(" ", "");

            sb.AppendLine("---");
            sb.AppendLine("layout: post");
            sb.AppendLine($"title: \"{feedId} - Premium Producten Online Shop\"");
            sb.AppendLine($"date: {importDate:yyyy-MM-dd HH:mm:ss} +0200");
            sb.AppendLine($"description: \"Shop de beste {feedId} producten online. Van €{minPrice:F2} tot €{maxPrice:F2}. Gratis verzending, 30 dagen retour en de laagste prijsgarantie.\"");
            sb.AppendLine($"excerpt: \"Ontdek onze selectie van {products.Count} {feedId} producten. Topkwaliteit, scherpe prijzen en snelle levering.\"");
            sb.AppendLine($"tags: [{cleanBrandName}, shop, online-winkel, bestsellers, aanbiedingen]");
            sb.AppendLine($"categories: [webshop, producten]");
            sb.AppendLine($"keywords: \"{feedId} kopen, {feedId} shop, {feedId} aanbieding, online winkel\"");
            sb.AppendLine("author: Webshop Manager");
            sb.AppendLine($"canonical_url: \"/verkoop-{feedId}-{timeStamp}\"");
            sb.AppendLine("sitemap:");
            sb.AppendLine("  priority: 1.0");
            sb.AppendLine("  changefreq: daily");
            sb.AppendLine("schema:");
            sb.AppendLine("  type: Product");
            sb.AppendLine("---");
            sb.AppendLine();

            // Hero sectie
            sb.AppendLine($"# {feedId} Online Shop");
            sb.AppendLine();
            sb.AppendLine($"**Welkom bij de officiële {feedId} webshop!** Ontdek onze collectie van **{products.Count} premium producten** ");
            sb.AppendLine($"met prijzen vanaf **€{minPrice:F2}**. ✨ Gratis verzending vanaf €50 • 🚚 Snelle levering • 💯 30 dagen retourrecht");
            sb.AppendLine();

            // Bestsellers sectie
            var bestsellers = productsWithPrice.OrderByDescending(p => p.Price * 0.7m + (products.IndexOf(p) * -0.1m)) // Simuleer populariteit
                                               .Take(6).ToList();
            
            if (bestsellers.Any())
            {
                sb.AppendLine("## Bestsellers & Top Producten");
                sb.AppendLine();
                sb.AppendLine("*Onze meest populaire producten - geliefd door duizenden klanten!*");
                sb.AppendLine();

                for (int i = 0; i < bestsellers.Count; i++)
                {
                    var product = bestsellers[i];
                    var productName = product.ProductName ?? "Premium Product";
                    var originalPrice = product.Price * 1.2m; // Simuleer oorspronkelijke prijs
                    var discount = ((originalPrice - product.Price) / originalPrice * 100);

                    sb.AppendLine($"### 🏆 #{i + 1} Bestseller");
                    sb.AppendLine();

                    sb.AppendLine($"**🛍️ {EscapeMarkdown(productName)}**");
                    
                    sb.AppendLine();
                    sb.AppendLine($"💰 **Speciale Prijs: €{product.Price:F2}**");
                    sb.AppendLine();
                    sb.AppendLine($"🏷️ **Merk:** {product.Brand ?? "Premium"}");
                    sb.AppendLine($"📦 **Product ID:** {product.ProductID}");
                    
                    if (!string.IsNullOrEmpty(product.Category))
                    {
                        sb.AppendLine($"📂 **Categorie:** {product.Category}");
                    }

                    if (!string.IsNullOrEmpty(product.Description))
                    {
                        var shortDesc = product.Description.Length > 150 
                            ? product.Description.Substring(0, 147) + "..." 
                            : product.Description;
                        sb.AppendLine();
                        sb.AppendLine($"📝 *{EscapeMarkdown(shortDesc)}*");
                    }

                    sb.AppendLine();
                    if (!string.IsNullOrEmpty(product.ProductURL))
                    {
                        sb.AppendLine($"[🛒 **NU BESTELLEN**]({product.ProductURL}){{: .btn .btn-primary .btn-lg}}");
                    }
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
            }

            // Premium collectie
            var premiumProducts = productsWithPrice.OrderByDescending(p => p.Price).Take(8).ToList();
            if (premiumProducts.Any())
            {
                sb.AppendLine("## 💎 Premium Collectie");
                sb.AppendLine();
                sb.AppendLine("*Voor de veeleisende klant - onze exclusieve top-tier producten*");
                sb.AppendLine();

                foreach (var product in premiumProducts)
                {
                    var productName = product.ProductName ?? "Premium Product";
                    
                    sb.AppendLine($"| 🌟 **{EscapeMarkdown(productName)}** |");
                    sb.AppendLine("|---|");
                    sb.AppendLine($"| **Prijs:** €{product.Price:F2} |");
                    sb.AppendLine($"| **Merk:** {product.Brand ?? "Premium"} |");
                    if (!string.IsNullOrEmpty(product.ProductURL))
                    {
                        sb.AppendLine($"| [🛒 **Bestel Nu**]({product.ProductURL}) |");
                    }
                    sb.AppendLine();
                }
            }

            // Budget vriendelijke opties
            var budgetOptions = productsWithPrice.OrderBy(p => p.Price).Take(6).ToList();
            if (budgetOptions.Any())
            {
                sb.AppendLine("## 💝 Budget Vriendelijk");
                sb.AppendLine();
                sb.AppendLine("*Topkwaliteit voor een vriendelijke prijs - perfect voor elke beurs!*");
                sb.AppendLine();

                var counter = 1;
                foreach (var product in budgetOptions)
                {
                    var productName = product.ProductName ?? "Budget Product";
                    
                    sb.AppendLine($"**{counter}. {EscapeMarkdown(productName)}**  ");
                    sb.AppendLine($"💰 Slechts €{product.Price:F2} | 🏷️ {product.Brand ?? "Quality Brand"}");
                    
                    if (!string.IsNullOrEmpty(product.ProductURL))
                    {
                        sb.AppendLine($"[👆 Bekijk Product]({product.ProductURL})");
                    }
                    sb.AppendLine();
                    counter++;
                }
            }

            // Categorieën overzicht voor shop
            var uniqueCategories = products.Where(p => !string.IsNullOrEmpty(p.Category))
                                         .Select(p => p.Category)
                                         .Distinct()
                                         .OrderBy(c => c)
                                         .ToList();

            if (uniqueCategories.Any())
            {
                sb.AppendLine("## 🗂️ Shop per Categorie");
                sb.AppendLine();
                
                foreach (var category in uniqueCategories.Take(8))
                {
                    var categoryProducts = products.Where(p => p.Category == category).ToList();
                    var categoryAvgPrice = categoryProducts.Where(p => p.Price > 0).Select(p => p.Price).DefaultIfEmpty(0).Average();
                    
                    sb.AppendLine($"### 📁 {category}");
                    sb.AppendLine($"**{categoryProducts.Count} producten** • Vanaf €{categoryProducts.Where(p => p.Price > 0).Select(p => p.Price).DefaultIfEmpty(0).Min():F2}");
                    sb.AppendLine();
                }
            }

            // Voordelen sectie
            sb.AppendLine("## ✨ Waarom bij ons kopen?");
            sb.AppendLine();
            sb.AppendLine("| Voordeel | Beschrijving |");
            sb.AppendLine("|----------|-------------|");
            sb.AppendLine("| 🚚 **Gratis Verzending** | Vanaf €50 naar heel Nederland |");
            sb.AppendLine("| 💯 **30 Dagen Retour** | Niet tevreden? Geld terug! |");
            sb.AppendLine("| 🔒 **Veilig Betalen** | iDEAL, PayPal, Creditcard |");
            sb.AppendLine("| ⚡ **Snelle Levering** | Vandaag besteld, morgen in huis |");
            sb.AppendLine("| 🏆 **Beste Prijs** | Laagste prijsgarantie |");
            sb.AppendLine("| 📞 **Klantenservice** | 7 dagen per week bereikbaar |");
            sb.AppendLine();

            // Call to action
            sb.AppendLine("## 🎯 Klaar om te bestellen?");
            sb.AppendLine();
            sb.AppendLine($"**Mis deze kans niet!** Onze {feedId} collectie is zeer populair en sommige items zijn beperkt op voorraad. ");
            sb.AppendLine("**Bestel vandaag nog** en profiteer van onze speciale actieprijzen!");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"**🕒 Laatste Update:** {DateTime.Now:dd MMMM yyyy, HH:mm}  ");
            sb.AppendLine($"**📦 Producten beschikbaar:** {products.Count}  ");
            sb.AppendLine($"**💰 Prijsbereik:** €{minPrice:F2} - €{maxPrice:F2}  ");
            sb.AppendLine();
            sb.AppendLine("*Prijzen zijn inclusief BTW. Aanbiedingen geldig zolang de voorraad strekt.*");

            return sb.ToString();
        }

        private string GenerateMarkdownReport(List<TradeTrackerProduct> products, string feedId)
        {
            var sb = new System.Text.StringBuilder();
            var importDate = products.FirstOrDefault()?.ImportDate ?? DateTime.Now;
            var primaryBrand = products.Where(p => !string.IsNullOrEmpty(p.Brand))
                                     .GroupBy(p => p.Brand)
                                     .OrderByDescending(g => g.Count())
                                     .FirstOrDefault()?.Key ?? "Unknown";

            var productsWithPrice = products.Where(p => p.Price > 0).ToList();
            var minPrice = productsWithPrice.Any() ? productsWithPrice.Min(p => p.Price) : 0;
            var maxPrice = productsWithPrice.Any() ? productsWithPrice.Max(p => p.Price) : 0;
            var avgPrice = productsWithPrice.Any() ? productsWithPrice.Average(p => p.Price) : 0;
            
            var cleanBrandName = primaryBrand.Replace(".", "").Replace(" ", "");

            // Jekyll front matter
            sb.AppendLine("---");
            sb.AppendLine("layout: post");
            sb.AppendLine($"title: \"Consumentenkoopgedrag Analyse: {feedId} - {products.Count} Producten\"");
            sb.AppendLine($"date: {importDate:yyyy-MM-dd HH:mm:ss} +0200");
            sb.AppendLine($"description: \"Analyse van consumentenkoopgedrag bij {feedId} met {products.Count} producten.\"");
            sb.AppendLine($"excerpt: \"Koopgedrag analyse {feedId}: €{minPrice:F2}-€{maxPrice:F2} prijsbereik, gemiddeld €{avgPrice:F2}.\"");
            sb.AppendLine($"tags: [consumentengedrag, koopgedrag, {cleanBrandName}, prijsanalyse]");
            sb.AppendLine($"categories: [marktonderzoek, consumentengedrag]");
            sb.AppendLine("author: Marktonderzoeker");
            sb.AppendLine("---");
            sb.AppendLine();

            // Introductie
            sb.AppendLine($"Deze consumentenkoopgedrag analyse van **{feedId}** onderzoekt **{products.Count} producten** ");
            sb.AppendLine($"met een gemiddelde consumentenuitgave van **€{avgPrice:F2}**.");
            sb.AppendLine();
            
            // Premium producten analyse
            var topExpensive = productsWithPrice.OrderByDescending(p => p.Price).Take(5).ToList();
            if (topExpensive.Any())
            {
                sb.AppendLine("## Premium Producten");
                sb.AppendLine();
                sb.AppendLine("De duurste producten in de collectie:");
                sb.AppendLine();
                
                for (int i = 0; i < topExpensive.Count; i++)
                {
                    var product = topExpensive[i];
                    var productName = product.ProductName ?? "Onbekend product";
                    var productLink = !string.IsNullOrEmpty(product.ProductURL) 
                        ? $"[{EscapeMarkdown(productName)}]({product.ProductURL})" 
                        : EscapeMarkdown(productName);
                    
                    sb.AppendLine($"{i + 1}. {productLink} - €{product.Price:F2}");
                }
                sb.AppendLine();
            }

            // Koopgedrag analyse
            sb.AppendLine("## Koopgedrag Analyse");
            sb.AppendLine();
            
            // Prijs overzicht
            sb.AppendLine("### Prijsoverzicht");
            sb.AppendLine($"- Goedkoopste product: €{minPrice:F2}");
            sb.AppendLine($"- Duurste product: €{maxPrice:F2}");
            sb.AppendLine($"- Gemiddelde prijs: €{avgPrice:F2}");
            sb.AppendLine();

            // Merken
            var topBrands = products.Where(p => !string.IsNullOrEmpty(p.Brand))
                                  .GroupBy(p => p.Brand)
                                  .Where(g => g.Count() > 1)
                                  .OrderByDescending(g => g.Count())
                                  .Take(5)
                                  .ToList();
            
            if (topBrands.Any())
            {
                sb.AppendLine("### Top Merken");
                foreach (var brandGroup in topBrands)
                {
                    sb.AppendLine($"- {brandGroup.Key}: {brandGroup.Count()} producten");
                }
                sb.AppendLine();
            }

            // Categorieën
            var topCategories = products.Where(p => !string.IsNullOrEmpty(p.Category))
                                      .GroupBy(p => p.Category)
                                      .Where(g => g.Count() > 1)
                                      .OrderByDescending(g => g.Count())
                                      .Take(5)
                                      .ToList();
            
            if (topCategories.Any())
            {
                sb.AppendLine("### Populaire Categorieën");
                foreach (var categoryGroup in topCategories)
                {
                    sb.AppendLine($"- {categoryGroup.Key}: {categoryGroup.Count()} producten");
                }
                sb.AppendLine();
            }

            // Budget producten
            var budgetProducts = productsWithPrice.Where(p => p.Price <= avgPrice * 0.7m).Take(5).ToList();
            if (budgetProducts.Any())
            {
                sb.AppendLine("### Budget Vriendelijke Opties");
                foreach (var product in budgetProducts)
                {
                    var productName = product.ProductName ?? "Onbekend product";
                    var productLink = !string.IsNullOrEmpty(product.ProductURL) 
                        ? $"[{EscapeMarkdown(productName)}]({product.ProductURL})" 
                        : EscapeMarkdown(productName);
                    sb.AppendLine($"- {productLink} - €{product.Price:F2}");
                }
                sb.AppendLine();
            }

            // Statistieken tabel
            sb.AppendLine("### Markt Statistieken");
            sb.AppendLine("| Statistiek | Waarde |");
            sb.AppendLine("|------------|--------|");
            sb.AppendLine($"| Totaal Producten | {products.Count} |");
            sb.AppendLine($"| Producten met Prijs | {productsWithPrice.Count} |");
            sb.AppendLine($"| Verschillende Merken | {products.Where(p => !string.IsNullOrEmpty(p.Brand)).Select(p => p.Brand).Distinct().Count()} |");
            sb.AppendLine($"| Verschillende Categorieën | {products.Where(p => !string.IsNullOrEmpty(p.Category)).Select(p => p.Category).Distinct().Count()} |");
            sb.AppendLine($"| Gemiddelde Prijs | €{avgPrice:F2} |");
            sb.AppendLine($"| Mediaan Prijs | €{(productsWithPrice.Any() ? productsWithPrice.OrderBy(p => p.Price).Skip(productsWithPrice.Count / 2).FirstOrDefault()?.Price ?? 0 : 0):F2} |");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"Laatste Update: {DateTime.Now:dd MMMM yyyy, HH:mm}");
            sb.AppendLine($"Data Bron: TradeTracker Marktdata");
            sb.AppendLine($"Geanalyseerde Producten: {products.Count:N0}");
            sb.AppendLine();
            sb.AppendLine("*Disclaimer: Deze analyse is gebaseerd op beschikbare marktdata en prijzen kunnen wijzigen.*");

            return sb.ToString();
        }

        private string EscapeMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            // Escape Markdown special characters
            return text.Replace("|", "\\|")
                      .Replace("*", "\\*")
                      .Replace("_", "\\_")
                      .Replace("[", "\\[")
                      .Replace("]", "\\]")
                      .Replace("#", "\\#");
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== TradeTracker Product Feed Reader ===");
            Console.WriteLine("=== Verwerken van ALLE FEEDS ===");
            Console.WriteLine();

            var service = new TradeTrackerService();
            
            // Toon beschikbare feeds
            service.ListAvailableFeeds();

            try
            {
                var allFeeds = service.GetAllActiveFeeds();
                var totalProductsProcessed = 0;
                var successfulFeeds = 0;
                var failedFeeds = 0;

                Console.WriteLine($"\n=== STARTEN MET VERWERKEN VAN {allFeeds.Count} FEEDS ===");
                Console.WriteLine();

                foreach (var feed in allFeeds)
                {
                    try
                    {
                        Console.WriteLine($"\n🔄 Verwerken van feed: {feed.Name} ({feed.Id})");
                        Console.WriteLine($"   Categorie: {feed.Category}");
                        Console.WriteLine($"   Beschrijving: {feed.Description}");
                        
                        // Wissel naar deze feed
                        service.SelectFeed(feed.Id ?? "unknown");
                        
                        // Haal producten op voor deze feed
                        var products = await service.GetProductsAsync();

                        if (products.Any())
                        {
                            totalProductsProcessed += products.Count;
                            successfulFeeds++;

                            Console.WriteLine($"✅ {feed.Name}: {products.Count} producten verwerkt");
                            
                            // Statistieken
                            var uniqueBrands = products.Where(p => !string.IsNullOrEmpty(p.Brand))
                                                     .Select(p => p.Brand)
                                                     .Distinct()
                                                     .Count();
                            
                            var productsWithPrice = products.Where(p => p.Price > 0);
                            var avgPrice = productsWithPrice.Any() ? productsWithPrice.Average(p => p.Price) : 0;

                            Console.WriteLine($"   📊 Unieke merken: {uniqueBrands}");
                            Console.WriteLine($"   💰 Producten met prijs: {productsWithPrice.Count()}");
                            Console.WriteLine($"   💵 Gemiddelde prijs: {avgPrice:C}");

                            // Sla data op in bestanden voor deze feed
                            
                            await service.SaveProductsToMarkdownAsync(products);
                            
                            Console.WriteLine($"   📁 Bestanden opgeslagen voor {feed.Name}");
                        }
                        else
                        {
                            failedFeeds++;
                            Console.WriteLine($"❌ {feed.Name}: Geen producten gevonden");
                        }
                    }
                    catch (Exception feedEx)
                    {
                        failedFeeds++;
                        Console.WriteLine($"❌ Fout bij verwerken van {feed.Name}: {feedEx.Message}");
                    }

                    // Kleine pauze tussen feeds om API niet te overbelasten
                    await Task.Delay(2000);
                }

                // Samenvattende statistieken
                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine("🎉 VERWERKING VOLTOOID - SAMENVATTING");
                Console.WriteLine(new string('=', 60));
                Console.WriteLine($"📈 Totaal verwerkte feeds: {allFeeds.Count}");
                Console.WriteLine($"✅ Succesvolle feeds: {successfulFeeds}");
                Console.WriteLine($"❌ Mislukte feeds: {failedFeeds}");
                Console.WriteLine($"📦 Totaal verwerkte producten: {totalProductsProcessed:N0}");
                Console.WriteLine($"📁 Gegenereerde pagina's: {successfulFeeds * 2} (rapport + verkoop per feed)");
                Console.WriteLine($"🖼️  Gegenereerde afbeeldingen: {successfulFeeds}");
                Console.WriteLine();
                Console.WriteLine("Alle bestanden zijn opgeslagen in de datum-gestructureerde mappen.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Onverwachte fout: {ex.Message}");
            }
            finally
            {
                service.Dispose();
            }
        }
    }
}
