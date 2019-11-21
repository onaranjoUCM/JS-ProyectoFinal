using System;
using System.Collections.Generic;
using System.Globalization;
using AssetPackage.Exceptions;

namespace AssetPackage.Utils
{
    public class TrackerAssetUtils
    {
        public TrackerAssetUtils(TrackerAsset tracker)
        {
            Tracker = tracker;
        }

        private TrackerAsset Tracker { get; set; }

        public static string[] parseCSV(string trace)
        {
            var p = new List<string>();

            var escape = false;
            var start = 0;
            for (var i = 0; i < trace.Length; i++)
            {
                switch (trace[i])
                {
                    case '\\':
                        escape = true;
                        break;
                    case ',':
                        if (!escape)
                        {
                            p.Add(trace.Substring(start, i - start).Replace("\\,", ","));
                            start = i + 1;
                        }
                        else
                        {
                            escape = false;
                        }

                        break;
                }
            }

            p.Add(trace.Substring(start).Replace("\\,", ","));

            return p.ToArray();
        }

        public static bool quickCheckExtension(string key, object value)
        {
            return quickCheck(key) && quickCheck(value);
        }


        public bool checkExtension(string key, object value)
        {
            return
                check<KeyExtensionException>(key, "Tracker: Extension key is null or empty. Ignored extension.",
                    "Tracker: Extension key is null or empty.")
                &&
                check<ValueExtensionException>(value, "Tracker: Extension value is null or empty. Ignored extension.",
                    "Tracker: Extension value is null or empty.");
        }

        public static bool quickCheck(object value)
        {
            return !(value == null
                     || value.GetType() == typeof(string) && (string) value == ""
                     || value.GetType() == typeof(float) && float.IsNaN((float) value));
        }

        public bool check<T>(object value, string message, string strict_message) where T : TrackerException
        {
            var r = quickCheck(value);

            if (!r)
            {
                notify<T>(message, strict_message);
            }

            return r;
        }

        public bool isFloat<T>(string value, string message, string strict_message, out float result)
            where T : TrackerException
        {
            if (!float.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result))
            {
                notify<T>(message, strict_message);
                return false;
            }

            return true;
        }

        public bool isBool<T>(string value, string message, string strict_message, out bool result)
            where T : TrackerException
        {
            if (!bool.TryParse(value, out result))
            {
                notify<T>(message, strict_message);
                return false;
            }

            return true;
        }

        public void notify<T>(string message, string strict_message) where T : TrackerException
        {
            if (Tracker.StrictMode)
            {
                throw (T) Activator.CreateInstance(typeof(T), strict_message);
            }

            Tracker.Log(Severity.Warning, message);
        }

        public static bool TryParseEnum<T>(string text, out T value)
        {
            var ret = true;
            value = (T) Enum.GetValues(typeof(T)).GetValue(0);

            try
            {
                value = (T) Enum.Parse(typeof(T), text, true);
            }
            catch (Exception e)
            {
                ret = false;
            }

            return ret;
        }
    }
}