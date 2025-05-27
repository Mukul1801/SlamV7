using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;
using TMPro;

[System.Serializable]
public class MapMetadata
{
    public string mapName;
    public string creationDate;
    public string lastModifiedDate;
    public Vector3 startPointPosition;
    public Vector3 endPointPosition;
    public int waypointCount;
    public int obstacleCount;
    public float totalPathLength;
    public List<string> landmarks = new List<string>();
    public string mapDescription;
    public List<EnvironmentAnchor> environmentAnchors = new List<EnvironmentAnchor>();
}

[System.Serializable]
public class EnvironmentAnchor
{
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;
    public string type; // "Floor", "Wall", "Ceiling", "Object"
    public string description;
    public float confidence; // 0-1 confidence in this anchor
}

[System.Serializable]
public class EnhancedMap
{
    public MapMetadata metadata = new MapMetadata();
    public List<PoseClass> waypoints = new List<PoseClass>();
    public byte[] featurePointsHash; // Used for environment validation
}

public class NavigationEnhancer : MonoBehaviour
{
    // References to existing components
    public HitPointManager hitPointManager;
    public NavigationManager navigationManager;
    public TextToSpeech textToSpeech;
    public AccessibilityManager accessibilityManager;

    // NEW: Integration with Enhanced3DMapManager
    private Enhanced3DMapManager enhanced3DMapManager;

    // AR components
    private ARSession arSession;
    private ARPointCloudManager pointCloudManager;
    private ARPlaneManager planeManager;

    // Map management
    private EnhancedMap currentMap = new EnhancedMap();
    private bool isMapValidated = false;
    private Dictionary<string, EnvironmentAnchor> detectedAnchors = new Dictionary<string, EnvironmentAnchor>();

    // UI references
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI pathInfoText;
    public GameObject validationPanel;
    public Button validateButton;

    // Accessible UI elements
    public AudioSource uiAudioSource;
    public AudioClip confirmSound;
    public AudioClip errorSound;
    public AudioClip successSound;

    // Navigation enhancement
    private Vector3 lastAnnouncementPosition;
    private float lastEnvironmentAnnouncement = 0f;
    private Dictionary<int, bool> announcedTurns = new Dictionary<int, bool>();
    private System.Random random = new System.Random();

    // Settings
    [Range(0f, 1f)]
    public float mapValidationThreshold = 0.6f;
    public float environmentScanRadius = 5.0f;
    public float minDistanceBetweenAnnouncements = 3.0f;
    public float upcomingTurnAnnouncementDistance = 3.0f;
    public float environmentAnnouncementInterval = 30f;

    // NEW: Enhanced integration settings
    [Header("Enhanced Integration")]
    public bool preferEnhanced3DManager = true;
    public bool enableSmartEnvironmentDesc = true;
    public bool enablePredictiveGuidance = true;

    // Speech patterns for more natural navigation
    public List<string> straightPhrases = new List<string> {
        "Continue straight ahead",
        "Keep going forward",
        "Go straight",
        "Walk straight",
        "Continue forward"
    };

    public List<string> rightTurnPhrases = new List<string> {
        "Turn right",
        "Take a right",
        "Make a right turn",
        "Go right",
        "Turn to your right"
    };

    public List<string> leftTurnPhrases = new List<string> {
        "Turn left",
        "Take a left",
        "Make a left turn",
        "Go left",
        "Turn to your left"
    };

    public List<string> obstacleWarningPhrases = new List<string> {
        "Caution, obstacle ahead",
        "Watch out for obstacle",
        "Careful, obstacle in the path",
        "Be aware, obstacle nearby",
        "Obstacle detected"
    };

    // NEW: Enhanced environmental descriptions
    public List<string> openAreaPhrases = new List<string> {
        "You're in an open area",
        "Wide open space around you",
        "Clear area with plenty of room",
        "Open environment detected"
    };

    public List<string> narrowPathPhrases = new List<string> {
        "Narrow path ahead",
        "Confined space",
        "Path narrows here",
        "Tight passage"
    };

    void Start()
    {
        // Find references if not set
        if (hitPointManager == null)
            hitPointManager = FindObjectOfType<HitPointManager>();

        if (navigationManager == null)
            navigationManager = FindObjectOfType<NavigationManager>();

        if (textToSpeech == null && navigationManager != null)
            textToSpeech = navigationManager.textToSpeech;

        if (accessibilityManager == null)
            accessibilityManager = FindObjectOfType<AccessibilityManager>();

        // NEW: Find Enhanced3DMapManager
        enhanced3DMapManager = FindObjectOfType<Enhanced3DMapManager>();

        // Get AR components
        arSession = FindObjectOfType<ARSession>();
        pointCloudManager = FindObjectOfType<ARPointCloudManager>();
        planeManager = FindObjectOfType<ARPlaneManager>();

        // Set up UI listeners
        if (validateButton != null)
            validateButton.onClick.AddListener(ValidateCurrentMap);

        // Initialize state
        lastAnnouncementPosition = Vector3.zero;

        // Hook into navigation events
        if (navigationManager != null)
        {
            // Add method to be called periodically during navigation
            StartCoroutine(NavigationEnhancementUpdater());
        }

        // NEW: Initialize integration with Enhanced3DMapManager
        if (enhanced3DMapManager != null && preferEnhanced3DManager)
        {
            Debug.Log("NavigationEnhancer: Integrated with Enhanced3DMapManager");
        }
    }

    #region Map Management

    public void StartNewMap()
    {
        // NEW: Prefer Enhanced3DMapManager if available
        if (enhanced3DMapManager != null && preferEnhanced3DManager)
        {
            // Let Enhanced3DMapManager handle this
            return;
        }

        currentMap = new EnhancedMap();
        currentMap.metadata.creationDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        isMapValidated = false;

        // Clear existing waypoints
        if (hitPointManager != null)
        {
            hitPointManager.ClearCurrentWaypoints();
            hitPointManager.poseClassList.Clear();
        }

        // Start environment scanning
        StartCoroutine(ScanEnvironmentAnchors());
    }

    public IEnumerator ScanEnvironmentAnchors()
    {
        detectedAnchors.Clear();

        // Initial delay to let AR systems initialize
        yield return new WaitForSeconds(2.0f);

        int scanIterations = 0;
        int maxScanIterations = 10;

        while (scanIterations < maxScanIterations)
        {
            scanIterations++;

            // Detect major planes in the environment
            DetectMajorSurfaces();

            // Detect distinctive features
            DetectDistinctiveFeatures();

            // Update UI
            float scanProgress = (float)scanIterations / maxScanIterations;

            if (statusText != null)
                statusText.text = $"Environment scan: {(scanProgress * 100):F0}% complete\nAnchors: {detectedAnchors.Count}";

            // Speak progress periodically
            if (scanIterations == 1 || scanIterations == 5 || scanIterations == maxScanIterations)
                SpeakMessage($"Environment scan {(scanProgress * 100):F0}% complete. {detectedAnchors.Count} landmarks detected.");

            yield return new WaitForSeconds(1.0f);
        }

        // Add anchors to current map
        currentMap.metadata.environmentAnchors = detectedAnchors.Values.ToList();

        // Speak completion
        SpeakMessage("Environment scan complete. Ready to create path.");

        // Update UI
        if (statusText != null)
            statusText.text = "Environment scan complete. Ready to create path.";
    }

    private void DetectMajorSurfaces()
    {
        if (planeManager == null)
            return;

        foreach (ARPlane plane in planeManager.trackables)
        {
            // Only add larger planes
            if (plane.size.x * plane.size.y < 1.0f)
                continue;

            string anchorId = $"Plane_{plane.trackableId}";

            // Skip if we already have this plane
            if (detectedAnchors.ContainsKey(anchorId))
                continue;

            // Determine plane type
            string planeType = "Unknown";
            switch (plane.alignment)
            {
                case PlaneAlignment.HorizontalUp:
                    planeType = "Floor";
                    break;
                case PlaneAlignment.HorizontalDown:
                    planeType = "Ceiling";
                    break;
                case PlaneAlignment.Vertical:
                    planeType = "Wall";
                    break;
            }

            // Create environment anchor for this plane
            EnvironmentAnchor anchor = new EnvironmentAnchor
            {
                position = plane.center,
                rotation = plane.transform.rotation,
                scale = new Vector3(plane.size.x, 0.01f, plane.size.y),
                type = planeType,
                description = $"{planeType} {plane.size.x:F1}x{plane.size.y:F1}m",
                confidence = Mathf.Clamp01(plane.size.x * plane.size.y / 10f) // Larger planes get higher confidence
            };

            detectedAnchors.Add(anchorId, anchor);
        }
    }

    private void DetectDistinctiveFeatures()
    {
        // If we have point cloud data, find distinctive clusters
        if (pointCloudManager != null && pointCloudManager.trackables.count > 0)
        {
            // Get point cloud points
            List<Vector3> points = new List<Vector3>();

            // Iterate through all trackables
            foreach (var pointCloud in pointCloudManager.trackables)
            {
                if (pointCloud.positions.HasValue)
                {
                    foreach (var position in pointCloud.positions.Value)
                    {
                        points.Add(position);
                    }
                }
            }

            // Find clusters of points that might be objects
            if (points.Count > 20)
            {
                // Simple clustering by grid
                Dictionary<Vector3Int, List<Vector3>> grid = new Dictionary<Vector3Int, List<Vector3>>();
                float cellSize = 0.5f;

                foreach (var point in points)
                {
                    Vector3Int cell = new Vector3Int(
                        Mathf.FloorToInt(point.x / cellSize),
                        Mathf.FloorToInt(point.y / cellSize),
                        Mathf.FloorToInt(point.z / cellSize)
                    );

                    if (!grid.ContainsKey(cell))
                        grid[cell] = new List<Vector3>();

                    grid[cell].Add(point);
                }

                // Find dense clusters
                foreach (var cell in grid.Keys)
                {
                    if (grid[cell].Count > 15) // Dense enough to be interesting
                    {
                        // Calculate centroid
                        Vector3 centroid = Vector3.zero;
                        foreach (var point in grid[cell])
                            centroid += point;

                        centroid /= grid[cell].Count;

                        // Check if we already have an anchor near this position
                        bool tooClose = false;
                        foreach (var existingAnchor in detectedAnchors.Values)
                        {
                            if (Vector3.Distance(existingAnchor.position, centroid) < 2.0f)
                            {
                                tooClose = true;
                                break;
                            }
                        }

                        if (!tooClose)
                        {
                            string anchorId = $"Feature_{cell.x}_{cell.y}_{cell.z}";
                            EnvironmentAnchor anchor = new EnvironmentAnchor
                            {
                                position = centroid,
                                rotation = Quaternion.identity,
                                scale = Vector3.one * 0.3f,
                                type = "Object",
                                description = $"Feature point cluster at ({centroid.x:F1}, {centroid.y:F1}, {centroid.z:F1})",
                                confidence = Mathf.Clamp01(grid[cell].Count / 30f)
                            };

                            detectedAnchors.Add(anchorId, anchor);
                        }
                    }
                }
            }
        }
    }

    public void SaveEnhancedMap(string mapName = "")
    {
        // NEW: Prefer Enhanced3DMapManager if available
        if (enhanced3DMapManager != null && preferEnhanced3DManager)
        {
            enhanced3DMapManager.SaveEnhanced3DMap();
            return;
        }

        if (string.IsNullOrEmpty(mapName))
        {
            mapName = $"Map_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}";
        }

        // Update metadata
        currentMap.metadata.mapName = mapName;
        currentMap.metadata.lastModifiedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // Gather waypoints from hitPointManager
        if (hitPointManager != null)
        {
            currentMap.waypoints = new List<PoseClass>(hitPointManager.poseClassList);

            // Update stats
            currentMap.metadata.waypointCount = hitPointManager.poseClassList.Count(p => p.waypointType == WaypointType.PathPoint);
            currentMap.metadata.obstacleCount = hitPointManager.poseClassList.Count(p => p.waypointType == WaypointType.Obstacle);

            // Find start and end points
            var startPoint = hitPointManager.poseClassList.FirstOrDefault(p => p.waypointType == WaypointType.StartPoint);
            var endPoint = hitPointManager.poseClassList.FirstOrDefault(p => p.waypointType == WaypointType.EndPoint);

            if (startPoint != null)
                currentMap.metadata.startPointPosition = startPoint.position;

            if (endPoint != null)
                currentMap.metadata.endPointPosition = endPoint.position;
        }

        // Calculate total path length
        CalculateTotalPathLength();

        // Extract feature points hash for later validation
        if (pointCloudManager != null && pointCloudManager.trackables.count > 0)
        {
            // Collect points from all point clouds
            List<Vector3> allPoints = new List<Vector3>();

            foreach (var trackable in pointCloudManager.trackables)
            {
                var pointCloud = trackable;
                if (pointCloud.positions.HasValue)
                {
                    allPoints.AddRange(pointCloud.positions.Value);
                }
            }

            if (allPoints.Count > 0)
            {
                // Convert to bytes for hashing
                byte[] bytes = new byte[allPoints.Count * 3 * sizeof(float)];
                System.Buffer.BlockCopy(allPoints.ToArray(), 0, bytes, 0, bytes.Length);
                currentMap.featurePointsHash = System.Security.Cryptography.SHA256.Create().ComputeHash(bytes);
            }
        }

        // Serialize map to JSON
        string json = JsonUtility.ToJson(currentMap, true);

        // Save to file
        string path = Path.Combine(hitPointManager.GetAndroidExternalStoragePath(), "ARCoreTrackables", $"{mapName}.json");

        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            // Write JSON file
            File.WriteAllText(path, json);

            // Also save the traditional CSV format for backward compatibility
            hitPointManager.SaveAllTheInformationToFile(mapName);

            Debug.Log($"Map saved successfully to {path}");
            SpeakMessage($"Map {mapName} saved successfully.");
            PlaySound(successSound);

            // Update UI
            if (statusText != null)
                statusText.text = $"Map '{mapName}' saved successfully with {currentMap.metadata.waypointCount} waypoints and {currentMap.metadata.obstacleCount} obstacles.";
        }
        catch (Exception e)
        {
            Debug.LogError($"Error saving map: {e.Message}");
            SpeakMessage($"Error saving map: {e.Message}");
            PlaySound(errorSound);
        }
    }

    private void CalculateTotalPathLength()
    {
        float length = 0f;

        if (currentMap.waypoints.Count < 2)
        {
            currentMap.metadata.totalPathLength = 0f;
            return;
        }

        // Sort waypoints by path order
        var pathPoints = currentMap.waypoints
            .Where(p => p.waypointType == WaypointType.PathPoint
                   || p.waypointType == WaypointType.StartPoint
                   || p.waypointType == WaypointType.EndPoint)
            .ToList();

        // Calculate length
        for (int i = 0; i < pathPoints.Count - 1; i++)
        {
            length += Vector3.Distance(pathPoints[i].position, pathPoints[i + 1].position);
        }

        currentMap.metadata.totalPathLength = length;
    }

    public void LoadEnhancedMap(string mapName)
    {
        // NEW: Prefer Enhanced3DMapManager if available
        if (enhanced3DMapManager != null && preferEnhanced3DManager)
        {
            enhanced3DMapManager.LoadEnhanced3DMap(mapName);
            return;
        }

        string jsonPath = Path.Combine(hitPointManager.GetAndroidExternalStoragePath(), "ARCoreTrackables", $"{mapName}.json");
        string csvPath = Path.Combine(hitPointManager.GetAndroidExternalStoragePath(), "ARCoreTrackables", $"{mapName}.csv");

        // Reset flags
        isMapValidated = false;

        // Try loading JSON format first
        if (File.Exists(jsonPath))
        {
            try
            {
                string json = File.ReadAllText(jsonPath);
                currentMap = JsonUtility.FromJson<EnhancedMap>(json);

                // Load waypoints into hitPointManager
                hitPointManager.ClearCurrentWaypoints();
                hitPointManager.poseClassList = new List<PoseClass>(currentMap.waypoints);

                // Create visuals for all loaded points
                for (int i = 0; i < hitPointManager.poseClassList.Count; i++)
                {
                    hitPointManager.CreateWaypointVisual(i);
                }

                SpeakMessage($"Map {mapName} loaded. Validating environment...");

                // Show validation panel if available
                if (validationPanel != null)
                    validationPanel.SetActive(true);

                // Start validation
                StartCoroutine(ValidateMapCoroutine());
            }
            catch (Exception e)
            {
                Debug.LogError($"Error loading JSON map: {e.Message}");
                SpeakMessage($"Error loading map: {e.Message}");
                PlaySound(errorSound);
            }
        }
        // Fall back to CSV format if JSON not available
        else if (File.Exists(csvPath))
        {
            // Use traditional loading method
            hitPointManager.LoadPathFromFile(mapName + ".csv");

            // Create a minimal map object
            currentMap = new EnhancedMap();
            currentMap.metadata.mapName = mapName;
            currentMap.waypoints = new List<PoseClass>(hitPointManager.poseClassList);

            // Legacy maps don't have environment anchors, so skip validation
            isMapValidated = true;
            SpeakMessage($"Legacy map {mapName} loaded successfully. No environment validation available.");
        }
        else
        {
            SpeakMessage($"Map {mapName} not found.");
            PlaySound(errorSound);
        }
    }

    public IEnumerator ValidateMapCoroutine()
    {
        // Wait for AR to initialize
        yield return new WaitForSeconds(2.0f);

        // Run validation
        ValidateCurrentMap();
    }

    public void ValidateCurrentMap()
    {
        // Reset validation
        isMapValidated = false;

        // If no environment anchors, skip validation
        if (currentMap.metadata.environmentAnchors == null || currentMap.metadata.environmentAnchors.Count == 0)
        {
            isMapValidated = true;
            SpeakMessage("Map validated (no environment data).");

            if (validationPanel != null)
                validationPanel.SetActive(false);

            return;
        }

        // Scan current environment
        StartCoroutine(ScanEnvironmentForValidation());
    }

    private IEnumerator ScanEnvironmentForValidation()
    {
        // Clear detected anchors
        detectedAnchors.Clear();

        if (statusText != null)
            statusText.text = "Scanning environment for validation...";

        SpeakMessage("Scanning environment to validate map...");

        // Run environment scan
        yield return ScanEnvironmentAnchors();

        // Compare environment with the map
        float matchScore = CalculateEnvironmentMatchScore();

        if (matchScore >= mapValidationThreshold)
        {
            isMapValidated = true;
            SpeakMessage($"Map validated successfully! Match score: {(matchScore * 100):F0} percent");
            PlaySound(successSound);

            // Try to align the coordinate systems
            AlignMapToEnvironment();
        }
        else
        {
            isMapValidated = false;
            SpeakMessage($"This doesn't seem to be the right location. Environment match score is only {(matchScore * 100):F0} percent. Try scanning more or load a different map.");
            PlaySound(errorSound);
        }

        // Hide validation panel
        if (validationPanel != null)
            validationPanel.SetActive(false);
    }

    private float CalculateEnvironmentMatchScore()
    {
        if (currentMap.metadata.environmentAnchors.Count == 0 || detectedAnchors.Count == 0)
            return 0f;

        int matchCount = 0;
        int totalAnchors = currentMap.metadata.environmentAnchors.Count;

        foreach (var mapAnchor in currentMap.metadata.environmentAnchors)
        {
            // Try to find a matching anchor in the detected set
            bool foundMatch = false;

            foreach (var detectedAnchor in detectedAnchors.Values)
            {
                // Check if type matches
                if (mapAnchor.type == detectedAnchor.type)
                {
                    // For floors and ceilings, check Y position
                    if (mapAnchor.type == "Floor" || mapAnchor.type == "Ceiling")
                    {
                        if (Mathf.Abs(mapAnchor.position.y - detectedAnchor.position.y) < 0.3f)
                        {
                            foundMatch = true;
                            break;
                        }
                    }
                    // For walls, check orientation and position
                    else if (mapAnchor.type == "Wall")
                    {
                        float angleDiff = Quaternion.Angle(mapAnchor.rotation, detectedAnchor.rotation);
                        float distance = Vector3.Distance(mapAnchor.position, detectedAnchor.position);

                        if (angleDiff < 30f && distance < 2.0f)
                        {
                            foundMatch = true;
                            break;
                        }
                    }
                    // For objects, check position
                    else if (mapAnchor.type == "Object")
                    {
                        float distance = Vector3.Distance(mapAnchor.position, detectedAnchor.position);

                        if (distance < 1.0f)
                        {
                            foundMatch = true;
                            break;
                        }
                    }
                }
            }

            if (foundMatch)
                matchCount++;
        }

        // Calculate score
        return (float)matchCount / totalAnchors;
    }

    private void AlignMapToEnvironment()
    {
        // Simple alignment based on matching floor points
        List<Vector3> mapFloorPoints = new List<Vector3>();
        List<Vector3> currentFloorPoints = new List<Vector3>();

        // Collect floor anchor points from map
        foreach (var anchor in currentMap.metadata.environmentAnchors)
        {
            if (anchor.type == "Floor")
            {
                mapFloorPoints.Add(anchor.position);
            }
        }

        // Collect floor anchor points from current environment
        foreach (var anchor in detectedAnchors.Values)
        {
            if (anchor.type == "Floor")
            {
                currentFloorPoints.Add(anchor.position);
            }
        }

        // If we have at least 3 matching points, try to align
        if (mapFloorPoints.Count >= 3 && currentFloorPoints.Count >= 3)
        {
            // Calculate centroids
            Vector3 mapCentroid = Vector3.zero;
            foreach (var point in mapFloorPoints)
                mapCentroid += point;
            mapCentroid /= mapFloorPoints.Count;

            Vector3 currentCentroid = Vector3.zero;
            foreach (var point in currentFloorPoints)
                currentCentroid += point;
            currentCentroid /= currentFloorPoints.Count;

            // Calculate translation
            Vector3 translation = currentCentroid - mapCentroid;

            // Apply translation to all waypoints
            for (int i = 0; i < hitPointManager.poseClassList.Count; i++)
            {
                hitPointManager.poseClassList[i].position += translation;
                hitPointManager.UpdateWaypointVisual(i);
            }

            SpeakMessage("Map has been aligned to current environment.");
        }
    }

    public bool IsMapValidForNavigation()
    {
        // NEW: Check Enhanced3DMapManager first
        if (enhanced3DMapManager != null && preferEnhanced3DManager)
        {
            return enhanced3DMapManager.GetCurrentMap() != null;
        }

        return isMapValidated || (currentMap.waypoints.Count > 0 &&
               currentMap.metadata.environmentAnchors.Count == 0); // Legacy maps with no environment info
    }

    public void AddLandmark(string landmarkName, Vector3 position)
    {
        // Add the landmark to map
        if (!string.IsNullOrEmpty(landmarkName))
        {
            // Add to landmarks list
            if (!currentMap.metadata.landmarks.Contains(landmarkName))
                currentMap.metadata.landmarks.Add(landmarkName);

            // Try to find existing waypoint nearby
            PoseClass landmarkPose = null;
            foreach (var pose in hitPointManager.poseClassList)
            {
                if (Vector3.Distance(pose.position, position) < 0.5f &&
                    pose.waypointType == WaypointType.PathPoint)
                {
                    landmarkPose = pose;
                    break;
                }
            }

            // If no existing waypoint, create one
            if (landmarkPose == null)
            {
                landmarkPose = new PoseClass
                {
                    trackingId = Guid.NewGuid().ToString(),
                    position = position,
                    rotation = Quaternion.identity,
                    waypointType = WaypointType.PathPoint,
                    description = landmarkName
                };

                hitPointManager.poseClassList.Add(landmarkPose);
                currentMap.waypoints.Add(landmarkPose);

                // Create visual
                hitPointManager.CreateWaypointVisual(hitPointManager.poseClassList.Count - 1);
            }
            else
            {
                // Update existing waypoint
                landmarkPose.description = landmarkName;
            }
        }

        SpeakMessage($"Added landmark: {landmarkName}");
        PlaySound(confirmSound);
    }

    #endregion

    #region Navigation Enhancement

    // Get a random phrase from a list to make navigation sound more natural
    private string GetRandomPhrase(List<string> phrases)
    {
        if (phrases == null || phrases.Count == 0)
            return "";

        int index = random.Next(phrases.Count);
        return phrases[index];
    }

    private IEnumerator NavigationEnhancementUpdater()
    {
        while (true)
        {
            // Only update while navigation is active
            if (navigationManager != null && navigationManager.isNavigating)
            {
                // Check for periodic environment announcements
                Vector3 userPosition = Camera.main.transform.position;
                float timeSinceLastAnnouncement = Time.time - lastEnvironmentAnnouncement;
                float distanceSinceLastAnnouncement = Vector3.Distance(userPosition, lastAnnouncementPosition);

                if (timeSinceLastAnnouncement > environmentAnnouncementInterval &&
                    distanceSinceLastAnnouncement > minDistanceBetweenAnnouncements)
                {
                    DescribeSurroundings();
                    lastEnvironmentAnnouncement = Time.time;
                    lastAnnouncementPosition = userPosition;
                }

                // Check for upcoming turns
                AnnounceTurns();
            }

            yield return new WaitForSeconds(1.0f);
        }
    }

    // Provide enhanced directional guidance
    public void ProvideEnhancedDirections(Vector3 userPosition, Vector3 targetPosition, float angle, float distance)
    {
        string directionMessage;

        // Create speech based on angle and distance
        if (Mathf.Abs(angle) < 15f)
        {
            // Going straight
            directionMessage = GetRandomPhrase(straightPhrases);
        }
        else if (angle > 15f)
        {
            // Right turn
            directionMessage = GetRandomPhrase(rightTurnPhrases);
        }
        else
        {
            // Left turn
            directionMessage = GetRandomPhrase(leftTurnPhrases);
        }

        // Add distance information
        directionMessage += $" for {distance:F1} meters.";

        // NEW: Enhanced obstacle detection
        List<PoseClass> nearbyObstacles = FindNearbyObstacles(userPosition, targetPosition, 2.0f);
        if (nearbyObstacles.Count > 0)
        {
            directionMessage += " " + GetRandomPhrase(obstacleWarningPhrases);
        }

        // NEW: Add environmental context if enabled
        if (enableSmartEnvironmentDesc)
        {
            string envContext = GetEnvironmentalContext(userPosition);
            if (!string.IsNullOrEmpty(envContext))
            {
                directionMessage += " " + envContext;
            }
        }

        // Speak the direction
        SpeakMessage(directionMessage);
    }

    // NEW: Get environmental context for enhanced descriptions
    private string GetEnvironmentalContext(Vector3 position)
    {
        if (!enableSmartEnvironmentDesc)
            return "";

        // Analyze space around user
        float spaceAnalysisRadius = 3.0f;
        int nearbyWalls = 0;
        bool hasFloor = false;

        foreach (var anchor in detectedAnchors.Values)
        {
            float distance = Vector3.Distance(position, anchor.position);
            if (distance < spaceAnalysisRadius)
            {
                if (anchor.type == "Wall")
                    nearbyWalls++;
                else if (anchor.type == "Floor")
                    hasFloor = true;
            }
        }

        // Generate contextual description
        if (nearbyWalls >= 2)
        {
            return GetRandomPhrase(narrowPathPhrases);
        }
        else if (nearbyWalls == 0 && hasFloor)
        {
            return GetRandomPhrase(openAreaPhrases);
        }

        return "";
    }

    // Find obstacles near a path segment
    public List<PoseClass> FindNearbyObstacles(Vector3 start, Vector3 end, float radius)
    {
        List<PoseClass> result = new List<PoseClass>();

        if (hitPointManager == null || hitPointManager.poseClassList == null)
            return result;

        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                float distance = DistancePointToLineSegment(pose.position, start, end);
                if (distance < radius)
                {
                    result.Add(pose);
                }
            }
        }

        return result;
    }

    // Calculate distance from a point to a line segment
    private float DistancePointToLineSegment(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
    {
        Vector3 lineDirection = lineEnd - lineStart;
        float lineLength = lineDirection.magnitude;
        lineDirection.Normalize();

        Vector3 pointVector = point - lineStart;
        float projection = Vector3.Dot(pointVector, lineDirection);

        if (projection < 0)
            return Vector3.Distance(point, lineStart);
        else if (projection > lineLength)
            return Vector3.Distance(point, lineEnd);
        else
            return Vector3.Distance(point, lineStart + lineDirection * projection);
    }

    // Describe the surroundings in detail
    public void DescribeSurroundings()
    {
        // NEW: Prefer Enhanced3DMapManager if available
        if (enhanced3DMapManager != null && preferEnhanced3DManager)
        {
            enhanced3DMapManager.GetCurrentMap(); // This will trigger environment description
            return;
        }

        Vector3 userPosition = Camera.main.transform.position;

        // Build description
        StringBuilder surroundingDescription = new StringBuilder();

        // Check for nearby waypoints
        List<PoseClass> nearbyWaypoints = new List<PoseClass>();
        List<PoseClass> nearbyObstacles = new List<PoseClass>();

        foreach (var pose in hitPointManager.poseClassList)
        {
            float distance = Vector3.Distance(userPosition, pose.position);

            if (distance < environmentScanRadius)
            {
                if (pose.waypointType == WaypointType.Obstacle)
                    nearbyObstacles.Add(pose);
                else if (pose.waypointType != WaypointType.PathPoint || !string.IsNullOrEmpty(pose.description))
                    nearbyWaypoints.Add(pose);
            }
        }

        // Describe landmarks
        if (nearbyWaypoints.Count > 0)
        {
            surroundingDescription.Append("Nearby landmarks: ");

            foreach (var waypoint in nearbyWaypoints)
            {
                float distance = Vector3.Distance(userPosition, waypoint.position);
                string direction = GetDirectionName(userPosition, waypoint.position);

                string landmarkName = !string.IsNullOrEmpty(waypoint.description)
                    ? waypoint.description
                    : waypoint.waypointType.ToString();

                surroundingDescription.Append($"{landmarkName} {direction} {distance:F1} meters. ");
            }
        }

        // Describe obstacles
        if (nearbyObstacles.Count > 0)
        {
            if (surroundingDescription.Length > 0)
                surroundingDescription.Append(" ");

            surroundingDescription.Append($"There are {nearbyObstacles.Count} obstacles nearby. ");

            // Describe closest obstacle
            var closestObstacle = nearbyObstacles.OrderBy(p => Vector3.Distance(userPosition, p.position)).First();
            float obstacleDistance = Vector3.Distance(userPosition, closestObstacle.position);
            string obstacleDirection = GetDirectionName(userPosition, closestObstacle.position);

            surroundingDescription.Append($"Closest obstacle is {obstacleDirection}, {obstacleDistance:F1} meters away.");
        }

        // Describe environment using ARFoundation data if available
        DescribeAREnvironment(surroundingDescription);

        // If we haven't found much to describe
        if (surroundingDescription.Length == 0)
        {
            surroundingDescription.Append("The path ahead appears clear.");

            // NEW: Enhanced predictive guidance
            if (enablePredictiveGuidance && navigationManager.isNavigating && navigationManager.safePathPlanner != null)
            {
                int currentIndex = navigationManager.safePathPlanner.GetCurrentPathIndex();
                int pathCount = navigationManager.safePathPlanner.GetPathCount();

                if (currentIndex < pathCount - 2)
                {
                    Vector3 currentPoint = navigationManager.safePathPlanner.GetPathPointAt(currentIndex);
                    Vector3 nextPoint = navigationManager.safePathPlanner.GetPathPointAt(currentIndex + 1);
                    Vector3 pointAfterNext = navigationManager.safePathPlanner.GetPathPointAt(currentIndex + 2);

                    // Calculate turn angle
                    Vector3 currentDir = (nextPoint - currentPoint).normalized;
                    Vector3 nextDir = (pointAfterNext - nextPoint).normalized;

                    float turnAngle = Vector3.SignedAngle(currentDir, nextDir, Vector3.up);

                    if (Mathf.Abs(turnAngle) > 30f)
                    {
                        float distanceToTurn = Vector3.Distance(userPosition, nextPoint);

                        if (distanceToTurn < upcomingTurnAnnouncementDistance * 2) // Extended range for prediction
                        {
                            string turnDirection = turnAngle > 0 ? "right" : "left";
                            surroundingDescription.Append($" Upcoming turn: {distanceToTurn:F1} meters ahead, turn {turnDirection}.");
                        }
                    }
                }
            }
        }

        SpeakMessage(surroundingDescription.ToString());
    }

    private void DescribeAREnvironment(StringBuilder environmentDesc)
    {
        if (planeManager == null)
            return;

        Vector3 userPosition = Camera.main.transform.position;

        int floors = 0;
        int walls = 0;
        int ceilings = 0;

        foreach (ARPlane plane in planeManager.trackables)
        {
            if (Vector3.Distance(userPosition, plane.center) < environmentScanRadius)
            {
                switch (plane.alignment)
                {
                    case PlaneAlignment.HorizontalUp:
                        floors++;
                        break;
                    case PlaneAlignment.HorizontalDown:
                        ceilings++;
                        break;
                    case PlaneAlignment.Vertical:
                        walls++;
                        break;
                }
            }
        }

        if (floors > 0 || walls > 0 || ceilings > 0)
        {
            if (environmentDesc.Length > 0)
                environmentDesc.Append(" ");

            environmentDesc.Append("Environment: ");

            if (floors > 0)
                environmentDesc.Append($"{floors} floor surfaces. ");

            if (walls > 0)
                environmentDesc.Append($"{walls} walls. ");

            if (ceilings > 0)
                environmentDesc.Append($"{ceilings} ceiling surfaces.");
        }
    }

    // Get a human-friendly direction name
    public string GetDirectionName(Vector3 fromPosition, Vector3 toPosition)
    {
        // Get direction vector (ignore y component for horizontal direction)
        Vector3 direction = toPosition - fromPosition;
        direction.y = 0;

        // Get user's forward direction
        Vector3 userForward = Camera.main.transform.forward;
        userForward.y = 0;
        userForward.Normalize();

        // Calculate angle
        float angle = Vector3.SignedAngle(userForward, direction, Vector3.up);

        // Convert angle to direction name
        if (Mathf.Abs(angle) < 22.5f)
            return "straight ahead";
        else if (angle < -157.5f || angle > 157.5f)
            return "behind you";
        else if (angle < -112.5f)
            return "behind you to the left";
        else if (angle < -67.5f)
            return "to your left";
        else if (angle < -22.5f)
            return "slightly to your left";
        else if (angle < 67.5f)
            return "slightly to your right";
        else if (angle < 112.5f)
            return "to your right";
        else
            return "behind you to the right";
    }

    // Describe any significant obstacles ahead
    public void AnnounceWhatsAhead()
    {
        Vector3 userPosition = Camera.main.transform.position;
        Vector3 userForward = Camera.main.transform.forward;

        // Cast rays to detect obstacles
        StringBuilder aheadDescription = new StringBuilder();
        bool foundObstacle = false;

        // Forward ray
        RaycastHit hit;
        if (Physics.Raycast(userPosition, userForward, out hit, 5f))
        {
            aheadDescription.Append($"Object detected {hit.distance:F1} meters directly ahead. ");
            foundObstacle = true;
        }

        // Cast rays at different angles
        float[] angles = new float[] { 30f, 60f, 90f, -30f, -60f, -90f };
        foreach (float angle in angles)
        {
            Vector3 rayDirection = Quaternion.Euler(0, angle, 0) * userForward;

            if (Physics.Raycast(userPosition, rayDirection, out hit, 3f))
            {
                string direction = angle > 0 ? "right" : "left";
                aheadDescription.Append($"Object {Mathf.Abs(angle):F0} degrees to your {direction}, {hit.distance:F1} meters away. ");
                foundObstacle = true;
            }
        }

        // Check waypoints too
        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                float distance = Vector3.Distance(userPosition, pose.position);

                if (distance < 3f)
                {
                    Vector3 directionToObstacle = pose.position - userPosition;
                    directionToObstacle.y = 0;

                    float obstacleAngle = Vector3.SignedAngle(userForward, directionToObstacle, Vector3.up);
                    string obstacleDirection = GetDirectionName(userPosition, pose.position);

                    if (aheadDescription.Length > 0)
                        aheadDescription.Append(" ");

                    aheadDescription.Append($"Marked obstacle {obstacleDirection}, {distance:F1} meters away.");
                    foundObstacle = true;
                }
            }
            else if (!string.IsNullOrEmpty(pose.description) && pose.waypointType != WaypointType.PathPoint)
            {
                // Describe landmarks
                float distance = Vector3.Distance(userPosition, pose.position);

                if (distance < environmentScanRadius)
                {
                    string direction = GetDirectionName(userPosition, pose.position);

                    if (aheadDescription.Length > 0)
                        aheadDescription.Append(" ");

                    aheadDescription.Append($"{pose.description} {direction}, {distance:F1} meters away.");
                }
            }
        }

        if (!foundObstacle)
            aheadDescription.Append("The path ahead appears clear.");

        SpeakMessage(aheadDescription.ToString());
    }

    // Describe the overall path quality and highlights
    public void DescribePathQuality()
    {
        // NEW: Check Enhanced3DMapManager first
        if (enhanced3DMapManager != null && preferEnhanced3DManager)
        {
            var currentMap = enhanced3DMapManager.GetCurrentMap();
            if (currentMap != null)
            {
                string pathDescription = $"Enhanced path loaded: {currentMap.name}. ";
                pathDescription += $"Total length: {currentMap.totalPathLength:F1} meters with {currentMap.waypoints.Count} waypoints. ";

                if (enhanced3DMapManager.IsNavigating())
                {
                    int currentWaypoint = enhanced3DMapManager.GetCurrentWaypointIndex();
                    pathDescription += $"Currently at waypoint {currentWaypoint + 1} of {currentMap.waypoints.Count}.";
                }

                SpeakMessage(pathDescription);
                return;
            }
        }

        if (navigationManager == null || navigationManager.safePathPlanner == null)
            return;

        int pathCount = navigationManager.safePathPlanner.GetPathCount();
        if (pathCount < 2)
            return;

        // Calculate total path length
        float totalLength = 0f;
        for (int i = 0; i < pathCount - 1; i++)
        {
            Vector3 current = navigationManager.safePathPlanner.GetPathPointAt(i);
            Vector3 next = navigationManager.safePathPlanner.GetPathPointAt(i + 1);
            totalLength += Vector3.Distance(current, next);
        }

        // Count obstacles and turns
        int obstacleCount = 0;
        int turnCount = 0;

        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
                obstacleCount++;
        }

        for (int i = 1; i < pathCount - 1; i++)
        {
            Vector3 prev = navigationManager.safePathPlanner.GetPathPointAt(i - 1);
            Vector3 current = navigationManager.safePathPlanner.GetPathPointAt(i);
            Vector3 next = navigationManager.safePathPlanner.GetPathPointAt(i + 1);

            Vector3 dir1 = (current - prev).normalized;
            Vector3 dir2 = (next - current).normalized;

            float angle = Vector3.Angle(dir1, dir2);
            if (angle > 30f)
                turnCount++;
        }

        // Build description
        StringBuilder pathQualityDesc = new StringBuilder();
        pathQualityDesc.Append($"Your route is {totalLength:F1} meters long with {turnCount} turns");

        if (obstacleCount > 0)
            pathQualityDesc.Append($" and {obstacleCount} marked obstacles along the way");

        pathQualityDesc.Append(". ");

        // Add info about landmarks if available
        List<string> landmarks = new List<string>();
        foreach (var pose in hitPointManager.poseClassList)
        {
            if (!string.IsNullOrEmpty(pose.description))
                landmarks.Add(pose.description);
        }

        if (landmarks.Count > 0)
        {
            pathQualityDesc.Append("You will pass by ");

            for (int i = 0; i < Mathf.Min(landmarks.Count, 3); i++)
            {
                if (i > 0)
                {
                    if (i == landmarks.Count - 1 || i == 2)
                        pathQualityDesc.Append(" and ");
                    else
                        pathQualityDesc.Append(", ");
                }

                pathQualityDesc.Append(landmarks[i]);
            }

            if (landmarks.Count > 3)
                pathQualityDesc.Append($" and {landmarks.Count - 3} other landmarks");

            pathQualityDesc.Append(" along the way.");
        }

        // Speak the description
        SpeakMessage(pathQualityDesc.ToString());
    }

    // Announce upcoming turns before we reach them
    public void AnnounceTurns()
    {
        if (!navigationManager.isNavigating || navigationManager.safePathPlanner == null)
            return;

        int currentIndex = navigationManager.safePathPlanner.GetCurrentPathIndex();

        // Skip if we already announced this turn
        if (announcedTurns.ContainsKey(currentIndex) && announcedTurns[currentIndex])
            return;

        // Look ahead for turns
        int pathCount = navigationManager.safePathPlanner.GetPathCount();
        if (currentIndex >= pathCount - 2)
            return;

        Vector3 userPosition = Camera.main.transform.position;
        Vector3 currentPoint = navigationManager.safePathPlanner.GetPathPointAt(currentIndex);
        Vector3 nextPoint = navigationManager.safePathPlanner.GetPathPointAt(currentIndex + 1);
        Vector3 pointAfterNext = navigationManager.safePathPlanner.GetPathPointAt(currentIndex + 2);

        // Calculate vectors and turn angle
        Vector3 currentDir = (nextPoint - currentPoint).normalized;
        Vector3 nextDir = (pointAfterNext - nextPoint).normalized;

        float turnAngle = Vector3.SignedAngle(currentDir, nextDir, Vector3.up);

        // If this is a significant turn
        if (Mathf.Abs(turnAngle) > 30f)
        {
            float distanceToTurn = Vector3.Distance(userPosition, nextPoint);

            // Announce when we're getting close
            if (distanceToTurn < upcomingTurnAnnouncementDistance)
            {
                string turnDirection = turnAngle > 0 ? "right" : "left";
                string turnPhrase = turnAngle > 0 ? GetRandomPhrase(rightTurnPhrases) : GetRandomPhrase(leftTurnPhrases);

                SpeakMessage($"In {distanceToTurn:F1} meters, {turnPhrase.ToLower()}.");

                // Mark as announced
                announcedTurns[currentIndex] = true;
            }
        }
    }

    // Helper method to restore navigation after map validation
    public void StartEnhancedNavigation()
    {
        if (navigationManager == null || !IsMapValidForNavigation())
            return;

        // Reset state
        announcedTurns.Clear();
        lastAnnouncementPosition = Vector3.zero;
        lastEnvironmentAnnouncement = Time.time;

        // Start navigation
        navigationManager.StartNavigation();

        // Update path info if available
        if (pathInfoText != null)
        {
            pathInfoText.text = $"Map: {currentMap.metadata.mapName}\n" +
                                $"Path length: {currentMap.metadata.totalPathLength:F1}m\n" +
                                $"Waypoints: {currentMap.metadata.waypointCount}\n" +
                                $"Obstacles: {currentMap.metadata.obstacleCount}";
        }

        // Provide initial guidance
        SpeakMessage("Enhanced navigation started. Follow the audio cues to reach your destination.");

        // Describe path after a short delay
        StartCoroutine(DelayedPathDescription());
    }

    private IEnumerator DelayedPathDescription()
    {
        yield return new WaitForSeconds(3.0f);
        DescribePathQuality();
    }

    #endregion

    #region Helper Methods

    public void SpeakMessage(string message)
    {
        if (textToSpeech != null)
        {
            textToSpeech.Speak(message);
        }
        else if (navigationManager != null && navigationManager.textToSpeech != null)
        {
            navigationManager.textToSpeech.Speak(message);
        }
        else if (accessibilityManager != null && accessibilityManager.textToSpeech != null)
        {
            accessibilityManager.textToSpeech.Speak(message);
        }
        else
        {
            Debug.Log("Speech: " + message);
        }

        // Update status text if available
        if (statusText != null)
            statusText.text = message;
    }

    private void PlaySound(AudioClip clip)
    {
        if (uiAudioSource != null && clip != null)
            uiAudioSource.PlayOneShot(clip);
    }

    // Hook this into NavigationManager for enhanced directions
    public void EnhanceNavigationManager()
    {
        if (navigationManager == null)
            return;

        Debug.Log("NavigationEnhancer is ready to provide enhanced directions. " +
                 "NavigationManager will use it automatically when available.");
    }

    #endregion
}
