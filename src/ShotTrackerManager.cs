using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace ShotTracker
{
    /// <summary>
    /// Singleton manager that persists across the entire game session
    /// </summary>
    public class ShotTrackerManager : MonoBehaviour
    {
        private static ShotTrackerManager instance;
        public static ShotTrackerManager Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject go = new GameObject("ShotTrackerManager");
                    instance = go.AddComponent<ShotTrackerManager>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        private ShotDataCollection shotCollection;
        private string filePath;
        private bool isInitialized = false;

        private Vector3 blueGoalPosition;
        private Vector3 redGoalPosition;
        private bool goalsInitialized = false;

        private const float GOAL_RADIUS = 3.5f;
        private float lastGoalTime = 0f;
        private const float GOAL_COOLDOWN = 15f; // Prevent duplicate goal events within 15 seconds

        // Track games: each game gets its own file

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);

            // SERVER ONLY: Only initialize on server/host
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                Plugin.Log("========================================");
                Plugin.Log("[SERVER] ShotTrackerManager initializing on SERVER");
                Plugin.Log("========================================");

                InitializeSession();

                try
                {
                    MonoBehaviourSingleton<EventManager>.Instance.AddEventListener("Event_Server_OnPuckEnterTeamGoal", OnGoalScored);
                    MonoBehaviourSingleton<EventManager>.Instance.AddEventListener("Event_OnGamePhaseChanged", OnGamePhaseChanged);
                    Plugin.Log("[SERVER] Event listeners registered successfully");
                }
                catch (Exception ex)
                {
                    Plugin.LogError($"[SERVER] Failed to add event listeners: {ex.Message}");
                }
            }
            else
            {
                Plugin.Log("========================================");
                Plugin.Log("[CLIENT] ShotTracker running on CLIENT - tracking DISABLED");
                Plugin.Log("[CLIENT] Shot tracking only works on server/host");
                Plugin.Log("========================================");
            }
        }

        private void InitializeSession()
        {
            if (isInitialized)
                return;

            isInitialized = true;

            // Start the first game
            StartNewGame();
            Plugin.Log($"[SERVER SESSION] Shot Tracker initialized");
        }

        private void StartNewGame()
        {
            // Save any existing game data before starting new game
            if (shotCollection != null && shotCollection.Shots.Count > 0)
            {
                SaveToFile();
                Plugin.Log($"[SERVER] Previous game finalized with {shotCollection.Shots.Count} shots");
            }

            // Create new collection for the new game
            shotCollection = new ShotDataCollection();

            // Capture server name
            try
            {
                var serverManager = NetworkBehaviourSingleton<ServerManager>.Instance;
                if (serverManager != null)
                {
                    shotCollection.ServerName = serverManager.Server.Name.ToString();
                    Plugin.Log($"[SERVER] Server name: {shotCollection.ServerName}");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Failed to get server name: {ex.Message}");
            }

            // Generate timestamp for this game (when it starts)
            string gameTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string configPath = Path.Combine(Path.GetFullPath("."), "config");
            string shotTrackerPath = Path.Combine(configPath, "ShotTracker");

            if (!Directory.Exists(shotTrackerPath))
            {
                Directory.CreateDirectory(shotTrackerPath);
            }

            filePath = Path.Combine(shotTrackerPath, $"shot_tracker_{gameTimestamp}.json");
            Plugin.Log($"[SERVER] Started tracking new game: {filePath}");
        }

        private void OnDestroy()
        {
            MonoBehaviourSingleton<EventManager>.Instance.RemoveEventListener("Event_Server_OnPuckEnterTeamGoal", OnGoalScored);
            MonoBehaviourSingleton<EventManager>.Instance.RemoveEventListener("Event_OnGamePhaseChanged", OnGamePhaseChanged);
            SaveToFile();
        }

        public void InitializeGoals()
        {
            if (goalsInitialized)
                return;

            try
            {
                Goal[] goals = FindObjectsByType<Goal>(FindObjectsSortMode.None);
                if (goals.Length >= 2)
                {
                    foreach (var goal in goals)
                    {
                        if (goal.transform.position.z > 0)
                            blueGoalPosition = goal.transform.position;
                        else
                            redGoalPosition = goal.transform.position;
                    }
                    goalsInitialized = true;
                    Plugin.Log($"[SESSION] Goals initialized - Blue: {blueGoalPosition}, Red: {redGoalPosition}");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Failed to initialize goals: {ex.Message}");
            }
        }

        public void InitializePhysics(Puck puck)
        {
            if (shotCollection.Physics != null || puck == null || puck.Rigidbody == null)
                return;

            try
            {
                Vector3 gravity = Physics.gravity;
                shotCollection.Physics = new PhysicsSettings
                {
                    GravityX = gravity.x,
                    GravityY = gravity.y,
                    GravityZ = gravity.z,
                    PuckMass = puck.Rigidbody.mass,
                    PuckDrag = puck.Rigidbody.linearDamping,
                    PuckAngularDrag = puck.Rigidbody.angularDamping,
                    MaxSpeed = puck.MaxSpeed,
                    MaxAngularSpeed = puck.MaxAngularSpeed
                };
                Plugin.Log($"[SESSION] Physics initialized - Gravity: {gravity}, Mass: {puck.Rigidbody.mass}, Damping: {puck.Rigidbody.linearDamping}");
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Failed to initialize physics: {ex.Message}");
            }
        }

        public void RecordShot(ShotData shotData)
        {
            shotCollection.Shots.Add(shotData);
            SaveToFile();
            Plugin.Log($"[SERVER] SHOT: {shotData.PlayerName} ({shotData.Team}#{shotData.PlayerNumber}) -> {shotData.TargetGoal} | Goalie: {shotData.GoalieName ?? "N/A"} | {shotData.ShotSpeed:F1} m/s | Period {shotData.Period}{(shotData.IsOvertime ? " (OT)" : "")}");
        }

        public void RecordShotOnGoal(ShotData shotData)
        {
            shotCollection.Shots.Add(shotData);
            SaveToFile();
            Plugin.Log($"[SERVER] SHOT ON GOAL: {shotData.PlayerName} ({shotData.Team}#{shotData.PlayerNumber}) -> {shotData.TargetGoal} | Goalie: {shotData.GoalieName ?? "N/A"} | {shotData.ShotSpeed:F1} m/s | Period {shotData.Period}{(shotData.IsOvertime ? " (OT)" : "")}");
        }

        public void RecordGoal(ShotData shotData)
        {
            shotCollection.Shots.Add(shotData);
            SaveToFile();
            Plugin.Log($"[SERVER] GOAL: {shotData.PlayerName} ({shotData.Team}#{shotData.PlayerNumber}) -> {shotData.TargetGoal} | Goalie: {shotData.GoalieName ?? "N/A"} | {shotData.ShotSpeed:F1} m/s | Period {shotData.Period}{(shotData.IsOvertime ? " (OT)" : "")}");
        }

        public bool HasPuckReachedGoalZone(string targetGoal, Vector3 puckPosition)
        {
            if (!goalsInitialized)
                return false;

            Vector3 goalCenter = (targetGoal == "Red") ? redGoalPosition : blueGoalPosition;
            float distanceToGoal = Vector3.Distance(puckPosition, goalCenter);
            return distanceToGoal <= GOAL_RADIUS;
        }

        private void OnGamePhaseChanged(Dictionary<string, object> message)
        {
            try
            {
                // Check if this is the first face-off of a new game
                if (message.ContainsKey("isFirstFaceOff") && (bool)message["isFirstFaceOff"])
                {
                    Plugin.Log($"[SERVER] New game detected (first face-off). Starting new tracking file.");
                    StartNewGame();
                }
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Error handling game phase change: {ex.Message}");
            }
        }

        private void OnGoalScored(Dictionary<string, object> message)
        {
            try
            {
                // Prevent duplicate goal events (replay pucks, multiple triggers, etc.)
                if (Time.time - lastGoalTime < GOAL_COOLDOWN)
                    return;

                Puck goalPuck = (Puck)message["puck"];

                // Ignore replay pucks
                if (goalPuck.IsReplay != null && goalPuck.IsReplay.Value)
                    return;

                // Try to upgrade pending shot first (this will have correct puck position from when stick released)
                ShotTrackerComponent tracker = goalPuck.GetComponent<ShotTrackerComponent>();
                if (tracker != null && tracker.HasPendingShot())
                {
                    tracker.UpgradePendingShotToGoal();
                    lastGoalTime = Time.time;
                    return;
                }

                // Fallback: If no pending shot exists, create goal record from current state
                // (This shouldn't normally happen, but handles edge cases)
                PlayerTeam scoringTeam = (PlayerTeam)message["team"];

                var playerCollisions = goalPuck.GetPlayerCollisions();
                if (playerCollisions.Count == 0)
                    return;

                Player goalScorer = null;
                for (int i = playerCollisions.Count - 1; i >= 0; i--)
                {
                    var player = playerCollisions[i].Key;
                    PlayerTeam attackingTeam = (scoringTeam == PlayerTeam.Blue) ? PlayerTeam.Red : PlayerTeam.Blue;
                    if (player != null && player.Team.Value == attackingTeam)
                    {
                        goalScorer = player;
                        break;
                    }
                }

                if (goalScorer == null)
                    return;

                Vector3 playerPos = Vector3.zero;
                if (goalScorer.PlayerBody != null)
                {
                    playerPos = goalScorer.PlayerBody.transform.position;
                }

                // Get normalized shot direction (magnitude = 1)
                Vector3 shotDirection = Vector3.zero;
                if (goalPuck.Rigidbody != null)
                {
                    shotDirection = goalPuck.Rigidbody.linearVelocity.normalized;
                }

                // Capture period information at goal time
                int currentPeriod = 0;
                bool isOvertimePeriod = false;
                GameManager gameManager = GameManager.Instance;
                if (gameManager != null && gameManager.GameState != null)
                {
                    currentPeriod = gameManager.GameState.Value.Period;
                    isOvertimePeriod = currentPeriod > 3;
                }

                // The team whose goal was entered IS the target goal
                string targetGoalStr = scoringTeam.ToString();

                // Look up the goalie defending the scored-on goal
                Player goalie = FindGoalie(scoringTeam);
                string goalieName = goalie?.Username?.Value.ToString();
                string goalieHand = goalie?.Handedness?.Value.ToString();

                // Get scorer's handedness
                string playerHand = goalScorer.Handedness?.Value.ToString();

                ShotData goalData = new ShotData
                {
                    PlayerName = goalScorer.Username.Value.ToString(),
                    PlayerNumber = goalScorer.Number.Value,
                    Team = goalScorer.Team.Value.ToString(),
                    GoalieName = goalieName,
                    GoalieHand = goalieHand,
                    PlayerHand = playerHand,
                    PlayerPositionX = playerPos.x,
                    PlayerPositionY = playerPos.y,
                    PlayerPositionZ = playerPos.z,
                    PuckPositionX = goalPuck.transform.position.x,
                    PuckPositionY = goalPuck.transform.position.y,
                    PuckPositionZ = goalPuck.transform.position.z,
                    ShotSpeed = goalPuck.ShotSpeed,
                    ShotType = "Goal",
                    TargetGoal = targetGoalStr,
                    Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    DirectionX = shotDirection.x,
                    DirectionY = shotDirection.y,
                    DirectionZ = shotDirection.z,
                    Period = currentPeriod,
                    IsOvertime = isOvertimePeriod
                };

                shotCollection.Shots.Add(goalData);
                lastGoalTime = Time.time;
                SaveToFile();
                Plugin.Log($"[SERVER] GOAL (fallback): {goalData.PlayerName} ({goalData.Team}#{goalData.PlayerNumber}) -> {targetGoalStr} | Goalie: {goalData.GoalieName ?? "N/A"} | {goalData.ShotSpeed:F1} m/s | Period {goalData.Period}{(goalData.IsOvertime ? " (OT)" : "")}");
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Error recording goal: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds the goalie defending the given team's goal.
        /// </summary>
        private Player FindGoalie(PlayerTeam goalTeam)
        {
            try
            {
                var players = NetworkBehaviourSingleton<PlayerManager>.Instance.GetPlayersByTeam(goalTeam);
                foreach (var p in players)
                {
                    if (p != null && p.Role != null && p.Role.Value == PlayerRole.Goalie)
                    {
                        return p;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Failed to find goalie for {goalTeam}: {ex.Message}");
            }
            return null;
        }

        private void SaveToFile()
        {
            if (shotCollection.Shots.Count == 0)
                return;

            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string json = JsonSerializer.Serialize(shotCollection, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Failed to save: {ex.Message}");
            }
        }
    }
}
