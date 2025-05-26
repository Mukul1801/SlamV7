using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Text;
using Unity.VisualScripting;

public class SafePathPlanner : MonoBehaviour
{
    // References
    public HitPointManager hitPointManager;
    public NavigationManager navigationManager;

    [Header("Path Planning Settings")]
    public float gridSize = 0.5f; // Size of grid cells for navigation
    public float obstacleAvoidanceRadius = 1.5f; // How far to stay from obstacles
    public float pathSegmentLength = 0.5f; // Length of path segments for guidance
    public int maxPathfindingIterations = 1000; // Prevent infinite loops

    [Header("Path Optimization")]
    public bool usePathSmoothing = true;
    public int pathSmoothingPasses = 3;
    public float pathSmoothingStrength = 0.5f;
    public bool useJPSPathfinding = true; // Use Jump Point Search (faster A*)
    public bool useHierarchicalPathfinding = true; // For very large environments

    [Header("Path Analysis")]
    public float significantTurnAngle = 15f; // Degrees threshold to identify significant turns

    [Header("Advanced Settings")]
    public bool useTerrainAnalysis = true; // Consider terrain slopes when planning
    public float maxAllowedSlope = 20f; // Maximum slope angle in degrees
    public float preferredPathWidth = 1.0f; // Prefer wider paths when possible
    public float energyEfficiencyWeight = 0.5f; // 0-1, how much to prefer flat paths

    // The calculated safe path
    private List<Vector3> safePath = new List<Vector3>();
    private int currentPathIndex = 0;

    // Grid for pathfinding - true = walkable, false = obstacle
    private bool[,] navigationGrid;
    private float[,] costGrid; // Additional cost factors (terrain, etc.)
    private Vector3 gridOrigin;
    private int gridWidth, gridHeight;

    // Start and end points
    private Vector3 startPoint, endPoint;

    // Path quality metrics
    private float totalPathLength = 0f;
    private float averageObstacleDistance = 0f;
    private int numberOfTurns = 0;

    // Debug visualization
    private bool showDebugVisualization = true;

    private void Start()
    {
        if (hitPointManager == null)
            hitPointManager = FindObjectOfType<HitPointManager>();

        if (navigationManager == null)
            navigationManager = FindObjectOfType<NavigationManager>();
    }

    public bool PlanSafePath()
    {
        // Clear previous path
        safePath.Clear();
        currentPathIndex = 0;

        // Check if we have waypoints
        if (hitPointManager.poseClassList.Count < 2)
        {
            Debug.LogError("Need at least start and end points for navigation");
            return false;
        }

        // Find start and end points
        PoseClass startPose = hitPointManager.poseClassList.FirstOrDefault(p => p.waypointType == WaypointType.StartPoint);
        PoseClass endPose = hitPointManager.poseClassList.FirstOrDefault(p => p.waypointType == WaypointType.EndPoint);

        // If not explicitly defined, use first and last waypoints
        if (startPose == null) startPose = hitPointManager.poseClassList[0];
        if (endPose == null) endPose = hitPointManager.poseClassList[hitPointManager.poseClassList.Count - 1];

        startPoint = startPose.position;
        endPoint = endPose.position;

        // Create navigation grid
        CreateNavigationGrid();

        // Find path using enhanced pathfinding
        bool pathFound;
        if (useJPSPathfinding)
            pathFound = FindPathJPS(startPoint, endPoint);
        else
            pathFound = FindPath(startPoint, endPoint);

        if (pathFound)
        {
            // Optimize the path
            if (usePathSmoothing)
                SmoothPath();

            // Add path points from collected waypoints
            EnhancePathWithKnownWaypoints();

            // Calculate path metrics
            CalculatePathMetrics();

            Debug.Log("Safe path planned with " + safePath.Count + " segments, length: " +
                     totalPathLength.ToString("F2") + "m, turns: " + numberOfTurns);
            return true;
        }
        else
        {
            Debug.LogError("Failed to find a safe path");
            return false;
        }
    }

    private void CreateNavigationGrid()
    {
        // Find bounds of the area
        Bounds areaBounds = new Bounds(hitPointManager.poseClassList[0].position, Vector3.zero);
        foreach (var pose in hitPointManager.poseClassList)
        {
            areaBounds.Encapsulate(pose.position);
        }

        // Add some margin
        areaBounds.Expand(5.0f);

        // Calculate grid dimensions
        gridOrigin = areaBounds.min;
        gridWidth = Mathf.CeilToInt(areaBounds.size.x / gridSize);
        gridHeight = Mathf.CeilToInt(areaBounds.size.z / gridSize);

        // Initialize grid (all walkable initially)
        navigationGrid = new bool[gridWidth, gridHeight];
        costGrid = new float[gridWidth, gridHeight];

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                navigationGrid[x, z] = true; // Walkable
                costGrid[x, z] = 1.0f; // Base cost
            }
        }

        // Mark obstacles on grid
        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                MarkObstacleOnGrid(pose.position, pose.obstacleWidth > 0 ? pose.obstacleWidth : obstacleAvoidanceRadius);
            }
        }

        // Analyze terrain if enabled
        if (useTerrainAnalysis)
        {
            AnalyzeTerrainForPathPlanning();
        }

        // Create gradient cost fields around obstacles
        CreateGradientCostField();
    }

    private void MarkObstacleOnGrid(Vector3 obstaclePosition, float avoidanceRadius)
    {
        // Convert world position to grid coordinates
        int gridX = Mathf.FloorToInt((obstaclePosition.x - gridOrigin.x) / gridSize);
        int gridZ = Mathf.FloorToInt((obstaclePosition.z - gridOrigin.z) / gridSize);

        // Mark obstacle and surrounding area as non-walkable
        int radius = Mathf.CeilToInt(avoidanceRadius / gridSize);

        for (int x = gridX - radius; x <= gridX + radius; x++)
        {
            for (int z = gridZ - radius; z <= gridZ + radius; z++)
            {
                if (x >= 0 && x < gridWidth && z >= 0 && z < gridHeight)
                {
                    float dist = Vector2.Distance(new Vector2(gridX, gridZ), new Vector2(x, z)) * gridSize;
                    if (dist <= avoidanceRadius)
                    {
                        navigationGrid[x, z] = false; // Not walkable
                    }
                }
            }
        }
    }

    private void CreateGradientCostField()
    {
        // Create gradient cost field around obstacles to prefer paths away from them
        int padding = Mathf.CeilToInt(preferredPathWidth / gridSize);

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                if (navigationGrid[x, z]) // Only process walkable cells
                {
                    // Find nearest non-walkable cell
                    float minDistance = float.MaxValue;

                    // Search in a square area around the current cell
                    for (int nx = Mathf.Max(0, x - padding); nx <= Mathf.Min(gridWidth - 1, x + padding); nx++)
                    {
                        for (int nz = Mathf.Max(0, z - padding); nz <= Mathf.Min(gridHeight - 1, z + padding); nz++)
                        {
                            if (!navigationGrid[nx, nz])
                            {
                                float dist = Vector2.Distance(new Vector2(x, z), new Vector2(nx, nz));
                                minDistance = Mathf.Min(minDistance, dist);
                            }
                        }
                    }

                    // If we found a nearby obstacle, adjust cost
                    if (minDistance != float.MaxValue && minDistance < padding)
                    {
                        // Cost increases as we get closer to obstacles
                        float proximityFactor = 1.0f - (minDistance / padding);
                        costGrid[x, z] += proximityFactor * 2.0f; // Add up to 2.0 additional cost
                    }
                }
            }
        }
    }

    private void AnalyzeTerrainForPathPlanning()
    {
        // Perform raycasts to detect terrain characteristics and slopes
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                if (navigationGrid[x, z]) // Only analyze walkable cells
                {
                    Vector3 worldPos = GridToWorldCoordinates(new Vector2Int(x, z));

                    // Cast ray downward to detect terrain
                    if (Physics.Raycast(worldPos + Vector3.up * 2, Vector3.down, out RaycastHit hitInfo, 5f))
                    {
                        // Check slope
                        float slope = Vector3.Angle(hitInfo.normal, Vector3.up);

                        // If slope is too steep, mark as unwalkable
                        if (slope > maxAllowedSlope)
                        {
                            navigationGrid[x, z] = false;
                        }
                        else if (slope > 5f) // If slope is moderate, increase cost
                        {
                            float slopeFactor = slope / maxAllowedSlope;
                            costGrid[x, z] += slopeFactor * 2.0f * energyEfficiencyWeight;
                        }
                    }
                }
            }
        }
    }

    private bool FindPath(Vector3 start, Vector3 end)
    {
        // A* pathfinding implementation

        // Convert world positions to grid coordinates
        Vector2Int startNode = WorldToGridCoordinates(start);
        Vector2Int endNode = WorldToGridCoordinates(end);

        // Check if positions are valid on grid
        if (!IsValidGridPosition(startNode) || !IsValidGridPosition(endNode))
        {
            Debug.LogError("Start or end position is outside the navigable area");
            return false;
        }

        // If start or end is on an obstacle, find nearest walkable point
        if (!navigationGrid[startNode.x, startNode.y])
        {
            startNode = FindNearestWalkable(startNode);
        }

        if (!navigationGrid[endNode.x, endNode.y])
        {
            endNode = FindNearestWalkable(endNode);
        }

        // Initialize A* algorithm
        var openSet = new List<AStarNode>();
        var closedSet = new HashSet<Vector2Int>();
        var pathParents = new Dictionary<Vector2Int, Vector2Int>();

        var startAStarNode = new AStarNode(startNode, 0, Vector2Int.Distance(startNode, endNode));
        openSet.Add(startAStarNode);

        int iterations = 0;

        while (openSet.Count > 0 && iterations < maxPathfindingIterations)
        {
            iterations++;

            // Get node with lowest F score (A* heuristic)
            var current = openSet.OrderBy(n => n.F).First();
            openSet.Remove(current);

            // If we reached the goal
            if (current.Position == endNode)
            {
                // Reconstruct path
                ReconstructPath(pathParents, current.Position);
                return true;
            }

            closedSet.Add(current.Position);

            // Check neighbors
            foreach (var neighborOffset in GetNeighborOffsets())
            {
                Vector2Int neighbor = current.Position + neighborOffset;

                // Skip if outside grid or not walkable or already processed
                if (!IsValidGridPosition(neighbor) || !navigationGrid[neighbor.x, neighbor.y] || closedSet.Contains(neighbor))
                {
                    continue;
                }

                // Calculate costs with terrain and obstacle proximity factors
                float movementCost = Vector2Int.Distance(current.Position, neighbor);
                float terrainCost = costGrid[neighbor.x, neighbor.y];
                float gCost = current.G + (movementCost * terrainCost);
                float hCost = Vector2Int.Distance(neighbor, endNode);

                // Check if neighbor is already in open set with lower G cost
                var existingNode = openSet.FirstOrDefault(n => n.Position == neighbor);
                if (existingNode != null && existingNode.G <= gCost)
                {
                    continue;
                }

                // Add or update neighbor in open set
                if (existingNode != null)
                {
                    openSet.Remove(existingNode);
                }

                pathParents[neighbor] = current.Position;
                openSet.Add(new AStarNode(neighbor, gCost, hCost));
            }
        }

        Debug.LogWarning("Path not found after " + iterations + " iterations");
        return false;
    }

    private bool FindPathJPS(Vector3 start, Vector3 end)
    {
        // Jump Point Search implementation - more efficient A* variant
        // Convert world positions to grid coordinates
        Vector2Int startNode = WorldToGridCoordinates(start);
        Vector2Int endNode = WorldToGridCoordinates(end);

        // Check if positions are valid on grid
        if (!IsValidGridPosition(startNode) || !IsValidGridPosition(endNode))
        {
            Debug.LogError("Start or end position is outside the navigable area");
            return false;
        }

        // If start or end is on an obstacle, find nearest walkable point
        if (!navigationGrid[startNode.x, startNode.y])
        {
            startNode = FindNearestWalkable(startNode);
        }

        if (!navigationGrid[endNode.x, endNode.y])
        {
            endNode = FindNearestWalkable(endNode);
        }

        // Initialize JPS algorithm
        var openSet = new List<AStarNode>();
        var closedSet = new HashSet<Vector2Int>();
        var pathParents = new Dictionary<Vector2Int, Vector2Int>();

        var startAStarNode = new AStarNode(startNode, 0, ManhattanDistance(startNode, endNode));
        openSet.Add(startAStarNode);

        int iterations = 0;

        while (openSet.Count > 0 && iterations < maxPathfindingIterations)
        {
            iterations++;

            // Get node with lowest F score
            var current = openSet.OrderBy(n => n.F).First();
            openSet.Remove(current);

            // If we reached the goal
            if (current.Position == endNode)
            {
                // Reconstruct path
                ReconstructPath(pathParents, current.Position);
                return true;
            }

            closedSet.Add(current.Position);

            // Identify jump points in all 8 directions
            foreach (var direction in GetNeighborOffsets())
            {
                // Find jump point in this direction
                Vector2Int jumpPoint = FindJumpPoint(current.Position, direction, endNode);

                if (jumpPoint != Vector2Int.zero && !closedSet.Contains(jumpPoint))
                {
                    // Calculate costs
                    float jumpDistance = Vector2Int.Distance(current.Position, jumpPoint);
                    float terrainCost = costGrid[jumpPoint.x, jumpPoint.y];
                    float gCost = current.G + (jumpDistance * terrainCost);
                    float hCost = ManhattanDistance(jumpPoint, endNode);

                    // Check if jump point is already in open set with lower G cost
                    var existingNode = openSet.FirstOrDefault(n => n.Position == jumpPoint);
                    if (existingNode != null && existingNode.G <= gCost)
                    {
                        continue;
                    }

                    // Add or update jump point in open set
                    if (existingNode != null)
                    {
                        openSet.Remove(existingNode);
                    }

                    pathParents[jumpPoint] = current.Position;
                    openSet.Add(new AStarNode(jumpPoint, gCost, hCost));
                }
            }
        }

        Debug.LogWarning("JPS path not found after " + iterations + " iterations");
        return false;
    }

    private Vector2Int FindJumpPoint(Vector2Int current, Vector2Int direction, Vector2Int goal)
    {
        // Implementation of Jump Point Search algorithm
        Vector2Int next = current + direction;

        // Check if next position is valid
        if (!IsValidGridPosition(next) || !navigationGrid[next.x, next.y])
            return Vector2Int.zero;

        // If we found the goal, return it
        if (next == goal)
            return next;

        // Check for forced neighbors in orthogonal directions
        if (direction.x != 0 && direction.y != 0) // Diagonal movement
        {
            // Check horizontal forced neighbors
            if (IsValidGridPosition(new Vector2Int(next.x - direction.x, next.y)) &&
                !navigationGrid[next.x - direction.x, next.y] &&
                IsValidGridPosition(new Vector2Int(next.x - direction.x, next.y + direction.y)) &&
                navigationGrid[next.x - direction.x, next.y + direction.y])
            {
                return next;
            }

            // Check vertical forced neighbors
            if (IsValidGridPosition(new Vector2Int(next.x, next.y - direction.y)) &&
                !navigationGrid[next.x, next.y - direction.y] &&
                IsValidGridPosition(new Vector2Int(next.x + direction.x, next.y - direction.y)) &&
                navigationGrid[next.x + direction.x, next.y - direction.y])
            {
                return next;
            }

            // Recursively search in both straight directions
            Vector2Int horizontalJump = FindJumpPoint(next, new Vector2Int(direction.x, 0), goal);
            Vector2Int verticalJump = FindJumpPoint(next, new Vector2Int(0, direction.y), goal);

            if (horizontalJump != Vector2Int.zero || verticalJump != Vector2Int.zero)
                return next;
        }
        else // Straight movement
        {
            if (direction.x != 0) // Horizontal
            {
                // Check for forced neighbors above and below
                if (IsValidGridPosition(new Vector2Int(next.x, next.y + 1)) &&
                    navigationGrid[next.x, next.y + 1] &&
                    IsValidGridPosition(new Vector2Int(next.x - direction.x, next.y + 1)) &&
                    !navigationGrid[next.x - direction.x, next.y + 1])
                {
                    return next;
                }

                if (IsValidGridPosition(new Vector2Int(next.x, next.y - 1)) &&
                    navigationGrid[next.x, next.y - 1] &&
                    IsValidGridPosition(new Vector2Int(next.x - direction.x, next.y - 1)) &&
                    !navigationGrid[next.x - direction.x, next.y - 1])
                {
                    return next;
                }
            }
            else // Vertical
            {
                // Check for forced neighbors left and right
                if (IsValidGridPosition(new Vector2Int(next.x + 1, next.y)) &&
                    navigationGrid[next.x + 1, next.y] &&
                    IsValidGridPosition(new Vector2Int(next.x + 1, next.y - direction.y)) &&
                    !navigationGrid[next.x + 1, next.y - direction.y])
                {
                    return next;
                }

                if (IsValidGridPosition(new Vector2Int(next.x - 1, next.y)) &&
                    navigationGrid[next.x - 1, next.y] &&
                    IsValidGridPosition(new Vector2Int(next.x - 1, next.y - direction.y)) &&
                    !navigationGrid[next.x - 1, next.y - direction.y])
                {
                    return next;
                }
            }
        }

        // If we didn't find a jump point, continue searching in the same direction
        return FindJumpPoint(next, direction, goal);
    }

    private float ManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private Vector2Int[] GetNeighborOffsets()
    {
        // Include diagonal neighbors for smoother paths
        return new Vector2Int[]
        {
            new Vector2Int(1, 0),   // Right
            new Vector2Int(-1, 0),  // Left
            new Vector2Int(0, 1),   // Up
            new Vector2Int(0, -1),  // Down
            new Vector2Int(1, 1),   // Up-Right
            new Vector2Int(-1, 1),  // Up-Left
            new Vector2Int(1, -1),  // Down-Right
            new Vector2Int(-1, -1)  // Down-Left
        };
    }

    private void ReconstructPath(Dictionary<Vector2Int, Vector2Int> parents, Vector2Int endNode)
    {
        safePath.Clear();

        // Start with end point (real world coordinate)
        safePath.Add(endPoint);

        Vector2Int current = endNode;

        // Follow parent pointers back to start
        while (parents.ContainsKey(current))
        {
            Vector3 worldPos = GridToWorldCoordinates(current);
            safePath.Add(worldPos);
            current = parents[current];
        }

        // Add start point
        safePath.Add(startPoint);

        // Reverse because we reconstructed from end to start
        safePath.Reverse();

        // Create intermediate points for smoother guidance with increased spacing
        List<Vector3> refinedPath = new List<Vector3>();
        float desiredWaypointSpacing = 2.0f; // Increased from original value
        
        for (int i = 0; i < safePath.Count - 1; i++)
        {
            Vector3 current3D = safePath[i];
            Vector3 next3D = safePath[i + 1];

            refinedPath.Add(current3D);

            // Add intermediate points if segments are too long, with proper spacing
            float distance = Vector3.Distance(current3D, next3D);
            int subdivisions = Mathf.FloorToInt(distance / desiredWaypointSpacing);

            for (int j = 1; j < subdivisions; j++)
            {
                float t = (float)j / subdivisions;
                Vector3 intermediate = Vector3.Lerp(current3D, next3D, t);
                refinedPath.Add(intermediate);
            }
        }

        // Add the final destination
        refinedPath.Add(safePath[safePath.Count - 1]);

        // Update the safe path with refined version
        safePath = refinedPath;
    }

    private void SmoothPath()
    {
        if (safePath.Count <= 2) return; // No need to smooth

        for (int pass = 0; pass < pathSmoothingPasses; pass++)
        {
            List<Vector3> smoothedPath = new List<Vector3>();
            smoothedPath.Add(safePath[0]); // Keep start point

            // Apply path smoothing
            for (int i = 1; i < safePath.Count - 1; i++)
            {
                // Get previous, current, and next points
                Vector3 prev = safePath[i - 1];
                Vector3 current = safePath[i];
                Vector3 next = safePath[i + 1];

                // Calculate smoothed position using weighted average
                Vector3 smoothed = current * (1 - pathSmoothingStrength) +
                                  (prev + next) * 0.5f * pathSmoothingStrength;

                // Only use smoothed point if it doesn't go through obstacles
                if (IsPathClear(current, smoothed) && IsPathClear(smoothed, next))
                {
                    smoothedPath.Add(smoothed);
                }
                else
                {
                    smoothedPath.Add(current);
                }
            }

            smoothedPath.Add(safePath[safePath.Count - 1]); // Keep end point
            safePath = smoothedPath;
        }

        // Final pass to reduce redundant points
        RemoveRedundantPoints();
    }

    private void RemoveRedundantPoints()
    {
        if (safePath.Count <= 2) return;

        List<Vector3> simplifiedPath = new List<Vector3>();
        simplifiedPath.Add(safePath[0]);
        
        // Increased minimum distance between waypoints
        float minDistanceBetweenWaypoints = 2.0f;

        for (int i = 1; i < safePath.Count - 1; i++)
        {
            Vector3 prev = simplifiedPath[simplifiedPath.Count - 1];
            Vector3 current = safePath[i];
            Vector3 next = safePath[i + 1];

            // Check if this point creates a significant direction change
            Vector3 dir1 = (current - prev).normalized;
            Vector3 dir2 = (next - current).normalized;

            float angle = Vector3.Angle(dir1, dir2);

            // Keep point if angle is significant or distance is appropriate
            if (angle > significantTurnAngle || Vector3.Distance(prev, current) > minDistanceBetweenWaypoints)
            {
                simplifiedPath.Add(current);
            }
        }

        simplifiedPath.Add(safePath[safePath.Count - 1]);
        safePath = simplifiedPath;
    }

    private void EnhancePathWithKnownWaypoints()
    {
        // Add known safe path points that are near our calculated path
        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.PathPoint)
            {
                // Check if this path point is near our calculated path
                bool isNearPath = false;
                int nearestSegmentIndex = -1;
                float minDistanceToPath = float.MaxValue;

                for (int i = 0; i < safePath.Count - 1; i++)
                {
                    Vector3 pathPoint1 = safePath[i];
                    Vector3 pathPoint2 = safePath[i + 1];

                    float distanceToSegment = DistancePointToLineSegment(pose.position, pathPoint1, pathPoint2);

                    if (distanceToSegment < minDistanceToPath)
                    {
                        minDistanceToPath = distanceToSegment;
                        nearestSegmentIndex = i;
                    }
                }

                // If this point is near our path but not too close to existing points, add it
                if (minDistanceToPath < 2.0f && nearestSegmentIndex >= 0)
                {
                    // Check if it's not too close to existing points
                    bool tooClose = false;
                    foreach (var pathPoint in safePath)
                    {
                        if (Vector3.Distance(pathPoint, pose.position) < 0.5f)
                        {
                            tooClose = true;
                            break;
                        }
                    }

                    if (!tooClose)
                    {
                        // Insert the point after the nearest segment start
                        safePath.Insert(nearestSegmentIndex + 1, pose.position);
                    }
                }
            }
        }
    }

    private bool IsPathClear(Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;
        float distance = direction.magnitude;
        direction.Normalize();

        // Check if a straight line from start to end passes through any obstacles
        RaycastHit hit;
        int obstacleLayer = LayerMask.GetMask("Obstacle");
        if (Physics.Raycast(start, direction, out hit, distance, obstacleLayer))
        {
            return false;
        }

        // Also check grid for obstacles
        int steps = Mathf.CeilToInt(distance / (gridSize * 0.5f));
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector3 point = Vector3.Lerp(start, end, t);
            Vector2Int gridPos = WorldToGridCoordinates(point);

            if (IsValidGridPosition(gridPos) && !navigationGrid[gridPos.x, gridPos.y])
            {
                return false;
            }
        }

        return true;
    }

    private float DistancePointToLineSegment(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
    {
        // Calculate the nearest point on the line segment to the given point
        Vector3 lineDirection = lineEnd - lineStart;
        float lineLength = lineDirection.magnitude;
        lineDirection.Normalize();

        Vector3 pointVector = point - lineStart;
        float dotProduct = Vector3.Dot(pointVector, lineDirection);

        // Clamp to line segment
        dotProduct = Mathf.Clamp(dotProduct, 0f, lineLength);

        Vector3 nearestPoint = lineStart + lineDirection * dotProduct;
        return Vector3.Distance(point, nearestPoint);
    }

    public Vector3 GetNextPathPoint()
    {
        if (safePath.Count == 0 || currentPathIndex >= safePath.Count)
        {
            return endPoint; // Default to end point if path is invalid
        }

        return safePath[currentPathIndex];
    }

    public void UpdateCurrentPathIndex(Vector3 userPosition)
    {
        if (safePath.Count == 0) return;

        // If we're already at the last point, stay there
        if (currentPathIndex >= safePath.Count - 1) return;

        // Get current target point
        Vector3 currentTarget = safePath[currentPathIndex];

        // Check if we've reached it (in 2D, ignoring height)
        Vector3 userPos2D = new Vector3(userPosition.x, 0, userPosition.z);
        Vector3 target2D = new Vector3(currentTarget.x, 0, currentTarget.z);

        float distanceToTarget = Vector3.Distance(userPos2D, target2D);

        // If close enough to current target, move to next one
        if (distanceToTarget < navigationManager.waypointReachedDistance)
        {
            currentPathIndex++;

            // Play sound when reaching intermediate waypoints
            if (currentPathIndex < safePath.Count - 1 && navigationManager.audioSource != null)
            {
                navigationManager.audioSource.PlayOneShot(navigationManager.waypointReachedSound);
            }
        }

        // Look-ahead logic - check if we can skip some points
        else if (currentPathIndex < safePath.Count - 2) // At least 2 points ahead
        {
            for (int i = currentPathIndex + 1; i < currentPathIndex + 3 && i < safePath.Count; i++)
            {
                Vector3 futureTarget = safePath[i];
                Vector3 futureTarget2D = new Vector3(futureTarget.x, 0, futureTarget.z);
                float distanceToFutureTarget = Vector3.Distance(userPos2D, futureTarget2D);

                // If we're heading directly to a future point and we can see it clearly
                if (distanceToFutureTarget < distanceToTarget && IsPathClear(userPosition, futureTarget))
                {
                    currentPathIndex = i;
                    break;
                }
            }
        }
    }

    public string GetUpcomingPathDescription(int fromIndex, int pointsAhead)
    {
        if (safePath.Count <= fromIndex + 1)
            return "";

        StringBuilder description = new StringBuilder();

        // Check for upcoming turns
        for (int i = fromIndex; i < Mathf.Min(fromIndex + pointsAhead, safePath.Count - 1); i++)
        {
            if (i + 2 < safePath.Count)
            {
                Vector3 current = safePath[i];
                Vector3 next = safePath[i + 1];
                Vector3 afterNext = safePath[i + 2];

                Vector3 dir1 = (next - current).normalized;
                Vector3 dir2 = (afterNext - next).normalized;
                dir1.y = 0;
                dir2.y = 0;
                dir1.Normalize();
                dir2.Normalize();

                float turnAngle = Vector3.SignedAngle(dir1, dir2, Vector3.up);

                if (Mathf.Abs(turnAngle) > 30f)
                {
                    float distanceToTurn = 0;
                    for (int j = fromIndex; j < i + 1; j++)
                    {
                        distanceToTurn += Vector3.Distance(safePath[j], safePath[j + 1]);
                    }

                    if (distanceToTurn > 0.5f)
                    {
                        string turnDirection = turnAngle > 0 ? "right" : "left";
                        description.Append("In " + distanceToTurn.ToString("F1") +
                                         " meters, turn " + turnDirection + ". ");
                        break; // Only describe the first major turn
                    }
                }
            }
        }

        // Check for nearby obstacles
        float distanceToObstacle = float.MaxValue;
        string obstacleDirection = "";

        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                // Check if obstacle is near the upcoming path
                for (int i = fromIndex; i < Mathf.Min(fromIndex + pointsAhead, safePath.Count); i++)
                {
                    float distance = DistancePointToLineSegment(
                        pose.position,
                        safePath[i],
                        i + 1 < safePath.Count ? safePath[i + 1] : safePath[i]);

                    if (distance < navigationManager.obstacleWarningDistance && distance < distanceToObstacle)
                    {
                        distanceToObstacle = distance;

                        // Calculate direction relative to path
                        Vector3 pathDir = (safePath[i + 1 < safePath.Count ? i + 1 : i] - safePath[i]).normalized;
                        Vector3 obstacleDir = (pose.position - safePath[i]).normalized;

                        float angle = Vector3.SignedAngle(pathDir, obstacleDir, Vector3.up);
                        obstacleDirection = angle > 0 ? "right" : "left";
                    }
                }
            }
        }

        if (distanceToObstacle < navigationManager.obstacleWarningDistance)
        {
            if (description.Length > 0)
                description.Append(" ");

            description.Append("Caution: obstacle to your " + obstacleDirection +
                            " side, " + distanceToObstacle.ToString("F1") + " meters away.");
        }

        return description.ToString();
    }

    private Vector2Int WorldToGridCoordinates(Vector3 worldPos)
    {
        int x = Mathf.FloorToInt((worldPos.x - gridOrigin.x) / gridSize);
        int z = Mathf.FloorToInt((worldPos.z - gridOrigin.z) / gridSize);
        return new Vector2Int(x, z);
    }

    private Vector3 GridToWorldCoordinates(Vector2Int gridPos)
    {
        float x = gridPos.x * gridSize + gridOrigin.x + gridSize / 2;
        float z = gridPos.y * gridSize + gridOrigin.z + gridSize / 2;
        // Use average height of the waypoints for Y coordinate
        float y = hitPointManager.poseClassList.Count > 0 ?
                  hitPointManager.poseClassList.Average(p => p.position.y) :
                  0f;
        return new Vector3(x, y, z);
    }

    private bool IsValidGridPosition(Vector2Int gridPos)
    {
        return gridPos.x >= 0 && gridPos.x < gridWidth &&
               gridPos.y >= 0 && gridPos.y < gridHeight;
    }

    private Vector2Int FindNearestWalkable(Vector2Int startPos)
    {
        // Breadth-first search to find nearest walkable cell
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        queue.Enqueue(startPos);
        visited.Add(startPos);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            // If we found a walkable cell, return it
            if (IsValidGridPosition(current) && navigationGrid[current.x, current.y])
            {
                return current;
            }

            // Check neighbors
            foreach (var offset in GetNeighborOffsets())
            {
                Vector2Int neighbor = current + offset;

                if (IsValidGridPosition(neighbor) && !visited.Contains(neighbor))
                {
                    queue.Enqueue(neighbor);
                    visited.Add(neighbor);
                }
            }
        }

        // Fallback if no walkable cell found
        Debug.LogError("Could not find any walkable cell near " + startPos);
        return startPos;
    }

    private void CalculatePathMetrics()
    {
        if (safePath.Count < 2)
            return;

        // Calculate total path length
        totalPathLength = 0;
        for (int i = 0; i < safePath.Count - 1; i++)
        {
            totalPathLength += Vector3.Distance(safePath[i], safePath[i + 1]);
        }

        // Calculate average obstacle distance
        float totalObstacleDistance = 0;
        int obstacleCount = 0;

        foreach (var point in safePath)
        {
            float minDistance = float.MaxValue;

            foreach (var pose in hitPointManager.poseClassList)
            {
                if (pose.waypointType == WaypointType.Obstacle)
                {
                    float distance = Vector3.Distance(point, pose.position);
                    minDistance = Mathf.Min(minDistance, distance);
                }
            }

            if (minDistance != float.MaxValue)
            {
                totalObstacleDistance += minDistance;
                obstacleCount++;
            }
        }

        averageObstacleDistance = obstacleCount > 0 ? totalObstacleDistance / obstacleCount : 0;

        // Calculate number of turns
        numberOfTurns = 0;
        for (int i = 1; i < safePath.Count - 1; i++)
        {
            Vector3 prev = safePath[i - 1];
            Vector3 current = safePath[i];
            Vector3 next = safePath[i + 1];

            Vector3 dir1 = (current - prev).normalized;
            Vector3 dir2 = (next - current).normalized;
            dir1.y = 0;
            dir2.y = 0;
            dir1.Normalize();
            dir2.Normalize();

            float angle = Vector3.Angle(dir1, dir2);

            if (angle > 30f) // Significant turn
            {
                numberOfTurns++;
            }
        }
    }

    // Additional getter methods
    public int GetCurrentPathIndex()
    {
        return currentPathIndex;
    }

    public int GetPathCount()
    {
        return safePath.Count;
    }

    // Helper class for A* pathfinding
    private class AStarNode
    {
        public Vector2Int Position { get; private set; }
        public float G { get; private set; } // Cost from start to this node
        public float H { get; private set; } // Estimated cost from this node to goal
        public float F => G + H; // Total cost

        public AStarNode(Vector2Int position, float g, float h)
        {
            Position = position;
            G = g;
            H = h;
        }
    }

    // For visualizing the path (helpful for debugging)
    private void OnDrawGizmos()
    {
        if (!showDebugVisualization || safePath == null || safePath.Count == 0)
            return;

        // Draw the safe path
        Gizmos.color = Color.green;
        for (int i = 0; i < safePath.Count - 1; i++)
        {
            Gizmos.DrawLine(safePath[i], safePath[i + 1]);
            Gizmos.DrawSphere(safePath[i], 0.1f);
        }
        Gizmos.DrawSphere(safePath[safePath.Count - 1], 0.1f);

        // Highlight current target
        if (currentPathIndex < safePath.Count)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(safePath[currentPathIndex], 0.2f);
        }
    }

    // Gets a specific path point by index
    public Vector3 GetPathPointAt(int index)
    {
        if (safePath != null && index >= 0 && index < safePath.Count)
        {
            return safePath[index];
        }

        // Return a default value if the index is invalid
        return Vector3.zero;
    }

    // Creates a spline-based smooth path from the current waypoints
    public void CreateSmoothPath()
    {
        if (safePath.Count < 2) return;

        List<Vector3> originalPath = new List<Vector3>(safePath);
        safePath.Clear();

        // Generate a smoother path with more points
        for (int i = 0; i < originalPath.Count - 1; i++)
        {
            Vector3 start = originalPath[i];
            Vector3 end = originalPath[i + 1];

            // Add the starting point
            safePath.Add(start);

            // Add intermediate points if the distance is significant
            float distance = Vector3.Distance(start, end);
            int subdivisions = Mathf.CeilToInt(distance / pathSegmentLength);

            for (int j = 1; j < subdivisions; j++)
            {
                float t = (float)j / subdivisions;
                Vector3 point;

                // Use cubic interpolation for smoother paths if we have enough points
                if (i > 0 && i < originalPath.Count - 2)
                {
                    Vector3 p0 = i > 0 ? originalPath[i - 1] : start;
                    Vector3 p1 = start;
                    Vector3 p2 = end;
                    Vector3 p3 = i < originalPath.Count - 2 ? originalPath[i + 2] : end;

                    // Catmull-Rom spline
                    point = CatmullRomPoint(p0, p1, p2, p3, t);
                }
                else
                {
                    // Simple linear interpolation for fewer points
                    point = Vector3.Lerp(start, end, t);
                }

                // Make sure our point is still valid
                if (IsPathClear(safePath[safePath.Count - 1], point))
                {
                    safePath.Add(point);
                }
            }
        }

        // Add the final point
        safePath.Add(originalPath[originalPath.Count - 1]);
    }

    // Calculate a point along a Catmull-Rom spline
    private Vector3 CatmullRomPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        // Catmull-Rom spline calculation
        float t2 = t * t;
        float t3 = t2 * t;

        Vector3 point = 0.5f * (
            (2 * p1) +
            (-p0 + p2) * t +
            (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
            (-p0 + 3 * p1 - 3 * p2 + p3) * t3
        );

        return point;
    }


    // Analyzes the upcoming path and returns information about turns and obstacles
    public string GetDetailedPathDescription(int fromIndex, int segmentsAhead)
    {
        if (safePath == null || safePath.Count <= fromIndex + 1)
            return "Path information not available.";

        StringBuilder description = new StringBuilder();

        // Look ahead in the path to identify significant features
        int turnsFound = 0;
        int obstaclesNearby = 0;

        // Check for turns
        for (int i = fromIndex; i < Mathf.Min(fromIndex + segmentsAhead, safePath.Count - 2); i++)
        {
            Vector3 current = safePath[i];
            Vector3 next = safePath[i + 1];
            Vector3 afterNext = safePath[i + 2];

            // Calculate vectors (ignoring height)
            Vector3 dir1 = next - current;
            Vector3 dir2 = afterNext - next;
            dir1.y = 0;
            dir2.y = 0;

            // Only proceed if both vectors have magnitude
            if (dir1.magnitude > 0.1f && dir2.magnitude > 0.1f)
            {
                dir1.Normalize();
                dir2.Normalize();

                // Calculate angle between segments
                float angle = Vector3.SignedAngle(dir1, dir2, Vector3.up);

                // If this is a significant turn
                if (Mathf.Abs(angle) > significantTurnAngle)
                {
                    turnsFound++;

                    // Calculate distance to this turn
                    float distanceToTurn = 0;
                    for (int j = fromIndex; j < i; j++)
                    {
                        distanceToTurn += Vector3.Distance(safePath[j], safePath[j + 1]);
                    }

                    // Only describe the first upcoming turn
                    if (turnsFound == 1)
                    {
                        string turnDirection = angle > 0 ? "right" : "left";
                        description.Append("In ").Append(distanceToTurn.ToString("F1"))
                                .Append(" meters, turn ").Append(turnDirection).Append(". ");
                    }
                }
            }
        }

        // Check for obstacles near the path
        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                // Check if this obstacle is near our upcoming path
                for (int i = fromIndex; i < Mathf.Min(fromIndex + segmentsAhead, safePath.Count - 1); i++)
                {
                    float distance = DistancePointToLineSegment(
                        pose.position,
                        safePath[i],
                        safePath[i + 1]
                    );

                    if (distance < obstacleAvoidanceRadius)
                    {
                        obstaclesNearby++;
                        break;
                    }
                }
            }
        }

        // Add obstacle information
        if (obstaclesNearby > 0)
        {
            if (description.Length > 0)
                description.Append(" ");

            description.Append("Caution: ");

            if (obstaclesNearby == 1)
                description.Append("1 obstacle nearby.");
            else
                description.Append(obstaclesNearby).Append(" obstacles nearby.");
        }

        // Add general path complexity
        if (description.Length == 0)
        {
            if (turnsFound == 0)
                description.Append("Continue straight ahead. Path is clear.");
            else
                description.Append("Path has ").Append(turnsFound).Append(" turns ahead.");
        }

        return description.ToString();
    }


    // Measures the complexity of the path ahead
    public float MeasurePathComplexity(int fromIndex, int segmentsAhead)
    {
        if (safePath == null || safePath.Count <= fromIndex + 1)
            return 0f;

        float complexity = 0f;

        // Sum of angle changes (turns)
        for (int i = fromIndex; i < Mathf.Min(fromIndex + segmentsAhead, safePath.Count - 2); i++)
        {
            Vector3 dir1 = safePath[i + 1] - safePath[i];
            Vector3 dir2 = safePath[i + 2] - safePath[i + 1];
            dir1.y = 0;
            dir2.y = 0;

            if (dir1.magnitude > 0.1f && dir2.magnitude > 0.1f)
            {
                dir1.Normalize();
                dir2.Normalize();

                float angle = Vector3.Angle(dir1, dir2);
                complexity += angle * 0.05f; // Weight angle changes
            }
        }

        // Count nearby obstacles
        int obstaclesNearby = 0;
        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                for (int i = fromIndex; i < Mathf.Min(fromIndex + segmentsAhead, safePath.Count - 1); i++)
                {
                    float distance = DistancePointToLineSegment(
                        pose.position,
                        safePath[i],
                        safePath[i + 1]
                    );

                    if (distance < obstacleAvoidanceRadius * 2f)
                    {
                        obstaclesNearby++;

                        // Add more complexity for very close obstacles
                        if (distance < obstacleAvoidanceRadius)
                        {
                            complexity += 2.0f;
                        }
                        else
                        {
                            complexity += 1.0f;
                        }

                        break;
                    }
                }
            }
        }

        return complexity;
    }

    // Get directions to landmarks along the path
    public string GetLandmarkDirections()
    {
        // Check for named landmarks along the path
        // Assuming some waypoints might have descriptions
        StringBuilder landmarks = new StringBuilder();
        int landmarksFound = 0;

        foreach (var pose in hitPointManager.poseClassList)
        {
            // Only consider waypoints with descriptions
            if (!string.IsNullOrEmpty(pose.description) &&
                pose.waypointType != WaypointType.Obstacle &&
                pose.waypointType != WaypointType.StartPoint)
            {
                // Calculate position on path
                float pathPosition = GetPathPositionForPoint(pose.position);

                if (pathPosition >= 0)
                {
                    // Get distance along path
                    float distanceAlongPath = CalculatePathLength(0, Mathf.FloorToInt(pathPosition));

                    landmarks.Append("At ").Append(distanceAlongPath.ToString("F1"))
                            .Append(" meters along the path: ").Append(pose.description).Append(". ");

                    landmarksFound++;
                }
            }
        }

        if (landmarksFound > 0)
        {
            return landmarks.ToString();
        }
        else
        {
            return "No landmarks found along this path.";
        }
    }

    // Find the relative position of a point along the path (0-based float index)
    private float GetPathPositionForPoint(Vector3 point)
    {
        if (safePath == null || safePath.Count < 2)
            return -1;

        float closestDistance = float.MaxValue;
        int closestSegmentIndex = -1;
        float closestT = 0;

        // Find closest path segment
        for (int i = 0; i < safePath.Count - 1; i++)
        {
            Vector3 segmentStart = safePath[i];
            Vector3 segmentEnd = safePath[i + 1];

            // Find normalized position along segment (0-1)
            float t = GetNormalizedPositionOnSegment(point, segmentStart, segmentEnd);

            // Get actual position
            Vector3 pointOnSegment = Vector3.Lerp(segmentStart, segmentEnd, t);

            // Calculate distance
            float distance = Vector3.Distance(point, pointOnSegment);

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestSegmentIndex = i;
                closestT = t;
            }
        }

        // If we found a close segment and it's reasonably close
        if (closestSegmentIndex >= 0 && closestDistance < 2.0f)
        {
            // Return floating point path position
            return closestSegmentIndex + closestT;
        }

        return -1; // Not found near path
    }

    // Calculate a normalized position (0-1) of a point projected onto a line segment
    private float GetNormalizedPositionOnSegment(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
    {
        Vector3 segment = segmentEnd - segmentStart;
        float segmentLength = segment.magnitude;

        if (segmentLength < 0.01f)
            return 0; // Zero-length segment

        Vector3 segmentDirection = segment / segmentLength;
        Vector3 pointVector = point - segmentStart;

        // Get raw projection
        float projection = Vector3.Dot(pointVector, segmentDirection);

        // Clamp to segment bounds
        return Mathf.Clamp01(projection / segmentLength);
    }

    // Calculate length of a section of the path
    private float CalculatePathLength(int startIndex, int endIndex)
    {
        if (safePath == null || startIndex < 0 || endIndex >= safePath.Count || startIndex > endIndex)
            return 0;

        float length = 0;

        for (int i = startIndex; i < endIndex; i++)
        {
            length += Vector3.Distance(safePath[i], safePath[i + 1]);
        }

        return length;
    }
    
}