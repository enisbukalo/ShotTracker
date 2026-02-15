using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace ShotTracker
{
    /// <summary>
    /// Lightweight component attached to each puck that tracks pending shots
    /// </summary>
    public class ShotTrackerComponent : MonoBehaviour
    {
        private Puck puck;
        private PendingShot pendingShot = null; // Only track one shot at a time
        private bool hitGoalie = false; // Track if goalie was hit during this shot

        private const float DIRECTION_THRESHOLD = 0.5f;

        private void Start()
        {
            // SERVER ONLY: Component should only exist on server
            if (!NetworkManager.Singleton.IsServer)
            {
                Destroy(this);
                return;
            }

            puck = GetComponent<Puck>();
            if (puck == null)
            {
                Destroy(this);
                return;
            }

            // Ensure manager exists and goals/physics are initialized
            ShotTrackerManager.Instance.InitializeGoals();
            ShotTrackerManager.Instance.InitializePhysics(puck);
        }

        private void FixedUpdate()
        {
            if (puck == null)
                return;

            GameManager gameManager = GameManager.Instance;
            if (gameManager == null || gameManager.Phase != GamePhase.Playing)
                return;

            if (puck.IsReplay != null && puck.IsReplay.Value)
                return;

            UpdatePendingShots();
        }

        public void OnStickReleasedPuck(Stick stick)
        {
            if (stick == null || stick.Player == null)
                return;

            Player player = stick.Player;

            // Null check for network variables
            if (player.Team == null || player.Username == null || player.Number == null)
                return;

            if (!IsShotTowardGoal(out string targetGoal))
                return;

            // CRITICAL: Ensure shot is toward OPPONENT's goal (Blue shoots at Red, Red shoots at Blue)
            string playerTeam = player.Team.Value.ToString();
            if (playerTeam == targetGoal)
                return;

            Vector3 playerPos = Vector3.zero;
            if (player.PlayerBody != null)
            {
                playerPos = player.PlayerBody.transform.position;
            }

            // Get normalized shot direction (magnitude = 1)
            Vector3 shotDirection = Vector3.zero;
            if (puck.Rigidbody != null)
            {
                shotDirection = puck.Rigidbody.linearVelocity.normalized;
            }

            // Capture period information at shot time
            int currentPeriod = 0;
            bool isOvertimePeriod = false;
            GameManager gameManager = GameManager.Instance;
            if (gameManager != null && gameManager.GameState != null)
            {
                currentPeriod = gameManager.GameState.Value.Period;
                isOvertimePeriod = currentPeriod > 3;
            }

            // Look up the goalie defending the target goal
            Player goalie = FindGoalie(targetGoal);
            string goalieName = goalie?.Username?.Value.ToString();
            string goalieHand = goalie?.Handedness?.Value.ToString();

            // Get shooter's handedness
            string playerHand = player.Handedness?.Value.ToString();

            ShotData shotData = new ShotData
            {
                PlayerName = player.Username.Value.ToString(),
                PlayerNumber = player.Number.Value,
                Team = player.Team.Value.ToString(),
                GoalieName = goalieName,
                GoalieHand = goalieHand,
                PlayerHand = playerHand,
                PlayerPositionX = playerPos.x,
                PlayerPositionY = playerPos.y,
                PlayerPositionZ = playerPos.z,
                PuckPositionX = puck.transform.position.x,
                PuckPositionY = puck.transform.position.y,
                PuckPositionZ = puck.transform.position.z,
                ShotSpeed = puck.ShotSpeed,
                ShotType = "Pending",
                TargetGoal = targetGoal,
                Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                DirectionX = shotDirection.x,
                DirectionY = shotDirection.y,
                DirectionZ = shotDirection.z,
                Period = currentPeriod,
                IsOvertime = isOvertimePeriod
            };

            // Replace any existing pending shot
            pendingShot = new PendingShot
            {
                Data = shotData,
                DetectionTime = Time.time
            };
        }

        public void OnStickTouchedPuck()
        {
            // Any stick touch clears the pending shot
            pendingShot = null;
            hitGoalie = false;
        }

        public void OnPuckHitSomething(Collision collision)
        {
            // Check if puck hit a goalie while we're tracking a shot
            if (pendingShot == null || collision == null || collision.gameObject == null)
                return;

            Player player = null;

            // Check if this collision is with a goalie's body
            player = collision.gameObject.GetComponent<PlayerBodyV2>()?.Player;

            // If not a body collision, check if it's a stick collision
            if (player == null)
            {
                Stick stick = collision.gameObject.GetComponent<Stick>();
                if (stick != null)
                {
                    player = stick.Player;
                }
            }

            if (player == null)
                return;

            // Null check for network variables
            if (player.Role == null || player.Team == null)
                return;

            if (player.Role.Value == PlayerRole.Goalie)
            {
                // Verify this goalie is defending the target goal
                string goalieTeam = player.Team.Value.ToString();
                if (goalieTeam == pendingShot.Data.TargetGoal)
                {
                    hitGoalie = true;
                    // Store the puck position when it hit the goalie
                    pendingShot.GoalieHitPosition = puck.transform.position;
                    // Capture the goalie's name and handedness from the actual collision
                    if (player.Username != null)
                    {
                        pendingShot.Data.GoalieName = player.Username.Value.ToString();
                    }
                    if (player.Handedness != null)
                    {
                        pendingShot.Data.GoalieHand = player.Handedness.Value.ToString();
                    }
                }
            }
        }

        private bool IsShotTowardGoal(out string targetGoal)
        {
            targetGoal = null;

            if (puck.Rigidbody == null)
                return false;

            Vector3 puckVelocity = puck.Rigidbody.linearVelocity;
            if (puckVelocity.magnitude < 0.1f)
                return false;

            Vector3 velocityDirection = puckVelocity.normalized;
            Vector3 puckPosition = puck.transform.position;

            // Use manager's goal positions
            Vector3 blueGoal = new Vector3(0, 0, 40.92f);
            Vector3 redGoal = new Vector3(0, 0, -40.92f);

            Vector3 toBlueGoal = (blueGoal - puckPosition).normalized;
            float dotBlue = Vector3.Dot(velocityDirection, toBlueGoal);

            Vector3 toRedGoal = (redGoal - puckPosition).normalized;
            float dotRed = Vector3.Dot(velocityDirection, toRedGoal);

            if (dotBlue > DIRECTION_THRESHOLD)
            {
                targetGoal = "Blue";
                return true;
            }

            if (dotRed > DIRECTION_THRESHOLD)
            {
                targetGoal = "Red";
                return true;
            }

            return false;
        }

        public bool HasPendingShot()
        {
            return pendingShot != null;
        }

        public void UpgradePendingShotToGoal()
        {
            if (pendingShot != null)
            {
                // Copy goalie hit position if it exists
                if (hitGoalie && pendingShot.GoalieHitPosition.HasValue)
                {
                    pendingShot.Data.GoalieHitPositionX = pendingShot.GoalieHitPosition.Value.x;
                    pendingShot.Data.GoalieHitPositionY = pendingShot.GoalieHitPosition.Value.y;
                    pendingShot.Data.GoalieHitPositionZ = pendingShot.GoalieHitPosition.Value.z;
                }

                pendingShot.Data.ShotType = "Goal";
                ShotTrackerManager.Instance.RecordGoal(pendingShot.Data);
                pendingShot = null;
                hitGoalie = false;
            }
        }

        private void UpdatePendingShots()
        {
            if (pendingShot == null || puck == null || puck.transform == null)
                return;

            // Check if puck is in goal zone
            bool currentlyInGoalZone = ShotTrackerManager.Instance.HasPuckReachedGoalZone(
                pendingShot.Data.TargetGoal,
                puck.transform.position
            );

            // Track zone entry
            if (currentlyInGoalZone && !pendingShot.ReachedGoalZone)
            {
                pendingShot.ReachedGoalZone = true;
            }

            // If puck was in zone and now left zone → finalize shot
            if (!currentlyInGoalZone && pendingShot.ReachedGoalZone)
            {
                // Final goalie lookup if initial lookup failed
                if (pendingShot.Data.GoalieName == null)
                {
                    Player goalie = FindGoalie(pendingShot.Data.TargetGoal);
                    if (goalie != null)
                    {
                        if (goalie.Username != null)
                            pendingShot.Data.GoalieName = goalie.Username.Value.ToString();
                        if (goalie.Handedness != null)
                            pendingShot.Data.GoalieHand = goalie.Handedness.Value.ToString();
                    }
                }

                // Check if goalie was hit during this shot
                if (hitGoalie)
                {
                    // Copy goalie hit position to shot data
                    if (pendingShot.GoalieHitPosition.HasValue)
                    {
                        pendingShot.Data.GoalieHitPositionX = pendingShot.GoalieHitPosition.Value.x;
                        pendingShot.Data.GoalieHitPositionY = pendingShot.GoalieHitPosition.Value.y;
                        pendingShot.Data.GoalieHitPositionZ = pendingShot.GoalieHitPosition.Value.z;
                    }
                    pendingShot.Data.ShotType = "Shot on Goal";
                    ShotTrackerManager.Instance.RecordShotOnGoal(pendingShot.Data);
                }
                else
                {
                    pendingShot.Data.ShotType = "Shot";
                    ShotTrackerManager.Instance.RecordShot(pendingShot.Data);
                }
                pendingShot = null;
                hitGoalie = false;
            }
        }

        /// <summary>
        /// Finds the goalie defending the given target goal team.
        /// </summary>
        private Player FindGoalie(string targetGoal)
        {
            try
            {
                PlayerTeam goalTeam = (targetGoal == "Blue") ? PlayerTeam.Blue : PlayerTeam.Red;
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
                Plugin.LogError($"Failed to find goalie for {targetGoal}: {ex.Message}");
            }
            return null;
        }

    }
}
