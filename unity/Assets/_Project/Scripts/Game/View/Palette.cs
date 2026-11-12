using UnityEngine;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Materials are generated as assets under Assets/Resources/M0 so the URP shaders are
    /// guaranteed to survive the build (Resources are always included, Shader.Find is not).
    /// </summary>
    public static class Palette
    {
        public static readonly Color Ballast   = new Color(0.205f, 0.19f, 0.17f);   // graded grey-brown gravel
        public static readonly Color RailWeb   = new Color(0.22f, 0.23f, 0.24f);
        public static readonly Color Rail      = new Color(0.56f, 0.585f, 0.60f);  // weathered steel top
        public static readonly Color Sleeper   = new Color(0.185f, 0.135f, 0.095f); // creosoted timber, dark against gravel
        public static readonly Color Ground    = new Color(0.155f, 0.145f, 0.125f); // weathered packed yard; distance is handled by the terrain shader
        public static readonly Color Body      = new Color(0.16f, 0.18f, 0.22f);
        public static readonly Color Accent    = new Color(0.85f, 0.18f, 0.12f);
        public static readonly Color Metal     = new Color(0.55f, 0.58f, 0.62f);
        public static readonly Color Neon      = new Color(0.25f, 0.95f, 1.0f);
        public static readonly Color Walker    = new Color(0.36f, 0.44f, 0.30f);
        public static readonly Color Sprinter  = new Color(0.55f, 0.60f, 0.42f);
        public static readonly Color Tank      = new Color(0.42f, 0.24f, 0.26f);

        public static Material Get(string name, Color fallback)
        {
            var m = Resources.Load<Material>("M0/" + name);
            if (m != null) return m;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(shader) { color = fallback };
            return m;
        }

        public static Material Emissive(string name, Color fallback)
        {
            var m = Get(name, fallback);
            if (m == null) return null;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", fallback * 1.6f);
            return m;
        }
    }
}
