using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace GlassPulse
{
    internal static class Json
    {
        public static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 128 };
        }

        public static Dictionary<string, object> Map(object value)
        {
            return value as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        public static object Get(Dictionary<string, object> map, string key)
        {
            object result;
            return map.TryGetValue(key, out result) ? result : null;
        }

        public static Dictionary<string, object> Child(Dictionary<string, object> map, string key)
        {
            return Map(Get(map, key));
        }

        public static string Text(Dictionary<string, object> map, string key, string fallback = "")
        {
            object value = Get(map, key);
            return value == null ? fallback : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static long Number(Dictionary<string, object> map, string key)
        {
            long result;
            return long.TryParse(Text(map, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? Math.Max(0, result) : 0;
        }
    }
}
