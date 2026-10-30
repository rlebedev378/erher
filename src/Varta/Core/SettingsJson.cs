using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Varta.Core
{
    /// <summary>Збереження/читання AppSettings у JSON (усі публічні поля, відсутні ключі лишаються за замовчуванням).</summary>
    public static class SettingsJson
    {
        static IEnumerable<FieldInfo> Fields()
        {
            return typeof(AppSettings).GetFields(BindingFlags.Public | BindingFlags.Instance);
        }

        public static string ToJson(AppSettings s)
        {
            var d = new Dictionary<string, object>();
            foreach (FieldInfo f in Fields())
            {
                object v = f.GetValue(s);
                var map = v as Dictionary<string, string>;
                if (map != null)
                {
                    var o = new Dictionary<string, object>();
                    foreach (var kv in map) o[kv.Key] = kv.Value;
                    d[f.Name] = o;
                }
                else d[f.Name] = v;
            }
            return MiniJson.Serialize(d, true);
        }

        public static AppSettings FromJson(string json, List<string> warnings)
        {
            var s = new AppSettings();
            var root = MiniJson.Obj(MiniJson.Parse(json));
            if (root == null) throw new FormatException("settings.json: очікувався JSON-об'єкт");
            foreach (FieldInfo f in Fields())
            {
                object jv;
                if (!root.TryGetValue(f.Name, out jv) || jv == null) continue;
                try { Assign(s, f, jv); }
                catch (Exception ex)
                {
                    if (warnings != null) warnings.Add(f.Name + ": " + ex.Message);
                }
            }
            s.Normalize();
            return s;
        }

        public static AppSettings Clone(AppSettings s)
        {
            return FromJson(ToJson(s), null);
        }

        static void Assign(AppSettings s, FieldInfo f, object jv)
        {
            Type t = f.FieldType;
            if (t == typeof(bool))
            {
                if (!(jv is bool)) throw new FormatException("очікувалось true/false");
                f.SetValue(s, (bool)jv);
            }
            else if (t == typeof(int))
            {
                if (jv is string || jv is bool) throw new FormatException("очікувалось число");
                f.SetValue(s, (int)Math.Round(Convert.ToDouble(jv, CultureInfo.InvariantCulture)));
            }
            else if (t == typeof(string))
            {
                f.SetValue(s, Convert.ToString(jv, CultureInfo.InvariantCulture) ?? "");
            }
            else if (t == typeof(Dictionary<string, string>))
            {
                var obj = MiniJson.Obj(jv);
                if (obj == null) throw new FormatException("очікувався об'єкт");
                var cur = (Dictionary<string, string>)f.GetValue(s);
                foreach (var kv in obj) cur[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? "";
            }
            else throw new NotSupportedException("тип " + t.Name);
        }
    }
}
