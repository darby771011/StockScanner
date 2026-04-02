using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Threading;

class Program
{
    static double minPrice = 60.0;
    static double maxPrice = 85.0;
    static double minGrowth = 7.85;

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.WriteLine("🚀 [全市場安全掃描啟動]");
        
        var ranges = new[] {(0050, 4450),(4501, 3500),(8001, 1998)};
        var stockList = new List<string>();
        foreach (var (start, count) in ranges)
        {
            var nums = Enumerable.Range(start, count);
            stockList.AddRange(nums.Select(i => $"{i}.TW"));
            stockList.AddRange(nums.Select(i => $"{i}.TWO"));
        }

        int total = stockList.Count;
        int completed = 0; 
        var matchedStocks = new List<string>();

        using HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

        var options = new ParallelOptions { MaxDegreeOfParallelism = 15 };
        
        await Parallel.ForEachAsync(stockList, options, async (symbol, token) =>
        {
            try
            {
                string url = $"https://yahoo.com{symbol}?range=1d&interval=1m";
                var response = await client.GetStringAsync(url);
                var json = JObject.Parse(response);
                
                // 使用 ?. 確保層級存在，避免 Null 警告
                var result = json["chart"]?["result"]?[0]; 
                
                if (result != null)
                {
                    var meta = result["meta"];
                    // 加入 (double?) 強制轉型並給予預設值 0
                    double currentPrice = (double?)meta?["regularMarketPrice"] ?? 0;
                    double prevClose = (double?)meta?["previousClose"] ?? 0;
                    
                    var highArray = result["indicators"]?["quote"]?[0]?["high"] as JArray;
                    if (highArray != null && prevClose > 0)
                    {
                        var highPrices = highArray
                                         .Select(h => (double?)h)
                                         .Where(h => h.HasValue)
                                         .Select(h => h!.Value);

                        if (highPrices.Any())
                        {
                            double todayMaxPrice = highPrices.Max();
                            double maxGrowthReached = ((todayMaxPrice - prevClose) / prevClose) * 100;

                            if (currentPrice >= minPrice && currentPrice <= maxPrice && maxGrowthReached >= minGrowth)
                            {
                                string info = $"{symbol,-9} | 現價: {currentPrice,7:F2} | 今日曾漲達: {maxGrowthReached,7:+0.00}%";
                                lock (matchedStocks) { matchedStocks.Add(info); }
                                Console.WriteLine($"\n🔥 [符合條件] {info}");
                            }
                        }
                    }
                }
            }
            catch { /* 忽略錯誤 */ }
            finally
            {
                int currentCount = Interlocked.Increment(ref completed);
                if (currentCount % 100 == 0 || currentCount == total)
                {
                    double progress = (double)currentCount / total * 100;
                    Console.Write($"\r⏳ 目前掃描進度：{progress:F1}% ({currentCount}/{total})   ");
                }
            }
        });

        Console.WriteLine("\n\n🎯 掃描完畢！");
        foreach (var item in matchedStocks) Console.WriteLine(item);
        Console.ReadKey();
    }
}
