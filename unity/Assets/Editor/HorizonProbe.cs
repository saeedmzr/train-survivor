using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// Isolates the dark horizontal band at the horizon by toggling one render contributor at a
    /// time in the same frame position, and logs the camera clear state. Evidence only.
    /// </summary>
    public static class HorizonProbe
    {
        const string Out = "C:/Users/saeed/projects/train-survivor/evidence/2026-11-11-camera-horizon";

        [MenuItem("Train Survivor/Probe/Horizon Isolation")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Run.unity");
            EditorApplication.EnterPlaymode();
            EditorApplication.update += Tick;
        }

        static int _frame, _stage;
        static string[] _names = {
            "A_baseline", "B_no_haze", "C_no_district", "D_no_ground", "E_no_trackmesh", "F_no_scenery_all"
        };

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            _frame++;
            if (_frame < 180) return;                       // let the world build and settle
            var cam = Camera.main;
            if (cam == null) return;

            if (_stage == 0)
            {
                Debug.Log(string.Format("[hz] cam clear={0} bg={1} near={2} far={3} fog={4} fogCol={5} canvas={6}x{7}",
                    cam.clearFlags, cam.backgroundColor, cam.nearClipPlane, cam.farClipPlane,
                    RenderSettings.fog, RenderSettings.fogColor, Screen.width, Screen.height));
                Snap("A_baseline");
                Toggle("HazeShell", false);
                _stage = 1; _frame = 175; return;
            }
            if (_stage == 1) { Snap("B_no_haze"); SetAll(typeof(TrainSurvivor.Game.View.DistrictScenery), false); _stage = 2; _frame = 175; return; }
            if (_stage == 2) { Snap("C_no_district"); SetAll(typeof(TrainSurvivor.Game.View.TrackMesh), false); _stage = 3; _frame = 175; return; }
            if (_stage == 3) { Snap("D_no_trackmesh"); SetAll(typeof(TrainSurvivor.Game.View.TrainRig), false); _stage = 4; _frame = 175; return; }
            if (_stage == 4)
            {
                Snap("E_no_train");
                Debug.Log("[hz] done — compare columns at x=505 y=0..220 across A..E");
                EditorApplication.update -= Tick;
                EditorApplication.ExitPlaymode();
                EditorApplication.Exit(0);
            }
        }

        static void SetAll(System.Type t, bool on)
        {
            foreach (var o in Object.FindObjectsByType(t, FindObjectsSortMode.None))
                ((MonoBehaviour)o).gameObject.SetActive(on);
        }

        static void Toggle(string name, bool on)
        {
            var go = GameObject.Find(name);
            if (go != null) go.SetActive(on);
            else Debug.Log("[hz] " + name + " not found");
        }

        static void Snap(string name)
        {
            System.IO.Directory.CreateDirectory(Out);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Out, name + ".png"));
            Debug.Log("[hz] captured " + name);
        }
    }
}
