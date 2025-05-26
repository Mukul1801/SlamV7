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

    // UI controls
    //public Button createPathButton;
    //public Button manualPathButton;
    //public Button loadPathButton;
    //public Button savePathButton;
    //public Button startNavigationButton;
    //public Button stopNavigationButton;
    //public Button scanEnvironmentButton;

    // Manager references
    private NavigationManager navigationManager;
    //private EnvironmentScanManager environmentScanManager;

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

        // Initialize environment map root
        if (environmentMapRoot == null)
        {
            environmentMapRoot = new GameObject("EnvironmentMapRoot");
            environmentMapRoot.transform.SetParent(transform);
        }

        // Create storage directory if it doesn't exist
        if (!Directory.Exists(GetAndroidExternalStoragePath() + "/" + "ARCoreTrackables"))
        {
            savedLocationPanel.SetActive(false);
            Directory.CreateDirectory(GetAndroidExternalStoragePath() + "/" + "ARCoreTrackables");
        }
        else
        {
            // Populate saved locations panel
            PopulateSavedLocations();
        }
    }

    void Start()
    {
        // Get AR components
        raycastManager = GetComponent<ARRaycastManager>();
        arPlaneManager = GetComponent<ARPlaneManager>();
        arPointCloudManager = GetComponent<ARPointCloudManager>();
        xrOrigin = GetComponent<XROrigin>();
        raycastHitList = new List<ARRaycastHit>();

        // Initialize camera reference
        if (mainCamera == null)
            mainCamera = Camera.main;

        // Initial UI setup
        if (textRefs != null)
            textRefs.GetComponent<TextMeshProUGUI>().text = "AR Navigation Assistant - Ready\n";

        // Initialize managers
        SetupManagers();

        // Setup button listeners
        //SetupUIButtons();

        // Start in environment scanning mode by default
        SwitchToEnvironmentScanningMode();
    }

    private void SetupManagers()
    {
        // Setup Navigation Manager
        navigationManager = GetComponent<NavigationManager>();
        if (navigationManager == null)
            navigationManager = FindObjectOfType<NavigationManager>();
        
        // Setup NavigationEnhancer
        navigationEnhancer = GetComponent<NavigationEnhancer>();
        if (navigationEnhancer == null)
            navigationEnhancer = FindObjectOfType<NavigationEnhancer>();
            
        // Connect NavigationEnhancer to this HitPointManager
        if (navigationEnhancer != null)
            navigationEnhancer.hitPointManager = this;

        // Setup Environment Scan Manager
        //environmentScanManager = GetComponent<EnvironmentScanManager>();
        //if (environmentScanManager == null)
        //    environmentScanManager = gameObject.AddComponent<EnvironmentScanManager>();
        //environmentScanManager.hitPointManager = this;
    }

    //private void SetupUIButtons()
    //{
    //    if (createPathButton != null)
    //        createPathButton.onClick.AddListener(SwitchToPathCreationMode);

    //    if (manualPathButton != null)
    //        manualPathButton.onClick.AddListener(SwitchToManualPathCreationMode);

    //    if (loadPathButton != null)
    //        loadPathButton.onClick.AddListener(PromptFilenameToLoad);

    //    if (savePathButton != null)
    //        savePathButton.onClick.AddListener(PromptSavePathWithName);

    //    if (startNavigationButton != null)
    //        startNavigationButton.onClick.AddListener(StartNavigationMode);

    //    if (stopNavigationButton != null)
    //        stopNavigationButton.onClick.AddListener(StopNavigationMode);

    //    if (scanEnvironmentButton != null)
    //        scanEnvironmentButton.onClick.AddListener(SwitchToEnvironmentScanningMode);
    //}

    public void PopulateSavedLocations()
    {
        // Clear any existing buttons
        foreach (Transform child in savedLocationPrefabHolder.transform)
        {
            Destroy(child.gameObject);
        }

        // Check for JSON files first (enhanced maps)
        string[] jsonFiles = Directory.GetFiles(GetAndroidExternalStoragePath() + "/ARCoreTrackables", "*.json");
        
        // If no JSON files, fall back to regular CSV files
        if (jsonFiles.Length == 0)
        {
            // Add buttons for each saved CSV file
            string[] files = Directory.GetFiles(GetAndroidExternalStoragePath() + "/ARCoreTrackables", "*.csv");
            foreach (string file in files)
            {
                CreateSavedLocationButton(file);
            }
            savedLocationPanel.SetActive(true);
        }
        else
        {
            // Add buttons for JSON files (enhanced maps)
            foreach (string file in jsonFiles)
            {
                CreateSavedLocationButton(file);
            }
        }
    }
    
    private void CreateSavedLocationButton(string file)
    {
        GameObject go = Instantiate(savedLocationButtonPrefab);
        go.transform.SetParent(savedLocationPrefabHolder.transform);
        go.transform.localScale = new Vector3(1, 1, 1);
        go.SetActive(true);

        string fileName = Path.GetFileName(file);
        go.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = fileName;

        // Add click listener
        Button button = go.GetComponent<Button>();
        string fileNameCopy = fileName; // Create a copy for the closure
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

        // Auto generate path points in a grid pattern across the screen
        if (width < Screen.width || height < Screen.height)
        {
            // Raycast at current screen position
            //TrackableType.FeaturePoint |
            bool hasDetectedHit = raycastManager.Raycast(new Vector2(width, height), raycastHitList, TrackableType.PlaneWithinPolygon);

            if (hasDetectedHit)
            {
                for (int i = 0; i < raycastHitList.Count; i++)
                {
                    // Check if we already have a point at this position
                    Vector3 hitPosition = raycastHitList[i].pose.position;
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
                        // If we don't have a point here, create one for the path
                        AddNewWaypoint(raycastHitList[i].pose.position, raycastHitList[i].pose.rotation, WaypointType.PathPoint);
                        textRefs.GetComponent<TextMeshProUGUI>().text += "Path point added at " + hitPosition + "\n";
                    }
                }

                // Move to next grid position
                width += (Screen.width / 16);
                height += (Screen.height / 16);
            }
        }
        else if (width >= Screen.width && height >= Screen.height && allowSavingToCSV)
        {
            // Completed scanning the screen grid
            arPlaneManager.enabled = false;

            // Mark first and last points as start and end
            if (poseClassList.Count >= 2)
            {
                poseClassList[0].waypointType = WaypointType.StartPoint;
                UpdateWaypointVisual(0);

                poseClassList[poseClassList.Count - 1].waypointType = WaypointType.EndPoint;
                UpdateWaypointVisual(poseClassList.Count - 1);
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

        // Handle tap input to place points manually
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            Touch touch = Input.GetTouch(0);

            // Check if touch is on UI
            bool touchedUI = UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(touch.fingerId);

            if (!touchedUI)
            {
                // Raycast from touch position
                Ray ray = mainCamera.ScreenPointToRay(touch.position);
                if (raycastManager.Raycast(ray, raycastHitList, TrackableType.FeaturePoint | TrackableType.PlaneWithinPolygon))
                {
                    ARRaycastHit hit = raycastHitList[0]; // Use closest hit

                    // Check if we're near an existing point - if so, determine if we should mark it as obstacle or path
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
                        // Cycle through waypoint types for the nearby point
                        CycleWaypointType(nearbyPointIndex);
                    }
                    else
                    {
                        // Add new waypoint
                        AddNewWaypoint(hit.pose.position, hit.pose.rotation, WaypointType.PathPoint);

                        // If this is the first point, mark it as start
                        if (poseClassList.Count == 1)
                        {
                            poseClassList[0].waypointType = WaypointType.StartPoint;
                            UpdateWaypointVisual(0);
                        }
                        // If we have more than one point, mark the previous as path and this one as end
                        else
                        {
                            // If there was a previous end point, change it to path
                            for (int i = 0; i < poseClassList.Count - 1; i++)
                            {
                                if (poseClassList[i].waypointType == WaypointType.EndPoint)
                                {
                                    poseClassList[i].waypointType = WaypointType.PathPoint;
                                    UpdateWaypointVisual(i);
                                }
                            }

                            // Mark the new point as end
                            poseClassList[poseClassList.Count - 1].waypointType = WaypointType.EndPoint;
                            UpdateWaypointVisual(poseClassList.Count - 1);
                        }
                    }
                }
            }
        }
    }

    private void UpdateEnvironmentScanningMode()
    {
        textRefs.GetComponent<TextMeshProUGUI>().text = "Environment Scanning Mode\n";
        textRefs.GetComponent<TextMeshProUGUI>().text += "Detected planes: " + detectedPlanes.Count + "\n";
        textRefs.GetComponent<TextMeshProUGUI>().text += "Detected obstacles: " + GetObstacleCount() + "\n";

        // Perform environment scanning at intervals
        if (Time.time - lastScanTime > scanningInterval)
        {
            lastScanTime = Time.time;

            // Scan for new planes
            UpdateDetectedPlanes();

            // Detect potential obstacles
            if (Vector3.Distance(mainCamera.transform.position, lastScanPosition) > minDistanceBetweenPoints)
            {
                DetectObstaclesAround(mainCamera.transform.position);
                lastScanPosition = mainCamera.transform.position;
            }
        }
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

        // Enable AR planes for detecting surfaces
        arPlaneManager.enabled = true;
        foreach (var plane in arPlaneManager.trackables)
            plane.gameObject.SetActive(true);

        if (navigationManager.textToSpeech != null)
            navigationManager.textToSpeech.Speak("Automatic path creation mode activated. Please move slowly around your environment.");

        textRefs.GetComponent<TextMeshProUGUI>().text = "Creating path automatically. Please move slowly.\n";

        // If NavigationEnhancer is available, start a new map
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

        // Enable AR planes for detecting surfaces
        arPlaneManager.enabled = true;
        foreach (var plane in arPlaneManager.trackables)
            plane.gameObject.SetActive(true);

        if (navigationManager.textToSpeech != null)
            navigationManager.textToSpeech.Speak("Manual path creation mode activated. Tap to place path points. Double tap on points to change their type.");

        textRefs.GetComponent<TextMeshProUGUI>().text = "Creating path manually. Tap to place points.\n";

        // If NavigationEnhancer is available, start a new map
        if (navigationEnhancer != null)
        {
            navigationEnhancer.StartNewMap();
        }
    }

    public void SwitchToEnvironmentScanningMode()
    {
        StopAllCoroutines();
        isPathCreationMode = false;
        isManualPathCreationMode = false;
        isScanningMode = true;
        lastScanTime = 0;
        
        // Add null check for mainCamera
        if (mainCamera != null)
            lastScanPosition = mainCamera.transform.position;
        else
            Debug.LogWarning("mainCamera is null in SwitchToEnvironmentScanningMode");

        // Add null check for arPlaneManager
        if (arPlaneManager != null)
        {
            arPlaneManager.enabled = true;
            foreach (var plane in arPlaneManager.trackables)
                plane.gameObject.SetActive(true);
        }
        else
            Debug.LogWarning("arPlaneManager is null in SwitchToEnvironmentScanningMode");

        // Enable point cloud for better environment mapping
        if (arPointCloudManager != null)
            arPointCloudManager.enabled = true;

        if (navigationManager != null && navigationManager.textToSpeech != null)
            navigationManager.textToSpeech.Speak("Environment scanning mode activated. Please move around to map your surroundings.");

        // Add null check for textRefs
        if (textRefs != null)
            textRefs.GetComponent<TextMeshProUGUI>().text = "Scanning environment. Move around to map more area.\n";
        else
            Debug.LogWarning("textRefs is null in SwitchToEnvironmentScanningMode");

        // If NavigationEnhancer is available, use it for enhanced scanning
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
            // Disable plane visualization during navigation
            foreach (var plane in arPlaneManager.trackables)
                plane.gameObject.SetActive(false);

            // Start navigation
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
        // Create a unique ID for the waypoint
        string waypointId = Guid.NewGuid().ToString();

        // Add to the pose list
        poseClassList.Add(new PoseClass
        {
            trackingId = waypointId,
            distance = Vector3.Distance(mainCamera.transform.position, position).ToString(),
            position = position,
            rotation = rotation,
            waypointType = type
        });

        // Create visual representation
        CreateWaypointVisual(poseClassList.Count - 1);
    }

    public void CreateWaypointVisual(int poseIndex)
    {
        if (poseIndex < 0 || poseIndex >= poseClassList.Count)
            return;

        PoseClass pose = poseClassList[poseIndex];
        GameObject waypointObject = null;

        // Skip creating visuals for obstacle waypoints
        if (pose.waypointType == WaypointType.Obstacle)
            return;  // This will keep the obstacle in the list but not show it visually

        // Create appropriate visual based on waypoint type
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

            // Store reference to created waypoint
            instantiatedWaypoints.Add(waypointObject);

            // Add waypoint index as component for easy reference
            WaypointIdentifier identifier = waypointObject.AddComponent<WaypointIdentifier>();
            identifier.waypointIndex = poseIndex;
        }
    }

    public void UpdateWaypointVisual(int poseIndex)
    {
        if (poseIndex < 0 || poseIndex >= poseClassList.Count)
            return;

        // Find and destroy the old visual
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

        // Create new visual with updated type
        CreateWaypointVisual(poseIndex);
    }

    private void CycleWaypointType(int poseIndex)
    {
        if (poseIndex < 0 || poseIndex >= poseClassList.Count)
            return;

        // Cycle through waypoint types
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

        // Update the visual representation
        UpdateWaypointVisual(poseIndex);

        // Speak the new type
        if (navigationManager.textToSpeech != null)
        {
            navigationManager.textToSpeech.Speak("Point changed to " + poseClassList[poseIndex].waypointType.ToString());
        }
    }

    public void ClearCurrentWaypoints()
    {
        // Destroy all waypoint objects
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

            // If we haven't stored this plane yet
            if (!detectedPlanes.ContainsKey(planeId))
            {
                // Create a visual representation for the plane
                GameObject planeVisual = new GameObject("Plane_" + planeId);
                planeVisual.transform.SetParent(environmentMapRoot.transform);

                // Add a mesh renderer and material to visualize the plane
                MeshFilter meshFilter = planeVisual.AddComponent<MeshFilter>();
                MeshRenderer meshRenderer = planeVisual.AddComponent<MeshRenderer>();

                // Copy the mesh from the AR plane
                meshFilter.mesh = plane.GetComponent<MeshFilter>().mesh;

                // Set position and rotation
                planeVisual.transform.position = plane.transform.position;
                planeVisual.transform.rotation = plane.transform.rotation;
                planeVisual.transform.localScale = plane.transform.localScale;

                // Create a semi-transparent material
                Material planeMaterial = new Material(Shader.Find("Standard"));
                planeMaterial.color = new Color(0.2f, 0.8f, 0.3f, 0.3f); // Green, semi-transparent
                meshRenderer.material = planeMaterial;

                // Make the plane not visible by default (we'll use this for navigation data)
                planeVisual.SetActive(false);

                // Store reference to the plane
                detectedPlanes.Add(planeId, planeVisual);

                // Add floor surface waypoints if this is a horizontal plane
                if (plane.alignment == PlaneAlignment.HorizontalUp)
                {
                    AddWaypointsToPlane(plane);
                }
            }
        }
    }

    private void AddWaypointsToPlane(ARPlane plane)
    {
        // Get the plane's boundary points
        Vector2[] boundaryPoints2D = plane.boundary.ToArray();

        if (boundaryPoints2D.Length < 3)
            return;

        // Get the plane center and normal
        Vector3 planeCenter = plane.center;
        Vector3 planeNormal = plane.normal;

        // Create grid of waypoints on the plane
        float gridSize = 0.5f; // Adjust based on density needed

        // Calculate plane dimensions
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (Vector2 point2D in boundaryPoints2D)
        {
            // Convert 2D point to 3D in plane's local space (y is up in local space)
            Vector3 localPoint = new Vector3(point2D.x, 0, point2D.y);
            Vector3 worldPoint = plane.transform.TransformPoint(localPoint);

            if (worldPoint.x < minX) minX = worldPoint.x;
            if (worldPoint.x > maxX) maxX = worldPoint.x;
            if (worldPoint.z < minZ) minZ = worldPoint.z;
            if (worldPoint.z > maxZ) maxZ = worldPoint.z;
        }

        // Create grid of waypoints
        for (float x = minX; x <= maxX; x += gridSize)
        {
            for (float z = minZ; z <= maxZ; z += gridSize)
            {
                Vector3 potentialPoint = new Vector3(x, planeCenter.y, z);

                // Check if point is inside plane boundary
                if (IsPointInPolygon(potentialPoint, plane))
                {
                    // Check if we already have a point nearby
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
                        // Add as a potential path point
                        AddNewWaypoint(potentialPoint, Quaternion.LookRotation(Vector3.forward, planeNormal), WaypointType.PathPoint);
                    }
                }
            }
        }
    }

    private bool IsPointInPolygon(Vector3 point, ARPlane plane)
    {
        // Convert world point to plane's local space
        Vector3 localPoint = plane.transform.InverseTransformPoint(point);

        // Convert to 2D point (in ARPlane's boundary coordinate system)
        Vector2 point2D = new Vector2(localPoint.x, localPoint.z);

        // Get boundary points in local space
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
        // Cast rays in multiple directions to detect obstacles
        int rayCount = 8; // Number of rays to cast around
        float rayLength = 3.0f; // How far to cast rays

        for (int i = 0; i < rayCount; i++)
        {
            float angle = i * (360f / rayCount);
            Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;

            RaycastHit hit;
            if (Physics.Raycast(position, direction, out hit, rayLength))
            {
                // If hit something that's not a waypoint, mark as obstacle
                if (hit.collider != null && !hit.collider.CompareTag("Waypoint"))
                {
                    // Check if we already have an obstacle nearby
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
                        // Add obstacle waypoint
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

        // If NavigationEnhancer is available, use enhanced map saving
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

        // Enhanced CSV format with additional data
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
                    description = "Safe navigation point";
                    break;
            }

            csvContent.AppendLine($"{poseClass.trackingId},{poseClass.distance},{poseClass.position.x},{poseClass.position.y},{poseClass.position.z},{poseClass.rotation.x},{poseClass.rotation.y},{poseClass.rotation.z},{poseClass.rotation.w},{(int)poseClass.waypointType},{description}");
        }

        // Ensure directory exists
        string directory = Path.GetDirectoryName(path);
        if (!Directory.Exists(directory))
        {
            Debug.Log("Creating directory: " + directory);
            Directory.CreateDirectory(directory);
        }

        // Write file
        try
        {
            File.WriteAllText(path, csvContent.ToString());
            Debug.Log("File successfully saved to: " + path);

            if (navigationManager != null && navigationManager.textToSpeech != null)
            {
                int pathPoints = poseClassList.Count(p => p.waypointType == WaypointType.PathPoint);
                int obstacles = poseClassList.Count(p => p.waypointType == WaypointType.Obstacle);

                navigationManager.textToSpeech.Speak($"Path saved successfully with {pathPoints} path points and {obstacles} obstacles marked. You can use it for future safe navigation.");
            }

            if (textRefs != null)
            {
                textRefs.GetComponent<TextMeshProUGUI>().text = "File Saved At: " + path;
            }

            // Update saved locations panel
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
        // Generate a filename with timestamp
        string filename = "Path_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

        // Save the file
        SaveAllTheInformationToFile(filename);

        // Wait a moment to ensure saving completes
        StartCoroutine(ExitAfterDelay(2.0f));
    }

    private IEnumerator ExitAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        // Exit application
        Debug.Log("Exiting application");

#if UNITY_EDITOR
        // In editor, stop play mode
        UnityEditor.EditorApplication.isPlaying = false;
#else
    // On device, quit application
    Application.Quit();
#endif
    }

    public void PromptSavePathWithName()
    {
        // Show text input field for filename
        filenameInputField.gameObject.SetActive(true);
        filenameInputField.text = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        filenameInputField.onEndEdit.RemoveAllListeners();
        filenameInputField.onEndEdit.AddListener(SaveAllTheInformationToFile);

        if (navigationManager != null && navigationManager.textToSpeech != null)
        {
            navigationManager.textToSpeech.Speak("Enter a name to save this path, or use the default timestamp.");
        }
    }

    public void PromptFilenameToLoad()
    {
        // Show saved locations panel
        savedLocationPanel.SetActive(true);

        if (navigationManager != null && navigationManager.textToSpeech != null)
        {
            navigationManager.textToSpeech.Speak("Please select a saved location to navigate.");
        }
    }

    public void LoadPathFromFile(string filename)
    {
        // If NavigationEnhancer is available and the file is a JSON, use enhanced loading
        string jsonPath = Path.Combine(GetAndroidExternalStoragePath(), "ARCoreTrackables", filename);
        if (navigationEnhancer != null && filename.EndsWith(".json"))
        {
            navigationEnhancer.LoadEnhancedMap(Path.GetFileNameWithoutExtension(filename));
            savedLocationPanel.SetActive(false);
            return;
        }

        // Legacy loading method for CSV files
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

            for (int i = 1; i < lines.Length; i++) // skip header
            {
                var tokens = lines[i].Split(',');
                if (tokens.Length >= 10) // Ensure we have enough data
                {
                    try
                    {
                        WaypointType type = WaypointType.PathPoint;
                        if (tokens.Length >= 10)
                        {
                            type = (WaypointType)int.Parse(tokens[9]);
                        }

                        PoseClass poseClass = new PoseClass
                        {
                            trackingId = tokens[0],
                            distance = tokens[1],
                            position = new Vector3(
                                float.Parse(tokens[2]),
                                float.Parse(tokens[3]),
                                float.Parse(tokens[4])
                            ),
                            rotation = new Quaternion(
                                float.Parse(tokens[5]),
                                float.Parse(tokens[6]),
                                float.Parse(tokens[7]),
                                float.Parse(tokens[8])
                            ),
                            waypointType = type
                        };

                        poseClassList.Add(poseClass);

                        // Count types
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

            // Create waypoint visuals for all loaded points
            for (int i = 0; i < poseClassList.Count; i++)
            {
                CreateWaypointVisual(i);
            }

            // Hide the saved locations panel
            savedLocationPanel.SetActive(false);

            // Provide audio feedback
            if (navigationManager != null && navigationManager.textToSpeech != null)
            {
                navigationManager.textToSpeech.Speak(
                    $"Loaded {poseClassList.Count} waypoints. {pathPoints} safe path points, " +
                    $"{obstacles} obstacles, {startPoints} start points, and {endPoints} end points."
                );
            }

            textRefs.GetComponent<TextMeshProUGUI>().text = $"Loaded path: {filename}\n" +
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
            // For testing in editor
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

// Helper class to identify waypoints
public class WaypointIdentifier : MonoBehaviour
{
    public int waypointIndex;
}
