using System.Linq;
using Donteco;
using UnityEngine;

namespace GhostWatchersTrainer
{
    // Screen-space labels for the ghost, other players and ghost orbs. Purely local drawing.
    internal static class Esp
    {
        public static bool Ghost;
        public static bool Players;
        public static bool Orbs;
        public static bool Ectoplasm;
        public static bool Items;

        private static GUIStyle style;
        private static GhostlyFireController[] orbCache = new GhostlyFireController[0];
        private static float nextOrbScan;

        public static bool AnyOn => Ghost || Players || Orbs || Ectoplasm || Items;

        // Revive orbs are spawned locally (9 at lurk points) when a run starts; none exist in the lobby
        private static GhostlyFireController[] OrbList()
        {
            if (Time.realtimeSinceStartup >= nextOrbScan)
            {
                nextOrbScan = Time.realtimeSinceStartup + 1f;
                orbCache = Object.FindObjectsOfType<GhostlyFireController>();
            }
            return orbCache;
        }

        public static int OrbCount => OrbList().Count(o => o != null);

        public static int EctoplasmCount => EctoplasmaController.List.Count(e => e != null && !e.MarkedToDestroy);

        private static Camera Cam()
        {
            Camera cam = Camera.main;
            if (cam != null && cam.isActiveAndEnabled) return cam;
            return Object.FindObjectsOfType<Camera>().FirstOrDefault(c => c.isActiveAndEnabled && c.targetTexture == null);
        }

        public static void Draw()
        {
            if (!AnyOn || Event.current.type != EventType.Repaint) return;
            Camera cam = Cam();
            if (cam == null) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 };
            }

            if (Ghost) DrawGhosts(cam);
            if (Players) DrawPlayers(cam);
            if (Orbs) DrawOrbs(cam);
            if (Ectoplasm) DrawEctoplasm(cam);
            if (Items) DrawItems(cam);
        }

        private static void DrawEctoplasm(Camera cam)
        {
            foreach (EctoplasmaController e in EctoplasmaController.List)
            {
                if (e == null || e.MarkedToDestroy) continue;
                Mark(cam, e.transform.position, "ectoplasm", new Color(0.8f, 0.5f, 1f), 7);
            }
        }

        // Tools lying around (not in someone's hand), within 60m to keep the screen readable
        private static void DrawItems(Camera cam)
        {
            foreach (Tool t in Tool.Tools)
            {
                if (t == null || !t.gameObject.activeInHierarchy || t.Current == Tool.State.InHand) continue;
                if (Vector3.Distance(cam.transform.position, t.transform.position) > 60f) continue;
                Mark(cam, t.transform.position, t.Type.ToString(), new Color(1f, 0.6f, 0.2f), 5);
            }
        }

        private static void DrawGhosts(Camera cam)
        {
            GhostSceneManager gsm = GhostSceneManager.Instance;
            if (gsm == null || gsm.Ghosts == null) return;
            foreach (GameObject g in gsm.Ghosts)
            {
                if (g == null) continue;
                Ghost ghost = g.GetComponent<Ghost>();
                string name = ghost != null ? ghost.GhostType.ToString() : "Ghost";
                Mark(cam, g.transform.position + Vector3.up * 1.6f, "GHOST: " + name, new Color(1f, 0.25f, 0.25f), 14);
            }
        }

        private static void DrawPlayers(Camera cam)
        {
            GameObject local = ObjectSpawner.LocalPlayer;
            foreach (GameObject p in ObjectSpawner.Players)
            {
                if (p == null || p == local) continue;
                PlayerStateController state = p.GetComponentInChildren<PlayerStateController>(true);
                string name = "Player";
                try { if (state != null) name = state.OwnerSteamName(); } catch { }
                string st = state != null ? state.GetCurrentState().ToString() : "";
                bool dead = state != null && state.IsDead;
                string label = dead ? $"{name} [{st}]" : name;
                Mark(cam, p.transform.position + Vector3.up * 2f, label, dead ? new Color(1f, 0.8f, 0.2f) : new Color(0.4f, 0.85f, 1f), 10);
            }
        }

        private static void DrawOrbs(Camera cam)
        {
            foreach (GhostlyFireController orb in OrbList())
            {
                if (orb == null) continue;
                Mark(cam, orb.transform.position, "orb", new Color(0.6f, 1f, 0.6f), 6);
            }
        }

        private static void Mark(Camera cam, Vector3 world, string text, Color color, float dot)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return; // behind camera
            float x = sp.x;
            float y = Screen.height - sp.y;
            float dist = Vector3.Distance(cam.transform.position, world);

            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x - dot / 2f, y - dot / 2f, dot, dot), Texture2D.whiteTexture);

            string label = $"{text}  {dist:0}m";
            Rect r = new Rect(x - 150f, y - dot - 22f, 300f, 20f);
            GUI.color = Color.black;
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), label, style);
            GUI.color = color;
            GUI.Label(r, label, style);
            GUI.color = prev;
        }
    }
}
