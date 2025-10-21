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

    public class TradeTrackerService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl = "https://pf.tradetracker.net/?aid=439092&encoding=utf-8&type=json&fid=2451096&r=xtrmbbq123jaloezie&categoryType=2&additionalType=2";

        public TradeTrackerService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        public async Task<List<TradeTrackerProduct>> GetProductsAsync()
        {
            try
            {
                Console.WriteLine($"Ophalen van data van: {_apiUrl}");
                
                var response = await _httpClient.GetStringAsync(_apiUrl);
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
                var fileName = $"tradetracker_products_{DateTime.Now:yyyyMMdd_HHmmss}.json";
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
                var fileName = $"tradetracker_report_{DateTime.Now:yyyyMMdd_HHmmss}.md";
                var markdown = GenerateMarkdownReport(products);
                await File.WriteAllTextAsync(fileName, markdown);
                Console.WriteLine($"Rapport opgeslagen in Markdown bestand: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fout bij opslaan naar Markdown bestand: {ex.Message}");
            }
        }

        private string GenerateMarkdownReport(List<TradeTrackerProduct> products)
        {
            var sb = new System.Text.StringBuilder();
            var importDate = products.FirstOrDefault()?.ImportDate ?? DateTime.Now;

            // Header
            sb.AppendLine("# TradeTracker Product Feed Report");
            sb.AppendLine();
            sb.AppendLine($"**Import Datum:** {importDate:yyyy-MM-dd HH:mm:ss}  ");
            sb.AppendLine($"**Totaal Producten:** {products.Count}  ");
            sb.AppendLine($"**Gegenereerd:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ");
            sb.AppendLine();

            // Statistieken
            var productsWithPrice = products.Where(p => p.Price > 0).ToList();
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

            sb.AppendLine("## 📊 Statistieken");
            sb.AppendLine();
            sb.AppendLine($"- **Producten met prijs:** {productsWithPrice.Count}");
            if (productsWithPrice.Any())
            {
                sb.AppendLine($"- **Gemiddelde prijs:** €{productsWithPrice.Average(p => p.Price):F2}");
                sb.AppendLine($"- **Laagste prijs:** €{productsWithPrice.Min(p => p.Price):F2}");
                sb.AppendLine($"- **Hoogste prijs:** €{productsWithPrice.Max(p => p.Price):F2}");
            }
            sb.AppendLine($"- **Unieke merken:** {uniqueBrands.Count}");
            sb.AppendLine($"- **Unieke categorieën:** {uniqueCategories.Count}");
            sb.AppendLine();

            // Merken overzicht
            if (uniqueBrands.Any())
            {
                sb.AppendLine("## 🏷️ Merken Overzicht");
                sb.AppendLine();
                foreach (var brand in uniqueBrands)
                {
                    var brandCount = products.Count(p => p.Brand == brand);
                    var avgPrice = products.Where(p => p.Brand == brand && p.Price > 0)
                                         .Select(p => p.Price)
                                         .DefaultIfEmpty(0)
                                         .Average();
                    sb.AppendLine($"- **{brand}:** {brandCount} producten (Gem. prijs: €{avgPrice:F2})");
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

            // Top producten per prijs
            var topExpensive = productsWithPrice.OrderByDescending(p => p.Price).Take(10).ToList();
            if (topExpensive.Any())
            {
                sb.AppendLine("## 💎 Top 10 Duurste Producten");
                sb.AppendLine();
                sb.AppendLine("| Rang | Product | Prijs | Merk | ID | Link |");
                sb.AppendLine("|------|---------|-------|------|-----|------|");
                
                for (int i = 0; i < topExpensive.Count; i++)
                {
                    var product = topExpensive[i];
                    var productName = product.ProductName?.Length > 45 
                        ? product.ProductName.Substring(0, 42) + "..." 
                        : product.ProductName ?? "N/A";
                    var productLink = !string.IsNullOrEmpty(product.ProductURL) 
                        ? $"[🔗 Bekijk]({product.ProductURL})" 
                        : "N/A";
                    sb.AppendLine($"| {i + 1} | {EscapeMarkdown(productName)} | €{product.Price:F2} | {EscapeMarkdown(product.Brand ?? "N/A")} | {product.ProductID} | {productLink} |");
                }
                sb.AppendLine();
            }

            // Goedkoopste producten
            var topCheap = productsWithPrice.Where(p => p.Price > 0).OrderBy(p => p.Price).Take(10).ToList();
            if (topCheap.Any())
            {
                sb.AppendLine("## 💰 Top 10 Goedkoopste Producten");
                sb.AppendLine();
                sb.AppendLine("| Rang | Product | Prijs | Merk | ID | Link |");
                sb.AppendLine("|------|---------|-------|------|-----|------|");
                
                for (int i = 0; i < topCheap.Count; i++)
                {
                    var product = topCheap[i];
                    var productName = product.ProductName?.Length > 45 
                        ? product.ProductName.Substring(0, 42) + "..." 
                        : product.ProductName ?? "N/A";
                    var productLink = !string.IsNullOrEmpty(product.ProductURL) 
                        ? $"[🔗 Bekijk]({product.ProductURL})" 
                        : "N/A";
                    sb.AppendLine($"| {i + 1} | {EscapeMarkdown(productName)} | €{product.Price:F2} | {EscapeMarkdown(product.Brand ?? "N/A")} | {product.ProductID} | {productLink} |");
                }
                sb.AppendLine();
            }

            // Alle producten tabel (eerste 50)
            sb.AppendLine("## 📋 Product Overzicht (Eerste 50)");
            sb.AppendLine();
            sb.AppendLine("| ID | Product | Prijs | Merk | Categorie | Link |");
            sb.AppendLine("|----|---------|-------|------|-----------|----- |");
            
            foreach (var product in products.Take(50))
            {
                var productName = product.ProductName?.Length > 35 
                    ? product.ProductName.Substring(0, 32) + "..." 
                    : product.ProductName ?? "N/A";
                var price = product.Price > 0 ? $"€{product.Price:F2}" : "N/A";
                var productLink = !string.IsNullOrEmpty(product.ProductURL) 
                    ? $"[🔗]({product.ProductURL})" 
                    : "N/A";
                
                sb.AppendLine($"| {product.ProductID} | {EscapeMarkdown(productName)} | {price} | {EscapeMarkdown(product.Brand ?? "N/A")} | {EscapeMarkdown(product.Category ?? "N/A")} | {productLink} |");
            }
            
            if (products.Count > 50)
            {
                sb.AppendLine();
                sb.AppendLine($"*... en nog {products.Count - 50} producten meer. Zie het JSON bestand voor de complete lijst.*");
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("*Rapport gegenereerd door TradeTracker Console App*");
            
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
