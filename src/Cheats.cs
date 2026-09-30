using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Donteco;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace GhostWatchersTrainer
{
    // All game manipulation lives here; TrainerMenu only draws the UI and calls into this.
    internal static class Cheats
    {
        public static Action<string> Show = _ => { };

        public static bool IsHost
        {
            get { try { return NetworkManager.IsMasterClient; } catch { return false; } }
        }

        public static GameObject Local => ObjectSpawner.LocalPlayer;

        // ============================================================ revive
        // Dead (Ghostly) players: the game's "collected ghostly orb" request until the orb count
        //   is reached -> host runs the real orb revive (state reset + body removed).
        // Downed/captured players: the game's "help stunned teammate" command.
        // The host executes both for any player without ownership checks -> works as host and client.
        public static int Revive(bool onlySelf, bool quiet = false)
        {
            GameObject local = Local;
            if (local == null) { if (!quiet) Show("Not in a run"); return 0; }
            PlayerHelpTeammate helper = local.GetComponentInChildren<PlayerHelpTeammate>(true);

            int revived = 0;
            foreach (GameObject player in ObjectSpawner.Players)
            {
                if (player == null || (onlySelf && player != local)) continue;
                PlayerStateController state = player.GetComponentInChildren<PlayerStateController>(true);
                if (state == null || !state.IsDead) continue;

                if (state.Is(PlayerState.Ghostly))
                {
                    PlayerGhostlyFireInteraction orbs = player.GetComponentInChildren<PlayerGhostlyFireInteraction>(true);
                    if (orbs == null) continue;
                    int missing = Math.Max(1, PlayerGhostlyFireInteraction.RequiredForRevive - orbs.CountCollectedGhostlyFire.Value);
                    for (int i = 0; i < missing; i++) orbs.Request(orbs.ReqCollectGhostlyFire, b => { }, (ok, r) => { });
                }
                else
                {
                    if (helper == null) continue;
                    int objectId = player.NetIdentity().ObjectId;
                    helper.Command(helper.HelpStunnedTeammate, b => b.Write(objectId));
                }
                revived++;
            }
            if (!quiet) Show(revived == 0 ? "Nobody to revive" : $"Revived {revived} player(s)");
            return revived;
        }

        // ============================================================ speed
        private static float baseWalk = -1, baseRun, baseCrouch, baseCrawl, baseJump;
        public static float SpeedMultiplier = 1f;

        public static void ApplySpeed(float mult)
        {
            if (baseWalk < 0)
            {
                baseWalk = PlayerMovementController.MovementSpeed;
                baseRun = PlayerMovementController.RunSpeed;
                baseCrouch = PlayerMovementController.CrouchSpeed;
                baseCrawl = PlayerMovementController.CrawlSpeed;
                baseJump = PlayerMovementController.MaxJumpSpeed;
            }
            SpeedMultiplier = mult;
            PlayerMovementController.MovementSpeed = baseWalk * mult;
            PlayerMovementController.RunSpeed = baseRun * mult;
            PlayerMovementController.CrouchSpeed = baseCrouch * mult;
            PlayerMovementController.CrawlSpeed = baseCrawl * mult;
            PlayerMovementController.MaxJumpSpeed = baseJump * Mathf.Min(mult, 1.5f);
        }

        // ============================================================ fly / noclip
        // Movement is client-side: with the CharacterController off the game's own movement
        // code stops (it returns early) and we move the transform ourselves -> no collisions.
        public static bool Fly;
        public static float FlySpeed = 6f;
        private static CharacterController flyController;

        public static void SetFly(bool on)
        {
            Fly = on;
            GameObject local = Local;
            if (local == null) { if (on) Show("Fly: not in a run yet, turns on when you spawn"); return; }
            CharacterController cc = local.GetComponentInChildren<CharacterController>(true);
            if (cc == null) return;
            if (on) { cc.enabled = false; flyController = cc; }
            else { cc.enabled = true; flyController = null; }
            Show(on ? "Fly ON (WASD, Space up, Ctrl down, Shift fast)" : "Fly OFF");
        }

        public static void FlyTick(bool menuOpen)
        {
            if (!Fly) return;
            GameObject local = Local;
            if (local == null) return;
            CharacterController cc = local.GetComponentInChildren<CharacterController>(true);
            if (cc == null) return;
            if (cc.enabled) { cc.enabled = false; flyController = cc; } // new run / respawn

            if (menuOpen) return;
            Keyboard kb = Keyboard.current;
            Camera cam = Camera.main;
            if (kb == null || cam == null) return;

            Vector3 dir = Vector3.zero;
            if (kb.wKey.isPressed) dir += cam.transform.forward;
            if (kb.sKey.isPressed) dir -= cam.transform.forward;
            if (kb.dKey.isPressed) dir += cam.transform.right;
            if (kb.aKey.isPressed) dir -= cam.transform.right;
            if (kb.spaceKey.isPressed) dir += Vector3.up;
            if (kb.leftCtrlKey.isPressed) dir -= Vector3.up;
            if (dir.sqrMagnitude < 0.001f) return;

            float speed = FlySpeed * (kb.leftShiftKey.isPressed ? 2.5f : 1f);
            cc.transform.root.position += dir.normalized * speed * Time.deltaTime;
        }

        // ============================================================ god mode
        // Instantly revives whoever goes down (same revive paths as above).
        public static bool GodSelf;
        public static bool GodAll;
        private static float nextGodTick;

        public static void GodTick()
        {
            if (!GodSelf && !GodAll) return;
            if (Time.realtimeSinceStartup < nextGodTick) return;
            nextGodTick = Time.realtimeSinceStartup + 0.25f;
            if (GodAll) Revive(onlySelf: false, quiet: true);
            else if (GodSelf) Revive(onlySelf: true, quiet: true);
        }

        // ============================================================ teleport
        private static GameObject lastLocal;
        private static Vector3 spawnPos;

        // Remember where the local player first appears in a run = the van / start point
        public static void TrackSpawn()
        {
            GameObject local = Local;
            if (local != null && local != lastLocal)
            {
                lastLocal = local;
                spawnPos = local.transform.position;
            }
        }

        private static void Warp(Vector3 pos)
        {
            GameObject local = Local;
            if (local == null) { Show("Not in a run"); return; }
            PlayerMovementController mv = local.GetComponentInChildren<PlayerMovementController>(true);
            if (mv != null) mv.Warp(pos);
            else local.transform.root.position = pos;
        }

        public static void TeleportToSpawn()
        {
            if (Local == null) { Show("Not in a run"); return; }
            Warp(spawnPos + Vector3.up * 0.2f);
            Show("Teleported to start / van");
        }

        public static void TeleportToGhost()
        {
            GameObject ghost = FirstGhost();
            if (ghost == null) { Show("No ghost"); return; }
            Vector3 p = ghost.transform.position - ghost.transform.forward * 2f + Vector3.up * 0.2f;
            Warp(p);
            Show("Teleported to the ghost");
        }

        public static void TeleportToPlayer(GameObject player)
        {
            if (player == null) return;
            Warp(player.transform.position + player.transform.right * 1f + Vector3.up * 0.2f);
            Show("Teleported to " + PlayerName(player));
        }

        public static IEnumerable<GameObject> OtherPlayers()
        {
            GameObject local = Local;
            return ObjectSpawner.Players.Where(p => p != null && p != local);
        }

        public static string PlayerName(GameObject p)
        {
            try
            {
                PlayerStateController s = p.GetComponentInChildren<PlayerStateController>(true);
                if (s != null) return s.OwnerSteamName();
            }
            catch { }
            return "Player";
        }

        // ============================================================ fullbright (HDRP)
        // A global high-priority volume that raises exposure and turns fog off.
        public static bool Fullbright;
        public static float Brightness = 4f; // EV added
        private static Volume brightVolume;
        private static Exposure brightExposure;
        private static ExposureMode baseMode;
        private static float baseFixed, baseComp;

        public static void SetFullbright(bool on)
        {
            Fullbright = on;
            if (on)
            {
                if (brightVolume == null)
                {
                    var go = new GameObject("GWTrainerFullbright") { hideFlags = HideFlags.HideAndDontSave };
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    brightVolume = go.AddComponent<Volume>();
                    brightVolume.isGlobal = true;
                    brightVolume.priority = 100000f;
                    var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    brightExposure = profile.Add<Exposure>(true);
                    Fog fog = profile.Add<Fog>(true);
                    fog.enabled.Override(false);
                    brightVolume.sharedProfile = profile;
                }
                brightVolume.enabled = false;
                CaptureBaseExposure();
                ApplyBrightness();
                brightVolume.enabled = true;
            }
            else if (brightVolume != null)
            {
                brightVolume.enabled = false;
            }
        }

        private static void CaptureBaseExposure()
        {
            baseMode = ExposureMode.Fixed; baseFixed = 10f; baseComp = 0f;
            try
            {
                Exposure cur = VolumeManager.instance.stack.GetComponent<Exposure>();
                if (cur != null)
                {
                    baseMode = cur.mode.value;
                    baseFixed = cur.fixedExposure.value;
                    baseComp = cur.compensation.value;
                }
            }
            catch { }
        }

        public static void ApplyBrightness()
        {
            if (brightExposure == null) return;
            brightExposure.mode.Override(baseMode);
            if (baseMode == ExposureMode.Fixed || baseMode == ExposureMode.UsePhysicalCamera)
            {
                brightExposure.mode.Override(ExposureMode.Fixed);
                brightExposure.fixedExposure.Override(baseFixed - Brightness);
            }
            else
            {
                brightExposure.compensation.Override(baseComp + Brightness);
            }
        }

        // ============================================================ ghost (host)
        public static GameObject FirstGhost()
        {
            GhostSceneManager gsm = GhostSceneManager.Instance;
            return gsm != null && gsm.Ghosts != null ? gsm.Ghosts.FirstOrDefault(g => g != null) : null;
        }

        public static string GhostInfo()
        {
            GhostSceneManager gsm = GhostSceneManager.Instance;
            if (gsm == null || gsm.Ghosts == null || gsm.Ghosts.Length == 0) return "No ghost (not in a run)";
            return string.Join("\n", gsm.Ghosts.Where(g => g != null).Select(g =>
            {
                Ghost ghost = g.GetComponent<Ghost>();
                return ghost == null ? "?" : $"{ghost.GhostType}   Age: {ghost.Age}   Mood: {ghost.Mood}";
            }));
        }

        // The AI only hunts/attacks/captures while these cooldown flags are false
        private static readonly FieldInfo[] cooldownFields = new[] { "isCooldownFastAttack", "isCooldownHunt", "isCooldownAttack", "isCooldownCapture" }
            .Select(n => typeof(GhostAI).GetField(n, BindingFlags.Instance | BindingFlags.NonPublic))
            .Where(f => f != null).ToArray();

        public static bool NoHunts;
        public static bool FreezeGhost;
        private static readonly Dictionary<GameObject, Vector3> frozenAt = new Dictionary<GameObject, Vector3>();

        public static void GhostTick()
        {
            if (!IsHost) return;
            GhostSceneManager gsm = GhostSceneManager.Instance;
            if (gsm == null || gsm.Ghosts == null) return;

            foreach (GameObject g in gsm.Ghosts)
            {
                if (g == null) continue;

                if (NoHunts)
                {
                    foreach (GhostAI ai in g.GetComponentsInChildren<GhostAI>(true))
                        foreach (FieldInfo f in cooldownFields) f.SetValue(ai, true);
                }

                NavMeshAgent agent = g.GetComponentInChildren<NavMeshAgent>(true);
                if (FreezeGhost)
                {
                    if (!frozenAt.TryGetValue(g, out Vector3 pos)) { pos = g.transform.position; frozenAt[g] = pos; }
                    if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
                    {
                        agent.isStopped = true;
                        agent.velocity = Vector3.zero;
                        if ((g.transform.position - pos).sqrMagnitude > 0.04f) agent.Warp(pos);
                    }
                    else if ((g.transform.position - pos).sqrMagnitude > 0.04f)
                    {
                        g.transform.position = pos;
                    }
                }
                else if (frozenAt.ContainsKey(g))
                {
                    frozenAt.Remove(g);
                    if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = false;
                }
            }
        }

        public static void SetNoHunts(bool on)
        {
            NoHunts = on;
            if (!on)
            {
                // hand the cooldowns back to the AI
                GameObject g = FirstGhost();
                if (g != null)
                    foreach (GhostAI ai in g.GetComponentsInChildren<GhostAI>(true))
                        foreach (FieldInfo f in cooldownFields) f.SetValue(ai, false);
            }
            Show(on ? "Ghost can't hunt / attack / grab" : "Ghost behaves normally");
        }

        // ============================================================ auto identify (host)
        private static readonly MethodInfo setSelectedInfo =
            typeof(DefinitionGhostManager).GetMethod("SetSelectedGhostInfo", BindingFlags.Instance | BindingFlags.NonPublic);

        // Online the game's server is the master. The terminal accepts "selected answer" and
        // "right definition" as commands from any player, so the same works there too.
        public static void AutoIdentify()
        {
            DefinitionGhostManager def = DefinitionGhostManager.Instance;
            GameObject ghostGo = FirstGhost();
            Ghost ghost = ghostGo != null ? ghostGo.GetComponent<Ghost>() : null;
            if (def == null || ghost == null) { Show("No ghost terminal (not in a run)"); return; }
            if (def.IsRightDefinition.Value) { Show("Ghost is already identified"); return; }
            if (Local == null) { Show("Not in a run"); return; }

            int type = (int)ghost.GhostType;
            int age = (int)ghost.Age;
            int mood = (int)ghost.Mood;
            int playerObjId = Local.NetIdentity().ObjectId;

            if (IsHost)
            {
                setSelectedInfo.Invoke(def, new object[] { type, age, mood });
                def.CheckGhost(Local);
            }
            else
            {
                def.Command(def.CmdSendSelectedGhostInfo, b => { b.Write(type); b.Write(age); b.Write(mood); });
                def.Command(def.CmdSendRightDefinitionSignal, b => b.Write(playerObjId));
            }
            Show($"Identified: {ghost.GhostType}, {ghost.Age}, {ghost.Mood}");
        }

        // ============================================================ weakening steps (host)
        // After identification RequiredActions lists the steps to weaken the ghost (last = catch it).
        // The host counts a step when a matching ExorciseActionSignal arrives -> we publish that.
        private static ExorcismTasksManager WeaknessManager()
        {
            ExorcismTasksManager mgr = ExorcismTasksManager.Instance;
            return mgr != null && mgr.RequiredActions != null && mgr.RequiredActions.Count > 0 ? mgr : null;
        }

        public static ExorciseActions NextWeaknessStep()
        {
            ExorcismTasksManager mgr = WeaknessManager();
            if (mgr == null) return null;
            int idx = mgr.CurrentRequiredAction;
            return idx < mgr.RequiredActions.Count - 1 ? mgr.RequiredActions[idx] : null;
        }

        public static string WeaknessProgress()
        {
            ExorcismTasksManager mgr = WeaknessManager();
            if (mgr == null) return "";
            int total = mgr.RequiredActions.Count - 1;
            int done = Math.Min(mgr.CurrentRequiredAction, total);
            return done >= total ? "(ghost is weak, catch it!)" : $"({done}/{total} done)";
        }

        public static string DescribeStep(ExorciseActions a)
        {
            switch (a.ActionType)
            {
                case ExorciseActionType.UseItem: return "Use " + a.Item;
                case ExorciseActionType.SaySomething: return $"Say \"{a.Phrase}\"";
                case ExorciseActionType.MaterializeGhost: return "Identify the ghost";
                default: return a.ActionType.ToString();
            }
        }

        // Which steps can be skipped when the game's server is the master (online lobbies)
        public static bool CanSkipOnline(ExorciseActions step)
        {
            if (step == null) return false;
            return step.ActionType == ExorciseActionType.SaySomething
                || step.ActionType == ExorciseActionType.TakeGhostPhoto
                || step.ActionType == ExorciseActionType.MaterializeGhost;
        }

        public static void SkipNextWeaknessStep()
        {
            if (WeaknessManager() == null) { Show("No weakening steps yet (not in a run?)"); return; }
            ExorciseActions step = NextWeaknessStep();
            if (step == null) { Show("Ghost is already weak - catch it!"); return; }

            if (step.ActionType == ExorciseActionType.MaterializeGhost) { AutoIdentify(); return; }

            if (IsHost)
            {
                SignalSystem<ExorciseActionSignal>.Pub(new ExorciseActionSignal { actionType = step.ActionType, itemType = step.Item, phrase = step.Phrase });
                Show("Done: " + DescribeStep(step));
                return;
            }

            // Online: only steps that have a command any player may send
            switch (step.ActionType)
            {
                case ExorciseActionType.SaySomething:
                {
                    PlayerSpeechController speech = Local != null ? Local.GetComponentInChildren<PlayerSpeechController>(true) : null;
                    if (speech == null) { Show("Speech component not found"); return; }
                    string phrase = step.Phrase;
                    speech.Command(speech.CmdSendPhrase, b => b.Write(phrase));
                    Show("Said: \"" + phrase + "\"");
                    return;
                }
                case ExorciseActionType.TakeGhostPhoto:
                {
                    PhotoCameraController cam = UnityEngine.Object.FindObjectsOfType<PhotoCameraController>().FirstOrDefault();
                    if (cam == null) { Show("Needs a photo camera somewhere in the level"); return; }
                    cam.Command(cam.CmdSendVisibleGhosts);
                    Show("Done: ghost photo");
                    return;
                }
                default:
                    Show("Online you have to do this step yourself: " + DescribeStep(step));
                    return;
            }
        }

        // ============================================================ bonus tasks (host)
        public static SessionTask NextBonusTask()
        {
            SessionTaskManager mgr = SessionTaskManager.Instance;
            if (mgr == null || mgr.SessionTasks == null) return null;
            return mgr.SessionTasks.AllItems.FirstOrDefault(t => t.TaskType != SessionTaskType.CatchGhost && !t.IsComplete);
        }

        // Online: photo camera / video camera / advanced thermometer each have a command that
        // finishes ANY session task type, and the server accepts it from any player.
        public static void SkipBonusTask()
        {
            SessionTask task = NextBonusTask();
            if (task == null) { Show("No open bonus task"); return; }
            int missing = Math.Max(1, task.QuantityToComplete - task.CurrQuantity);
            SessionTaskType type = task.TaskType;

            if (IsHost)
            {
                int playerId = Local != null ? Local.NetIdentity().ObjectId : -1;
                for (int i = 0; i < missing; i++)
                    SignalSystem<FinishSessionTaskSignal>.Pub(new FinishSessionTaskSignal { Type = type, PlayerId = playerId });
                Show("Skipped bonus task: " + type);
                return;
            }

            Action<Action<Lidgren.Network.NetBuffer>> send = null;
            PhotoCameraController photo = UnityEngine.Object.FindObjectsOfType<PhotoCameraController>().FirstOrDefault();
            VideoCameraController video = UnityEngine.Object.FindObjectsOfType<VideoCameraController>().FirstOrDefault();
            ThermometrAdvancedController thermo = UnityEngine.Object.FindObjectsOfType<ThermometrAdvancedController>().FirstOrDefault();
            if (photo != null) send = fill => photo.Command(photo.CmdSendFinishSessionTask, fill);
            else if (video != null) send = fill => video.Command(video.CmdSendFinishSessionTask, fill);
            else if (thermo != null) send = fill => thermo.Command(thermo.CmdSendFinishSessionTask, fill);
            if (send == null) { Show("Needs a photo camera, video camera or advanced thermometer in the level"); return; }

            for (int i = 0; i < missing; i++) send(b => b.Write((int)type));
            Show("Skipped bonus task: " + type);
        }

        // ============================================================ sanity (host)
        public static bool LockSanity;
        private static float nextSanityTick;

        public static int FillSanity()
        {
            if (!IsHost) return -1;
            int healed = 0;
            foreach (GameObject player in ObjectSpawner.Players)
            {
                if (player == null) continue;
                PlayerMentalHealth mh = player.GetComponentInChildren<PlayerMentalHealth>(true);
                if (mh == null || mh.Current.Value >= PlayerMentalHealth.MAX_MENTAL_HEALTH) continue;
                mh.Current.Value = PlayerMentalHealth.MAX_MENTAL_HEALTH;
                healed++;
            }
            return healed;
        }

        public static void SanityTick()
        {
            if (!LockSanity || Time.realtimeSinceStartup < nextSanityTick) return;
            nextSanityTick = Time.realtimeSinceStartup + 0.5f;
            FillSanity();
        }

        public static string MySanity()
        {
            GameObject local = Local;
            PlayerMentalHealth mh = local != null ? local.GetComponentInChildren<PlayerMentalHealth>(true) : null;
            return mh != null ? mh.Current.Value + "%" : "-";
        }

        // ============================================================ infinite items (host)
        // Item "used up" state is host-synced: IsUsed flags and use counters. Keep resetting them.
        public static bool InfiniteItems;
        private static float nextItemTick;

        public static void ItemsTick()
        {
            if (!InfiniteItems || !IsHost || Time.realtimeSinceStartup < nextItemTick) return;
            nextItemTick = Time.realtimeSinceStartup + 0.5f;

            foreach (Tool t in Tool.Tools.ToArray())
                if (t != null && t.IsUsed.Value) t.IsUsed.Value = false;

            foreach (var c in UnityEngine.Object.FindObjectsOfType<MedKitSetController>()) c.CurrentChargeSync.Value = 0;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<MentalHealthSetController>()) c.CurrentChargeSync.Value = 0;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<AdrenalinePillsSetController>()) c.CurrentChargeSync.Value = 0;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<ThrowableProjectileToolController>()) c.ChargesUsed.Value = 0;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<IncineratingLightController>()) c.UsedCount.Value = 0;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<PlasmaAbsorberController>()) c.CountUsed.Value = 0;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<PhotoCameraController>()) if (c.ShotsCount.Value < 5) c.ShotsCount.Value = 10;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<PhotoCameraTripodController>()) if (c.ShotsCount.Value < 5) c.ShotsCount.Value = 10;
        }
    }
}
