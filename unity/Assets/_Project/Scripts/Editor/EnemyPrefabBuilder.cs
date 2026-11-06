using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TrainSurvivor.Game.View;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// Builds one prefab per enemy from the art cut in art-work/enemies.
    /// The list comes from art-work/enemies/_unity.json (written by tools/pack_enemy_atlas.py),
    /// sprites come from Assets/_Project/Art/Sprites/Enemies/&lt;id&gt;.png.
    ///
    /// Menu: Train Survivor / Art / Rebuild Enemy Prefabs
    /// Batch: -executeMethod TrainSurvivor.EditorTools.EnemyPrefabBuilder.BuildFromBatch
    /// </summary>
    public static class EnemyPrefabBuilder
    {
        const string JsonPath = "art-work/enemies/_unity.json";
        const string SpriteDir = "Assets/_Project/Art/Sprites/Enemies";
        const string PrefabDir = "Assets/_Project/Prefabs/Enemies";

        [MenuItem("Train Survivor/Art/Rebuild Enemy Prefabs")]
        public static void BuildMenu() => Build(true);

        public static void BuildFromBatch()
        {
            try
            {
                Build(false);
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[EnemyPrefabBuilder] " + e);
                EditorApplication.Exit(1);
            }
        }

        static void Build(bool verbose)
        {
            string root = Directory.GetParent(Application.dataPath).Parent.FullName; // project root
            string jsonPath = Path.Combine(root, JsonPath);
            if (!File.Exists(jsonPath))
            {
                Debug.LogError("[EnemyPrefabBuilder] missing " + jsonPath + " — run tools/pack_enemy_atlas.py");
                return;
            }

            var doc = SimpleJson.Parse(File.ReadAllText(jsonPath));
            var sprites = doc["sprites"];
            Directory.CreateDirectory(PrefabDir);

            int built = 0;
            foreach (var kv in sprites.Fields)
            {
                string artId = kv.Key;
                var info = kv.Value;
                string id = info["id"].AsString(artId);
                string spriteName = info["sprite"].AsString("en_" + artId);
                float height = info["heightM"].AsFloat(2f);

                string spritePath = SpriteDir + "/" + artId + ".png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                if (sprite == null)
                {
                    Debug.LogWarning("[EnemyPrefabBuilder] no sprite at " + spritePath);
                    continue;
                }

                var go = new GameObject("Enemy_" + id);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.drawMode = SpriteDrawMode.Simple;
                sr.sortingOrder = 10;

                var model = go.AddComponent<EnemyModel>();
                model.enemyId = id;
                model.spriteName = spriteName;
                model.heightM = height;

                string prefabPath = PrefabDir + "/" + id + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                Object.DestroyImmediate(go);
                built++;
                if (verbose) Debug.Log("[EnemyPrefabBuilder] " + prefabPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[EnemyPrefabBuilder] built " + built + " prefabs in " + PrefabDir);
        }
    }

    /// <summary>Tiny JSON reader — enough for the generated flat _unity.json.</summary>
    sealed class JsonNode
    {
        public string Str;
        public float Num;
        public bool IsObject;
        public readonly System.Collections.Generic.List<string> Keys = new System.Collections.Generic.List<string>();
        public readonly System.Collections.Generic.List<JsonNode> Values = new System.Collections.Generic.List<JsonNode>();

        public System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, JsonNode>> Fields
        {
            get
            {
                for (int i = 0; i < Keys.Count; i++)
                    yield return new System.Collections.Generic.KeyValuePair<string, JsonNode>(Keys[i], Values[i]);
            }
        }

        public JsonNode this[string key]
        {
            get
            {
                for (int i = 0; i < Keys.Count; i++) if (Keys[i] == key) return Values[i];
                return new JsonNode();
            }
        }

        public string AsString(string fallback) => Str ?? fallback;
        public float AsFloat(float fallback) => IsObject ? fallback : (Str != null ? fallback : Num);
    }

    static class SimpleJson
    {
        public static JsonNode Parse(string text)
        {
            int i = 0;
            return Value(text, ref i);
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static JsonNode Value(string s, ref int i)
        {
            Ws(s, ref i);
            var node = new JsonNode();
            if (s[i] == '{')
            {
                node.IsObject = true;
                i++;
                while (true)
                {
                    Ws(s, ref i);
                    if (s[i] == '}') { i++; break; }
                    string key = String(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ':') i++;
                    node.Keys.Add(key);
                    node.Values.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') i++;
                }
            }
            else if (s[i] == '[')
            {
                i++;
                while (true)
                {
                    Ws(s, ref i);
                    if (s[i] == ']') { i++; break; }
                    node.Keys.Add(node.Keys.Count.ToString());
                    node.Values.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') i++;
                }
            }
            else if (s[i] == '"')
            {
                node.Str = String(s, ref i);
            }
            else
            {
                int start = i;
                while (i < s.Length && "-+.eE0123456789truefalsn".IndexOf(s[i]) >= 0) i++;
                string raw = s.Substring(start, i - start);
                if (raw == "null") node.Str = null;
                else if (raw == "true") node.Num = 1;
                else if (raw == "false") node.Num = 0;
                else if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out node.Num))
                    node.Str = raw;
            }
            return node;
        }

        static string String(string s, ref int i)
        {
            if (s[i] != '"') return null;
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    char c = s[i];
                    sb.Append(c == 'n' ? '\n' : c == 't' ? '\t' : c);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }
    }
}
