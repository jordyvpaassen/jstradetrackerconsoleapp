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

        public async Task<List<TradeTrackerProduct>> GetProductsAsync()
        {
            try
            {
                var apiUrl = _currentFeed?.Url ?? "https://pf.tradetracker.net/?aid=439092&encoding=utf-8&type=json&fid=2451096&r=xtrmbbq123jaloezie&categoryType=2&additionalType=2";
                
                Console.WriteLine($"Ophalen van data van: {_currentFeed?.Name ?? "Standaard feed"}");
                Console.WriteLine($"Feed categorie: {_currentFeed?.Category ?? "Onbekend"}");
                
                var response = await _httpClient.GetStringAsync(apiUrl);
                Console.WriteLine($"Data ontvangen, grootte: {response.Length} characters");

                var products = ParseJsonData(response);
                
                Console.WriteLine($"Aantal producten verwerkt: {products.Count}");
                return products;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fout bij ophalen data: {ex.Message}");
                return new List<TradeTrackerProduct>();
            }
        }

        private List<TradeTrackerProduct> ParseJsonData(string jsonData)
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
                    var product = ParseProduct(productToken, importDate);
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

        private TradeTrackerProduct? ParseProduct(JToken productToken, DateTime importDate)
        {
            try
            {
                var product = new TradeTrackerProduct
                {
                    ImportDate = importDate
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

        public async Task SaveProductsToFileAsync(List<TradeTrackerProduct> products)
        {
            try
            {
                var dateTime = DateTime.Now;
                var datePrefix = dateTime.ToString("yyyy-MM-dd");
                var timeStamp = dateTime.ToString("HHmmss");
                var feedId = _currentFeed?.Id ?? "unknown";
                var fileName = $"{datePrefix}-{feedId}-products-{timeStamp}.json";
                var json = JsonConvert.SerializeObject(products, Formatting.Indented);
                await File.WriteAllTextAsync(fileName, json);
                Console.WriteLine($"Producten opgeslagen in JSON bestand: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fout bij opslaan naar JSON bestand: {ex.Message}");
            }
        }

        public async Task SaveProductsToMarkdownAsync(List<TradeTrackerProduct> products)
        {
            try
            {
                var dateTime = DateTime.Now;
                var datePrefix = dateTime.ToString("yyyy-MM-dd");
                var timeStamp = dateTime.ToString("HHmmss");
                var feedId = _currentFeed?.Id ?? "unknown";
                var fileName = $"{datePrefix}-{feedId}-report-{timeStamp}.markdown";
                var imageName = $"{datePrefix}-{feedId}-{timeStamp}.svg";
                
                var markdown = GenerateMarkdownReport(products, imageName, feedId);
                await File.WriteAllTextAsync(fileName, markdown);
                Console.WriteLine($"Rapport opgeslagen in Markdown bestand: {fileName}");
                
                // Genereer verkooppagina
                var salesPageMarkdown = GenerateSalesPageMarkdown(products, imageName, feedId);
                var salesPageTimestamp = DateTime.Now.ToString("yyyy-MM-dd");
                var salesPageFileName = $"{salesPageTimestamp}-verkoop-{feedId}.markdown";
                await File.WriteAllTextAsync(salesPageFileName, salesPageMarkdown);
                Console.WriteLine($"Verkooppagina opgeslagen: {salesPageFileName}");
                
                // Genereer en sla afbeelding op
                await GenerateBlogImageAsync(products, imageName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fout bij opslaan naar Markdown bestand: {ex.Message}");
            }
        }

        public async Task GenerateBlogImageAsync(List<TradeTrackerProduct> products, string imageName)
        {
            try
            {
                // Maak img directory aan als deze niet bestaat
                var imagesDir = "img";
                if (!Directory.Exists(imagesDir))
                {
                    Directory.CreateDirectory(imagesDir);
                }

                var primaryBrand = products.Where(p => !string.IsNullOrEmpty(p.Brand))
                                         .GroupBy(p => p.Brand)
                                         .OrderByDescending(g => g.Count())
                                         .FirstOrDefault()?.Key ?? "TradeTracker";

                var productsWithPrice = products.Where(p => p.Price > 0).ToList();
                var avgPrice = productsWithPrice.Any() ? productsWithPrice.Average(p => p.Price) : 0;

                // Genereer SVG afbeelding
                var svgContent = GenerateSvgImage(primaryBrand, products.Count, avgPrice);
                
                var imagePath = Path.Combine(imagesDir, imageName);
                await File.WriteAllTextAsync(imagePath, svgContent);
                
                Console.WriteLine($"Blog afbeelding opgeslagen: {imagePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fout bij genereren afbeelding: {ex.Message}");
            }
        }

        private string GenerateSvgImage(string brandName, int productCount, decimal avgPrice)
        {
            var svg = $@"<svg width=""800"" height=""400"" xmlns=""http://www.w3.org/2000/svg"">
  <!-- Background gradient -->
  <defs>
    <linearGradient id=""bgGradient"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""100%"">
      <stop offset=""0%"" style=""stop-color:#667eea;stop-opacity:1"" />
      <stop offset=""100%"" style=""stop-color:#764ba2;stop-opacity:1"" />
    </linearGradient>
    <linearGradient id=""cardGradient"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""100%"">
      <stop offset=""0%"" style=""stop-color:rgba(255,255,255,0.9);stop-opacity:1"" />
      <stop offset=""100%"" style=""stop-color:rgba(255,255,255,0.7);stop-opacity:1"" />
    </linearGradient>
  </defs>
  
  <!-- Background -->
  <rect width=""800"" height=""400"" fill=""url(#bgGradient)""/>
  
  <!-- Main card -->
  <rect x=""50"" y=""50"" width=""700"" height=""300"" rx=""20"" fill=""url(#cardGradient)"" stroke=""rgba(255,255,255,0.3)"" stroke-width=""2""/>
  
  <!-- TradeTracker logo area -->
  <rect x=""70"" y=""70"" width=""60"" height=""60"" rx=""10"" fill=""#FF6B35""/>
  <text x=""100"" y=""110"" text-anchor=""middle"" fill=""white"" font-family=""Arial, sans-serif"" font-size=""20"" font-weight=""bold"">TT</text>
  
  <!-- Title -->
  <text x=""150"" y=""95"" fill=""#2D3748"" font-family=""Arial, sans-serif"" font-size=""28"" font-weight=""bold"">TradeTracker Analysis</text>
  <text x=""150"" y=""125"" fill=""#4A5568"" font-family=""Arial, sans-serif"" font-size=""22"">{EscapeSvgText(brandName)}</text>
  
  <!-- Statistics -->
  <g transform=""translate(70, 160)"">
    <!-- Product count -->
    <rect x=""0"" y=""0"" width=""180"" height=""80"" rx=""10"" fill=""rgba(255,255,255,0.8)"" stroke=""#E2E8F0"" stroke-width=""1""/>
    <text x=""90"" y=""25"" text-anchor=""middle"" fill=""#2D3748"" font-family=""Arial, sans-serif"" font-size=""14"" font-weight=""bold"">PRODUCTEN</text>
    <text x=""90"" y=""50"" text-anchor=""middle"" fill=""#667eea"" font-family=""Arial, sans-serif"" font-size=""24"" font-weight=""bold"">{productCount:N0}</text>
    <text x=""90"" y=""70"" text-anchor=""middle"" fill=""#718096"" font-family=""Arial, sans-serif"" font-size=""12"">items in feed</text>
    
    <!-- Average price -->
    <rect x=""200"" y=""0"" width=""180"" height=""80"" rx=""10"" fill=""rgba(255,255,255,0.8)"" stroke=""#E2E8F0"" stroke-width=""1""/>
    <text x=""290"" y=""25"" text-anchor=""middle"" fill=""#2D3748"" font-family=""Arial, sans-serif"" font-size=""14"" font-weight=""bold"">GEM. PRIJS</text>
    <text x=""290"" y=""50"" text-anchor=""middle"" fill=""#38A169"" font-family=""Arial, sans-serif"" font-size=""24"" font-weight=""bold"">€{avgPrice:F0}</text>
    <text x=""290"" y=""70"" text-anchor=""middle"" fill=""#718096"" font-family=""Arial, sans-serif"" font-size=""12"">per product</text>
    
    <!-- Brand info -->
    <rect x=""400"" y=""0"" width=""180"" height=""80"" rx=""10"" fill=""rgba(255,255,255,0.8)"" stroke=""#E2E8F0"" stroke-width=""1""/>
    <text x=""490"" y=""25"" text-anchor=""middle"" fill=""#2D3748"" font-family=""Arial, sans-serif"" font-size=""14"" font-weight=""bold"">AFFILIATE</text>
    <text x=""490"" y=""50"" text-anchor=""middle"" fill=""#9F7AEA"" font-family=""Arial, sans-serif"" font-size=""20"" font-weight=""bold"">READY</text>
    <text x=""490"" y=""70"" text-anchor=""middle"" fill=""#718096"" font-family=""Arial, sans-serif"" font-size=""12"">marketing</text>
  </g>
  
  <!-- Date -->
  <text x=""720"" y=""330"" text-anchor=""end"" fill=""rgba(255,255,255,0.8)"" font-family=""Arial, sans-serif"" font-size=""12"">{DateTime.Now:dd-MM-yyyy}</text>
  
  <!-- Decorative elements -->
  <circle cx=""650"" cy=""120"" r=""30"" fill=""rgba(255,255,255,0.1)""/>
  <circle cx=""680"" cy=""90"" r=""20"" fill=""rgba(255,255,255,0.05)""/>
  <circle cx=""720"" cy=""140"" r=""25"" fill=""rgba(255,255,255,0.08)""/>
</svg>";

            return svg;
        }

        private string EscapeSvgText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            return text.Replace("&", "&amp;")
                      .Replace("<", "&lt;")
                      .Replace(">", "&gt;")
                      .Replace("\"", "&quot;")
                      .Replace("'", "&apos;");
        }

        private string GenerateSalesPageMarkdown(List<TradeTrackerProduct> products, string imageName, string feedId)
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

            // Jekyll front matter voor verkooppagina
            sb.AppendLine("---");
            sb.AppendLine("layout: product-page");
            sb.AppendLine($"title: \"{primaryBrand} - Premium Producten Online Shop\"");
            sb.AppendLine($"date: {importDate:yyyy-MM-dd HH:mm:ss} +0200");
            sb.AppendLine($"description: \"Shop de beste {primaryBrand} producten online. Van €{minPrice:F2} tot €{maxPrice:F2}. Gratis verzending, 30 dagen retour en de laagste prijsgarantie.\"");
            sb.AppendLine($"excerpt: \"Ontdek onze selectie van {products.Count} {primaryBrand} producten. Topkwaliteit, scherpe prijzen en snelle levering.\"");
            sb.AppendLine($"img: {imageName}");
            sb.AppendLine($"tags: [{cleanBrandName}, shop, online-winkel, bestsellers, aanbiedingen]");
            sb.AppendLine($"categories: [webshop, producten]");
            sb.AppendLine($"keywords: \"{primaryBrand} kopen, {primaryBrand} shop, {primaryBrand} aanbieding, online winkel\"");
            sb.AppendLine("author: Webshop Manager");
            sb.AppendLine($"canonical_url: \"/shop-{cleanBrandName.ToLower()}\"");
            sb.AppendLine("sitemap:");
            sb.AppendLine("  priority: 1.0");
            sb.AppendLine("  changefreq: daily");
            sb.AppendLine("schema:");
            sb.AppendLine("  type: Product");
            sb.AppendLine("---");
            sb.AppendLine();

            // Hero sectie
            sb.AppendLine($"# 🛒 {primaryBrand} Online Shop");
            sb.AppendLine();
            sb.AppendLine($"**Welkom bij de officiële {primaryBrand} webshop!** Ontdek onze collectie van **{products.Count} premium producten** ");
            sb.AppendLine($"met prijzen vanaf **€{minPrice:F2}**. ✨ Gratis verzending vanaf €50 • 🚚 Snelle levering • 💯 30 dagen retourrecht");
            sb.AppendLine();

            // Bestsellers sectie
            var bestsellers = productsWithPrice.OrderByDescending(p => p.Price * 0.7m + (products.IndexOf(p) * -0.1m)) // Simuleer populariteit
                                               .Take(6).ToList();
            
            if (bestsellers.Any())
            {
                sb.AppendLine("## 🔥 Bestsellers & Top Producten");
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
                    
                    if (!string.IsNullOrEmpty(product.ProductURL))
                    {
                        sb.AppendLine($"[![{EscapeMarkdown(productName)}]({product.ProductURL})]({product.ProductURL})");
                        sb.AppendLine();
                        sb.AppendLine($"**[🛍️ {EscapeMarkdown(productName)}]({product.ProductURL})**");
                    }
                    else
                    {
                        sb.AppendLine($"**🛍️ {EscapeMarkdown(productName)}**");
                    }
                    
                    sb.AppendLine();
                    sb.AppendLine($"💰 **Speciale Prijs: €{product.Price:F2}** ~~€{originalPrice:F2}~~ *({discount:F0}% korting!)*");
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
            sb.AppendLine($"**Mis deze kans niet!** Onze {primaryBrand} collectie is zeer populair en sommige items zijn beperkt op voorraad. ");
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

        private string GenerateMarkdownReport(List<TradeTrackerProduct> products, string imageName, string feedId)
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
            var categoryKeywords = products.Where(p => !string.IsNullOrEmpty(p.Category))
                                         .Select(p => p.Category)
                                         .Distinct()
                                         .Take(5)
                                         .ToList();

            // Jekyll front matter met consumentenkoopgedrag focus
            sb.AppendLine("---");
            sb.AppendLine("layout: post");
            sb.AppendLine($"title: \"Consumentenkoopgedrag Analyse: {primaryBrand} - {products.Count} Producten\"");
            sb.AppendLine($"date: {importDate:yyyy-MM-dd HH:mm:ss} +0200");
            sb.AppendLine($"description: \"Diepgaande analyse van consumentenkoopgedrag bij {primaryBrand}. Ontdek kooppatronen, prijsvoorkeuren en populaire productcategorieën van {products.Count} producten.\"");
            sb.AppendLine($"excerpt: \"Koopgedrag analyse {primaryBrand}: €{minPrice:F2}-€{maxPrice:F2} prijsbereik, gemiddeld €{avgPrice:F2}. Inzichten in consumentenvoorkeuren en aankooppatronen.\"");
            sb.AppendLine($"img: {imageName}");
            sb.AppendLine($"tags: [consumentengedrag, koopgedrag, {cleanBrandName}, prijsanalyse, marktonderzoek, e-commerce]");
            sb.AppendLine($"categories: [marktonderzoek, consumentengedrag]");
            sb.AppendLine($"keywords: \"consumentenkoopgedrag, {primaryBrand}, prijsanalyse, kooppatronen, marktonderzoek, e-commerce trends\"");
            sb.AppendLine("author: Marktonderzoeker");
            sb.AppendLine($"canonical_url: \"/consumentengedrag-{feedId.ToLower()}-analyse\"");
            sb.AppendLine("sitemap:");
            sb.AppendLine("  priority: 0.9");
            sb.AppendLine("  changefreq: weekly");
            sb.AppendLine("schema:");
            sb.AppendLine("  type: Article");
            sb.AppendLine($"  wordCount: {products.Count * 15 + 1000}");
            sb.AppendLine("---");
            sb.AppendLine();

            // Introductie gericht op consumentenkoopgedrag
            sb.AppendLine($"Deze **consumentenkoopgedrag analyse van {primaryBrand}** onderzoekt **{products.Count} producten** ");
            sb.AppendLine($"met een gemiddelde consumentenuitgave van **€{avgPrice:F2}**. Perfect voor marktonderzoekers, retailers ");
            sb.AppendLine("en merkstrategen die consumentenvoorkeuren en kooppatronen willen begrijpen.");
            sb.AppendLine();
            var uniqueBrands = products.Where(p => !string.IsNullOrEmpty(p.Brand))
                                     .Select(p => p.Brand)
                                     .Distinct()
                                     .OrderBy(b => b)
                                     .ToList();
            var uniqueCategories = products.Where(p => !string.IsNullOrEmpty(p.Category))
                                         .Select(p => p.Category)
                                         .Distinct()
                                         .OrderBy(c => c)
                                         .ToList();

            sb.AppendLine("## Consumentenkoopgedrag Analyse");
            sb.AppendLine();
            sb.AppendLine($"### Prijspsychologie en Uitgavenpatronen");
            sb.AppendLine();
            if (productsWithPrice.Any())
            {
                var sortedPrices = productsWithPrice.Select(p => p.Price).OrderBy(p => p).ToList();
                var medianPrice = sortedPrices.Count % 2 == 0 
                    ? (sortedPrices[sortedPrices.Count / 2 - 1] + sortedPrices[sortedPrices.Count / 2]) / 2
                    : sortedPrices[sortedPrices.Count / 2];
                sb.AppendLine($"Het {primaryBrand} productassortiment toont een prijsspectrum van €{minPrice:F2} tot €{maxPrice:F2}, ");
                sb.AppendLine($"met een gemiddelde consumentenuitgave van €{avgPrice:F2} en mediaan van €{medianPrice:F2}. ");
                sb.AppendLine("Dit prijspatroon onthult belangrijke inzichten over het doelgroepgedrag.");
                sb.AppendLine();
                
                // Consumentenpsychologie insights
                if (avgPrice > medianPrice * 1.2m)
                {
                    sb.AppendLine("Koopgedrag Inzicht: Het verschil tussen gemiddelde en mediaanprijs duidt op een segment ");
                    sb.AppendLine("premium consumenten die bereid zijn significant meer te betalen voor kwaliteit of status.");
                }
                else if (avgPrice > 50)
                {
                    sb.AppendLine("Koopgedrag Inzicht: De gemiddelde uitgave van €" + avgPrice.ToString("F2") + " suggereert overwogen aankopen ");
                    sb.AppendLine("waarbij consumenten voldoende tijd nemen voor productonderzoek en vergelijking.");
                }
                else
                {
                    sb.AppendLine("Koopgedrag Inzicht: Het toegankelijke prijsniveau stimuleert impulskopen en ");
                    sb.AppendLine("herhalingsaankopen, typisch voor convenience en lifestyle producten.");
                }
                sb.AppendLine();
            }
            
            sb.AppendLine("### Merkvoorkeur en Loyaliteit Patronen");
            sb.AppendLine();
            if (uniqueBrands.Count > 1)
            {
                sb.AppendLine($"De diversiteit van {uniqueBrands.Count} verschillende merken toont dat consumenten in dit segment ");
                sb.AppendLine("vergelijkingsgedrag vertonen en niet gebonden zijn aan één specifiek merk. Dit duidt op:");
                sb.AppendLine("- Prijs-kwaliteit bewuste consumenten");
                sb.AppendLine("- Lagere merktrouw in deze productcategorie");
                sb.AppendLine("- Kansen voor nieuwe merken om marktaandeel te winnen");
            }
            else
            {
                sb.AppendLine($"De exclusieve focus op {primaryBrand} suggereert een merkgetrouw consumentensegment met:");
                sb.AppendLine("- Hoge merkherkenning en vertrouwen");
                sb.AppendLine("- Bereidheid om premiumprijs te betalen voor merkwaarde");
                sb.AppendLine("- Lagere prijsgevoeligheid binnen het merksegment");
                sb.AppendLine("- Potentieel voor cross-selling naar gerelateerde producten");
            }
            sb.AppendLine();

            // Merkperceptie en prijspositionering analyse
            if (uniqueBrands.Any() && uniqueBrands.Count > 1)
            {
                sb.AppendLine("## Merkperceptie en Prijspositionering");
                sb.AppendLine();
                sb.AppendLine("De merkanalyse toont interessante consumentenvoorkeuren en prijsbereidheid per merk:");
                sb.AppendLine();
                
                var brandPriceAnalysis = uniqueBrands.Take(10).Select(brand => new
                {
                    Brand = brand,
                    Count = products.Count(p => p.Brand == brand),
                    AvgPrice = products.Where(p => p.Brand == brand && p.Price > 0).Select(p => p.Price).DefaultIfEmpty(0).Average()
                }).OrderByDescending(b => b.AvgPrice).ToList();

                foreach (var brandInfo in brandPriceAnalysis)
                {
                    string priceCategory = brandInfo.AvgPrice > avgPrice * 1.2m ? "Premium" :
                                         brandInfo.AvgPrice < avgPrice * 0.8m ? "Budget" : 
                                         "Mainstream";
                    sb.AppendLine($"- {brandInfo.Brand} ({priceCategory}): {brandInfo.Count} producten, gemiddeld €{brandInfo.AvgPrice:F2}");
                }
                if (uniqueBrands.Count > 10)
                {
                    sb.AppendLine($"- *... en nog {uniqueBrands.Count - 10} andere merken*");
                }
                sb.AppendLine();
                
                sb.AppendLine("Consumentengedrag inzicht: De prijsverschillen tussen merken tonen hoe merkperceptie ");
                sb.AppendLine("de bereidheid tot betalen beïnvloedt en segmentatie binnen de markt creëert.");
                sb.AppendLine();
            }

            // Categorievoorkeuren en koopgedrag
            if (uniqueCategories.Any())
            {
                sb.AppendLine("## �️ Consumentenvoorkeuren per Categorie");
                sb.AppendLine();
                sb.AppendLine("De verdeling over productcategorieën toont waar consumenten hun prioriteiten leggen:");
                sb.AppendLine();
                
                var categoryAnalysis = uniqueCategories.Take(10).Select(category => new
                {
                    Category = category,
                    Count = products.Count(p => p.Category == category),
                    AvgPrice = products.Where(p => p.Category == category && p.Price > 0).Select(p => p.Price).DefaultIfEmpty(0).Average()
                }).OrderByDescending(c => c.Count).ToList();

                foreach (var catInfo in categoryAnalysis)
                {
                    var percentage = (catInfo.Count * 100.0 / products.Count);
                    sb.AppendLine($"- {catInfo.Category}: {catInfo.Count} producten ({percentage:F1}%) - gemiddeld €{catInfo.AvgPrice:F2}");
                }
                sb.AppendLine();
                
                var topCategory = categoryAnalysis.FirstOrDefault();
                if (topCategory != null)
                {
                    sb.AppendLine($"Dominant koopgedrag: De focus op {topCategory.Category} ({topCategory.Count} producten) ");
                    sb.AppendLine("suggereert dat dit de primaire interesse van het doelsegment vormt.");
                }
                sb.AppendLine();
            }

            // Premium koopgedrag analyse
            var topExpensive = productsWithPrice.OrderByDescending(p => p.Price).Take(5).ToList();
            if (topExpensive.Any())
            {
                sb.AppendLine("## Premium Consumentengedrag");
                sb.AppendLine();
                sb.AppendLine("Analyse van de duurste producten onthult het gedrag van statusbewuste consumenten ");
                sb.AppendLine("die bereid zijn significant meer te investeren. Deze groep kenmerkt zich door:");
                sb.AppendLine();
                sb.AppendLine("- Kwaliteit boven prijs mentaliteit");
                sb.AppendLine("- Uitgebreid onderzoek voor grote aankopen");
                sb.AppendLine("- Merkgetrouwheid en statusoverweging");
                sb.AppendLine();
                sb.AppendLine("### Top Premium Producten:");
                sb.AppendLine();
                
                for (int i = 0; i < topExpensive.Count; i++)
                {
                    var product = topExpensive[i];
                    var productName = product.ProductName ?? "Onbekend product";
                    var productLink = !string.IsNullOrEmpty(product.ProductURL) 
                        ? $"[{EscapeMarkdown(productName)}]({product.ProductURL})" 
                        : EscapeMarkdown(productName);
                    
                    sb.AppendLine($"**{i + 1}. {productLink}**  ");
                    sb.AppendLine($"Prijs: €{product.Price:F2} | Merk: {product.Brand ?? "N/A"} | Product ID: {product.ProductID}");
                    if (!string.IsNullOrEmpty(product.Description))
                    {
                        var shortDesc = product.Description.Length > 100 
                            ? product.Description.Substring(0, 97) + "..." 
                            : product.Description;
                        sb.AppendLine($"*{EscapeMarkdown(shortDesc)}*");
                    }
                    sb.AppendLine();
                }
            }

            // Prijsbewust koopgedrag
            var topCheap = productsWithPrice.Where(p => p.Price > 0).OrderBy(p => p.Price).Take(5).ToList();
            if (topCheap.Any())
            {
                sb.AppendLine("## Prijsbewust Consumentengedrag");
                sb.AppendLine();
                sb.AppendLine("De meest betaalbare producten trekken een prijsgevoelig segment aan dat:");
                sb.AppendLine();
                sb.AppendLine("- Waarde-voor-geld als hoofdcriterium hanteert");
                sb.AppendLine("- Snelle beslissingen neemt bij aantrekkelijke prijzen");
                sb.AppendLine("- Impulskopen doet bij kortingen");
                sb.AppendLine("- Minder merkgevoelig is");
                sb.AppendLine();
                sb.AppendLine("### Toegankelijke Prijsopties:");
                sb.AppendLine();
                
                for (int i = 0; i < topCheap.Count; i++)
                {
                    var product = topCheap[i];
                    var productName = product.ProductName ?? "Onbekend product";
                    var productLink = !string.IsNullOrEmpty(product.ProductURL) 
                        ? $"[{EscapeMarkdown(productName)}]({product.ProductURL})" 
                        : EscapeMarkdown(productName);
                    
                    sb.AppendLine($"**{i + 1}. {productLink}**  ");
                    sb.AppendLine($"Prijs: €{product.Price:F2} | Merk: {product.Brand ?? "N/A"} | Product ID: {product.ProductID}");
                    sb.AppendLine();
                }
            }

            // Consumentengedrag samenvattende statistieken
            sb.AppendLine("## Consumentengedrag Samenvatting");
            sb.AppendLine();
            sb.AppendLine($"### Marktinzichten - {primaryBrand}");
            sb.AppendLine();
            sb.AppendLine("Kernstatistieken voor consumentengedrag analyse:");
            sb.AppendLine();
            sb.AppendLine($"| Gedragsindicator | Waarde | Consumentenimplicatie |");
            sb.AppendLine("|------------------|--------|----------------------|");
            sb.AppendLine($"| **Productdiversiteit** | {products.Count:N0} producten | {(products.Count > 100 ? "Uitgebreide keuzemogelijkheden" : "Gefocuste selectie")} |");
            if (productsWithPrice.Any())
            {
                sb.AppendLine($"| **Uitgavenspectrum** | €{minPrice:F2} - €{maxPrice:F2} | {(maxPrice > minPrice * 10 ? "Sterke prijssegmentatie" : "Homogeen prijsniveau")} |");
                sb.AppendLine($"| **Gem. Besteding** | €{avgPrice:F2} | {(avgPrice > 50 ? "Overwogen aankopen" : "Impulsgevoelige aankopen")} |");
            }
            sb.AppendLine($"| **Merkdiversiteit** | {uniqueBrands.Count} merken | {(uniqueBrands.Count > 1 ? "Vergelijkend koopgedrag" : "Merkgetrouwheid dominant")} |");
            if (uniqueCategories.Any())
            {
                sb.AppendLine($"| **Interessegebieden** | {uniqueCategories.Count} categorieën | Gevarieerde consumentenbehoeften |");
            }
            sb.AppendLine();
            
            // Consumentengedrag strategische conclusies
            sb.AppendLine($"## Strategische Marktinzichten - {primaryBrand}");
            sb.AppendLine();
            sb.AppendLine("### Consumentenprofilering");
            sb.AppendLine();
            sb.AppendLine("Primaire Doelgroep Karakteristieken:");
            if (avgPrice > 75)
            {
                sb.AppendLine("- Premium segment: Kwaliteitsbewuste consumenten");
                sb.AppendLine("- Overwogen aankoopproces: Uitgebreid onderzoek voor aankoop");
                sb.AppendLine("- Merkgevoeligheid: Hoge waarde gehecht aan merkreputation");
                sb.AppendLine("- Lagere prijselasticiteit: Minder gevoelig voor prijswijzigingen");
            }
            else if (avgPrice > 30)
            {
                sb.AppendLine("- Mainstream consumenten: Balans tussen prijs en kwaliteit");
                sb.AppendLine("- Vergelijkend gedrag: Shoppt rond voor beste deal");
                sb.AppendLine("- Promotiegevoelig: Reageert op kortingsacties");  
                sb.AppendLine("- Review-afhankelijk: Beïnvloed door online beoordelingen");
            }
            else
            {
                sb.AppendLine("- Prijsbewust segment: Prijs als primaire beslissingsfactor");
                sb.AppendLine("- Impulsief koopgedrag: Snelle beslissingen bij goede deals");
                sb.AppendLine("- Minder merkgevoelig: Focus op functionaliteit boven status");
                sb.AppendLine("- Hoge volume potentieel: Frequent terugkerende aankopen");
            }
            sb.AppendLine();
            
            sb.AppendLine("### 🎬 Aanbevelingen voor Marktbenadering");
            sb.AppendLine();
            if (uniqueCategories.Count > 3)
            {
                sb.AppendLine("**Multi-categorie strategie**: De diversiteit aan productcategorieën vereist ");
                sb.AppendLine("**gesegmenteerde marketing** per categorie met aangepaste boodschappen.");
            }
            else
            {
                sb.AppendLine("**Niche focus strategie**: De beperkte categorieën maken **diepgaande specialisatie** ");
                sb.AppendLine("en **expertise-positionering** mogelijk binnen dit segment.");
            }
            sb.AppendLine();
            
            sb.AppendLine("### � Seizoen- en Trendgevoeligheid");
            sb.AppendLine();
            if (avgPrice > 50)
            {
                sb.AppendLine("- **Geplande aankopen**: Vaak gekoppeld aan specifieke gebeurtenissen");
                sb.AppendLine("- **Seizoensinvloeden**: Black Friday, kerst, en andere shopping events");
                sb.AppendLine("- **Langere besluitvorming**: Meerdere touchpoints nodig voor conversie");
            }
            else
            {
                sb.AppendLine("- **Impulsgevoelige timing**: Social media en trending products");
                sb.AppendLine("- **Jaar-rond potentieel**: Minder seizoensafhankelijk");
                sb.AppendLine("- **Snelle trend adoptie**: Early adopters van nieuwe producten");
            }
            sb.AppendLine();
            
            // Updated footer with schema markup hint
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"Laatste Update: {DateTime.Now:dd MMMM yyyy, HH:mm}");
            sb.AppendLine("Data Bron: TradeTracker Marktdata");
            sb.AppendLine($"Geanalyseerde Producten: {products.Count:N0}");
            sb.AppendLine();
            sb.AppendLine("*Disclaimer: Deze analyse is gebaseerd op beschikbare marktdata en prijzen kunnen wijzigen. ");
            sb.AppendLine("Gebruik deze inzichten als richtlijn voor consumentengedrag trends en marktonderzoek.*");

            
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
            Console.WriteLine();

            var service = new TradeTrackerService();
            
            // Toon beschikbare feeds
            service.ListAvailableFeeds();

            try
            {
                var products = await service.GetProductsAsync();

                if (products.Any())
                {
                    Console.WriteLine("\n=== RESULTATEN ===");
                    Console.WriteLine($"Totaal aantal producten: {products.Count}");
                    Console.WriteLine($"Importdatum: {products.First().ImportDate:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine();

                    // Toon eerste 5 producten als voorbeeld
                    Console.WriteLine("Eerste 5 producten:");
                    foreach (var product in products.Take(5))
                    {
                        Console.WriteLine($"- ID: {product.ProductID}");
                        Console.WriteLine($"  Naam: {product.ProductName}");
                        Console.WriteLine($"  Prijs: {product.Price:C} {product.Currency}");
                        Console.WriteLine($"  Categorie: {product.Category}");
                        Console.WriteLine($"  Merk: {product.Brand}");
                        Console.WriteLine($"  Import: {product.ImportDate:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine();
                    }

                    // Statistieken
                    var uniqueBrands = products.Where(p => !string.IsNullOrEmpty(p.Brand))
                                             .Select(p => p.Brand)
                                             .Distinct()
                                             .Count();
                    
                    var productsWithPrice = products.Where(p => p.Price > 0);
                    var avgPrice = productsWithPrice.Any() ? productsWithPrice.Average(p => p.Price) : 0;

                    Console.WriteLine("=== STATISTIEKEN ===");
                    Console.WriteLine($"Unieke merken: {uniqueBrands}");
                    Console.WriteLine($"Producten met prijs: {productsWithPrice.Count()}");
                    Console.WriteLine($"Gemiddelde prijs: {avgPrice:C}");

                    // Sla data op in bestanden
                    await service.SaveProductsToFileAsync(products);
                    await service.SaveProductsToMarkdownAsync(products);
                    
                    // Toon merken als er zijn gevonden
                    if (uniqueBrands > 0)
                    {
                        Console.WriteLine("\nGevonden merken:");
                        var brands = products.Where(p => !string.IsNullOrEmpty(p.Brand))
                                           .Select(p => p.Brand)
                                           .Distinct()
                                           .OrderBy(b => b)
                                           .Take(10);
                        foreach (var brand in brands)
                        {
                            Console.WriteLine($"- {brand}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Geen producten gevonden of fout bij ophalen data.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Onverwachte fout: {ex.Message}");
            }
            finally
            {
                service.Dispose();
            }

            Console.WriteLine("\nDruk op een toets om af te sluiten...");
            Console.ReadKey();
        }
    }
}
