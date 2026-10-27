using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GlassPulse
{
    public sealed class LogFile
    {
        public string Path { get; private set; }
        public string Provider { get; private set; }
        public long Offset { get; private set; }
        public int Errors { get; private set; }
        public string SessionId { get; private set; }
        public Dictionary<string, UsageRecord> Records { get; private set; }
        public Dictionary<string, string> TurnModels { get; private set; }
        private string model = "unknown";
        private UsageRecord previousTotal;
        private readonly MemoryStream pending = new MemoryStream();
        private bool droppingLine;
        private DateTime creationTime;
        private const int MaxLine = 16 * 1024 * 1024;

        public LogFile(string path, string provider)
        {
            Path = path;
            Provider = provider;
            Reset();
        }

        private void Reset()
        {
            Offset = 0;
            Errors = 0;
            SessionId = System.IO.Path.GetFileNameWithoutExtension(Path);
            Records = new Dictionary<string, UsageRecord>();
            TurnModels = new Dictionary<string, string>();
            model = "unknown";
            previousTotal = null;
            pending.SetLength(0);
            droppingLine = false;
        }

        public long ReadChanges()
        {
            FileInfo info = new FileInfo(Path);
            if (!info.Exists) return 0;
            if (info.Length < Offset || (creationTime != default(DateTime) && creationTime != info.CreationTimeUtc)) Reset();
            creationTime = info.CreationTimeUtc;
            long before = Offset;
            // Sharing keeps actively written agent logs readable without locking them.
            using (FileStream stream = new FileStream(Path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan))
            {
                stream.Seek(Offset, SeekOrigin.Begin);
                long end = stream.Length;
                byte[] buffer = new byte[65536];
                while (stream.Position < end)
                {
                    int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, end - stream.Position));
                    if (count == 0) break;
                    int cursor = 0;
                    while (cursor < count)
                    {
                        int newline = Array.IndexOf(buffer, (byte)10, cursor, count - cursor);
                        int until = newline < 0 ? count : newline;
                        int length = until - cursor;
                        if (!droppingLine && pending.Length + length > MaxLine)
                        {
                            // A corrupt/oversized line cannot exhaust memory or stop later records.
                            pending.SetLength(0);
                            droppingLine = true;
                            Errors++;
                        }
                        if (!droppingLine && length > 0) pending.Write(buffer, cursor, length);
                        if (newline >= 0)
                        {
                            if (!droppingLine && pending.Length > 0)
                                ParseLine(Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length).TrimEnd('\r'));
                            pending.SetLength(0);
                            if (pending.Capacity > 262144) pending.Capacity = 65536;
                            droppingLine = false;
                        }
                        cursor = newline < 0 ? count : newline + 1;
                    }
                }
                Offset = stream.Position;
            }
            // A trailing partial UTF-8 sequence stays as bytes until a complete line arrives.
            return Math.Max(0, Offset - before);
        }

        private static UsageRecord Counters(Dictionary<string, object> usage, string provider)
        {
            UsageRecord r = new UsageRecord { Provider = provider };
            r.Output = Json.Number(usage, "output_tokens");
            if (provider == "codex")
            {
                long input = Json.Number(usage, "input_tokens");
                r.CacheRead = Math.Min(input, Json.Number(usage, "cached_input_tokens"));
                long write = Json.Number(usage, "cache_write_input_tokens");
                r.CacheWriteLong = Math.Min(write, Json.Number(usage, "cache_write_12h_input_tokens"));
                r.CacheWriteShort = Math.Max(0, write - r.CacheWriteLong);
                r.Input = Math.Max(0, input - r.CacheRead - write);
                r.PromptSize = input;
                // Reasoning tokens are a subset of output_tokens, and must not be added again.
            }
            else
            {
                r.Input = Json.Number(usage, "input_tokens");
                r.CacheRead = Json.Number(usage, "cache_read_input_tokens");
                long write = Json.Number(usage, "cache_creation_input_tokens");
                Dictionary<string, object> creation = Json.Child(usage, "cache_creation");
                r.CacheWriteLong = Json.Number(creation, "ephemeral_1h_input_tokens");
                r.CacheWriteShort = Json.Number(creation, "ephemeral_5m_input_tokens");
                if (r.CacheWriteLong + r.CacheWriteShort < write)
                {
                    r.CacheWriteShort += write - r.CacheWriteLong - r.CacheWriteShort;
                    r.EstimatedCacheTtl = true;
                }
                r.PromptSize = r.Input + r.CacheRead + r.CacheWriteShort + r.CacheWriteLong;
            }
            return r;
        }

        private static UsageRecord Difference(UsageRecord current, UsageRecord previous)
        {
            return new UsageRecord {
                Provider = "codex", Input = Math.Max(0, current.Input - previous.Input),
                CacheRead = Math.Max(0, current.CacheRead - previous.CacheRead),
                CacheWriteShort = Math.Max(0, current.CacheWriteShort - previous.CacheWriteShort),
                CacheWriteLong = Math.Max(0, current.CacheWriteLong - previous.CacheWriteLong),
                Output = Math.Max(0, current.Output - previous.Output)
            };
        }

        public void ParseLine(string line)
        {
            // Do not deserialize prompts, tool outputs, or message text unless a usage/context marker exists.
            if (Provider == "codex" && line.IndexOf("token_count", StringComparison.Ordinal) < 0 &&
                line.IndexOf("token_usage_record", StringComparison.Ordinal) < 0 &&
                line.IndexOf("turn_context", StringComparison.Ordinal) < 0 &&
                line.IndexOf("session_meta", StringComparison.Ordinal) < 0) return;
            if (Provider == "claude" && line.IndexOf("\"usage\"", StringComparison.Ordinal) < 0) return;
            try
            {
                Dictionary<string, object> row = Json.Map(Json.Serializer().DeserializeObject(line));
                string type = Json.Text(row, "type");
                Dictionary<string, object> payload = Json.Child(row, "payload");
                if (Provider == "codex" && type == "session_meta")
                {
                    SessionId = Json.Text(payload, "id", SessionId);
                    return;
                }
                if (Provider == "codex" && type == "turn_context")
                {
                    model = Json.Text(payload, "model", model);
                    string turnId = Json.Text(payload, "turn_id");
                    if (turnId.Length > 0) TurnModels[turnId] = model;
                    return;
                }
                DateTimeOffset timestamp;
                if (!DateTimeOffset.TryParse(Json.Text(row, "timestamp"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out timestamp)) return;
                UsageRecord record = null;
                if (Provider == "codex" && type == "token_usage_record")
                {
                    record = Counters(Json.Child(payload, "usage"), Provider);
                    record.Modern = true;
                    record.ThreadId = Json.Text(payload, "thread_id", SessionId);
                    record.TurnId = Json.Text(payload, "turn_id");
                    record.Model = Json.Text(payload, "model", record.ThreadId == SessionId ? model : "unknown");
                    string responseId = Json.Text(payload, "response_id");
                    record.Key = "codex:response:" + (responseId.Length > 0 ? responseId :
                        record.ThreadId + ":" + record.TurnId + ":" + timestamp.ToString("o") + ":" + record.Signature());
                }
                else if (Provider == "codex" && type == "event_msg" && Json.Text(payload, "type") == "token_count")
                {
                    Dictionary<string, object> info = Json.Child(payload, "info");
                    if (info.Count == 0) return; // Rate-limit-only heartbeat; last usage is not a new request.
                    Dictionary<string, object> totals = Json.Child(info, "total_token_usage");
                    Dictionary<string, object> last = Json.Child(info, "last_token_usage");
                    UsageRecord cumulative = totals.Count > 0 ? Counters(totals, Provider) : null;
                    if (cumulative != null && previousTotal != null && cumulative.Signature() == previousTotal.Signature()) return;
                    if (cumulative != null && previousTotal != null && cumulative.Total >= previousTotal.Total &&
                        cumulative.Output >= previousTotal.Output)
                        record = Difference(cumulative, previousTotal);
                    else if (last.Count > 0) record = Counters(last, Provider);
                    else if (cumulative != null) record = cumulative;
                    previousTotal = cumulative;
                    if (record == null) return;
                    record.Model = model;
                    record.ThreadId = SessionId;
                    record.PromptSize = record.Input + record.CacheRead + record.CacheWriteShort + record.CacheWriteLong;
                    record.Key = "codex:legacy:" + SessionId + ":" + (cumulative != null ? cumulative.Signature() :
                        timestamp.ToString("o") + ":" + record.Signature());
                }
                else if (Provider == "claude" && type == "assistant")
                {
                    Dictionary<string, object> message = Json.Child(row, "message");
                    Dictionary<string, object> usage = Json.Child(message, "usage");
                    if (usage.Count == 0 || Json.Text(message, "model") == "<synthetic>") return;
                    record = Counters(usage, Provider);
                    record.Model = Json.Text(message, "model", "unknown");
                    string id = Json.Text(message, "id");
                    if (id.Length == 0) id = Json.Text(row, "uuid");
                    if (id.Length == 0) id = SessionId + ":" + timestamp.ToString("o") + ":" + record.Signature();
                    record.Key = "claude:" + id + ":" + Json.Text(row, "requestId");
                }
                if (record == null || record.Total == 0) return;
                record.Timestamp = timestamp;
                UsageRecord existing;
                if (Records.TryGetValue(record.Key, out existing)) Merge(existing, record);
                else Records[record.Key] = record;
            }
            catch (ArgumentException) { Errors++; }
            catch (InvalidOperationException) { Errors++; }
            catch (FormatException) { Errors++; }
            catch (OverflowException) { Errors++; }
        }

        public static void Merge(UsageRecord existing, UsageRecord value)
        {
            // Claude can emit multiple content blocks and later final usage for a single response.
            existing.Input = Math.Max(existing.Input, value.Input);
            existing.CacheRead = Math.Max(existing.CacheRead, value.CacheRead);
            existing.CacheWriteShort = Math.Max(existing.CacheWriteShort, value.CacheWriteShort);
            existing.CacheWriteLong = Math.Max(existing.CacheWriteLong, value.CacheWriteLong);
            existing.Output = Math.Max(existing.Output, value.Output);
            existing.PromptSize = Math.Max(existing.PromptSize, value.PromptSize);
            if (value.Timestamp < existing.Timestamp) existing.Timestamp = value.Timestamp;
            existing.EstimatedCacheTtl |= value.EstimatedCacheTtl;
            if (existing.Model == "unknown" && value.Model != "unknown") existing.Model = value.Model;
        }
    }

    public sealed class UsageEngine
    {
        private readonly Dictionary<string, LogFile> files = new Dictionary<string, LogFile>(StringComparer.OrdinalIgnoreCase);
        private readonly string codexRoot;
        private readonly string claudeRoot;
        private readonly string pricePath;
        private PriceCatalog prices;
        private DateTime priceWriteTime;
        private string priceWarning = "";
        private List<UsageRecord> normalized;
        private WeekSummary cachedWeek;
        private string cachedZone;
        private bool initialMemoryReleased;
        public long TotalBytesRead { get; private set; }

        public UsageEngine(string codexRoot, string claudeRoot, string pricePath)
        {
            this.codexRoot = codexRoot;
            this.claudeRoot = claudeRoot;
            this.pricePath = pricePath;
            prices = PriceCatalog.Load(pricePath);
            priceWriteTime = File.GetLastWriteTimeUtc(pricePath);
        }

        private static IEnumerable<string> FindLogs(string root, Action readError)
        {
            if (!Directory.Exists(root)) yield break;
            Stack<string> directories = new Stack<string>();
            directories.Push(root);
            while (directories.Count > 0)
            {
                string directory = directories.Pop();
                string[] entries;
                string[] children;
                try { entries = Directory.GetFiles(directory, "*.jsonl"); children = Directory.GetDirectories(directory); }
                catch (IOException) { readError(); continue; }
                catch (UnauthorizedAccessException) { readError(); continue; }
                foreach (string path in entries) yield return path;
                foreach (string child in children)
                {
                    bool safe = false;
                    try { safe = (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0; }
                    catch (IOException) { readError(); }
                    catch (UnauthorizedAccessException) { readError(); }
                    if (safe) directories.Push(child);
                }
            }
        }

        public IEnumerable<UsageRecord> UniqueRecords()
        {
            Dictionary<string, string> turns = new Dictionary<string, string>();
            foreach (LogFile file in files.Values)
                foreach (KeyValuePair<string, string> turn in file.TurnModels) turns[turn.Key] = turn.Value;
            Dictionary<string, UsageRecord> unique = new Dictionary<string, UsageRecord>();
            foreach (LogFile file in files.Values)
                foreach (UsageRecord source in file.Records.Values)
                {
                    UsageRecord record = source.Copy();
                    string known;
                    if (record.Modern && record.TurnId != null && turns.TryGetValue(record.TurnId, out known)) record.Model = known;
                    UsageRecord existing;
                    if (unique.TryGetValue(record.Key, out existing)) LogFile.Merge(existing, record);
                    else unique[record.Key] = record;
                }
            Dictionary<string, List<DateTimeOffset>> modern = new Dictionary<string, List<DateTimeOffset>>();
            foreach (UsageRecord record in unique.Values.Where(r => r.Modern))
            {
                string signature = record.ThreadId + ":" + record.Signature();
                List<DateTimeOffset> stamps;
                if (!modern.TryGetValue(signature, out stamps)) modern[signature] = stamps = new List<DateTimeOffset>();
                stamps.Add(record.Timestamp);
            }
            foreach (UsageRecord record in unique.Values)
            {
                List<DateTimeOffset> stamps;
                // Modern Codex writes a response record AND a legacy counter a few milliseconds later.
                // Match only near-identical events; keep earlier legacy-only history in mixed-version logs.
                if (!record.Modern && record.Provider == "codex" &&
                    modern.TryGetValue(record.ThreadId + ":" + record.Signature(), out stamps) &&
                    stamps.Any(t => Math.Abs((t - record.Timestamp).TotalSeconds) <= 2)) continue;
                yield return record;
            }
        }

        public WeekSummary Refresh(DateTimeOffset now, TimeZoneInfo zone, int startDay, int weekOffset)
        {
            int ioErrors = 0;
            bool dirty = normalized == null;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string provider in new[] { "codex", "claude" })
            {
                string[] roots = provider == "codex" ?
                    new[] { System.IO.Path.Combine(codexRoot, "sessions"), System.IO.Path.Combine(codexRoot, "archived_sessions") } :
                    new[] { System.IO.Path.Combine(claudeRoot, "projects") };
                foreach (string root in roots)
                    foreach (string path in FindLogs(root, delegate { ioErrors++; }))
                    {
                        seen.Add(path);
                        LogFile file;
                        if (!files.TryGetValue(path, out file)) { files[path] = file = new LogFile(path, provider); dirty = true; }
                        try
                        {
                            long oldOffset = file.Offset;
                            long bytes = file.ReadChanges();
                            TotalBytesRead += bytes;
                            if (bytes > 0 || file.Offset != oldOffset) dirty = true;
                        }
                        catch (IOException) { ioErrors++; }
                        catch (UnauthorizedAccessException) { ioErrors++; }
                    }
            }
            foreach (string deleted in files.Keys.Where(p => !seen.Contains(p) && !File.Exists(p)).ToArray()) { files.Remove(deleted); dirty = true; }
            if (File.GetLastWriteTimeUtc(pricePath) != priceWriteTime)
            {
                try
                {
                    PriceCatalog updated = PriceCatalog.Load(pricePath);
                    prices = updated;
                    priceWriteTime = File.GetLastWriteTimeUtc(pricePath);
                    priceWarning = "";
                    cachedWeek = null;
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is ArgumentException || ex is InvalidOperationException)) throw;
                    // Keep the last valid catalog while showing that the edited file failed validation.
                    priceWarning = "价格表无效，正在使用上次有效价格";
                }
            }
            if (dirty) { normalized = UniqueRecords().ToList(); cachedWeek = null; }
            DateTime selectedStart = WeekClock.StartOfWeek(TimeZoneInfo.ConvertTime(now, zone).DateTime, startDay).AddDays(weekOffset * 7);
            if (cachedWeek == null || cachedWeek.Start != selectedStart || cachedZone != zone.Id)
            {
                cachedWeek = WeekClock.Aggregate(normalized, prices, now, zone, startDay, weekOffset);
                cachedZone = zone.Id;
                foreach (ProviderSummary provider in new[] { cachedWeek.Codex, cachedWeek.Claude })
                {
                    var first = normalized.Where(x => x.Provider == provider.Provider).OrderBy(x => x.Timestamp).FirstOrDefault();
                    provider.FirstDate = first == null ? null : TimeZoneInfo.ConvertTime(first.Timestamp, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
            }
            WeekSummary week = cachedWeek.Copy();
            week.RefreshedAt = now;
            week.Files = files.Count;
            week.Errors = ioErrors + files.Values.Sum(x => x.Errors);
            week.BytesRead = TotalBytesRead;
            week.PriceWarning = priceWarning;
            week.SourceWarning = (!Directory.Exists(System.IO.Path.Combine(codexRoot, "sessions")) ? "未找到 Codex 日志。" : "") +
                (!Directory.Exists(System.IO.Path.Combine(claudeRoot, "projects")) ? "未找到 Claude Code 日志。" : "");
            if (!initialMemoryReleased && TotalBytesRead > 64 * 1024 * 1024)
            {
                // Large transcript strings are transient. Release the startup scan's large-object heap once,
                // instead of retaining hundreds of MB while a small native meter sits idle.
                initialMemoryReleased = true;
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
            }
            return week;
        }
    }
}
