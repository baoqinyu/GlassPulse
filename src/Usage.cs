using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GlassPulse
{
    public sealed class UsageRecord
    {
        public string Key { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public string ThreadId { get; set; }
        public string TurnId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public long Input { get; set; }
        public long CacheRead { get; set; }
        public long CacheWriteShort { get; set; }
        public long CacheWriteLong { get; set; }
        public long Output { get; set; }
        public long PromptSize { get; set; }
        public bool Modern { get; set; }
        public bool EstimatedCacheTtl { get; set; }
        public long Total { get { return Input + CacheRead + CacheWriteShort + CacheWriteLong + Output; } }

        public string Signature()
        {
            return string.Join(":", Input, CacheRead, CacheWriteShort, CacheWriteLong, Output);
        }

        public UsageRecord Copy() { return (UsageRecord)MemberwiseClone(); }
    }

    public sealed class ProviderSummary
    {
        public string Provider { get; set; }
        public long Input { get; set; }
        public long CacheRead { get; set; }
        public long CacheWrite { get; set; }
        public long Output { get; set; }
        public long Tokens { get; set; }
        public decimal Cost { get; set; }
        public long UnpricedTokens { get; set; }
        public int Requests { get; set; }
        public int EstimatedCacheTtl { get; set; }
        public Dictionary<string, long> UnknownModels { get; set; }
        public long[] DailyTokens { get; set; }
        public decimal[] DailyCosts { get; set; }
        public long[] HourlyTokens { get; set; }
        public decimal[] HourlyCosts { get; set; }
        public ProviderSummary(string provider)
        {
            Provider = provider;
            UnknownModels = new Dictionary<string, long>();
            DailyTokens = new long[7];
            DailyCosts = new decimal[7];
            HourlyTokens = new long[24];
            HourlyCosts = new decimal[24];
        }
    }

    public sealed class WeekSummary
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public ProviderSummary Codex { get; set; }
        public ProviderSummary Claude { get; set; }
        public DateTimeOffset RefreshedAt { get; set; }
        public int Files { get; set; }
        public int Errors { get; set; }
        public long BytesRead { get; set; }
        public string PriceWarning { get; set; }
        public string SourceWarning { get; set; }
        public bool Demo { get; set; }
        public Dictionary<string, long> ModelTokens { get; set; }
        public WeekSummary()
        {
            Codex = new ProviderSummary("codex");
            Claude = new ProviderSummary("claude");
            ModelTokens = new Dictionary<string, long>();
        }

        public WeekSummary Copy() { return (WeekSummary)MemberwiseClone(); }
    }

    public static class WeekClock
    {
        public static DateTime StartOfWeek(DateTime local, int startDay)
        {
            return local.Date.AddDays(-(((int)local.DayOfWeek - startDay + 7) % 7));
        }

        public static WeekSummary Aggregate(IEnumerable<UsageRecord> records, PriceCatalog prices,
            DateTimeOffset now, TimeZoneInfo zone, int startDay, int weekOffset)
        {
            DateTime start = StartOfWeek(TimeZoneInfo.ConvertTime(now, zone).DateTime, startDay).AddDays(weekOffset * 7);
            WeekSummary week = new WeekSummary { Start = start, End = start.AddDays(7), RefreshedAt = now };
            foreach (UsageRecord record in records)
            {
                DateTime local = TimeZoneInfo.ConvertTime(record.Timestamp, zone).DateTime;
                // Compare local dates, rather than assuming a week is 168 UTC hours (DST).
                if (local < start || local >= week.End) continue;
                ProviderSummary summary = record.Provider == "codex" ? week.Codex : week.Claude;
                int day = (local.Date - start).Days;
                summary.Input += record.Input;
                summary.CacheRead += record.CacheRead;
                summary.CacheWrite += record.CacheWriteShort + record.CacheWriteLong;
                summary.Output += record.Output;
                summary.Tokens += record.Total;
                summary.DailyTokens[day] += record.Total;
                summary.HourlyTokens[local.Hour] += record.Total;
                string model = PriceCatalog.Normalize(record.Model);
                long modelTokens;
                week.ModelTokens.TryGetValue(model, out modelTokens);
                week.ModelTokens[model] = modelTokens + record.Total;
                summary.Requests++;
                if (record.EstimatedCacheTtl) summary.EstimatedCacheTtl++;
                decimal cost;
                if (prices.TryCost(record, out cost))
                {
                    summary.Cost += cost;
                    summary.DailyCosts[day] += cost;
                    summary.HourlyCosts[local.Hour] += cost;
                }
                else
                {
                    // Fail closed on unknown prices: keep tokens visible and mark the dollar total partial.
                    summary.UnpricedTokens += record.Total;
                    long count;
                    summary.UnknownModels.TryGetValue(record.Model ?? "unknown", out count);
                    summary.UnknownModels[record.Model ?? "unknown"] = count + record.Total;
                }
            }
            return week;
        }
    }

    public static class Format
    {
        public static string Tokens(long value)
        {
            if (value >= 1000000000) return (value / 1000000000d).ToString("0.##", CultureInfo.InvariantCulture) + "B";
            if (value >= 1000000) return (value / 1000000d).ToString("0.##", CultureInfo.InvariantCulture) + "M";
            if (value >= 1000) return (value / 1000d).ToString("0.##", CultureInfo.InvariantCulture) + "K";
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Money(decimal value) { return "$" + value.ToString("N2", CultureInfo.InvariantCulture); }
    }
}
