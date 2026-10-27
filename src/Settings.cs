using System;
using System.IO;
using System.Text;

namespace GlassPulse
{
    public sealed class Settings
    {
        public decimal CodexBudget { get; set; }
        public decimal ClaudeBudget { get; set; }
        public int RefreshSeconds { get; set; }
        public int WeekStart { get; set; }
        public double GlassOpacity { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool Mini { get; set; }
        public bool Expanded { get; set; }
        public bool ClickThrough { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public string CodexRoot { get; set; }
        public string ClaudeRoot { get; set; }

        public Settings()
        {
            CodexBudget = 100;
            ClaudeBudget = 100;
            RefreshSeconds = 2;
            WeekStart = 1;
            GlassOpacity = 0.64;
            AlwaysOnTop = true;
            Left = -10000;
            Top = -10000;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            CodexRoot = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(profile, ".codex");
            ClaudeRoot = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(profile, ".claude");
        }

        public static Settings Load(string dataDirectory)
        {
            string path = Path.Combine(dataDirectory, "settings.json");
            Settings result = File.Exists(path) ? Json.Serializer().Deserialize<Settings>(File.ReadAllText(path)) : new Settings();
            if (result == null) result = new Settings();
            result.RefreshSeconds = Math.Max(1, Math.Min(60, result.RefreshSeconds));
            result.GlassOpacity = double.IsNaN(result.GlassOpacity) ? 0.64 : Math.Max(0.35, Math.Min(1, result.GlassOpacity));
            result.WeekStart = result.WeekStart == 0 ? 0 : 1;
            result.CodexBudget = Math.Max(1, result.CodexBudget);
            result.ClaudeBudget = Math.Max(1, result.ClaudeBudget);
            if (string.IsNullOrWhiteSpace(result.CodexRoot) || string.IsNullOrWhiteSpace(result.ClaudeRoot))
                throw new InvalidDataException("日志目录不能为空，请修正 settings.json");
            return result;
        }

        public void Save(string dataDirectory)
        {
            Directory.CreateDirectory(dataDirectory);
            string path = Path.Combine(dataDirectory, "settings.json");
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, Json.Serializer().Serialize(this), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
