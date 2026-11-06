using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace TrainSurvivor.Game
{
    /// <summary>
    /// Reads optional key=value overrides from <c>{persistentDataPath}/tune.txt</c> at boot.
    /// Lets camera framing and pacing be dialled in on the device without a rebuild:
    ///
    ///   adb push tune.txt /sdcard/Android/data/com.trainsurvivor.game/files/tune.txt
    ///   adb shell am force-stop com.trainsurvivor.game && adb shell am start -n ...
    ///
    /// Unknown keys are ignored; a missing file is not an error.
    /// </summary>
    public static class DevTuning
    {
        static readonly Dictionary<string, float> Values = new Dictionary<string, float>();

        public static bool Loaded { get; private set; }

        public static void Load()
        {
            Loaded = true;
            Values.Clear();

            try
            {
                string path = Path.Combine(Application.persistentDataPath, "tune.txt");
                if (!File.Exists(path)) return;

                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                        Values[key] = f;
                }

                Debug.Log($"[Tune] loaded {Values.Count} overrides from {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Tune] " + e.Message);
            }
        }

        public static float Get(string key, float fallback)
        {
            return Values.TryGetValue(key, out float v) ? v : fallback;
        }
    }
}
