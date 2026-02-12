using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShotTracker
{
    [Serializable]
    public class ShotData
    {
        public string PlayerName { get; set; }
        public int PlayerNumber { get; set; }
        public string Team { get; set; }

        // Player position (XYZ)
        public float PlayerPositionX { get; set; }
        public float PlayerPositionY { get; set; }
        public float PlayerPositionZ { get; set; }

        // Puck position when shot was taken (XYZ)
        public float PuckPositionX { get; set; }
        public float PuckPositionY { get; set; }
        public float PuckPositionZ { get; set; }

        public float ShotSpeed { get; set; }
        public string ShotType { get; set; } // "Shot", "Shot on Goal", "Goal"
        public string TargetGoal { get; set; }
        public string Timestamp { get; set; }

        // Shot direction (normalized velocity vector)
        public float DirectionX { get; set; }
        public float DirectionY { get; set; }
        public float DirectionZ { get; set; }

        // Goalie hit position (only set if ShotType is "Shot on Goal")
        public float? GoalieHitPositionX { get; set; }
        public float? GoalieHitPositionY { get; set; }
        public float? GoalieHitPositionZ { get; set; }
    }

    [Serializable]
    public class PhysicsSettings
    {
        public float GravityX { get; set; }
        public float GravityY { get; set; }
        public float GravityZ { get; set; }
        public float PuckMass { get; set; }
        public float PuckDrag { get; set; }
        public float PuckAngularDrag { get; set; }
        public float MaxSpeed { get; set; }
        public float MaxAngularSpeed { get; set; }
    }

    [Serializable]
    public class ShotDataCollection
    {
        public string SessionStart { get; set; }
        public PhysicsSettings Physics { get; set; }
        public List<ShotData> Shots { get; set; }

        public ShotDataCollection()
        {
            Shots = new List<ShotData>();
            SessionStart = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        }
    }

    public class PendingShot
    {
        public ShotData Data { get; set; }
        public float DetectionTime { get; set; }
        public bool ReachedGoalZone { get; set; } // Track if puck ever entered the goal zone
        public Vector3? GoalieHitPosition { get; set; } // Track where puck hit goalie
    }
}
