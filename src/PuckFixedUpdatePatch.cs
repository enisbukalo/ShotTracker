using System;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace ShotTracker
{
    /// <summary>
    /// Attaches ShotTrackerComponent to the puck during Playing phase.
    /// </summary>
    [HarmonyPatch(typeof(Puck), "FixedUpdate")]
    public class PuckFixedUpdatePatch
    {
        private static bool hasLoggedPatchExecution = false;

        [HarmonyPostfix]
        public static void Postfix(Puck __instance)
        {
            try
            {
                // Log once to confirm this patch is executing
                if (!hasLoggedPatchExecution)
                {
                    Plugin.Log("[PATCH] PuckFixedUpdatePatch.Postfix is executing!");
                    hasLoggedPatchExecution = true;
                }

                // SERVER ONLY: Only run on server/host
                if (!NetworkManager.Singleton.IsServer)
                    return;

                // Null checks to prevent errors during initialization
                if (__instance == null || __instance.gameObject == null)
                    return;

                // Only during active play
                GameManager gameManager = GameManager.Instance;
                if (gameManager == null || gameManager.Phase != GamePhase.Playing)
                    return;

                if (__instance.IsReplay != null && __instance.IsReplay.Value)
                    return;

                // Ensure the tracker component exists on the puck
                if (__instance.GetComponent<ShotTrackerComponent>() == null)
                {
                    __instance.gameObject.AddComponent<ShotTrackerComponent>();
                    Plugin.Log("========================================");
                    Plugin.Log("[SERVER] ShotTrackerComponent added to puck");
                    Plugin.Log("[SERVER] Now tracking shots from all players");
                    Plugin.Log("========================================");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogError($"[PATCH] Error in PuckFixedUpdatePatch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Hooks into Puck.OnCollisionEnter to detect when a stick touches the puck.
    /// Any stick touch clears the pending shot tracking.
    /// </summary>
    [HarmonyPatch(typeof(Puck), "OnCollisionEnter")]
    public class PuckCollisionEnterPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Puck __instance, Collision collision)
        {
            try
            {
                // SERVER ONLY: Only run on server/host
                if (!NetworkManager.Singleton.IsServer)
                    return;

                // Null checks to prevent errors during puck drop
                if (__instance == null || collision == null || collision.gameObject == null)
                    return;

                // Only care about sticks touching the puck
                Stick stick = collision.gameObject.GetComponent<Stick>();
                if (stick == null)
                    return;

                // Only during active play
                GameManager gameManager = GameManager.Instance;
                if (gameManager == null || gameManager.Phase != GamePhase.Playing)
                    return;

                if (__instance.IsReplay != null && __instance.IsReplay.Value)
                    return;

                ShotTrackerComponent tracker = __instance.GetComponent<ShotTrackerComponent>();
                if (tracker != null)
                {
                    // If it's a stick, clear tracking
                    tracker.OnStickTouchedPuck();
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Hooks into Puck.OnCollisionEnter to also detect goalie hits
    /// </summary>
    [HarmonyPatch(typeof(Puck), "OnCollisionEnter")]
    public class PuckCollisionEnterGoalieCheckPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        public static void Postfix(Puck __instance, Collision collision)
        {
            try
            {
                // SERVER ONLY: Only run on server/host
                if (!NetworkManager.Singleton.IsServer)
                    return;

                if (__instance == null || collision == null)
                    return;

                GameManager gameManager = GameManager.Instance;
                if (gameManager == null || gameManager.Phase != GamePhase.Playing)
                    return;

                if (__instance.IsReplay != null && __instance.IsReplay.Value)
                    return;

                // Check if this is a goalie hit
                ShotTrackerComponent tracker = __instance.GetComponent<ShotTrackerComponent>();
                if (tracker != null)
                {
                    tracker.OnPuckHitSomething(collision);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Hooks into Puck.OnCollisionExit to detect when a stick releases the puck.
    /// This is exactly how the game itself detects shots (sets ShotSpeed).
    /// </summary>
    [HarmonyPatch(typeof(Puck), "OnCollisionExit")]
    public class PuckCollisionExitPatch
    {
        private static bool hasLoggedPatchExecution = false;

        [HarmonyPostfix]
        public static void Postfix(Puck __instance, Collision collision)
        {
            try
            {
                // Log once to confirm this patch is executing
                if (!hasLoggedPatchExecution)
                {
                    Plugin.Log("[PATCH] PuckCollisionExitPatch.Postfix is executing!");
                    hasLoggedPatchExecution = true;
                }

                // SERVER ONLY: Only run on server/host
                if (!NetworkManager.Singleton.IsServer)
                    return;

                // Null checks to prevent errors during puck drop
                if (__instance == null || collision == null || collision.gameObject == null)
                    return;

                // Only care about sticks leaving the puck
                Stick stick = collision.gameObject.GetComponent<Stick>();
                if (stick == null || stick.Player == null)
                    return;

                // Only during active play
                GameManager gameManager = GameManager.Instance;
                if (gameManager == null || gameManager.Phase != GamePhase.Playing)
                    return;

                if (__instance.IsReplay != null && __instance.IsReplay.Value)
                    return;

                // Pass to the tracker component
                ShotTrackerComponent tracker = __instance.GetComponent<ShotTrackerComponent>();
                if (tracker != null)
                {
                    tracker.OnStickReleasedPuck(stick);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogError($"[PATCH] Error in PuckCollisionExitPatch: {ex.Message}");
            }
        }
    }
}
