using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace GhostWatchersTrainer
{
    // The game can destroy BepInEx's manager object, so the menu runs on its own hidden persistent object
    [BepInPlugin("local.ghostwatchers.trainer", "Ghost Watchers Trainer", TrainerVersion.Current)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            UpdateChecker.Start(System.IO.Path.GetDirectoryName(Info.Location));
            var go = new GameObject("GWTrainerRunner") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<TrainerMenu>();
        }
    }

    public class TrainerMenu : MonoBehaviour
    {
        private const float W = 420;
        private const float Pad = 12;
        private const float RowH = 30;
        private static readonly ManualLogSource Logger = Plugin.Log;

        private bool loggedAlive;
        private bool menuOpen;
        private int tab; // 0 = local, 1 = host
        private CursorLockMode prevLock;
        private bool prevVisible;
        private Rect window = new Rect(30, 30, W, 600);
        private Vector2 scroll;
        private float contentHeight = 600;
        private string status = "";
        private float statusUntil;
        private float nextSlotApply;
        private bool slotsExpanded;
        private bool teleportExpanded;

        private GUIStyle sBtn, sLbl, sHdr, sTgl, sTab, sTabOn, sWin;

        private void Awake()
        {
            Cheats.Show = Show;
        }

        // ------------------------------------------------------------------ loop
        // Game uses the new Input System; check it first and fall back to the legacy API
        private static bool MenuKeyPressed()
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.f9Key.wasPressedThisFrame) return true;
            }
            catch { }
            try { return Input.GetKeyDown(KeyCode.F9); }
            catch { return false; }
        }

        private void Update()
        {
            if (!loggedAlive) { loggedAlive = true; Logger.LogInfo("Cheat menu running, press F9"); }
            if (MenuKeyPressed()) SetMenu(!menuOpen);

            Safe(Cheats.TrackSpawn);
            Safe(() => Cheats.FlyTick(menuOpen));
            Safe(Cheats.GodTick);
            Safe(Cheats.SanityTick);
            Safe(Cheats.GhostTick);
            Safe(Cheats.ItemsTick);

            if (Time.realtimeSinceStartup >= nextSlotApply)
            {
                nextSlotApply = Time.realtimeSinceStartup + 1f;
                Safe(SlotMachineCheat.Apply);
            }
        }

        // The game re-locks the cursor every frame during a run, so keep it free while the menu is open
        private void LateUpdate()
        {
            if (!menuOpen) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void SetMenu(bool open)
        {
            if (open == menuOpen) return;
            menuOpen = open;
            Logger.LogInfo(open ? "Menu opened" : "Menu closed");
            if (open)
            {
                prevLock = Cursor.lockState;
                prevVisible = Cursor.visible;
            }
            else
            {
                Cursor.lockState = prevLock;
                Cursor.visible = prevVisible;
            }
        }

        private void Safe(Action a)
        {
            try { a(); }
            catch (Exception e) { Show("Error: " + e.Message); Logger.LogError(e); }
        }

        private void Show(string msg)
        {
            status = msg;
            statusUntil = Time.realtimeSinceStartup + 4f;
            Logger.LogInfo(msg);
        }

        // ------------------------------------------------------------------ GUI
        private void InitStyles()
        {
            if (sBtn != null) return;
            sBtn = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            sLbl = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = true };
            sHdr = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, richText = true };
            sTgl = new GUIStyle(GUI.skin.toggle) { fontSize = 15 };
            sTab = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            sTabOn = new GUIStyle(sTab);
            sTabOn.normal = sTab.active;
            sTabOn.normal.textColor = new Color(1f, 0.85f, 0.3f);
            sWin = new GUIStyle(GUI.skin.window) { fontSize = 16, fontStyle = FontStyle.Bold };
        }

        private void OnGUI()
        {
            try { Esp.Draw(); } catch { }
            InitStyles();

            if (menuOpen)
            {
                window.height = Mathf.Min(contentHeight + 80, Screen.height - 60);
                window = GUI.Window(0x6857, window, DrawWindow, "Ghost Watchers Cheat Menu", sWin);
            }
            else if (Time.realtimeSinceStartup < statusUntil)
            {
                GUI.Box(new Rect(30, 30, W, 30), status, sBtn);
            }
        }

        private void DrawWindow(int id)
        {
            // tabs
            bool host = Cheats.IsHost;
            if (GUI.Button(new Rect(Pad, 28, (W - 2 * Pad) / 2 - 3, 32), "LOCAL", tab == 0 ? sTabOn : sTab)) tab = 0;
            if (GUI.Button(new Rect(W / 2 + 3, 28, (W - 2 * Pad) / 2 - 3, 32), host ? "HOST" : "HOST / SERVER", tab == 1 ? sTabOn : sTab)) tab = 1;

            Rect view = new Rect(0, 66, W, window.height - 66 - 34);
            Rect content = new Rect(0, 0, W - 20, contentHeight);
            scroll = GUI.BeginScrollView(view, scroll, content);
            float y = 4;
            try
            {
                if (tab == 0) DrawLocal(ref y);
                else DrawHost(ref y, host);
            }
            catch (Exception e)
            {
                GUI.Label(new Rect(Pad, y, W - 2 * Pad - 20, 40), "Error: " + e.Message, sLbl);
                y += 40;
            }
            contentHeight = y + 6;
            GUI.EndScrollView();

            string footer = Time.realtimeSinceStartup < statusUntil ? status
                : UpdateChecker.Downloaded ? $"<color=#66ff66>Update v{UpdateChecker.NewerVersion} downloaded - restart the game to use it</color>"
                : UpdateChecker.NewerVersion != null ? $"<color=#66ff66>Downloading update v{UpdateChecker.NewerVersion}...</color>"
                : $"v{TrainerVersion.Current}   F9 = open / close menu";
            GUI.Label(new Rect(Pad, window.height - 30, W - 2 * Pad, 26), footer, sLbl);
            GUI.DragWindow(new Rect(0, 0, W, 24));
        }

        // ------------------------------------------------------------ LOCAL tab
        private void DrawLocal(ref float y)
        {
            Header(ref y, "Revive  (works for everyone)");
            Row2(ref y, "Revive everyone", () => { Cheats.Revive(onlySelf: false); }, "Revive yourself", () => { Cheats.Revive(onlySelf: true); });
            Toggle(ref y, ref Cheats.GodSelf, "God mode: instantly revive me when I go down", v => Show(v ? "God mode ON" : "God mode OFF"));
            Toggle(ref y, ref Cheats.GodAll, "God mode for everyone (instant revive)", v => Show(v ? "God mode for everyone ON" : "God mode for everyone OFF"));

            Header(ref y, $"Movement speed: {Cheats.SpeedMultiplier:0.0}x");
            float s = Slider(ref y, Cheats.SpeedMultiplier, 1f, 3f, () => Cheats.ApplySpeed(1f));
            if (!float.IsNaN(s) && !Mathf.Approximately(s, Cheats.SpeedMultiplier)) Cheats.ApplySpeed(s);

            Header(ref y, "Fly / noclip  (WASD, Space up, Ctrl down, Shift fast)");
            bool fly = Cheats.Fly;
            Toggle(ref y, ref fly, $"Fly through walls   speed {Cheats.FlySpeed:0}", v => Safe(() => Cheats.SetFly(v)));
            float fs = Slider(ref y, Cheats.FlySpeed, 2f, 20f, () => Cheats.FlySpeed = 6f);
            if (!float.IsNaN(fs)) Cheats.FlySpeed = Mathf.Round(fs);

            Header(ref y, "Fullbright");
            bool fb = Cheats.Fullbright;
            Toggle(ref y, ref fb, $"Light up everything   +{Cheats.Brightness:0.0}", v => Safe(() => Cheats.SetFullbright(v)));
            float b = Slider(ref y, Cheats.Brightness, 1f, 10f, () => Cheats.Brightness = 4f);
            if (float.IsNaN(b)) Safe(Cheats.ApplyBrightness);
            else { b = Mathf.Round(b * 2f) / 2f; if (!Mathf.Approximately(b, Cheats.Brightness)) { Cheats.Brightness = b; Safe(Cheats.ApplyBrightness); } }

            Header(ref y, "Teleport");
            Row2(ref y, "To the ghost", Cheats.TeleportToGhost, "To start / van", Cheats.TeleportToSpawn);
            var others = Cheats.OtherPlayers().ToArray();
            if (others.Length > 0)
            {
                if (Button(ref y, (teleportExpanded ? "â–¼ " : "â–º ") + $"To a teammate ({others.Length})")) teleportExpanded = !teleportExpanded;
                if (teleportExpanded)
                    foreach (GameObject p in others)
                    {
                        GameObject target = p;
                        if (Button(ref y, "   â†’ " + Cheats.PlayerName(p))) Safe(() => Cheats.TeleportToPlayer(target));
                    }
            }

            Header(ref y, "ESP  (see through walls)");
            int orbs = 0, ecto = 0;
            try { orbs = Esp.OrbCount; ecto = Esp.EctoplasmCount; } catch { }
            float half = (W - 2 * Pad - 20) / 2;
            Esp.Ghost = GUI.Toggle(new Rect(Pad, y, half, 24), Esp.Ghost, " Ghost", sTgl);
            Esp.Players = GUI.Toggle(new Rect(Pad + half, y, half, 24), Esp.Players, " Players", sTgl);
            y += 28;
            Esp.Orbs = GUI.Toggle(new Rect(Pad, y, half, 24), Esp.Orbs, $" Revive orbs ({orbs})", sTgl);
            Esp.Ectoplasm = GUI.Toggle(new Rect(Pad + half, y, half, 24), Esp.Ectoplasm, $" Ectoplasm ({ecto})", sTgl);
            y += 28;
            Esp.Items = GUI.Toggle(new Rect(Pad, y, half * 2, 24), Esp.Items, " Items lying around", sTgl);
            y += 32;

            Header(ref y, "Ghost info");
            Label(ref y, SafeText(Cheats.GhostInfo), 44);

            DrawSlots(ref y);
        }

        // ------------------------------------------------------------- HOST tab
        private void DrawHost(ref float y, bool host)
        {
            // Online lobbies are run by the game's server (it is the "master"), so no player is ever
            // host there. Only single-player makes you the master.
            Header(ref y, "Works online too");
            if (Button(ref y, "Auto-identify ghost (enter correct answer)")) Safe(Cheats.AutoIdentify);

            ExorciseStepButton(ref y, host);

            string next = SafeText(() => Cheats.NextBonusTask()?.TaskType.ToString() ?? "");
            if (Button(ref y, !string.IsNullOrEmpty(next) && next != "-" ? "Skip bonus task: " + next : "Skip bonus task")) Safe(Cheats.SkipBonusTask);

            Header(ref y, "Single-player only" + (host ? "" : "  (disabled)"));
            if (!host)
                Label(ref y, "<color=#ffcc44>Online the game's server controls the ghost, sanity and items - these only work in single-player games.</color>", 44);

            bool prevEnabled = GUI.enabled;
            GUI.enabled = host;
            Toggle(ref y, ref Cheats.FreezeGhost, "Freeze ghost (can't move)", v => Show(v ? "Ghost frozen" : "Ghost unfrozen"));
            bool nh = Cheats.NoHunts;
            Toggle(ref y, ref nh, "Ghost can't hunt / attack / grab", v => Safe(() => Cheats.SetNoHunts(v)));
            if (Button(ref y, "Fill sanity   (yours: " + SafeText(Cheats.MySanity) + ")"))
                Safe(() => { int n = Cheats.FillSanity(); Show(n <= 0 ? "Sanity is already full" : "Sanity filled"); });
            Toggle(ref y, ref Cheats.LockSanity, "Keep sanity full", v => Show(v ? "Sanity lock ON" : "Sanity lock OFF"));
            Toggle(ref y, ref Cheats.InfiniteItems, "Infinite item uses (medkits, pills, camera, ...)", v => Show(v ? "Infinite items ON" : "Infinite items OFF"));
            GUI.enabled = prevEnabled;
        }

        private void ExorciseStepButton(ref float y, bool host)
        {
            string progress = SafeText(Cheats.WeaknessProgress);
            Donteco.ExorciseActions a = null;
            try { a = Cheats.NextWeaknessStep(); } catch { }
            string label;
            if (a == null) label = "Skip weakening step " + progress;
            else
            {
                string desc = Cheats.DescribeStep(a);
                bool possible = host || Cheats.CanSkipOnline(a);
                label = possible ? $"Skip step: {desc}  {progress}" : $"Do it yourself: {desc}  {progress}";
            }
            if (Button(ref y, label)) Safe(Cheats.SkipNextWeaknessStep);
        }

        // -------------------------------------------------------------- slots
        private void DrawSlots(ref float y)
        {
            Donteco.Minigames.SlotsLinesWin[] lines = null;
            try { lines = SlotMachineCheat.Lines(); } catch { }

            string mode = SlotMachineCheat.ForcedIndex < 0 || lines == null || SlotMachineCheat.ForcedIndex >= lines.Length
                ? "normal odds"
                : "always " + SlotMachineCheat.Describe(lines[SlotMachineCheat.ForcedIndex]);
            if (Button(ref y, (slotsExpanded ? "â–¼ " : "â–º ") + "Slot machine: " + mode)) slotsExpanded = !slotsExpanded;
            if (!slotsExpanded) return;

            if (lines == null || lines.Length == 0) { Label(ref y, "Slot machine not loaded (go to the lobby)", 26); return; }

            if (Button(ref y, "Normal odds")) { SlotMachineCheat.ForcedIndex = -1; Safe(SlotMachineCheat.Apply); Show("Slot machine: normal odds"); }
            for (int i = 0; i < lines.Length; i++)
            {
                int idx = i;
                string label = "Always win: " + SlotMachineCheat.Describe(lines[i]) + $"   ({SlotMachineCheat.OriginalProbability(lines[i]) * 100f:0.#}%)";
                if (Button(ref y, label))
                {
                    SlotMachineCheat.ForcedIndex = idx;
                    Safe(SlotMachineCheat.Apply);
                    Show("Slot machine: always " + SlotMachineCheat.Describe(lines[idx]));
                }
            }
        }

        // ------------------------------------------------------------ widgets
        private float Inner => W - 2 * Pad - 20;

        private static string SafeText(Func<string> f)
        {
            try { return f(); } catch { return "-"; }
        }

        private void Header(ref float y, string text)
        {
            y += 6;
            GUI.Label(new Rect(Pad, y, Inner, 24), text, sHdr);
            y += 26;
        }

        private void Label(ref float y, string text, float h)
        {
            GUI.Label(new Rect(Pad, y, Inner, h), text, sLbl);
            y += h;
        }

        private bool Button(ref float y, string text)
        {
            bool clicked = GUI.Button(new Rect(Pad, y, Inner, RowH), text, sBtn);
            y += RowH + 4;
            return clicked;
        }

        private void Row2(ref float y, string a, Action onA, string b, Action onB)
        {
            float half = Inner / 2 - 3;
            if (GUI.Button(new Rect(Pad, y, half, RowH), a, sBtn)) Safe(onA);
            if (GUI.Button(new Rect(Pad + half + 6, y, half, RowH), b, sBtn)) Safe(onB);
            y += RowH + 4;
        }


        private void Toggle(ref float y, ref bool value, string text, Action<bool> changed)
        {
            bool v = GUI.Toggle(new Rect(Pad, y, Inner, 26), value, " " + text, sTgl);
            y += 30;
            if (v != value) { value = v; changed(v); }
        }

        private float Slider(ref float y, float value, float min, float max, Action reset)
        {
            float v = GUI.HorizontalSlider(new Rect(Pad, y + 8, Inner - 80, 20), value, min, max);
            v = Mathf.Round(v * 10f) / 10f;
            if (GUI.Button(new Rect(Pad + Inner - 70, y, 70, 26), "Reset", sBtn)) { reset(); v = float.NaN; }
            y += 32;
            return v; // NaN = reset was pressed, caller keeps the reset value
        }
    }
}
