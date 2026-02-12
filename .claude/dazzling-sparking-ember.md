# Shot Tracker Mod - Implementation Plan

## Overview
Create a mod that tracks shots in Puck by detecting when puck velocity changes by over 250%, identifying which player made the shot, and storing shot location data (X, Z coordinates) to a JSON file.

## Key Information Gathered

### Game Architecture
- **Server/Client**: Game uses Unity Netcode. Check `NetworkManager.Singleton.IsServer` for server-side code
- **Puck Access**: `NetworkBehaviourSingleton<PuckManager>.Instance.GetPuck()`
- **Player Access**: `NetworkBehaviourSingleton<PlayerManager>.Instance.GetPlayers()`
- **Puck Properties** (from `PUCK DLL\Puck.cs`):
  - `Rigidbody.linearVelocity` - velocity vector
  - `Speed` - current speed (velocity magnitude)
  - `TouchingStick` - stick currently touching the puck (if any)
  - `transform.position` - position (X, Y, Z)
  - `GetPlayerCollisions()` - gets recent player collisions with timestamps

### Modding Patterns
- **Harmony Patching**: Use `[HarmonyPatch(typeof(Puck), "FixedUpdate")]` to monitor puck every frame
- **Example**: `GoalieSwitcher\GoalieAutoSwitcher\Class1.cs` shows how to patch Puck.FixedUpdate
- **JSON Serialization**: Use `System.Text.Json.JsonSerializer` (see `PuckTracking\ConstantAngleTrack\ModSettings.cs`)
- **Config Path**: Store in `config` folder: `Path.Combine(Path.GetFullPath("."), "config", "filename.json")`

## Implementation Steps

### 1. Create Shot Data Model
Create a `ShotData` class to store shot information:
- `string PlayerName` - name of player who took the shot
- `int PlayerNumber` - player jersey number
- `string Team` - "Blue" or "Red"
- `float PositionX` - X coordinate on rink
- `float PositionZ` - Z coordinate on rink
- `float Velocity` - shot velocity/speed
- `string ShotType` - "Shot" or "Shot on Goal"
- `string TargetGoal` - "Blue" or "Red" (which goal was targeted)
- `string Timestamp` - when shot was taken
- `float VelocityIncrease` - percentage velocity increased

### 2. Create Shot Tracker Component
Create a `ShotTrackerComponent` MonoBehaviour class that:
- **Tracks State**:
  - Previous velocity (to calculate changes)
  - List of shots taken
  - Last save time (for periodic saves)

- **Detects Shots** (in Update or FixedUpdate):
  - Get current puck velocity
  - Calculate velocity change percentage: `(newVelocity - oldVelocity) / oldVelocity * 100`
  - If change > 250%:
    - Check if shot is toward a goal (see "Direction Check" below)
    - If NOT toward goal → ignore (hard pass)
    - If toward goal → proceed to record shot

- **Finds Shooter**:
  - Iterate through all players with spawned sticks
  - Calculate distance from each stick to puck position
  - Find closest stick
  - **If closest stick is within 1m**: Record the shot with that player
  - **If NO stick within 1m**: Wall bounce → ignore, don't record shot

- **Determines Shot Type**:
  - Check next few frames for collision with goalie
  - **If hits goalie**: "Shot on Goal"
  - **If misses**: "Shot"

- **Records Shot Data**:
  - Store player info, position (X, Z), velocity, shot type, target goal, timestamp
  - Add to shot list

- **Saves to JSON**:
  - Save to separate file per game/session
  - Filename: `config/shot_tracker_YYYY-MM-DD_HH-mm-ss.json` (timestamp when server started)
  - Save periodically (every 5 shots or every 30 seconds)
  - Use `System.Text.Json.JsonSerializer.Serialize()` with indented formatting

### 3. Harmony Patch for Puck Monitoring
Create a Harmony patch class:
- **Patch Target**: `[HarmonyPatch(typeof(Puck), "FixedUpdate")]`
- **Patch Type**: `[HarmonyPostfix]` (runs after original method)
- **Functionality**:
  - Check if running on server: `if (!NetworkManager.Singleton.IsServer) return;`
  - Get or create `ShotTrackerComponent` attached to the Puck GameObject
  - Let the component handle shot detection and tracking

### 4. Update Main.cs Plugin
Modify `Main.cs`:
- Apply Harmony patches in `OnEnable()`
- Unpatch in `OnDisable()`
- Add server check: only run on server/host

## File Structure
```
Main.cs                    - Plugin entry point (already exists)
ShotData.cs                - Data model for shot information
ShotTrackerComponent.cs    - MonoBehaviour that tracks shots
PuckFixedUpdatePatch.cs    - Harmony patch for Puck.FixedUpdate
```

## Critical Implementation Details

### Velocity Change Detection
```csharp
// Track previous velocity
float previousVelocity = puck.Speed;

// In next frame
float currentVelocity = puck.Speed;
float velocityChange = currentVelocity - previousVelocity;

// Calculate percentage increase
if (previousVelocity > 0.1f) // Avoid division by zero
{
    float percentIncrease = (velocityChange / previousVelocity) * 100f;
    if (percentIncrease > 250f)
    {
        // Check if toward goal
        if (IsShotTowardGoal(puck))
        {
            // Shot detected!
        }
    }
}
```

### Direction Check (Toward Goal)
```csharp
// Cache goal positions on startup
Vector3 blueGoalPosition;
Vector3 redGoalPosition;

void CacheGoalPositions()
{
    Goal[] goals = GameObject.FindObjectsOfType<Goal>();
    foreach (var goal in goals)
    {
        // Goal has a Team property (from PUCK DLL\Goal.cs)
        // Access via reflection or make assumptions based on position
        if (goal.transform.position.z > 0)
            blueGoalPosition = goal.transform.position;
        else
            redGoalPosition = goal.transform.position;
    }
}

bool IsShotTowardGoal(Puck puck, out string targetGoal)
{
    Vector3 puckPosition = puck.transform.position;
    Vector3 puckVelocity = puck.Rigidbody.linearVelocity;

    // Calculate dot product to see if velocity is toward either goal
    Vector3 toBlueGoal = (blueGoalPosition - puckPosition).normalized;
    Vector3 toRedGoal = (redGoalPosition - puckPosition).normalized;
    Vector3 velocityDirection = puckVelocity.normalized;

    float dotBlue = Vector3.Dot(velocityDirection, toBlueGoal);
    float dotRed = Vector3.Dot(velocityDirection, toRedGoal);

    // If dot product > 0.5 (within ~60 degrees), consider it toward goal
    if (dotBlue > 0.5f)
    {
        targetGoal = "Blue";
        return true;
    }
    if (dotRed > 0.5f)
    {
        targetGoal = "Red";
        return true;
    }

    targetGoal = null;
    return false;
}
```

### Finding the Shooter
```csharp
Player FindClosestPlayer(Puck puck)
{
    Vector3 puckPosition = puck.transform.position;
    Player closestPlayer = null;
    float closestDistance = 1f; // Max 1 meter threshold

    var players = NetworkBehaviourSingleton<PlayerManager>.Instance.GetPlayers();
    foreach (var player in players)
    {
        if (player.Stick == null) continue;

        // Use blade handle position as it's the part that hits puck
        Vector3 stickPosition = player.Stick.BladeHandlePosition;
        float distance = Vector3.Distance(puckPosition, stickPosition);

        if (distance < closestDistance)
        {
            closestDistance = distance;
            closestPlayer = player;
        }
    }

    return closestPlayer; // Returns null if no stick within 1m
}
```

### Detecting Shot on Goal vs Shot
```csharp
// After detecting shot, track it as "pending"
// Monitor puck collisions for next ~1-2 seconds
// Use puck.GetPlayerCollisions() to check for goalie hits

bool CheckIfHitsGoalie(Puck puck)
{
    var playerCollisions = puck.GetPlayerCollisions();
    foreach (var collision in playerCollisions)
    {
        Player player = collision.Key;
        if (player != null && player.Role.Value == PlayerRole.Goalie)
        {
            return true; // Shot on Goal!
        }
    }
    return false; // Just a Shot
}
```

### JSON File Format
```json
{
  "sessionStart": "2026-02-11T10:30:00",
  "shots": [
    {
      "playerName": "Player123",
      "playerNumber": 42,
      "team": "Blue",
      "positionX": 5.2,
      "positionZ": -3.7,
      "velocity": 15.8,
      "velocityIncrease": 287.5,
      "shotType": "Shot on Goal",
      "targetGoal": "Red",
      "timestamp": "2026-02-11T10:30:45"
    }
  ]
}
```

## Key Files Referenced
- `PUCK DLL\Puck.cs` - Puck class with velocity and collision info
- `PUCK DLL\Stick.cs` - Stick class with Player reference
- `PUCK DLL\Player.cs` - Player class with name, number, team
- `PUCK DLL\PuckManager.cs` - Access to puck instances
- `GoalieSwitcher\GoalieAutoSwitcher\Class1.cs` - Example of patching Puck.FixedUpdate
- `PuckTracking\ConstantAngleTrack\ModSettings.cs` - Example of JSON serialization

## Testing & Verification
1. **Build the mod**: Compile the DLL
2. **Install**: Place DLL in mods folder
3. **Start dedicated server**: Run with server flag
4. **Join and shoot**: Take shots in game
5. **Verify detection**: Check logs for shot detection messages
6. **Check JSON file**: Verify `config/shot_tracker_data.json` contains shot data with correct player names and positions

## Updated Requirements from User

1. **Direction matters**: Only count velocity spikes that are toward a goal
   - Hard passes (not toward goal) are ignored
   - Deflections/bounces toward goal DO count as shots

2. **Player identification**: Find closest stick within 1m
   - If no stick within 1m → wall bounce → ignore
   - This automatically filters out wall bounces

3. **Shot classification**:
   - "Shot on Goal" if puck hits goalie after velocity spike
   - "Shot" if toward goal but doesn't hit goalie

4. **Data storage**: Separate file per game session with timestamp

## Potential Issues & Solutions
- **Multiple pucks**: Always track the main game puck, exclude replay pucks (check `puck.IsReplay.Value`)
- **Goal positions**: Need to find exact goal positions from game (check Goal.cs or GoalController.cs)
- **Goalie detection**: May need small delay (1-2 seconds) after shot to determine if it hits goalie
- **Pending shots**: Track shots in temporary state until we know if they hit goalie
- **Save performance**: Save periodically to avoid file I/O overhead
- **Direction threshold**: May need to tune the 60-degree cone (dot product > 0.5) based on testing
