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
                var fileName = $"{datePrefix}-tradetracker-products-{timeStamp}.json";
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
                var fileName = $"{datePrefix}-tradetracker-report-{timeStamp}.markdown";
                var imageName = $"{datePrefix}-tradetracker-{timeStamp}.svg";
                
                var markdown = GenerateMarkdownReport(products, imageName);
                await File.WriteAllTextAsync(fileName, markdown);
                Console.WriteLine($"Rapport opgeslagen in Markdown bestand: {fileName}");
                
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

        private string GenerateMarkdownReport(List<TradeTrackerProduct> products, string imageName)
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

            // Jekyll front matter with SEO optimization
            sb.AppendLine("---");
            sb.AppendLine("layout: post");
            sb.AppendLine($"title: \"{primaryBrand} TradeTracker Feed Analysis - {products.Count} Products\"");
            sb.AppendLine($"date: {importDate:yyyy-MM-dd HH:mm:ss} +0200");
            sb.AppendLine($"description: \"Complete analysis of {primaryBrand} TradeTracker affiliate feed with {products.Count} products. Price range €{minPrice:F2} - €{maxPrice:F2}. Perfect for affiliate marketers, bloggers and comparison sites.\"");
            sb.AppendLine($"excerpt: \"TradeTracker feed analysis for {primaryBrand}: {products.Count} products, average price €{avgPrice:F2}. Detailed insights for affiliate marketing success.\"");
            sb.AppendLine($"img: {imageName}");
            sb.AppendLine($"tags: [TradeTracker, {cleanBrandName}, affiliate-marketing, product-feed, price-analysis, ecommerce]");
            sb.AppendLine($"categories: [affiliate-marketing, product-analysis]");
            sb.AppendLine($"keywords: \"TradeTracker, {primaryBrand}, affiliate marketing, product feed, price comparison, ecommerce analysis\"");
            sb.AppendLine("author: TradeTracker Analyzer");
            sb.AppendLine($"canonical_url: \"/tradetracker-{cleanBrandName.ToLower()}-analysis\"");
            sb.AppendLine("sitemap:");
            sb.AppendLine("  priority: 0.8");
            sb.AppendLine("  changefreq: weekly");
            sb.AppendLine("schema:");
            sb.AppendLine("  type: Article");
            sb.AppendLine($"  wordCount: {products.Count * 15 + 800}"); // Estimate based on content
            sb.AppendLine("---");
            sb.AppendLine();

            // SEO-optimized intro paragraph
            sb.AppendLine($"Deze **{primaryBrand} TradeTracker affiliate feed analyse** biedt inzicht in **{products.Count} producten** ");
            sb.AppendLine($"met een gemiddelde prijs van **€{avgPrice:F2}**. Perfect voor affiliate marketers, product vergelijkingsites ");
            sb.AppendLine("en e-commerce professionals die op zoek zijn naar winstgevende partnerschappen.");
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

            sb.AppendLine("## TradeTracker Affiliate Marketing Overzicht");
            sb.AppendLine();
            sb.AppendLine($"### Prijsstrategie en Marktpositie");
            sb.AppendLine();
            if (productsWithPrice.Any())
            {
                sb.AppendLine($"De **{primaryBrand} affiliate productfeed** biedt een strategisch prijsbereik van **€{minPrice:F2}** tot **€{maxPrice:F2}**, ");
                sb.AppendLine($"met een **gemiddelde orderwaarde van €{avgPrice:F2}**. Deze prijsstructuur maakt het mogelijk om ");
                sb.AppendLine("zowel budget-bewuste als premium klanten te bedienen, wat de conversiekansen voor affiliates verhoogt.");
                sb.AppendLine();
                
                // Add conversion optimization insight
                if (avgPrice > 50)
                {
                    sb.AppendLine("💡 **Affiliate Tip**: Met een gemiddelde orderwaarde boven €50 is deze feed ideaal voor ");
                    sb.AppendLine("content marketing en productreviews met hogere commissies.");
                }
                else
                {
                    sb.AppendLine("💡 **Affiliate Tip**: De betaalbare prijzen maken deze producten perfect voor ");
                    sb.AppendLine("social media marketing en impulse aankopen.");
                }
                sb.AppendLine();
            }
            
            sb.AppendLine("### Merk Consistentie en Kwaliteit");
            sb.AppendLine();
            if (uniqueBrands.Count > 1)
            {
                sb.AppendLine($"Met **{uniqueBrands.Count} verschillende merken** biedt deze TradeTracker feed ");
                sb.AppendLine("uitstekende mogelijkheden voor **product vergelijkingen** en niche marketing strategieën.");
            }
            else
            {
                sb.AppendLine($"Deze **{primaryBrand} exclusieve feed** biedt voordelen voor affiliate marketers:");
                sb.AppendLine("- Consistente merkbeleving verhoogt vertrouwen");
                sb.AppendLine("- Eenvoudigere content creatie door focus op één merk");
                sb.AppendLine("- Betere conversieratio door merkbekendheid");
            }
            sb.AppendLine();

            // Merken analyse
            if (uniqueBrands.Any() && uniqueBrands.Count > 1)
            {
                sb.AppendLine("## Merk Diversiteit");
                sb.AppendLine();
                sb.AppendLine("De volgende merken zijn vertegenwoordigd in deze productfeed:");
                sb.AppendLine();
                foreach (var brand in uniqueBrands.Take(10))
                {
                    var brandCount = products.Count(p => p.Brand == brand);
                    var brandAvgPrice = products.Where(p => p.Brand == brand && p.Price > 0)
                                         .Select(p => p.Price)
                                         .DefaultIfEmpty(0)
                                         .Average();
                    sb.AppendLine($"- **{brand}**: {brandCount} producten met een gemiddelde prijs van €{brandAvgPrice:F2}");
                }
                if (uniqueBrands.Count > 10)
                {
                    sb.AppendLine($"- *... en nog {uniqueBrands.Count - 10} andere merken*");
                }
                sb.AppendLine();
            }

            // Categorieën overzicht
            if (uniqueCategories.Any())
            {
                sb.AppendLine("## 📂 Categorieën Overzicht");
                sb.AppendLine();
                foreach (var category in uniqueCategories.Take(10))
                {
                    var categoryCount = products.Count(p => p.Category == category);
                    sb.AppendLine($"- **{category}:** {categoryCount} producten");
                }
                sb.AppendLine();
            }

            // Premium producten
            var topExpensive = productsWithPrice.OrderByDescending(p => p.Price).Take(5).ToList();
            if (topExpensive.Any())
            {
                sb.AppendLine("## Premium Productlijn");
                sb.AppendLine();
                sb.AppendLine("Voor klanten die op zoek zijn naar de hoogwaardige producten, biedt de feed enkele interessante opties:");
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

            // Budget vriendelijke opties
            var topCheap = productsWithPrice.Where(p => p.Price > 0).OrderBy(p => p.Price).Take(5).ToList();
            if (topCheap.Any())
            {
                sb.AppendLine("## Budget-Vriendelijke Opties");
                sb.AppendLine();
                sb.AppendLine("Voor kosteneffectieve keuzes biedt de feed deze betaalbare alternatieven:");
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

            // SEO-optimized statistics section
            sb.AppendLine("## TradeTracker Feed Performance Metrics");
            sb.AppendLine();
            sb.AppendLine($"### Affiliate Marketing Potentieel - {primaryBrand}");
            sb.AppendLine();
            sb.AppendLine("**Kernstatistieken voor affiliate success:**");
            sb.AppendLine();
            sb.AppendLine($"| Metric | Waarde | Affiliate Impact |");
            sb.AppendLine("|--------|--------|------------------|");
            sb.AppendLine($"| **Totaal Producten** | {products.Count:N0} | Grote productkeuze verhoogt conversie |");
            if (productsWithPrice.Any())
            {
                sb.AppendLine($"| **Prijsbereik** | €{minPrice:F2} - €{maxPrice:F2} | Breed doelgroepbereik |");
                sb.AppendLine($"| **Gem. Orderwaarde** | €{avgPrice:F2} | {(avgPrice > 50 ? "Hoge commissie potentieel" : "Snelle conversies mogelijk")} |");
            }
            sb.AppendLine($"| **Merken** | {uniqueBrands.Count} | {(uniqueBrands.Count > 1 ? "Vergelijkingsmogelijkheden" : "Merkfocus strategie")} |");
            if (uniqueCategories.Any())
            {
                sb.AppendLine($"| **Categorieën** | {uniqueCategories.Count} | Niche marketing kansen |");
            }
            sb.AppendLine();
            
            // SEO-focused affiliate section
            sb.AppendLine($"## Affiliate Marketing Strategieën voor {primaryBrand}");
            sb.AppendLine();
            sb.AppendLine("### 🎯 Content Marketing Kansen");
            sb.AppendLine();
            sb.AppendLine("**Voor Content Creators & Bloggers:**");
            sb.AppendLine("- Productreviews met focus op prijs-kwaliteit verhouding");
            sb.AppendLine("- \"Best of\" lijsten per productcategorie");
            sb.AppendLine($"- Seizoensgebonden content (gemiddelde prijs €{avgPrice:F2} past bij diverse budgetten)");
            sb.AppendLine();
            
            sb.AppendLine("**Voor Vergelijkingswebsites:**");
            sb.AppendLine($"- Prijsvergelijkingen binnen €{minPrice:F2} - €{maxPrice:F2} range");
            sb.AppendLine("- Feature comparisons tussen verschillende producten");
            sb.AppendLine("- \"Beste deal\" alerts voor budget-bewuste shoppers");
            sb.AppendLine();
            
            sb.AppendLine("### 📱 Social Media & Influencer Marketing");
            sb.AppendLine();
            sb.AppendLine("**Instagram & TikTok Strategieën:**");
            sb.AppendLine($"- Product showcases (ideaal voor {(avgPrice < 30 ? "impulse purchases" : "considered purchases")})");
            sb.AppendLine("- Before/after content voor transformatie producten");
            sb.AppendLine("- Unboxing videos voor nieuwe producten");
            sb.AppendLine();
            
            sb.AppendLine("### 🔍 SEO & Organische Traffic");
            sb.AppendLine();
            sb.AppendLine("**Long-tail Keywords:**");
            sb.AppendLine($"- \"{primaryBrand.ToLower()} review\"");
            sb.AppendLine($"- \"{primaryBrand.ToLower()} vergelijking\"");
            sb.AppendLine($"- \"beste {primaryBrand.ToLower()} producten {DateTime.Now.Year}\"");
            sb.AppendLine($"- \"{primaryBrand.ToLower()} aanbieding\"");
            sb.AppendLine();
            
            // Updated footer with schema markup hint
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"**Laatste Update:** {DateTime.Now:dd MMMM yyyy, HH:mm}");
            sb.AppendLine("**Data Bron:** TradeTracker Affiliate Network");
            sb.AppendLine($"**Geanalyseerde Producten:** {products.Count:N0}");
            sb.AppendLine();
            sb.AppendLine("*Disclaimer: Prijzen en beschikbaarheid kunnen wijzigen. ");
            sb.AppendLine("Controleer altijd de actuele informatie via de affiliate links voor de meest recente details.*");

            
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
