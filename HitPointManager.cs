using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;
using TMPro;
using System.IO;
using System.Text;
using UnityEngine.UI;
using System.Linq;

public class HitPointManager : MonoBehaviour
{
    // AR components
    private List<ARRaycastHit> raycastHitList;
    private XROrigin xrOrigin;
    private ARRaycastManager raycastManager;
    private ARPlaneManager arPlaneManager;
    private ARPointCloudManager arPointCloudManager;

    // Reference to NavigationEnhancer
    private NavigationEnhancer navigationEnhancer;

    // NEW: Reference to Enhanced3DMapManager
    public Enhanced3DMapManager enhanced3DMapManager;

    // UI references
    public GameObject textRefs;
    public TMP_InputField filenameInputField;
    public GameObject totalEntriesText;
    public GameObject savedLocationPanel, savedLocationButtonPrefab, savedLocationPrefabHolder;

    // Environment mapping
    public Camera mainCamera;
    public GameObject environmentMapRoot;
    private Dictionary<string, GameObject> detectedPlanes = new Dictionary<string, GameObject>();

    // Waypoint system
    public GameObject waypointPrefab, safePathPointPrefab, obstaclePrefab, startPointPrefab, endPointPrefab;
    public List<PoseClass> poseClassList;
    private List<GameObject> instantiatedWaypoints = new List<GameObject>();
    private float scanningInterval = 0.2f;
    private float lastScanTime = 0f;

    // Path creation
    float width, height;
    public bool allowSavingToCSV = true;
    [SerializeField] private bool isPathCreationMode = false;
    private bool isManualPathCreationMode = false;
    private bool isScanningMode = false;
    private Vector3 lastScanPosition;
    private float minDistanceBetweenPoints = 0.5f;

    // NEW: Enhanced walkable surface filtering
    [Header("Walkable Surface Detection")]
    [SerializeField] private float maxGroundSlope = 20f; // Maximum slope angle for walkable surface
    [SerializeField] private float minPlaneArea = 1.0f; // Minimum area for a plane to be considered walkable
    [SerializeField] private float maxHeightDifference = 0.3f; // Max height difference between waypoints
    [SerializeField] private bool onlyPlaceOnFloors = true; // Only place waypoints on horizontal floors
    [SerializeField] private float walkableHeightRange = 1.5f; // Height range considered walkable
    [SerializeField] private LayerMask walkableLayerMask = -1; // Layer mask for walkable surfaces

    // NEW: Enhanced path creation settings
    [SerializeField] private bool useEnhanced3DMode = true;
    [SerializeField] private bool autoOptimizeWaypoints = true;
    [SerializeField] private float waypointOptimizationRadius = 1.0f;

    // Manager references
    private NavigationManager navigationManager;

    // Settings
    [SerializeField] private float environmentScanRadius = 10f;
    [SerializeField] private float environmentScanDensity = 0.5f;

    #region Initialization

    private void Awake()
    {
        savedLocationPanel.SetActive(false);
        poseClassList = new List<PoseClass>();
        filenameInputField.gameObject.SetActive(false);
        width = 0;
        height = 0;

        if (environmentMapRoot == null)
        {
            environmentMapRoot = new GameObject("EnvironmentMapRoot");
            environmentMapRoot.transform.SetParent(transform);
        }

        if (!Directory.Exists(GetAndroidExternalStoragePath() + "/" + "ARCoreTrackables"))
        {
            savedLocationPanel.SetActive(false);
            Directory.CreateDirectory(GetAndroidExternalStoragePath() + "/" + "ARCoreTrackables");
        }
        else
        {
            PopulateSavedLocations();
        }
    }

    void Start()
    {
        raycastManager = GetComponent<ARRaycastManager>();
        arPlaneManager = GetComponent<ARPlaneManager>();
        arPointCloudManager = GetComponent<ARPointCloudManager>();
        xrOrigin = GetComponent<XROrigin>();
        raycastHitList = new List<ARRaycastHit>();

        if (mainCamera == null)
            mainCamera = Camera.main;

        if (textRefs != null)
            textRefs.GetComponent<TextMeshProUGUI>().text = "AR Navigation Assistant - Ready\n";

        SetupManagers();

        // NEW: Integration with Enhanced3D system
        IntegrateWithEnhanced3D();

        SwitchToEnvironmentScanningMode();
    }

    private void SetupManagers()
    {
        navigationManager = GetComponent<NavigationManager>();
        if (navigationManager == null)
            navigationManager = FindObjectOfType<NavigationManager>();

        navigationEnhancer = GetComponent<NavigationEnhancer>();
        if (navigationEnhancer == null)
            navigationEnhancer = FindObjectOfType<NavigationEnhancer>();

        if (navigationEnhancer != null)
            navigationEnhancer.hitPointManager = this;
    }

    // NEW: Integration method for Enhanced3DMapManager
    public void IntegrateWithEnhanced3D()
    {
        enhanced3DMapManager = FindObjectOfType<Enhanced3DMapManager>();
        if (enhanced3DMapManager != null)
        {
            Debug.Log("HitPointManager integrated with Enhanced3DMapManager");
            // Enhanced 3D mapping will handle waypoint creation and management
        }
    }

    public void PopulateSavedLocations()
    {
        foreach (Transform child in savedLocationPrefabHolder.transform)
        {
            Destroy(child.gameObject);
        }

        // NEW: Check for enhanced 3D maps first
        if (enhanced3DMapManager != null)
        {
            var enhancedMaps = enhanced3DMapManager.GetAvailableMaps();
            foreach (string mapName in enhancedMaps)
            {
                CreateSavedLocationButton(mapName + ".json", true);
            }
        }

        // Check for JSON files (enhanced maps)
        string[] jsonFiles = Directory.GetFiles(GetAndroidExternalStoragePath() + "/ARCoreTrackables", "*.json");

        if (jsonFiles.Length == 0)
        {
            string[] files = Directory.GetFiles(GetAndroidExternalStoragePath() + "/ARCoreTrackables", "*.csv");
            foreach (string file in files)
            {
                CreateSavedLocationButton(file, false);
            }
            savedLocationPanel.SetActive(true);
        }
        else
        {
            foreach (string file in jsonFiles)
            {
                CreateSavedLocationButton(file, true);
            }
        }
    }

    private void CreateSavedLocationButton(string file, bool isEnhanced)
    {
        GameObject go = Instantiate(savedLocationButtonPrefab);
        go.transform.SetParent(savedLocationPrefabHolder.transform);
        go.transform.localScale = new Vector3(1, 1, 1);
        go.SetActive(true);

        string fileName = Path.GetFileName(file);
        string displayName = fileName;

        // Add indicator for enhanced maps
        if (isEnhanced && fileName.EndsWith(".json"))
        {
            displayName = "📍 " + fileName.Replace(".json", " (Enhanced)");
        }

        go.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = displayName;

        Button button = go.GetComponent<Button>();
        string fileNameCopy = fileName;
        button.onClick.AddListener(() => LoadPathFromFile(fileNameCopy));
    }

    #endregion

    #region Update Modes

    void Update()
    {
        if (isPathCreationMode)
        {
            UpdatePathCreationMode();
        }
        else if (isManualPathCreationMode)
        {
            UpdateManualPathCreationMode();
        }
        else if (isScanningMode)
        {
            UpdateEnvironmentScanningMode();
        }
    }

    private void UpdatePathCreationMode()
    {
        textRefs.GetComponent<TextMeshProUGUI>().text = "Auto Path Creation Mode\n";
        textRefs.GetComponent<TextMeshProUGUI>().text += poseClassList.Count + " Points Detected\n";

        if (width < Screen.width || height < Screen.height)
        {
            // NEW: Only raycast for walkable horizontal planes
            bool hasDetectedHit = raycastManager.Raycast(new Vector2(width, height), raycastHitList, TrackableType.PlaneWithinPolygon);

            if (hasDetectedHit)
            {
                for (int i = 0; i < raycastHitList.Count; i++)
                {
                    Vector3 hitPosition = raycastHitList[i].pose.position;

                    // NEW: Validate if this is a walkable surface
                    if (IsWalkableSurface(raycastHitList[i]))
                    {
                        bool pointAlreadyExists = false;

                        foreach (var pose in poseClassList)
                        {
                            if (Vector3.Distance(pose.position, hitPosition) < minDistanceBetweenPoints)
                            {
                                pointAlreadyExists = true;
                                break;
                            }
                        }

                        if (!pointAlreadyExists)
                        {
                            AddNewWaypoint(raycastHitList[i].pose.position, raycastHitList[i].pose.rotation, WaypointType.PathPoint);
                            textRefs.GetComponent<TextMeshProUGUI>().text += "Walkable path point added at " + hitPosition + "\n";
                        }
                    }
                }

                width += (Screen.width / 16);
                height += (Screen.height / 16);
            }
        }
        else if (width >= Screen.width && height >= Screen.height && allowSavingToCSV)
        {
            arPlaneManager.enabled = false;

            if (poseClassList.Count >= 2)
            {
                poseClassList[0].waypointType = WaypointType.StartPoint;
                UpdateWaypointVisual(0);

                poseClassList[poseClassList.Count - 1].waypointType = WaypointType.EndPoint;
                UpdateWaypointVisual(poseClassList.Count - 1);
            }

            // NEW: Optimize waypoints before saving
            if (autoOptimizeWaypoints)
            {
                OptimizeWaypoints();
            }

            PromptSavePathWithName();
            allowSavingToCSV = false;
        }
    }

    private void UpdateManualPathCreationMode()
    {
        textRefs.GetComponent<TextMeshProUGUI>().text = "Manual Path Creation Mode\n";
        textRefs.GetComponent<TextMeshProUGUI>().text += "Tap to place path points\n";
        textRefs.GetComponent<TextMeshProUGUI>().text += "Current points: " + poseClassList.Count + "\n";

        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            Touch touch = Input.GetTouch(0);

            bool touchedUI = UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(touch.fingerId);

            if (!touchedUI)
            {
                Ray ray = mainCamera.ScreenPointToRay(touch.position);

                // NEW: Only raycast for walkable surfaces
                if (raycastManager.Raycast(ray, raycastHitList, TrackableType.PlaneWithinPolygon))
                {
                    ARRaycastHit hit = raycastHitList[0];

                    // NEW: Validate walkable surface before placing waypoint
                    if (IsWalkableSurface(hit))
                    {
                        bool pointNearby = false;
                        int nearbyPointIndex = -1;

                        for (int i = 0; i < poseClassList.Count; i++)
                        {
                            if (Vector3.Distance(poseClassList[i].position, hit.pose.position) < minDistanceBetweenPoints * 2)
                            {
                                pointNearby = true;
                                nearbyPointIndex = i;
                                break;
                            }
                        }

                        if (pointNearby)
                        {
                            CycleWaypointType(nearbyPointIndex);
                        }
                        else
                        {
                            AddNewWaypoint(hit.pose.position, hit.pose.rotation, WaypointType.PathPoint);

                            if (poseClassList.Count == 1)
                            {
                                poseClassList[0].waypointType = WaypointType.StartPoint;
                                UpdateWaypointVisual(0);
                            }
                            else
                            {
                                for (int i = 0; i < poseClassList.Count - 1; i++)
                                {
                                    if (poseClassList[i].waypointType == WaypointType.EndPoint)
                                    {
                                        poseClassList[i].waypointType = WaypointType.PathPoint;
                                        UpdateWaypointVisual(i);
                                    }
                                }

                                poseClassList[poseClassList.Count - 1].waypointType = WaypointType.EndPoint;
                                UpdateWaypointVisual(poseClassList.Count - 1);
                            }
                        }
                    }
                    else
                    {
                        // Provide feedback that surface is not walkable
                        if (navigationManager != null && navigationManager.textToSpeech != null)
                        {
                            navigationManager.textToSpeech.Speak("Cannot place waypoint here. Surface is not walkable.");
                        }
                    }
                }
            }
        }
    }

    private void UpdateEnvironmentScanningMode()
    {
        textRefs.GetComponent<TextMeshProUGUI>().text = "Environment Scanning Mode\n";
        textRefs.GetComponent<TextMeshProUGUI>().text += "Detected walkable planes: " + GetWalkablePlaneCount() + "\n";
        textRefs.GetComponent<TextMeshProUGUI>().text += "Detected obstacles: " + GetObstacleCount() + "\n";

        if (Time.time - lastScanTime > scanningInterval)
        {
            lastScanTime = Time.time;

            UpdateDetectedPlanes();

            if (Vector3.Distance(mainCamera.transform.position, lastScanPosition) > minDistanceBetweenPoints)
            {
                DetectObstaclesAround(mainCamera.transform.position);
                lastScanPosition = mainCamera.transform.position;
            }
        }
    }

    #endregion

    #region NEW: Walkable Surface Validation

    /// <summary>
    /// Check if an ARRaycastHit represents a walkable surface
    /// </summary>
    private bool IsWalkableSurface(ARRaycastHit hit)
    {
        // Get the trackable that was hit
        if (hit.trackable == null) return false;

        // Check if it's a plane
        if (hit.trackable is ARPlane plane)
        {
            // Only allow horizontal floors if onlyPlaceOnFloors is true
            if (onlyPlaceOnFloors && plane.alignment != PlaneAlignment.HorizontalUp)
            {
                return false;
            }

            // Check plane area - too small planes are not walkable
            if (plane.size.x * plane.size.y < minPlaneArea)
            {
                return false;
            }

            // Check surface normal for slope
            Vector3 normal = plane.normal;
            float slopeAngle = Vector3.Angle(normal, Vector3.up);

            if (slopeAngle > maxGroundSlope)
            {
                return false;
            }

            // Check height relative to camera/user
            float heightDifference = Mathf.Abs(hit.pose.position.y - mainCamera.transform.position.y);
            if (heightDifference > walkableHeightRange)
            {
                return false;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Check if a position is on a walkable surface using raycast
    /// </summary>
    private bool IsPositionWalkable(Vector3 position)
    {
        // Cast ray downward from slightly above the position
        Vector3 rayStart = position + Vector3.up * 0.5f;
        RaycastHit hit;

        if (Physics.Raycast(rayStart, Vector3.down, out hit, 1.0f, walkableLayerMask))
        {
            // Check surface normal
            float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
            if (slopeAngle > maxGroundSlope)
            {
                return false;
            }

            // Check if we hit an obstacle
            if (hit.collider.CompareTag("Obstacle"))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Get count of walkable planes
    /// </summary>
    private int GetWalkablePlaneCount()
    {
        int count = 0;
        if (arPlaneManager != null)
        {
            foreach (ARPlane plane in arPlaneManager.trackables)
            {
                if (IsPlaneWalkable(plane))
                {
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Check if an ARPlane is walkable
    /// </summary>
    private bool IsPlaneWalkable(ARPlane plane)
    {
        // Must be horizontal floor
        if (plane.alignment != PlaneAlignment.HorizontalUp)
            return false;

        // Must have sufficient area
        if (plane.size.x * plane.size.y < minPlaneArea)
            return false;

        // Check slope
        float slopeAngle = Vector3.Angle(plane.normal, Vector3.up);
        if (slopeAngle > maxGroundSlope)
            return false;

        return true;
    }

    /// <summary>
    /// Optimize waypoints by removing redundant ones and ensuring proper spacing
    /// </summary>
    private void OptimizeWaypoints()
    {
        if (poseClassList.Count <= 2) return;

        List<PoseClass> optimizedWaypoints = new List<PoseClass>();

        // Always keep start point
        if (poseClassList.Count > 0)
            optimizedWaypoints.Add(poseClassList[0]);

        // Process middle waypoints
        for (int i = 1; i < poseClassList.Count - 1; i++)
        {
            PoseClass currentPose = poseClassList[i];

            // Skip if too close to previous waypoint
            if (optimizedWaypoints.Count > 0)
            {
                float distanceToPrevious = Vector3.Distance(currentPose.position, optimizedWaypoints[optimizedWaypoints.Count - 1].position);
                if (distanceToPrevious < minDistanceBetweenPoints)
                    continue;
            }

            // Check if waypoint is still on walkable surface
            if (IsPositionWalkable(currentPose.position))
            {
                optimizedWaypoints.Add(currentPose);
            }
        }

        // Always keep end point
        if (poseClassList.Count > 1)
            optimizedWaypoints.Add(poseClassList[poseClassList.Count - 1]);

        // Replace waypoint list
        ClearCurrentWaypoints();
        poseClassList = optimizedWaypoints;

        // Recreate visuals
        for (int i = 0; i < poseClassList.Count; i++)
        {
            CreateWaypointVisual(i);
        }

        Debug.Log($"Optimized waypoints: {poseClassList.Count} points remaining");
    }

    #endregion

    #region Mode Switching

    public void SwitchToPathCreationMode()
    {
        StopAllCoroutines();
        ClearCurrentWaypoints();
        poseClassList.Clear();
        isPathCreationMode = true;
        isManualPathCreationMode = false;
        isScanningMode = false;
        width = 0;
        height = 0;
        allowSavingToCSV = true;

        arPlaneManager.enabled = true;
        foreach (var plane in arPlaneManager.trackables)
            plane.gameObject.SetActive(true);

        if (navigationManager.textToSpeech != null)
            navigationManager.textToSpeech.Speak("Automatic path creation mode activated. I will only place waypoints on walkable surfaces. Please move slowly around your environment.");

        textRefs.GetComponent<TextMeshProUGUI>().text = "Creating path automatically. Scanning for walkable surfaces.\n";

        if (navigationEnhancer != null)
        {
            navigationEnhancer.StartNewMap();
        }
    }

    public void SwitchToManualPathCreationMode()
    {
        StopAllCoroutines();
        ClearCurrentWaypoints();
        poseClassList.Clear();
        isPathCreationMode = false;
        isManualPathCreationMode = true;
        isScanningMode = false;

        arPlaneManager.enabled = true;
        foreach (var plane in arPlaneManager.trackables)
            plane.gameObject.SetActive(true);

        if (navigationManager.textToSpeech != null)
            navigationManager.textToSpeech.Speak("Manual path creation mode activated. Tap to place path points on walkable surfaces only. Double tap on points to change their type.");

        textRefs.GetComponent<TextMeshProUGUI>().text = "Creating path manually. Tap on walkable surfaces to place points.\n";

        if (navigationEnhancer != null)
        {
            navigationEnhancer.StartNewMap();
        }
    }

    // NEW: Enhanced path creation method
    public void StartEnhancedPathCreation()
    {
        if (enhanced3DMapManager != null)
        {
            string pathName = "Path_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            enhanced3DMapManager.StartPathRecording(pathName);

            if (navigationManager.textToSpeech != null)
            {
                navigationManager.textToSpeech.Speak("Enhanced path recording started. I will create optimal waypoints on walkable surfaces as you walk.");
            }

            // Switch to enhanced mode
            StopAllCoroutines();
            ClearCurrentWaypoints();
            poseClassList.Clear();
            isPathCreationMode = false;
            isManualPathCreationMode = false;
            isScanningMode = false;

            textRefs.GetComponent<TextMeshProUGUI>().text = "Enhanced 3D Path Recording Active\nWalk slowly along your desired route.\n";
        }
        else
        {
            // Fall back to manual path creation
            SwitchToManualPathCreationMode();
        }
    }

    public void SwitchToEnvironmentScanningMode()
    {
        StopAllCoroutines();
        isPathCreationMode = false;
        isManualPathCreationMode = false;
        isScanningMode = true;
        lastScanTime = 0;

        if (mainCamera != null)
            lastScanPosition = mainCamera.transform.position;
        else
            Debug.LogWarning("mainCamera is null in SwitchToEnvironmentScanningMode");

        if (arPlaneManager != null)
        {
            arPlaneManager.enabled = true;
            foreach (var plane in arPlaneManager.trackables)
                plane.gameObject.SetActive(true);
        }
        else
            Debug.LogWarning("arPlaneManager is null in SwitchToEnvironmentScanningMode");

        if (arPointCloudManager != null)
            arPointCloudManager.enabled = true;

        if (navigationManager != null && navigationManager.textToSpeech != null)
            navigationManager.textToSpeech.Speak("Environment scanning mode activated. I'm scanning for walkable surfaces and obstacles. Please move around to map more area.");

        if (textRefs != null)
            textRefs.GetComponent<TextMeshProUGUI>().text = "Scanning environment for walkable surfaces. Move around to map more area.\n";
        else
            Debug.LogWarning("textRefs is null in SwitchToEnvironmentScanningMode");

        if (navigationEnhancer != null)
        {
            navigationEnhancer.StartNewMap();
        }
    }

    public void StartNavigationMode()
    {
        StopAllCoroutines();
        isPathCreationMode = false;
        isManualPathCreationMode = false;
        isScanningMode = false;

        if (poseClassList.Count > 0)
        {
            foreach (var plane in arPlaneManager.trackables)
                plane.gameObject.SetActive(false);

            navigationManager.StartNavigation();
        }
        else if (navigationManager.textToSpeech != null)
        {
            navigationManager.textToSpeech.Speak("No path available. Please load a path or create a new one first.");
        }
    }

    public void StopNavigationMode()
    {
        if (navigationManager != null)
        {
            navigationManager.StopNavigation();
        }
    }

    #endregion

    #region Waypoint Management

    public void AddNewWaypoint(Vector3 position, Quaternion rotation, WaypointType type)
    {
        // NEW: Validate position is walkable before adding
        if (type != WaypointType.Obstacle && !IsPositionWalkable(position))
        {
            Debug.LogWarning("Attempted to place waypoint on non-walkable surface: " + position);
            return;
        }

        string waypointId = Guid.NewGuid().ToString();

        poseClassList.Add(new PoseClass
        {
            trackingId = waypointId,
            distance = Vector3.Distance(mainCamera.transform.position, position).ToString(),
            position = position,
            rotation = rotation,
            waypointType = type
        });

        CreateWaypointVisual(poseClassList.Count - 1);
    }

    public void CreateWaypointVisual(int poseIndex)
    {
        if (poseIndex < 0 || poseIndex >= poseClassList.Count)
            return;

        PoseClass pose = poseClassList[poseIndex];
        GameObject waypointObject = null;

        if (pose.waypointType == WaypointType.Obstacle)
            return;

        switch (pose.waypointType)
        {
            case WaypointType.StartPoint:
                waypointObject = Instantiate(startPointPrefab, pose.position, pose.rotation);
                break;
            case WaypointType.EndPoint:
                waypointObject = Instantiate(endPointPrefab, pose.position, pose.rotation);
                break;
            case WaypointType.PathPoint:
                waypointObject = Instantiate(safePathPointPrefab, pose.position, pose.rotation);
                break;
            default:
                waypointObject = Instantiate(waypointPrefab, pose.position, pose.rotation);
                break;
        }

        if (waypointObject != null)
        {
            waypointObject.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
            waypointObject.name = "Waypoint_" + poseIndex + "_" + pose.waypointType.ToString();
            waypointObject.tag = "Waypoint";

            instantiatedWaypoints.Add(waypointObject);

            WaypointIdentifier identifier = waypointObject.AddComponent<WaypointIdentifier>();
            identifier.waypointIndex = poseIndex;
        }
    }

    public void UpdateWaypointVisual(int poseIndex)
    {
        if (poseIndex < 0 || poseIndex >= poseClassList.Count)
            return;

        for (int i = 0; i < instantiatedWaypoints.Count; i++)
        {
            WaypointIdentifier identifier = instantiatedWaypoints[i].GetComponent<WaypointIdentifier>();
            if (identifier != null && identifier.waypointIndex == poseIndex)
            {
                Destroy(instantiatedWaypoints[i]);
                instantiatedWaypoints.RemoveAt(i);
                break;
            }
        }

        CreateWaypointVisual(poseIndex);
    }

    private void CycleWaypointType(int poseIndex)
    {
        if (poseIndex < 0 || poseIndex >= poseClassList.Count)
            return;

        switch (poseClassList[poseIndex].waypointType)
        {
            case WaypointType.PathPoint:
                poseClassList[poseIndex].waypointType = WaypointType.Obstacle;
                break;
            case WaypointType.Obstacle:
                poseClassList[poseIndex].waypointType = WaypointType.StartPoint;
                break;
            case WaypointType.StartPoint:
                poseClassList[poseIndex].waypointType = WaypointType.EndPoint;
                break;
            case WaypointType.EndPoint:
                poseClassList[poseIndex].waypointType = WaypointType.PathPoint;
                break;
        }

        UpdateWaypointVisual(poseIndex);

        if (navigationManager.textToSpeech != null)
        {
            navigationManager.textToSpeech.Speak("Point changed to " + poseClassList[poseIndex].waypointType.ToString());
        }
    }

    public void ClearCurrentWaypoints()
    {
        foreach (GameObject waypoint in instantiatedWaypoints)
        {
            Destroy(waypoint);
        }
        instantiatedWaypoints.Clear();
    }

    private int GetObstacleCount()
    {
        int count = 0;
        foreach (var pose in poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
                count++;
        }
        return count;
    }

    #endregion

    #region Environment Mapping

    private void UpdateDetectedPlanes()
    {
        foreach (var plane in arPlaneManager.trackables)
        {
            string planeId = plane.trackableId.ToString();

            if (!detectedPlanes.ContainsKey(planeId))
            {
                GameObject planeVisual = new GameObject("Plane_" + planeId);
                planeVisual.transform.SetParent(environmentMapRoot.transform);

                MeshFilter meshFilter = planeVisual.AddComponent<MeshFilter>();
                MeshRenderer meshRenderer = planeVisual.AddComponent<MeshRenderer>();

                meshFilter.mesh = plane.GetComponent<MeshFilter>().mesh;

                planeVisual.transform.position = plane.transform.position;
                planeVisual.transform.rotation = plane.transform.rotation;
                planeVisual.transform.localScale = plane.transform.localScale;

                Material planeMaterial = new Material(Shader.Find("Standard"));
                planeMaterial.color = new Color(0.2f, 0.8f, 0.3f, 0.3f);
                meshRenderer.material = planeMaterial;

                planeVisual.SetActive(false);

                detectedPlanes.Add(planeId, planeVisual);

                // NEW: Only add waypoints to walkable horizontal planes
                if (IsPlaneWalkable(plane))
                {
                    AddWaypointsToWalkablePlane(plane);
                }
            }
        }
    }

    // NEW: Updated method to only add waypoints to walkable planes
    private void AddWaypointsToWalkablePlane(ARPlane plane)
    {
        Vector2[] boundaryPoints2D = plane.boundary.ToArray();

        if (boundaryPoints2D.Length < 3)
            return;

        Vector3 planeCenter = plane.center;
        Vector3 planeNormal = plane.normal;

        // NEW: Increased grid size to reduce waypoint density
        float gridSize = Mathf.Max(1.0f, minDistanceBetweenPoints * 2);

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (Vector2 point2D in boundaryPoints2D)
        {
            Vector3 localPoint = new Vector3(point2D.x, 0, point2D.y);
            Vector3 worldPoint = plane.transform.TransformPoint(localPoint);

            if (worldPoint.x < minX) minX = worldPoint.x;
            if (worldPoint.x > maxX) maxX = worldPoint.x;
            if (worldPoint.z < minZ) minZ = worldPoint.z;
            if (worldPoint.z > maxZ) maxZ = worldPoint.z;
        }

        // NEW: Limit waypoint density by adding margin
        float margin = gridSize * 0.5f;
        minX += margin;
        maxX -= margin;
        minZ += margin;
        maxZ -= margin;

        for (float x = minX; x <= maxX; x += gridSize)
        {
            for (float z = minZ; z <= maxZ; z += gridSize)
            {
                Vector3 potentialPoint = new Vector3(x, planeCenter.y, z);

                if (IsPointInPolygon(potentialPoint, plane))
                {
                    // NEW: Additional validation for walkable position
                    if (IsPositionWalkable(potentialPoint))
                    {
                        bool pointExists = false;
                        foreach (var pose in poseClassList)
                        {
                            if (Vector3.Distance(pose.position, potentialPoint) < minDistanceBetweenPoints)
                            {
                                pointExists = true;
                                break;
                            }
                        }

                        if (!pointExists)
                        {
                            AddNewWaypoint(potentialPoint, Quaternion.LookRotation(Vector3.forward, planeNormal), WaypointType.PathPoint);
                        }
                    }
                }
            }
        }
    }

    private bool IsPointInPolygon(Vector3 point, ARPlane plane)
    {
        Vector3 localPoint = plane.transform.InverseTransformPoint(point);

        Vector2 point2D = new Vector2(localPoint.x, localPoint.z);

        Vector2[] boundaryPoints = plane.boundary.ToArray();

        int j = boundaryPoints.Length - 1;
        bool isInside = false;

        for (int i = 0; i < boundaryPoints.Length; i++)
        {
            Vector2 pi = boundaryPoints[i];
            Vector2 pj = boundaryPoints[j];

            if (((pi.y <= point2D.y && point2D.y < pj.y) || (pj.y <= point2D.y && point2D.y < pi.y)) &&
                (point2D.x < (pj.x - pi.x) * (point2D.y - pi.y) / (pj.y - pi.y) + pi.x))
            {
                isInside = !isInside;
            }

            j = i;
        }

        return isInside;
    }

    private void DetectObstaclesAround(Vector3 position)
    {
        int rayCount = 8;
        float rayLength = 3.0f;

        for (int i = 0; i < rayCount; i++)
        {
            float angle = i * (360f / rayCount);
            Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;

            RaycastHit hit;
            if (Physics.Raycast(position, direction, out hit, rayLength))
            {
                if (hit.collider != null && !hit.collider.CompareTag("Waypoint"))
                {
                    bool obstacleExists = false;
                    foreach (var pose in poseClassList)
                    {
                        if (pose.waypointType == WaypointType.Obstacle &&
                            Vector3.Distance(pose.position, hit.point) < minDistanceBetweenPoints)
                        {
                            obstacleExists = true;
                            break;
                        }
                    }

                    if (!obstacleExists)
                    {
                        AddNewWaypoint(hit.point, Quaternion.LookRotation(hit.normal), WaypointType.Obstacle);
                    }
                }
            }
        }
    }

    #endregion

    #region File Operations

    public void SaveAllTheInformationToFile(string filename = "")
    {
        Debug.Log("SaveAllTheInformationToFile called with filename: " + filename);

        // NEW: Use Enhanced3DMapManager if available
        if (enhanced3DMapManager != null && enhanced3DMapManager.GetCurrentMap() != null)
        {
            if (string.IsNullOrEmpty(filename))
                filename = "EnhancedMap_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            enhanced3DMapManager.SaveEnhanced3DMap();
            return;
        }

        if (navigationEnhancer != null)
        {
            navigationEnhancer.SaveEnhancedMap(filename);
            return;
        }

        // Legacy saving method
        if (string.IsNullOrEmpty(filename))
        {
            filename = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        filenameInputField.gameObject.SetActive(false);

        string path = GetAndroidExternalStoragePath() + "/" + "ARCoreTrackables" + "/" + filename + ".csv";
        Debug.Log("Saving to path: " + path);

        StringBuilder csvContent = new StringBuilder("TrackingID,Distance,PositionX,PositionY,PositionZ,RotationX,RotationY,RotationZ,RotationW,WaypointType,Description\n");

        foreach (var poseClass in poseClassList)
        {
            string description = "";
            switch (poseClass.waypointType)
            {
                case WaypointType.StartPoint:
                    description = "Start of navigation path";
                    break;
                case WaypointType.EndPoint:
                    description = "End of navigation path";
                    break;
                case WaypointType.Obstacle:
                    description = "Obstacle to avoid";
                    break;
                case WaypointType.PathPoint:
                    description = "Safe walkable navigation point";
                    break;
            }

            csvContent.AppendLine($"{poseClass.trackingId},{poseClass.distance},{poseClass.position.x},{poseClass.position.y},{poseClass.position.z},{poseClass.rotation.x},{poseClass.rotation.y},{poseClass.rotation.z},{poseClass.rotation.w},{(int)poseClass.waypointType},{description}");
        }

        string directory = Path.GetDirectoryName(path);
        if (!Directory.Exists(directory))
        {
            Debug.Log("Creating directory: " + directory);
            Directory.CreateDirectory(directory);
        }

        try
        {
            File.WriteAllText(path, csvContent.ToString());
            Debug.Log("File successfully saved to: " + path);

            if (navigationManager != null && navigationManager.textToSpeech != null)
            {
                int pathPoints = poseClassList.Count(p => p.waypointType == WaypointType.PathPoint);
                int obstacles = poseClassList.Count(p => p.waypointType == WaypointType.Obstacle);

                navigationManager.textToSpeech.Speak($"Path saved successfully with {pathPoints} walkable path points and {obstacles} obstacles marked. All waypoints are verified to be on safe, walkable surfaces.");
            }

            if (textRefs != null)
            {
                textRefs.GetComponent<TextMeshProUGUI>().text = "File Saved At: " + path;
            }

            PopulateSavedLocations();
        }
        catch (Exception e)
        {
            Debug.LogError("Error saving file: " + e.Message);
            if (navigationManager != null && navigationManager.textToSpeech != null)
            {
                navigationManager.textToSpeech.Speak("Error saving file: " + e.Message);
            }
        }
    }

    public void SaveAndExit()
    {
        string filename = "Path_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

        SaveAllTheInformationToFile(filename);

        StartCoroutine(ExitAfterDelay(2.0f));
    }

    private IEnumerator ExitAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        Debug.Log("Exiting application");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void PromptSavePathWithName()
    {
        filenameInputField.gameObject.SetActive(true);
        filenameInputField.text = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        filenameInputField.onEndEdit.RemoveAllListeners();
        filenameInputField.onEndEdit.AddListener(SaveAllTheInformationToFile);

        if (navigationManager != null && navigationManager.textToSpeech != null)
        {
            navigationManager.textToSpeech.Speak("Enter a name to save this walkable path, or use the default timestamp.");
        }
    }

    public void PromptFilenameToLoad()
    {
        savedLocationPanel.SetActive(true);

        if (navigationManager != null && navigationManager.textToSpeech != null)
        {
            navigationManager.textToSpeech.Speak("Please select a saved location to navigate.");
        }
    }

    // NEW: Enhanced path loading method
    public void LoadEnhancedPath(string pathName)
    {
        if (enhanced3DMapManager != null)
        {
            bool success = enhanced3DMapManager.LoadEnhanced3DMap(pathName);
            if (!success)
            {
                // Fall back to loading regular CSV path
                LoadPathFromFile(pathName + ".csv");
            }
        }
        else
        {
            // Fall back to original loading
            LoadPathFromFile(pathName);
        }
    }

    public void LoadPathFromFile(string filename)
    {
        // NEW: Check if this is an enhanced 3D map
        if (filename.EndsWith(".json"))
        {
            string mapName = filename.Replace(".json", "");
            LoadEnhancedPath(mapName);
            savedLocationPanel.SetActive(false);
            return;
        }

        if (navigationEnhancer != null && filename.EndsWith(".json"))
        {
            navigationEnhancer.LoadEnhancedMap(Path.GetFileNameWithoutExtension(filename));
            savedLocationPanel.SetActive(false);
            return;
        }

        ClearCurrentWaypoints();
        poseClassList.Clear();

        string path = GetAndroidExternalStoragePath() + "/" + "ARCoreTrackables" + "/" + filename;

        if (!File.Exists(path))
        {
            textRefs.GetComponent<TextMeshProUGUI>().text = "File not found: " + path;
            if (navigationManager != null && navigationManager.textToSpeech != null)
            {
                navigationManager.textToSpeech.Speak("File not found. Please try another location.");
            }
            return;
        }

        try
        {
            var lines = File.ReadAllLines(path);

            int startPoints = 0;
            int endPoints = 0;
            int pathPoints = 0;
            int obstacles = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                var tokens = lines[i].Split(',');
                if (tokens.Length >= 10)
                {
                    try
                    {
                        WaypointType type = WaypointType.PathPoint;
                        if (tokens.Length >= 10)
                        {
                            type = (WaypointType)int.Parse(tokens[9]);
                        }

                        Vector3 position = new Vector3(
                            float.Parse(tokens[2]),
                            float.Parse(tokens[3]),
                            float.Parse(tokens[4])
                        );

                        // NEW: Validate loaded waypoints are on walkable surfaces
                        if (type != WaypointType.Obstacle && !IsPositionWalkable(position))
                        {
                            Debug.LogWarning($"Loaded waypoint at {position} is not on walkable surface, skipping");
                            continue;
                        }

                        PoseClass poseClass = new PoseClass
                        {
                            trackingId = tokens[0],
                            distance = tokens[1],
                            position = position,
                            rotation = new Quaternion(
                                float.Parse(tokens[5]),
                                float.Parse(tokens[6]),
                                float.Parse(tokens[7]),
                                float.Parse(tokens[8])
                            ),
                            waypointType = type
                        };

                        poseClassList.Add(poseClass);

                        switch (type)
                        {
                            case WaypointType.StartPoint: startPoints++; break;
                            case WaypointType.EndPoint: endPoints++; break;
                            case WaypointType.PathPoint: pathPoints++; break;
                            case WaypointType.Obstacle: obstacles++; break;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("Error parsing line: " + lines[i] + " - " + e.Message);
                    }
                }
            }

            for (int i = 0; i < poseClassList.Count; i++)
            {
                CreateWaypointVisual(i);
            }

            savedLocationPanel.SetActive(false);

            if (navigationManager != null && navigationManager.textToSpeech != null)
            {
                navigationManager.textToSpeech.Speak(
                    $"Loaded {poseClassList.Count} verified waypoints. {pathPoints} walkable path points, " +
                    $"{obstacles} obstacles, {startPoints} start points, and {endPoints} end points. All waypoints are on safe, walkable surfaces."
                );
            }

            textRefs.GetComponent<TextMeshProUGUI>().text = $"Loaded verified walkable path: {filename}\n" +
                                                           $"Total points: {poseClassList.Count}\n" +
                                                           $"Path points: {pathPoints}, Obstacles: {obstacles}";
        }
        catch (Exception e)
        {
            textRefs.GetComponent<TextMeshProUGUI>().text = "Error loading file: " + e.Message;
            Debug.LogError("Error loading path: " + e.Message);

            if (navigationManager != null && navigationManager.textToSpeech != null)
            {
                navigationManager.textToSpeech.Speak("Error loading path. Please try another file.");
            }
        }
    }

    public string GetAndroidExternalStoragePath()
    {
        if (Application.platform == RuntimePlatform.Android)
        {
            try
            {
                var jc = new AndroidJavaClass("android.os.Environment");
                var path = jc.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory",
                    jc.GetStatic<string>("DIRECTORY_DOCUMENTS")).Call<string>("getAbsolutePath");
                return path;
            }
            catch (Exception e)
            {
                Debug.LogError("Error getting Android path: " + e.Message);
                return Application.persistentDataPath;
            }
        }
        else
        {
            return Application.persistentDataPath;
        }
    }

    #endregion

    public void OnSkipButtonClicked()
    {
        savedLocationPanel.SetActive(false);
        SwitchToEnvironmentScanningMode();
    }
}

public class WaypointIdentifier : MonoBehaviour
{
    public int waypointIndex;
}
