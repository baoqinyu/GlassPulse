using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GlassPulse;

internal static class CoreTests
{
    private static int passed, failed;
    private static string priceFile;
    private static PriceCatalog prices;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-27T12:00:00Z");
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual)
    {
        if (!object.Equals(expected, actual)) throw new Exception("Expected " + expected + ", got " + actual);
    }
    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static string Encode(object value) { return Json.Serializer().Serialize(value); }
    private static object Counters(long input, long cached, long output)
    {
        return new { input_tokens = input, cached_input_tokens = cached, output_tokens = output, reasoning_output_tokens = output / 2 };
    }
    private static string Context(string model, string turn = "t1")
    {
        return Encode(new { type = "turn_context", payload = new { model = model, turn_id = turn } });
    }
    private static string Session(string id = "s1") { return Encode(new { type = "session_meta", payload = new { id = id } }); }
    private static string Legacy(long input, long cached, long output, string stamp = "2026-10-27T10:00:00.010Z", object last = null)
    {
        object total = Counters(input, cached, output);
        return Encode(new { type = "event_msg", timestamp = stamp,
            payload = new { type = "token_count", info = new { total_token_usage = total, last_token_usage = last ?? total } } });
    }
    private static string Modern(string response = "r1", string stamp = "2026-10-27T10:00:00Z", string thread = "s1", string turn = "t1")
    {
        return Encode(new { type = "token_usage_record", timestamp = stamp,
            payload = new { response_id = response, thread_id = thread, turn_id = turn, usage = Counters(1000, 800, 100) } });
    }
    private static string Claude(string id = "m1", long output = 100, bool oneHour = false)
    {
        return Encode(new { type = "assistant", timestamp = "2026-10-27T10:00:00Z", requestId = "req1",
            message = new { id = id, model = "claude-opus-5-5", content = "合成测试数据",
                usage = new { input_tokens = 100, cache_read_input_tokens = 800, cache_creation_input_tokens = 200, output_tokens = output,
                    cache_creation = new { ephemeral_1h_input_tokens = oneHour ? 200 : 0, ephemeral_5m_input_tokens = oneHour ? 0 : 200 } } } });
    }
    private static string Sandbox()
    {
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scenario-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "codex", "sessions"));
        Directory.CreateDirectory(Path.Combine(root, "codex", "archived_sessions"));
        Directory.CreateDirectory(Path.Combine(root, "claude", "projects"));
        File.Copy(priceFile, Path.Combine(root, "pricing.json"));
        return root;
    }
    private static UsageEngine Engine(string root)
    {
        return new UsageEngine(Path.Combine(root, "codex"), Path.Combine(root, "claude"), Path.Combine(root, "pricing.json"));
    }
    private static string CodexPath(string root) { return Path.Combine(root, "codex", "sessions", "rollout.jsonl"); }
    private static string ClaudePath(string root) { return Path.Combine(root, "claude", "projects", "session.jsonl"); }
    private static void Write(string path, params string[] rows) { File.WriteAllText(path, string.Join("\n", rows) + "\n", Utf8); }
    private static WeekSummary Refresh(UsageEngine engine) { return engine.Refresh(Now, TimeZoneInfo.Utc, 1, 0); }
    private static UsageRecord Record(long tokens = 1000, string model = "gpt-6.1-sol", string stamp = "2026-10-27T10:00:00Z")
    {
        return new UsageRecord { Provider = "codex", Model = model, Input = tokens, PromptSize = tokens, Timestamp = DateTimeOffset.Parse(stamp) };
    }

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Utf8;
        priceFile = Path.GetFullPath(args[0]); prices = PriceCatalog.Load(priceFile);
        Run("Codex caches included in input, reasoning included in output", delegate {
            LogFile file = new LogFile("synthetic.jsonl", "codex"); file.ParseLine(Context("gpt-6.1-sol")); file.ParseLine(Legacy(1000, 800, 100));
            UsageRecord r = file.Records.Values.Single(); Equal(200L, r.Input); Equal(800L, r.CacheRead); Equal(1100L, r.Total);
            decimal cost; Check(prices.TryCost(r, out cost), "Known price missing"); Equal(.00148m, cost);
        });
        Run("Repeated Codex heartbeat is not billed again", delegate {
            LogFile file = new LogFile("x", "codex"); file.ParseLine(Context("gpt-6.1-sol"));
            file.ParseLine(Legacy(1000, 800, 100)); file.ParseLine(Legacy(1000, 800, 100));
            file.ParseLine(Encode(new { type = "event_msg", timestamp = "2026-10-27T10:01:00Z", payload = new { type = "token_count", info = (object)null } }));
            Equal(1, file.Records.Count);
        });
        Run("Cumulative counter deltas count requests once", delegate {
            LogFile file = new LogFile("x", "codex"); file.ParseLine(Legacy(1000, 800, 100));
            file.ParseLine(Legacy(2000, 1600, 200, "2026-10-27T10:01:00Z")); Equal(2200L, file.Records.Values.Sum(r => r.Total));
        });
        Run("Forked session inherited counters do not inflate first request", delegate {
            LogFile file = new LogFile("x", "codex"); file.ParseLine(Legacy(100000, 80000, 10000, last: Counters(1000, 800, 100)));
            Equal(1100L, file.Records.Values.Single().Total);
        });
        Run("Counter reset uses last request instead of a negative delta", delegate {
            LogFile file = new LogFile("x", "codex"); file.ParseLine(Legacy(1000, 800, 100));
            file.ParseLine(Legacy(500, 400, 50, "2026-10-27T10:01:00Z")); Equal(1650L, file.Records.Values.Sum(r => r.Total));
        });
        Run("Modern and legacy Codex records represent one request", delegate {
            string root = Sandbox(); Write(CodexPath(root), Session(), Context("gpt-6.1-sol"), Modern(), Legacy(1000, 800, 100));
            WeekSummary week = Refresh(Engine(root)); Equal(1100L, week.Codex.Tokens); Equal(1, week.Codex.Requests);
        });
        Run("Legacy-only history is retained in mixed-version logs", delegate {
            string root = Sandbox(); Write(CodexPath(root), Session(), Context("gpt-6.1-sol"), Legacy(1000, 800, 100, "2026-10-27T09:00:00Z"),
                Modern(), Legacy(2000, 1600, 200)); Equal(2200L, Refresh(Engine(root)).Codex.Tokens);
        });
        Run("Archived copies of the same response are deduplicated", delegate {
            string root = Sandbox(); Write(CodexPath(root), Session(), Context("gpt-6.1-sol"), Modern());
            File.Copy(CodexPath(root), Path.Combine(root, "codex", "archived_sessions", "copy.jsonl")); Equal(1100L, Refresh(Engine(root)).Codex.Tokens);
        });
        Run("Child thread prices resolve through that turn's actual model", delegate {
            string root = Sandbox(); Write(CodexPath(root), Session(), Modern(thread: "child", turn: "childturn"));
            Write(Path.Combine(root, "codex", "sessions", "child.jsonl"), Session("child"), Context("gpt-6.1-sol", "childturn"));
            WeekSummary week = Refresh(Engine(root)); Equal(0L, week.Codex.UnpricedTokens); Equal(.00148m, week.Codex.Cost);
        });
        Run("Claude input excludes cache tokens", delegate {
            LogFile file = new LogFile("x", "claude"); file.ParseLine(Claude()); UsageRecord r = file.Records.Values.Single();
            Equal(100L, r.Input); Equal(1200L, r.Total); decimal cost; prices.TryCost(r, out cost); Equal(.00356m, cost);
        });
        Run("One-hour Claude cache writes use the correct rate", delegate {
            LogFile file = new LogFile("x", "claude"); file.ParseLine(Claude(oneHour: true));
            UsageRecord r = file.Records.Values.Single(); Equal(200L, r.CacheWriteLong); Equal(0L, r.CacheWriteShort);
            decimal cost; prices.TryCost(r, out cost); Equal(.00416m, cost);
        });
        Run("Repeated Claude blocks merge final usage instead of summing", delegate {
            LogFile file = new LogFile("x", "claude"); file.ParseLine(Claude(output: 10)); file.ParseLine(Claude(output: 100)); file.ParseLine(Claude(output: 100));
            Equal(1, file.Records.Count); Equal(1200L, file.Records.Values.Single().Total);
        });
        Run("Claude response copies across files are deduplicated", delegate {
            string root = Sandbox(); Write(ClaudePath(root), Claude()); Write(Path.Combine(root, "claude", "projects", "copy.jsonl"), Claude());
            Equal(1200L, Refresh(Engine(root)).Claude.Tokens);
        });
        Run("Unknown model keeps tokens and marks cost incomplete", delegate {
            WeekSummary week = WeekClock.Aggregate(new[] { Record(1000000, "future-model") }, prices, Now, TimeZoneInfo.Utc, 1, 0);
            Equal(1000000L, week.Codex.Tokens); Equal(1000000L, week.Codex.UnpricedTokens); Equal(0m, week.Codex.Cost);
        });
        Run("Dated and dotted model aliases normalize without guessing families", delegate {
            Equal("claude-sonnet-4-6", PriceCatalog.Normalize("claude-sonnet-4.6-20260217"));
            Equal("gpt-5.4", PriceCatalog.Normalize("gpt-5.4-2026-03-05"));
            decimal cost; Check(!prices.TryCost(Record(1000, "gpt-6.1-sol-unlisted"), out cost), "Unknown alias was guessed");
        });
        Run("Unpublished cache-write prices are not treated as free", delegate {
            UsageRecord r = Record(1000, "gpt-5.3-codex"); r.CacheWriteShort = 100;
            decimal cost; Check(!prices.TryCost(r, out cost), "Missing write rate must be flagged");
        });
        Run("Long context applies above, not at, 272000 prompt tokens", delegate {
            UsageRecord r = Record(1000000); r.CacheRead = r.CacheWriteShort = r.CacheWriteLong = r.Output = 1000000;
            decimal cost; r.PromptSize = 272000; prices.TryCost(r, out cost); Equal(19.6m, cost);
            r.PromptSize = 272001; prices.TryCost(r, out cost); Equal(34.2m, cost);
        });
        Run("Haiku 5.5 uses its separate 100K context price tier", delegate {
            UsageRecord r = Record(1000000, "claude-haiku-5-5"); r.Provider = "claude"; r.Output = 1000000;
            decimal cost; r.PromptSize = 100000; prices.TryCost(r, out cost); Equal(.6m, cost);
            r.PromptSize = 100001; prices.TryCost(r, out cost); Equal(3m, cost);
        });
        Run("Monday rollover and half-open week boundaries", delegate {
            UsageRecord[] r = { Record(100, stamp: "2026-10-25T23:59:59Z"), Record(200, stamp: "2026-10-26T00:00:00Z"), Record(300, stamp: "2026-11-02T00:00:00Z") };
            WeekSummary week = WeekClock.Aggregate(r, prices, Now, TimeZoneInfo.Utc, 1, 0); Equal(200L, week.Codex.Tokens);
            Equal(new DateTime(2026, 10, 26), week.Start);
        });
        Run("Sunday week start and historical week navigation", delegate {
            Equal(new DateTime(2026, 10, 25), WeekClock.StartOfWeek(new DateTime(2026, 10, 27), 0));
            WeekSummary week = WeekClock.Aggregate(new[] { Record(100, stamp: "2026-10-25T12:00:00Z") }, prices, Now, TimeZoneInfo.Utc, 1, -1);
            Equal(100L, week.Codex.Tokens); Equal(new DateTime(2026, 10, 19), week.Start);
        });
        Run("Local week boundary survives the 169-hour DST week", delegate {
            TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var r = new[] { Record(100, stamp: "2026-11-01T05:30:00Z"), Record(200, stamp: "2026-11-01T06:30:00Z"),
                Record(300, stamp: "2026-11-02T04:59:59Z"), Record(400, stamp: "2026-11-02T05:00:00Z") };
            WeekSummary previous = WeekClock.Aggregate(r, prices, DateTimeOffset.Parse("2026-11-02T06:00:00Z"), zone, 1, -1);
            Equal(600L, previous.Codex.Tokens); Equal(300L, previous.Codex.HourlyTokens[1]);
            WeekSummary current = WeekClock.Aggregate(r, prices, DateTimeOffset.Parse("2026-11-02T06:00:00Z"), zone, 1, 0);
            Equal(400L, current.Codex.Tokens);
        });
        Run("Hourly, daily and model distributions reconcile with totals", delegate {
            WeekSummary week = WeekClock.Aggregate(new[] { Record(100), Record(250, stamp: "2026-10-28T19:00:00Z") }, prices, Now, TimeZoneInfo.Utc, 1, 0);
            Equal(350L, week.Codex.DailyTokens.Sum()); Equal(350L, week.Codex.HourlyTokens.Sum());
            Equal(350L, week.ModelTokens.Values.Sum()); Equal(week.Codex.Cost, week.Codex.DailyCosts.Sum()); Equal(week.Codex.Cost, week.Codex.HourlyCosts.Sum());
        });
        Run("Partial UTF-8 lines are only counted after newline arrives", delegate {
            string root = Sandbox(), path = ClaudePath(root); byte[] bytes = Utf8.GetBytes(Claude() + "\n");
            int split = Array.IndexOf(bytes, (byte)0xE5) + 1; if (split < 1) split = bytes.Length / 2;
            using (FileStream f = File.Create(path)) f.Write(bytes, 0, split);
            UsageEngine engine = Engine(root); Equal(0L, Refresh(engine).Claude.Tokens);
            using (FileStream f = new FileStream(path, FileMode.Append)) f.Write(bytes, split, bytes.Length - split);
            Equal(1200L, Refresh(engine).Claude.Tokens); Equal(0, Refresh(engine).Errors);
        });
        Run("Incremental refresh does not reread unchanged files", delegate {
            string root = Sandbox(); Write(ClaudePath(root), Claude()); UsageEngine engine = Engine(root);
            Refresh(engine); long initial = engine.TotalBytesRead; Refresh(engine); Equal(initial, engine.TotalBytesRead);
        });
        Run("Appended requests appear on the next refresh", delegate {
            string root = Sandbox(); Write(ClaudePath(root), Claude()); UsageEngine engine = Engine(root); Refresh(engine);
            File.AppendAllText(ClaudePath(root), Claude("m2") + "\n", Utf8); Equal(2400L, Refresh(engine).Claude.Tokens);
        });
        Run("Truncated logs replace previous contents", delegate {
            string root = Sandbox(); Write(ClaudePath(root), Claude(), Claude("m2")); UsageEngine engine = Engine(root); Refresh(engine);
            Write(ClaudePath(root), Claude()); Equal(1200L, Refresh(engine).Claude.Tokens);
        });
        Run("Deleted logs disappear from locally reconstructed totals", delegate {
            string root = Sandbox(); Write(ClaudePath(root), Claude()); UsageEngine engine = Engine(root); Refresh(engine);
            File.Delete(ClaudePath(root)); Equal(0L, Refresh(engine).Claude.Tokens);
        });
        Run("New session files are discovered while running", delegate {
            string root = Sandbox(); UsageEngine engine = Engine(root); Equal(0, Refresh(engine).Files);
            Write(ClaudePath(root), Claude()); Equal(1, Refresh(engine).Files); Equal(1200L, Refresh(engine).Claude.Tokens);
        });
        Run("Malformed record cannot prevent valid later records", delegate {
            string root = Sandbox(); Write(ClaudePath(root), "{\"usage\": broken}", Claude()); WeekSummary week = Refresh(Engine(root));
            Equal(1200L, week.Claude.Tokens); Equal(1, week.Errors);
        });
        Run("Invalid price update preserves the last valid catalog visibly", delegate {
            string root = Sandbox(); Write(CodexPath(root), Session(), Context("gpt-6.1-sol"), Modern()); UsageEngine engine = Engine(root); Refresh(engine);
            string path = Path.Combine(root, "pricing.json"); File.WriteAllText(path, "{broken}", Utf8); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(3));
            WeekSummary week = Refresh(engine); Equal(.00148m, week.Codex.Cost); Check(week.PriceWarning.Length > 0, "Stale price was not marked");
            File.Copy(priceFile, path, true); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(6)); Equal("", Refresh(engine).PriceWarning);
        });
        Run("Negative and duplicate prices fail validation", delegate {
            string root = Sandbox(), path = Path.Combine(root, "pricing.json");
            File.WriteAllText(path, Encode(new PriceCatalog { Models = new List<ModelPrice> { new ModelPrice { Model = "x", Input = -1 } } }), Utf8);
            bool rejected = false; try { PriceCatalog.Load(path); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Negative price accepted");
            File.WriteAllText(path, Encode(new PriceCatalog { Models = new List<ModelPrice> {
                new ModelPrice { Model = "x", Input = 1, Output = 1 }, new ModelPrice { Model = "x", Input = 2, Output = 2 } } }), Utf8);
            rejected = false; try { PriceCatalog.Load(path); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Duplicate model accepted");
        });
        Run("Missing log roots produce a clear disconnected state", delegate {
            string root = Sandbox(); WeekSummary week = new UsageEngine(Path.Combine(root, "missing1"), Path.Combine(root, "missing2"), Path.Combine(root, "pricing.json")).Refresh(Now, TimeZoneInfo.Utc, 1, 0);
            Equal(0L, week.Codex.Tokens); Check(week.SourceWarning.Length > 0, "Missing source is not marked");
        });
        Run("Settings persist without storing conversations", delegate {
            string root = Sandbox(); Settings settings = new Settings { CodexBudget = 240, ClaudeBudget = 500, RefreshSeconds = 3, Mini = true };
            settings.Save(root); Settings loaded = Settings.Load(root); Equal(240m, loaded.CodexBudget); Equal(3, loaded.RefreshSeconds); Equal(true, loaded.Mini);
            Check(!File.ReadAllText(Path.Combine(root, "settings.json")).Contains("合成测试数据"), "Message content leaked into preferences");
        });
        Run("Cached totals still roll over and navigate without log writes", delegate {
            string root = Sandbox(); Write(ClaudePath(root), Claude()); UsageEngine engine = Engine(root);
            Equal(1200L, Refresh(engine).Claude.Tokens);
            Equal(0L, engine.Refresh(DateTimeOffset.Parse("2026-11-02T12:00:00Z"), TimeZoneInfo.Utc, 1, 0).Claude.Tokens);
            Equal(1200L, engine.Refresh(DateTimeOffset.Parse("2026-11-02T12:00:00Z"), TimeZoneInfo.Utc, 1, -1).Claude.Tokens);
        });
        Run("Valid price edits revalue cached history without log writes", delegate {
            string root = Sandbox(); Write(CodexPath(root), Session(), Context("gpt-6.1-sol"), Modern()); UsageEngine engine = Engine(root);
            Equal(.00148m, Refresh(engine).Codex.Cost);
            PriceCatalog changed = PriceCatalog.Load(priceFile); changed.Models.Single(p => p.Model == "gpt-6.1-sol").Input = 4;
            string path = Path.Combine(root, "pricing.json"); File.WriteAllText(path, Encode(changed), Utf8); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(10));
            Equal(.00188m, Refresh(engine).Codex.Cost);
        });
        Console.WriteLine("RESULT " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
