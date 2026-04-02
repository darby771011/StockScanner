using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Threading;

class Program
{
    // 固定篩選門檻
    static double minPrice = 60.0;
    static double maxPrice = 85.0;
    static double minGrowth = 7.85;

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        
        // --- 啟動時先顯示固定門檻 ---
        Console.WriteLine("===========================================");
        Console.WriteLine("🚀 [全台股時段精確掃描啟動]");
        Console.WriteLine($"📌 固定篩選門檻：");
        Console.WriteLine($"   💰 股價範圍：{minPrice} ~ {maxPrice} 元");
        Console.WriteLine($"   🔥 漲幅門檻：{minGrowth}% 以上");
        Console.WriteLine("===========================================\n");
        
        // 1. 輸入日期與時段
        Console.Write("📅 查詢日期 (YYYY-MM-DD，Enter 則查今日): ");
        string dateInput = Console.ReadLine();
        DateTime targetDate = string.IsNullOrWhiteSpace(dateInput) ? DateTime.Today : DateTime.Parse(dateInput);

        Console.Write("⏰ 開始時間 (例如 11:00): ");
        string startTime = Console.ReadLine();
        Console.Write("⏰ 結束時間 (例如 11:15): ");
        string endTime = Console.ReadLine();

        // --- 計算該時段的 Unix Timestamp (p1, p2) ---
        DateTime baseDate = targetDate.Date;
        long p1 = new DateTimeOffset(baseDate.Add(TimeSpan.Parse(startTime))).ToUnixTimeSeconds();
        long p2 = new DateTimeOffset(baseDate.Add(TimeSpan.Parse(endTime))).ToUnixTimeSeconds();

        // 2. 生成全市場代號 (1101~9999 上市/上櫃)
        var stockList = new List<string>();
        for (int i = 1101; i <= 9999; i++) { stockList.Add($"{i}.TW"); stockList.Add($"{i}.TWO"); }

        int total = stockList.Count;
        int completed = 0;
        var matchedStocks = new List<string>();

        using HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
        
        // 併發設定：建議 20-30
        var options = new ParallelOptions { MaxDegreeOfParallelism = 25 };

        Console.WriteLine($"\n🔍 正在掃描 {targetDate:yyyy-MM-dd} {startTime} ~ {endTime} 數據...\n");

        await Parallel.ForEachAsync(stockList, options, async (symbol, token) =>
        {
            try
            {
                // 第一步：校正基準價 (抓取真正的昨日收盤，確保 1303 計算基準正確)
                long histStart = new DateTimeOffset(targetDate.AddDays(-7)).ToUnixTimeSeconds();
                long histEnd = new DateTimeOffset(targetDate.Date).ToUnixTimeSeconds();
                string histUrl = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?period1={histStart}&period2={histEnd}&interval=1d";
                
                var histResp = await client.GetAsync(histUrl, token);
                if (!histResp.IsSuccessStatusCode) return;

                var histJson = JObject.Parse(await histResp.Content.ReadAsStringAsync(token));
                var histCloses = histJson["chart"]?["result"]?[0]?["indicators"]?["quote"]?[0]?["close"] as JArray;
                double realPrevClose = histCloses?.Select(c => (double?)c).LastOrDefault(c => c.HasValue) ?? 0;

                if (realPrevClose <= 0) return;

                // 第二步：使用您要求的 URL 格式抓取自訂時段數據
                // URL: https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?period1={p1}&period2={p2}&interval=1m
                string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?period1={p1}&period2={p2}&interval=1m";
                
                var response = await client.GetAsync(url, token);
                if (!response.IsSuccessStatusCode) return;

                var json = JObject.Parse(await response.Content.ReadAsStringAsync(token));
                var result = json["chart"]?["result"]?[0];

                if (result != null)
                {
                    double currentPrice = (double?)result["meta"]?["regularMarketPrice"] ?? 0;
                    var highArray = result["indicators"]?["quote"]?[0]?["high"] as JArray;

                    if (highArray != null)
                    {
                        var validHighs = highArray.Select(h => (double?)h).Where(h => h.HasValue).Select(h => h!.Value);
                        if (validHighs.Any())
                        {
                            // 找出指定時段內的區間最高價
                            double periodMax = validHighs.Max();
                            double growth = ((periodMax - realPrevClose) / realPrevClose) * 100;

                            // 固定篩選判斷
                            if (currentPrice >= minPrice && currentPrice <= maxPrice && growth >= minGrowth)
                            {
                                string info = $"{symbol,-9} | 基準昨收: {realPrevClose,6:F2} | 區間最高: {periodMax,6:F2} | 漲幅: {growth,7:+0.00}%";
                                lock (matchedStocks) { matchedStocks.Add(info); }
                                Console.WriteLine($"\n🔥 [符合條件] {info}");
                            }
                        }
                    }
                }
            }
            catch { /* 忽略異常標的 */ }
            finally { Interlocked.Increment(ref completed); }
        });

        Console.WriteLine("\n\n🎯 掃描完畢！符合標的一覽：");
        foreach (var item in matchedStocks.OrderBy(x => x)) Console.WriteLine(item);
        
        Console.WriteLine("\n按任意鍵結束...");
        Console.ReadKey();
    }
}
