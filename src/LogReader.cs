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
        private Dictionary<string, object> previousTotal;
        private readonly Dictionary<string, UsageRecord> pendingModern = new Dictionary<string, UsageRecord>();
        private readonly Dictionary<string, string> modernTotals = new Dictionary<string, string>();
        public HashSet<string> CompactionIds { get; private set; }
        public Dictionary<string, DateTimeOffset> ResponseTimes { get; private set; }
        public string ParentSessionId { get; private set; }
        public DateTimeOffset? SessionStartedAt { get; private set; }
        private string latestResponse;
        private readonly HashSet<string> accountedResponses = new HashSet<string>();
        private long sequence;
        private static readonly string[] CounterKeys = { "input_tokens", "cached_input_tokens", "cache_write_input_tokens", "output_tokens", "reasoning_output_tokens", "total_tokens" };
        private readonly MemoryStream pending = new MemoryStream();
        private bool droppingLine;
        private DateTime creationTime;
        private long compressedLength = -1;
        private DateTime compressedWrite;
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
            previousTotal = null; pendingModern.Clear(); modernTotals.Clear(); accountedResponses.Clear();
            CompactionIds = new HashSet<string>(); ResponseTimes = new Dictionary<string, DateTimeOffset>();
            ParentSessionId = null; SessionStartedAt = null; latestResponse = null; sequence = 0;
            pending.SetLength(0);
            droppingLine = false;
        }

        public long ReadChanges()
        {
            FileInfo info = new FileInfo(Path);
            if (Path.EndsWith(".jsonl.zst", StringComparison.OrdinalIgnoreCase)) return ReadCompressed(info);
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
                    Consume(buffer, count);
                }
                Offset = stream.Position;
            }
            // A trailing partial UTF-8 sequence stays as bytes until a complete line arrives.
            return Math.Max(0, Offset - before);
        }

        private void Consume(byte[] buffer, int count)
        {
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

        private long ReadCompressed(FileInfo info)
        {
            if (!info.Exists) return 0;
            if (info.Length == compressedLength && info.LastWriteTimeUtc == compressedWrite) return 0;
            // Commit a replacement only after the complete compressed stream was decoded successfully.
            var replacement = new LogFile(Path, Provider);
            long bytes = 0;
            using (Stream source = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (Stream decoded = ZstdCodec.Open(source))
            {
                byte[] buffer = new byte[65536]; int count;
                while ((count = decoded.Read(buffer, 0, buffer.Length)) > 0) { replacement.Consume(buffer, count); bytes += count; }
            }
            if (!replacement.droppingLine && replacement.pending.Length > 0)
                replacement.ParseLine(Encoding.UTF8.GetString(replacement.pending.GetBuffer(), 0, (int)replacement.pending.Length).TrimEnd('\r'));
            Records = replacement.Records; TurnModels = replacement.TurnModels; SessionId = replacement.SessionId;
            model = replacement.model; previousTotal = replacement.previousTotal; Errors = replacement.Errors;
            ParentSessionId = replacement.ParentSessionId; SessionStartedAt = replacement.SessionStartedAt;
            CompactionIds = replacement.CompactionIds; ResponseTimes = replacement.ResponseTimes; latestResponse = replacement.latestResponse; sequence = replacement.sequence;
            accountedResponses.Clear(); foreach (string response in replacement.accountedResponses) accountedResponses.Add(response);
            pendingModern.Clear(); foreach (var item in replacement.pendingModern) pendingModern[item.Key] = item.Value;
            modernTotals.Clear(); foreach (var item in replacement.modernTotals) modernTotals[item.Key] = item.Value;
            Offset = bytes; compressedLength = info.Length; compressedWrite = info.LastWriteTimeUtc;
            return bytes;
        }

        private static UsageRecord Counters(Dictionary<string, object> usage, string provider)
        {
            UsageRecord r = new UsageRecord { Provider = provider };
            r.Output = Json.Number(usage, "output_tokens");
            r.ReasoningOutput = Json.Number(usage, "reasoning_output_tokens");
            r.ReportedTotal = Json.Number(usage, "total_tokens");
            if (provider == "codex")
            {
                long input = Json.Number(usage, "input_tokens");
                r.CacheRead = Math.Min(input, Json.Number(usage, "cached_input_tokens"));
                long write = Math.Min(Math.Max(0, input - r.CacheRead), Json.Number(usage, "cache_write_input_tokens"));
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

        private static bool SameCounters(Dictionary<string, object> left, Dictionary<string, object> right)
        {
            return right != null && CounterKeys.All(key => Json.Number(left, key) == Json.Number(right, key));
        }

        private static Dictionary<string, object> DeltaCounters(Dictionary<string, object> current, Dictionary<string, object> previous)
        {
            var delta = new Dictionary<string, object>();
            foreach (string key in CounterKeys) delta[key] = Math.Max(0, Json.Number(current, key) - (previous == null ? 0 : Json.Number(previous, key)));
            return delta;
        }

        private static void ResolveModel(UsageRecord record, string requested)
        {
            record.Model = string.IsNullOrWhiteSpace(requested) ? "gpt-5" : requested;
            record.EstimatedModel = string.IsNullOrWhiteSpace(requested) || requested == "unknown";
            if (record.Model == "unknown") record.Model = "gpt-5";
            if (record.Model == "gpt-reserve") { record.Model = "gpt-5.6-luna"; record.EstimatedModel = true; }
            if (record.Model == "codex-auto-review")
            {
                string day = record.Timestamp.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string[] dates = { "2026-07-30", "2026-03-05", "2026-02-05", "2025-12-11", "2025-11-13", "2025-09-15", "2025-08-07" };
                string[] models = { "gpt-5.6-luna", "gpt-5.4", "gpt-5.3-codex", "gpt-5.2-codex", "gpt-5.1-codex", "gpt-5-codex", "gpt-5" };
                record.Model = "gpt-5";
                for (int i = 0; i < dates.Length; i++) if (string.CompareOrdinal(day, dates[i]) >= 0) { record.Model = models[i]; break; }
                record.EstimatedModel = true;
            }
        }

        public void ParseLine(string line)
        {
            // Do not deserialize prompts, tool outputs, or message text unless a usage/context marker exists.
            if (Provider == "codex" && line.IndexOf("token_count", StringComparison.Ordinal) < 0 &&
                line.IndexOf("token_usage_record", StringComparison.Ordinal) < 0 &&
                line.IndexOf("turn_context", StringComparison.Ordinal) < 0 &&
                line.IndexOf("session_meta", StringComparison.Ordinal) < 0 && line.IndexOf("compacted", StringComparison.Ordinal) < 0) return;
            if (Provider == "claude" && line.IndexOf("\"usage\"", StringComparison.Ordinal) < 0) return;
            try
            {
                Dictionary<string, object> row = Json.Map(Json.Serializer().DeserializeObject(line));
                string type = Json.Text(row, "type");
                Dictionary<string, object> payload = Json.Child(row, "payload");
                if (Provider == "codex" && type == "session_meta")
                {
                    SessionId = Json.Text(payload, "id", SessionId);
                    ParentSessionId = Json.Text(payload, "forked_from_id");
                    if (ParentSessionId.Length == 0) ParentSessionId = Json.Text(Json.Child(Json.Child(Json.Child(payload, "source"), "subagent"), "thread_spawn"), "parent_thread_id");
                    DateTimeOffset started;
                    if (DateTimeOffset.TryParse(Json.Text(row, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out started)) SessionStartedAt = started;
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
                if (Provider == "codex" && type == "compacted")
                {
                    string response = Json.Text(payload, "compaction_response_id");
                    if (response.Length == 0) return;
                    CompactionIds.Add(response);
                    if (!accountedResponses.Contains(response) && pendingModern.TryGetValue(response, out record)) { pendingModern.Remove(response); Records[record.Key] = record; }
                    return;
                }
                if (Provider == "codex" && type == "token_usage_record")
                {
                    string response = Json.Text(payload, "response_id");
                    if (response.Length == 0 || accountedResponses.Contains(response)) return;
                    record = Counters(Json.Child(payload, "usage"), Provider);
                    record.Timestamp = timestamp; record.Modern = true; record.ThreadId = Json.Text(payload, "thread_id", SessionId);
                    record.TurnId = Json.Text(payload, "turn_id"); record.Sequence = sequence++;
                    ResolveModel(record, Json.Text(payload, "model", model));
                    record.Key = "codex:response:" + response;
                    ResponseTimes[response] = timestamp; latestResponse = response;
                    modernTotals[response] = Counters(Json.Child(payload, "thread_token_usage"), Provider).Signature();
                    // Ordinary request metadata is not another request. Only explicit, uncovered compactions contribute.
                    if (CompactionIds.Contains(response)) Records[record.Key] = record;
                    else if (!pendingModern.ContainsKey(response)) pendingModern[response] = record;
                    return;
                }
                else if (Provider == "codex" && type == "event_msg" && Json.Text(payload, "type") == "token_count")
                {
                    Dictionary<string, object> info = Json.Child(payload, "info");
                    if (info.Count == 0) return;
                    Dictionary<string, object> totals = Json.Child(info, "total_token_usage");
                    Dictionary<string, object> last = Json.Child(info, "last_token_usage");
                    bool advancing = totals.Count == 0 || !SameCounters(totals, previousTotal);
                    Dictionary<string, object> delta = last.Count > 0 && advancing ? last : totals.Count > 0 ? DeltaCounters(totals, previousTotal) : null;
                    if (totals.Count > 0) previousTotal = totals;
                    if (delta == null) return;
                    record = Counters(delta, Provider);
                    if (record.Total == 0) return;
                    record.Timestamp = timestamp; record.ThreadId = SessionId; record.Sequence = sequence++;
                    string requested = Json.Text(payload, "model", Json.Text(info, "model", model));
                    ResolveModel(record, requested);
                    UsageRecord candidate;
                    string totalSignature;
                    if (advancing && latestResponse != null && pendingModern.TryGetValue(latestResponse, out candidate) &&
                        (candidate.Signature() == record.Signature() || (totals.Count > 0 && modernTotals.TryGetValue(latestResponse, out totalSignature) && totalSignature == Counters(totals, Provider).Signature())))
                    { pendingModern.Remove(latestResponse); accountedResponses.Add(latestResponse); }
                    record.Key = "codex:usage:" + timestamp.UtcDateTime.Ticks + ":" + record.Model + ":" + record.Signature();
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
            ZstdCodec.Configure(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(pricePath)));
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
                try { entries = Directory.GetFiles(directory, "*.jsonl").Concat(Directory.GetFiles(directory, "*.jsonl.zst")).ToArray(); children = Directory.GetDirectories(directory); }
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
            var ordered = files.Values.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            var bySession = ordered.GroupBy(x => x.SessionId).ToDictionary(x => x.Key, x => x.First());
            var unique = new Dictionary<string, UsageRecord>();
            foreach (LogFile file in ordered)
            {
                LogFile parent;
                bool fork = !string.IsNullOrEmpty(file.ParentSessionId);
                bool hasParent = fork && bySession.TryGetValue(file.ParentSessionId, out parent) && parent != file;
                parent = hasParent ? bySession[file.ParentSessionId] : null;
                var prefix = parent == null ? new UsageRecord[0] : parent.Records.Values.Where(x => !x.Modern &&
                    (!file.SessionStartedAt.HasValue || x.Timestamp <= file.SessionStartedAt.Value)).OrderBy(x => x.Sequence).ToArray();
                var normal = file.Records.Values.Where(x => x.Provider == "codex" && !x.Modern).OrderBy(x => x.Sequence).ToArray();
                bool dense = fork && normal.Length > 1 && Math.Abs((normal[1].Timestamp - normal[0].Timestamp).TotalSeconds) <= 1 &&
                    (!file.SessionStartedAt.HasValue || Math.Abs((normal[0].Timestamp - file.SessionStartedAt.Value).TotalSeconds) <= 1);
                bool matching = fork, skippingBurst = false; int index = 0; DateTimeOffset previousBurst = dense ? normal[0].Timestamp : default(DateTimeOffset);
                foreach (UsageRecord source in file.Records.Values.OrderBy(x => x.Sequence))
                {
                    if (source.Provider == "codex")
                    {
                        if (source.Modern && parent != null && parent.CompactionIds.Contains(source.Key.Substring("codex:response:".Length)))
                        {
                            DateTimeOffset original;
                            if (!file.SessionStartedAt.HasValue || !parent.ResponseTimes.TryGetValue(source.Key.Substring("codex:response:".Length), out original) || original <= file.SessionStartedAt.Value) continue;
                        }
                        if (!source.Modern && matching)
                        {
                            if (index < prefix.Length && prefix[index].Signature() == source.Signature()) { index++; continue; }
                            matching = false; skippingBurst = index == 0 && dense;
                        }
                        if (!source.Modern && skippingBurst)
                        {
                            if (Math.Abs((source.Timestamp - previousBurst).TotalSeconds) <= 1) { previousBurst = source.Timestamp; continue; }
                            skippingBurst = false;
                        }
                    }
                    UsageRecord record = source.Copy(), existing;
                    if (unique.TryGetValue(record.Key, out existing)) LogFile.Merge(existing, record);
                    else unique[record.Key] = record;
                }
            }
            return unique.Values;
        }

        public WeekSummary Refresh(DateTimeOffset now, TimeZoneInfo zone, int startDay, int weekOffset)
        {
            int ioErrors = 0;
            bool dirty = normalized == null;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> shadowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string provider in new[] { "codex", "claude" })
            {
                var logicalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string[] roots = provider == "codex" ?
                    new[] { System.IO.Path.Combine(codexRoot, "sessions"), System.IO.Path.Combine(codexRoot, "archived_sessions") } :
                    new[] { System.IO.Path.Combine(claudeRoot, "projects") };
                foreach (string root in roots)
                    foreach (string path in FindLogs(root, delegate { ioErrors++; }))
                    {
                        string logical = path.Substring(root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar).Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                        if (logical.EndsWith(".zst", StringComparison.OrdinalIgnoreCase)) logical = logical.Substring(0, logical.Length - 4);
                        if (!logicalPaths.Add(logical)) { shadowed.Add(path); continue; } // Active/plain rollouts win over archived/compressed copies.
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
                        catch (InvalidDataException) { ioErrors++; }
                        catch (UnauthorizedAccessException) { ioErrors++; }
                    }
            }
            foreach (string deleted in files.Keys.Where(p => shadowed.Contains(p) || (!seen.Contains(p) && !File.Exists(p))).ToArray()) { files.Remove(deleted); dirty = true; }
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
                    if (!(ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException)) throw;
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
            week.SourceWarning = (!Directory.Exists(System.IO.Path.Combine(codexRoot, "sessions")) && !Directory.Exists(System.IO.Path.Combine(codexRoot, "archived_sessions")) ? "未找到 Codex 日志。" : "") +
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
