using UnityEngine;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Lit track materials. The supplied rail albedo carries polished steel on the head and rust
    /// on the sides; keeping it on URP Lit with metalness and a mid smoothness is what lets the
    /// trackside lamps throw a warm specular line along both railheads while the flanks stay dark.
    /// </summary>
    public static class TrackMaterials
    {
        static Material _rail, _railHead, _sleeper;

        public static Material Rail()
        {
            if (_rail != null) return _rail;
            var m = Lit("Track/rail_albedo", "RailSteel");
            // A near-1 metallic with no reflection probe renders as pure specular: in this dusk
            // scene that turned both rails into thin cool-blue wires and threw away the rust in
            // the albedo. Keep enough diffuse for the steel to read, and let the warm spots do
            // the highlighting on the head.
            m.SetFloat("_Metallic", 0.22f);
            m.SetFloat("_Smoothness", 0.55f);
            m.SetColor("_BaseColor", new Color(1.25f, 1.25f, 1.25f));
            return _rail = m;
        }

        /// <summary>Polished running surface: untextured steel that the warm spots can highlight.</summary>
        public static Material RailHead()
        {
            if (_railHead != null) return _railHead;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(shader != null ? shader : Shader.Find("Standard")) { name = "RailHead" };
            m.SetColor("_BaseColor", new Color(0.46f, 0.45f, 0.43f));
            m.SetFloat("_Metallic", 0.62f);
            m.SetFloat("_Smoothness", 0.70f);   // tight specular: lamps draw a bright line on it
            m.enableInstancing = true;
            return _railHead = m;
        }

        public static Material Sleeper()
        {
            if (_sleeper != null) return _sleeper;
            var m = Lit("Track/sleeper_albedo", "SleeperTimber");
            m.SetFloat("_Metallic", 0.10f);      // baseplates are metal, timber is not
            m.SetFloat("_Smoothness", 0.22f);
            m.SetColor("_BaseColor", new Color(1.35f, 1.3f, 1.25f));   // lift the timber out of the dusk
            return _sleeper = m;
        }

        static Material Lit(string tex, string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(shader != null ? shader : Shader.Find("Standard")) { name = name };
            var t = Resources.Load<Texture2D>(tex);
            if (t != null)
            {
                m.SetTexture("_BaseMap", t);
                m.SetTexture("_MainTex", t);
            }
            m.SetColor("_BaseColor", Color.white);
            m.color = Color.white;
            m.enableInstancing = true;
            return m;
        }
    }
}
