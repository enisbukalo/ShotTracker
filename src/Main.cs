using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

namespace ShotTracker
{
    public class Plugin : IPuckMod
    {
        public static string MOD_NAME = "ShotTracker";
        public static string MOD_GUID = "ShotTracker";

        private static readonly Harmony harmony = new Harmony(MOD_GUID);

        // Static constructor - runs when the class is first loaded
        static Plugin()
        {
            Debug.Log("========================================");
            Debug.Log("[ShotTracker] STATIC CONSTRUCTOR - Class is being loaded!");
            Debug.Log($"[ShotTracker] Assembly location: {typeof(Plugin).Assembly.Location}");
            Debug.Log("========================================");
        }

        // Constructor - runs when instance is created
        public Plugin()
        {
            Debug.Log("========================================");
            Debug.Log("[ShotTracker] INSTANCE CONSTRUCTOR - Plugin instance created!");
            Debug.Log("========================================");
        }

        public bool OnEnable()
        {
            try
            {
                Log("========================================");
                Log("OnEnable() called - Starting mod initialization...");
                Log($"Assembly: {typeof(Plugin).Assembly.FullName}");
                Log($"Assembly Location: {typeof(Plugin).Assembly.Location}");
                Log("========================================");

                Log("Applying Harmony patches...");
                harmony.PatchAll();

                // Verify patches were applied
                var patches = Harmony.GetAllPatchedMethods();
                int patchCount = 0;
                foreach (var method in patches)
                {
                    var info = Harmony.GetPatchInfo(method);
                    if (info.Owners.Contains(MOD_GUID))
                    {
                        patchCount++;
                        Log($"  Patched: {method.DeclaringType?.Name}.{method.Name}");
                    }
                }
                Log($"Total patches applied: {patchCount}");

                Log("========================================");
                Log("Shot Tracker mod enabled - SERVER-SIDE ONLY");
                Log("Harmony patches applied - will only track shots on server/host");
                Log("========================================");

                // Log network status when available
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

                Log("Mod initialization complete!");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to enable mod: {ex.Message}");
                LogError($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                if (Unity.Netcode.NetworkManager.Singleton != null)
                {
                    bool isServer = Unity.Netcode.NetworkManager.Singleton.IsServer;
                    bool isHost = Unity.Netcode.NetworkManager.Singleton.IsHost;
                    bool isClient = Unity.Netcode.NetworkManager.Singleton.IsClient;

                    Log("========================================");
                    Log($"NETWORK STATUS:");
                    Log($"  IsServer: {isServer}");
                    Log($"  IsHost: {isHost}");
                    Log($"  IsClient: {isClient}");
                    if (isServer)
                    {
                        Log("  >>> SHOT TRACKING ACTIVE <<<");
                    }
                    else
                    {
                        Log("  >>> SHOT TRACKING DISABLED (client only) <<<");
                    }
                    Log("========================================");
                }
            }
            catch { }
        }

        public bool OnDisable()
        {
            try
            {
                harmony.UnpatchSelf();
                Log("Shot Tracker mod disabled");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to disable mod: {ex.Message}");
                return false;
            }
        }

        public static void Log(string message)
        {
            Debug.Log((object)("[" + MOD_NAME + "] " + message));
        }

        public static void LogError(string message)
        {
            Debug.LogError((object)("[" + MOD_NAME + "] " + message));
        }
    }
}
