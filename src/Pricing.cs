using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GlassPulse
{
    public sealed class ModelPrice
    {
        public string Model { get; set; }
        public decimal Input { get; set; }
        public decimal Cached { get; set; }
        public decimal Output { get; set; }
        public decimal WriteShort { get; set; }
        public decimal WriteLong { get; set; }
        public long LongContextThreshold { get; set; }
        public decimal LongInput { get; set; }
        public decimal LongOutput { get; set; }
        public decimal LongCached { get; set; }
        public decimal LongWriteShort { get; set; }
        public decimal LongWriteLong { get; set; }
    }

    public sealed class PriceCatalog
    {
        public string VerifiedAt { get; set; }
        public string Basis { get; set; }
        public string[] Sources { get; set; }
        public List<ModelPrice> Models { get; set; }

        public static PriceCatalog Load(string path)
        {
            PriceCatalog catalog = Json.Serializer().Deserialize<PriceCatalog>(File.ReadAllText(path));
            if (catalog == null || catalog.Models == null || catalog.Models.Count == 0)
                throw new InvalidDataException("价格表为空");
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModelPrice model in catalog.Models)
            {
                if (string.IsNullOrWhiteSpace(model.Model) || !names.Add(model.Model) ||
                    model.Input <= 0 || model.Cached < 0 || model.Output <= 0 || model.WriteShort < 0 || model.WriteLong < 0 ||
                    model.LongInput < 0 || model.LongCached < 0 || model.LongOutput < 0 || model.LongWriteShort < 0 || model.LongWriteLong < 0)
                    throw new InvalidDataException("价格表包含重复模型或无效单价");
                if (model.LongContextThreshold > 0 && (model.LongInput <= 0 || model.LongOutput <= 0 ||
                    (model.Cached > 0 && model.LongCached <= 0) || (model.WriteShort > 0 && model.LongWriteShort <= 0) ||
                    (model.WriteLong > 0 && model.LongWriteLong <= 0)))
                    throw new InvalidDataException("长上下文价格不完整");
            }
            return catalog;
        }

        public static string Normalize(string model)
        {
            string value = (model ?? "unknown").ToLowerInvariant().Trim();
            value = Regex.Replace(value, @"\[.*?\]$", "");
            // Only remove documented dated suffixes; never guess an unknown model's family price.
            value = Regex.Replace(value, @"-\d{4}-\d{2}-\d{2}$", "");
            value = Regex.Replace(value, @"-\d{8}$", "");
            if (value.StartsWith("claude-", StringComparison.Ordinal)) value = value.Replace('.', '-');
            return value;
        }

        public bool TryCost(UsageRecord record, out decimal cost)
        {
            string name = Normalize(record.Model);
            ModelPrice p = Models.FirstOrDefault(x => string.Equals(x.Model, name, StringComparison.OrdinalIgnoreCase));
            cost = 0;
            if (p == null) return false;
            if ((record.CacheWriteShort > 0 && p.WriteShort <= 0) || (record.CacheWriteLong > 0 && p.WriteLong <= 0)) return false;
            bool longer = p.LongContextThreshold > 0 && record.PromptSize > p.LongContextThreshold;
            cost = (record.Input * (longer ? p.LongInput : p.Input) +
                record.CacheRead * (longer ? p.LongCached : p.Cached) +
                record.Output * (longer ? p.LongOutput : p.Output) +
                record.CacheWriteShort * (longer ? p.LongWriteShort : p.WriteShort) +
                record.CacheWriteLong * (longer ? p.LongWriteLong : p.WriteLong)) / 1000000m;
            return true;
        }
    }
}
