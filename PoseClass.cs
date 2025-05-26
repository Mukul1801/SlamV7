using UnityEngine;

[System.Serializable]
public class PoseClass
{
    public string trackingId;
    public string distance;
    public Vector3 position;
    public Quaternion rotation;
    public WaypointType waypointType = WaypointType.PathPoint; // Default to path point
    public string description; // Optional description of the waypoint

    // Optional data for environment mapping
    public float obstacleHeight = 0f;
    public float obstacleWidth = 0f;
    public float obstacleSeverity = 0f; // 0-1 scale, 1 being completely impassable
}

public enum WaypointType
{
    StartPoint,   // Starting point of navigation
    EndPoint,     // Destination point
    PathPoint,    // Safe navigation point
    Obstacle      // Point to avoid
}