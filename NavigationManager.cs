using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;
using TMPro;

public class NavigationManager : MonoBehaviour
{
    // References
    public HitPointManager hitPointManager;
    public SafePathPlanner safePathPlanner;
    public AudioSource audioSource;
    public TextToSpeech textToSpeech;
    public NavigationEnhancer navigationEnhancer;
    public Enhanced3DMapManager enhanced3DMapManager; // New integration

    [Header("Audio Feedback")]
    public AudioClip waypointReachedSound;
    public AudioClip turnLeftSound;
    public AudioClip turnRightSound;
    public AudioClip straightAheadSound;
    public AudioClip destinationReachedSound;
    public AudioClip obstacleNearbySound;
    public AudioClip pathStartedSound;
    public AudioClip errorSound;
    public AudioClip beaconSound;

    [Header("3D Audio")]
    public bool use3DAudio = true;
    public float maxAudioDistance = 20f;
    public Transform audioSourceTransform;
    public AudioSource beaconAudioSource;

    [Header("Haptic Feedback")]
    public bool useVibration = true;
    public float vibrationDuration = 0.5f;
    public float vibrationIntensity = 1.0f;

    [Header("Navigation Settings")]
    public float waypointReachedDistance = 1.0f;
    public float directionUpdateFrequency = 1.0f;
    public float obstacleWarningDistance = 2.5f;
    public bool isNavigating = false;
    public bool useDistanceBasedGuidance = true;
    public float minDistanceForUpdate = 0.5f;
    public float turnAngleThreshold = 15f;

    [Header("Accessibility")]
    public bool useDetailedAudioDescriptions = true;
    public bool useCoarseSpatialNavigation = false;
    public TextMeshProUGUI debugTextDisplay;
    public bool enableTapForNextDirection = true;
    public float hazardProximityWarningFrequency = 0.5f;
    public int hapticFeedbackMode = 1;
    public bool useContinuousBeacon = true;
    public bool useRightOrLeftCalls = true;

    [Header("Advanced Settings")]
    public bool useProgressiveGuidance = true;
    public float userSpeedThreshold = 0.5f;
    public float pathCompletionAnnouncementDistance = 5.0f;
    public float beaconMinimumVolume = 0.1f;
    public float beaconMaximumVolume = 0.7f;
    public bool announceLandmarks = true;
    public float environmentAnnouncementInterval = 20f;

    // Private state variables
    private float lastDirectionUpdate = 0f;
    private float lastEnvironmentAnnouncement = 0f;
    private Vector3 lastUpdatePosition;
    private Vector3 lastUserPosition;
    private float lastUserSpeed = 0f;
    private float lastObstacleWarningTime = 0f;
    private string debugText = "";
    private List<string> spokenDirections = new List<string>();
    private bool destinationAnnouncementStarted = false;
    private bool isBeaconActive = false;
    private Coroutine beaconCoroutine;
    private int currentBeaconSegment = 0;
    private bool waypointAnnouncementNeeded = false;
    private string nextWaypointDescription = "";

    void Start()
    {
        // Find references if not set
        if (hitPointManager == null)
            hitPointManager = FindObjectOfType<HitPointManager>();

        if (safePathPlanner == null)
            safePathPlanner = GetComponent<SafePathPlanner>();

        if (safePathPlanner == null)
            safePathPlanner = gameObject.AddComponent<SafePathPlanner>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        if (textToSpeech == null)
            textToSpeech = FindObjectOfType<TextToSpeech>();

        if (textToSpeech == null && FindObjectOfType<TextToSpeech>() == null)
            textToSpeech = gameObject.AddComponent<TextToSpeech>();

        if (navigationEnhancer == null)
            navigationEnhancer = FindObjectOfType<NavigationEnhancer>();

        // NEW: Integrate with Enhanced3DMapManager
        IntegrateWith3DMapManager();

        // Initialize NavigationEnhancer reference if available
        if (navigationEnhancer != null)
        {
            navigationEnhancer.navigationManager = this;
            navigationEnhancer.hitPointManager = hitPointManager;
        }

        // Set up 3D audio
        if (use3DAudio && audioSourceTransform == null)
            audioSourceTransform = transform;

        if (audioSource != null)
        {
            audioSource.spatialBlend = use3DAudio ? 1.0f : 0.0f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 1.0f;
            audioSource.maxDistance = maxAudioDistance;
        }

        // Set up beacon audio source if needed
        if (useContinuousBeacon && beaconAudioSource == null)
        {
            GameObject beaconObj = new GameObject("BeaconAudioSource");
            beaconObj.transform.parent = transform;
            beaconAudioSource = beaconObj.AddComponent<AudioSource>();
            beaconAudioSource.loop = true;
            beaconAudioSource.spatialBlend = 1.0f;
            beaconAudioSource.rolloffMode = AudioRolloffMode.Linear;
            beaconAudioSource.minDistance = 1.0f;
            beaconAudioSource.maxDistance = maxAudioDistance * 1.5f;
            beaconAudioSource.volume = beaconMinimumVolume;
            beaconAudioSource.clip = beaconSound;
            beaconAudioSource.playOnAwake = false;
        }

        // Initialize path planner
        if (safePathPlanner != null)
        {
            safePathPlanner.hitPointManager = hitPointManager;
            safePathPlanner.navigationManager = this;
        }

        // Initialize state variables
        lastUpdatePosition = Camera.main.transform.position;
        lastUserPosition = Camera.main.transform.position;
    }

    // NEW: Integration method for Enhanced3DMapManager
    public void IntegrateWith3DMapManager()
    {
        enhanced3DMapManager = FindObjectOfType<Enhanced3DMapManager>();
        if (enhanced3DMapManager != null)
        {
            Debug.Log("NavigationManager integrated with Enhanced3DMapManager");
        }
    }

    void Update()
    {
        if (isNavigating)
        {
            // Track user movement speed
            float deltaTime = Time.deltaTime;
            Vector3 userPosition = Camera.main.transform.position;
            float distanceMoved = Vector3.Distance(lastUserPosition, userPosition);
            lastUserSpeed = distanceMoved / deltaTime;
            lastUserPosition = userPosition;

            // Check if we should use enhanced navigation
            if (enhanced3DMapManager != null && enhanced3DMapManager.IsNavigating())
            {
                // Enhanced3DMapManager is handling navigation, just provide audio support
                UpdateBeacon();
            }
            else
            {
                // Use traditional navigation
                NavigateAlongPath();
                UpdateBeacon();
            }

            // Check for tap input if enabled
            if (enableTapForNextDirection && Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                bool isTapOnUI = UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);

                if (!isTapOnUI)
                {
                    GiveDirectionToNextPathPoint(true);
                }
            }

            // Periodic environment descriptions
            if (Time.time - lastEnvironmentAnnouncement > environmentAnnouncementInterval)
            {
                if (navigationEnhancer != null)
                {
                    navigationEnhancer.DescribeSurroundings();
                    lastEnvironmentAnnouncement = Time.time;
                }
            }
        }

        // Update debug text if enabled
        if (debugTextDisplay != null)
        {
            debugTextDisplay.text = debugText;
        }
    }

    public void StartNavigation()
    {
        // NEW: Check if Enhanced3DMapManager should handle navigation
        if (enhanced3DMapManager != null && enhanced3DMapManager.GetCurrentMap() != null)
        {
            enhanced3DMapManager.StartEnhancedNavigation();
            isNavigating = true; // Set this for compatibility
            SpeakMessage("Starting enhanced navigation with 3D mapping.");
            return;
        }
    
        // Use NavigationEnhancer if available and map is validated
        if (navigationEnhancer != null && navigationEnhancer.IsMapValidForNavigation())
        {
            navigationEnhancer.StartEnhancedNavigation();
            isNavigating = true; // NEW: Ensure flag is set
            return;
        }
    
        // Regular navigation start
        if (hitPointManager.poseClassList.Count > 0)
        {
            // NEW: Exit any active modes in HitPointManager
            hitPointManager.StopAllCoroutines();
            hitPointManager.isPathCreationMode = false;
            hitPointManager.isManualPathCreationMode = false;
            hitPointManager.isScanningMode = false;
    
            bool hasStartPoint = hitPointManager.poseClassList.Any(p => p.waypointType == WaypointType.StartPoint);
            bool hasEndPoint = hitPointManager.poseClassList.Any(p => p.waypointType == WaypointType.EndPoint);

        if (!hasStartPoint || !hasEndPoint)
        {
            if (hitPointManager.poseClassList.Count >= 2)
            {
                hitPointManager.poseClassList[0].waypointType = WaypointType.StartPoint;
                hitPointManager.poseClassList[hitPointManager.poseClassList.Count - 1].waypointType = WaypointType.EndPoint;

                SpeakMessage("No explicit start and end points found. Using first and last points instead.");
            }
            else
            {
                SpeakMessage("Error: At least two points are needed for navigation - a start and an end point.");
                PlaySound(errorSound);
                return;
            }
        }

        bool pathPlanned = safePathPlanner.PlanSafePath();

        if (pathPlanned)
        {
            isNavigating = true;
            lastUpdatePosition = Camera.main.transform.position;
            destinationAnnouncementStarted = false;
            lastEnvironmentAnnouncement = Time.time;

            spokenDirections.Clear();

            PlaySound(pathStartedSound);

            SpeakMessage("Navigation started. Follow the audio cues to safely reach your destination.");

            if (useContinuousBeacon)
            {
                StartBeacon();
            }

            if (useVibration)
                Vibrate();

            GiveDirectionToNextPathPoint(true);

            UpdateDebugText("Navigation active - follow audio cues.");

            if (navigationEnhancer != null && useDetailedAudioDescriptions)
            {
                StartCoroutine(DelayedPathDescription(3.0f));
            }
        }
        else
        {
            SpeakMessage("Unable to find a safe path to your destination. Please try again in a different location.");
            PlaySound(errorSound);
            UpdateDebugText("Error: No safe path found");
        }
    }
    else
    {
        SpeakMessage("No waypoints available. Please load a path or create a new one.");
        PlaySound(errorSound);
        UpdateDebugText("Error: No waypoints available");
    }
}

    private IEnumerator DelayedPathDescription(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (navigationEnhancer != null)
        {
            navigationEnhancer.DescribePathQuality();
        }
    }

    public void StopNavigation()
    {
        if (isNavigating)
        {
            isNavigating = false;

            // NEW: Stop enhanced navigation if active
            if (enhanced3DMapManager != null && enhanced3DMapManager.IsNavigating())
            {
                enhanced3DMapManager.StopNavigation();
            }

            StopBeacon();

            if (textToSpeech != null)
                textToSpeech.StopSpeaking();

            SpeakMessage("Navigation stopped.");
            UpdateDebugText("Navigation stopped");
        }
    }

    private void NavigateAlongPath()
    {
        Vector3 userPosition = Camera.main.transform.position;

        int previousPathIndex = safePathPlanner.GetCurrentPathIndex();
        safePathPlanner.UpdateCurrentPathIndex(userPosition);
        int currentPathIndex = safePathPlanner.GetCurrentPathIndex();

        if (currentPathIndex > previousPathIndex && waypointAnnouncementNeeded)
        {
            AnnounceWaypointReached();
        }

        Vector3 nextPathPoint = safePathPlanner.GetNextPathPoint();

        Vector3 horizontalUserPosition = new Vector3(userPosition.x, 0, userPosition.z);
        Vector3 horizontalPathPoint = new Vector3(nextPathPoint.x, 0, nextPathPoint.z);
        float distance = Vector3.Distance(horizontalUserPosition, horizontalPathPoint);

        UpdateDebugText("Distance to next point: " + distance.ToString("F2") + "m\n" +
                        "Speed: " + lastUserSpeed.ToString("F2") + "m/s");

        if (Time.time - lastObstacleWarningTime > hazardProximityWarningFrequency)
        {
            WarnAboutNearbyObstacles(userPosition);
            lastObstacleWarningTime = Time.time;
        }

        bool shouldUpdate = false;

        if (useDistanceBasedGuidance)
        {
            float distanceMoved = Vector3.Distance(lastUpdatePosition, userPosition);

            if (distanceMoved > minDistanceForUpdate ||
                (useProgressiveGuidance && lastUserSpeed < userSpeedThreshold && Time.time - lastDirectionUpdate > directionUpdateFrequency * 2))
            {
                shouldUpdate = true;
                lastUpdatePosition = userPosition;
            }
        }
        else
        {
            if (Time.time - lastDirectionUpdate > directionUpdateFrequency)
            {
                shouldUpdate = true;
            }
        }

        if (shouldUpdate)
        {
            GiveDirectionToNextPathPoint();
            lastDirectionUpdate = Time.time;
        }

        CheckDestinationReached(userPosition);

        if (!destinationAnnouncementStarted)
        {
            PoseClass endPose = hitPointManager.poseClassList.FirstOrDefault(p => p.waypointType == WaypointType.EndPoint);
            if (endPose != null)
            {
                float distanceToDestination = Vector3.Distance(
                    new Vector3(userPosition.x, 0, userPosition.z),
                    new Vector3(endPose.position.x, 0, endPose.position.z));

                if (distanceToDestination < pathCompletionAnnouncementDistance)
                {
                    SpeakMessage("Approaching your destination. About " +
                                distanceToDestination.ToString("F1") + " meters to go.");
                    destinationAnnouncementStarted = true;
                }
            }
        }
    }

    private void WarnAboutNearbyObstacles(Vector3 userPosition)
    {
        //bool obstacleWarningGiven = false;
        List<PoseClass> nearbyObstacles = new List<PoseClass>();

        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                float obstacleDistance = Vector3.Distance(
                    new Vector3(userPosition.x, 0, userPosition.z),
                    new Vector3(pose.position.x, 0, pose.position.z));

                if (obstacleDistance < obstacleWarningDistance)
                {
                    nearbyObstacles.Add(pose);
                }
            }
        }

        if (nearbyObstacles.Count > 0)
        {
            nearbyObstacles.Sort((a, b) =>
                Vector3.Distance(userPosition, a.position).CompareTo(
                Vector3.Distance(userPosition, b.position)));

            PoseClass closestObstacle = nearbyObstacles[0];
            float obstacleDistance = Vector3.Distance(userPosition, closestObstacle.position);

            Vector3 directionToObstacle = closestObstacle.position - userPosition;
            directionToObstacle.y = 0;

            Vector3 userForward = Camera.main.transform.forward;
            userForward.y = 0;
            userForward.Normalize();

            float angle = Vector3.SignedAngle(userForward, directionToObstacle, Vector3.up);

            string direction = GetDirectionName(angle);

            if (Mathf.Abs(angle) < 60f)
            {
                float warningVolume = Mathf.Lerp(0.3f, 1.0f, 1.0f - (obstacleDistance / obstacleWarningDistance));

                if (use3DAudio && audioSource != null)
                {
                    audioSource.transform.position = closestObstacle.position;
                }

                PlaySound(obstacleNearbySound, warningVolume);
                //obstacleWarningGiven = true;

                if (useVibration)
                {
                    float intensity = Mathf.Lerp(0.3f, 1.0f, 1.0f - (obstacleDistance / obstacleWarningDistance));

                    if (hapticFeedbackMode == 2)
                    {
                        if (angle < -30f)
                            VibratePattern(new float[] { intensity, 0.1f, 0, 0.1f, intensity, 0.2f });
                        else if (angle > 30f)
                            VibratePattern(new float[] { intensity, 0.2f, 0, 0.1f, intensity, 0.1f });
                        else
                            Vibrate(intensity);
                    }
                    else
                    {
                        Vibrate(intensity);
                    }
                }

                string intensityWord = "";
                if (obstacleDistance < 1.0f)
                    intensityWord = "very close ";
                else if (obstacleDistance < 1.5f)
                    intensityWord = "close ";

                SpeakMessage("Caution! " + intensityWord + "Obstacle " + direction + ", " +
                             obstacleDistance.ToString("F1") + " meters away.");

                UpdateDebugText("⚠️ Obstacle " + direction + ": " + obstacleDistance.ToString("F1") + "m");
            }
        }
    }

    private void GiveDirectionToNextPathPoint(bool forceUpdate = false)
    {
        // Use enhanced directions if NavigationEnhancer is available
        if (navigationEnhancer != null && !forceUpdate)
        {
            Vector3 enhancedUserPos = Camera.main.transform.position;
            Vector3 nextPoint = safePathPlanner.GetNextPathPoint();

            Vector3 enhancedUserFwd = Camera.main.transform.forward;
            enhancedUserFwd.y = 0;
            enhancedUserFwd.Normalize();

            Vector3 directionToPoint = nextPoint - enhancedUserPos;
            directionToPoint.y = 0;

            if (directionToPoint.magnitude > 0.1f)
            {
                directionToPoint.Normalize();

                float enhancedAngle = Vector3.SignedAngle(enhancedUserFwd, directionToPoint, Vector3.up);
                float enhancedDistance = Vector3.Distance(enhancedUserPos, nextPoint);

                navigationEnhancer.ProvideEnhancedDirections(enhancedUserPos, nextPoint, enhancedAngle, enhancedDistance);
                return;
            }
        }

        // Fall back to regular direction guidance
        Vector3 nextPathPoint = safePathPlanner.GetNextPathPoint();
        int currentPathIndex = safePathPlanner.GetCurrentPathIndex();
        bool isLastPoint = currentPathIndex >= safePathPlanner.GetPathCount() - 1;

        Transform cameraTransform = Camera.main.transform;
        Vector3 userPosition = cameraTransform.position;
        Vector3 userForward = cameraTransform.forward;
        userForward.y = 0;
        userForward.Normalize();

        Vector3 directionToPathPoint = nextPathPoint - userPosition;
        directionToPathPoint.y = 0;

        if (directionToPathPoint.magnitude < 0.1f && !forceUpdate)
            return;

        directionToPathPoint.Normalize();

        float angle = Vector3.SignedAngle(userForward, directionToPathPoint, Vector3.up);

        string direction = GetDirectionName(angle);
        AudioClip directionSound = GetDirectionSound(angle);

        float distance = Vector3.Distance(userPosition, nextPathPoint);
        string distanceStr = distance.ToString("F1");

        bool isSignificantTurn = Mathf.Abs(angle) > turnAngleThreshold;

        string directionMessage;

        if (isLastPoint)
        {
            directionMessage = "Your destination is " + direction + ", " + distanceStr + " meters away.";
        }
        else
        {
            string pathDescription = "";
            if (useProgressiveGuidance && lastUserSpeed < userSpeedThreshold)
            {
                pathDescription = safePathPlanner.GetUpcomingPathDescription(currentPathIndex, 3);
                if (!string.IsNullOrEmpty(pathDescription))
                {
                    pathDescription = " " + pathDescription;
                }
            }

            if (useCoarseSpatialNavigation)
            {
                directionMessage = "Go " + direction + ", " + distanceStr + " meters.";
            }
            else if (isSignificantTurn)
            {
                directionMessage = "Turn " + direction + " and continue for " + distanceStr + " meters." + pathDescription;
            }
            else
            {
                directionMessage = "Continue " + direction + " for " + distanceStr + " meters." + pathDescription;
            }

            if (currentPathIndex < safePathPlanner.GetPathCount() - 1)
            {
                waypointAnnouncementNeeded = true;
                nextWaypointDescription = "Waypoint reached. ";

                int nextObstacleCount = CountNearbyObstacles(nextPathPoint, 3.0f);
                if (nextObstacleCount > 0)
                {
                    nextWaypointDescription += nextObstacleCount == 1
                        ? "There is an obstacle nearby. "
                        : "There are " + nextObstacleCount + " obstacles nearby. ";
                }

                if (currentPathIndex + 1 < safePathPlanner.GetPathCount() - 1)
                {
                    Vector3 pointAfterNext = safePathPlanner.GetPathPointAt(currentPathIndex + 2);
                    Vector3 nextSegmentDirection = pointAfterNext - nextPathPoint;
                    nextSegmentDirection.y = 0;
                    nextSegmentDirection.Normalize();

                    float turnAngle = Vector3.SignedAngle(directionToPathPoint, nextSegmentDirection, Vector3.up);

                    if (Mathf.Abs(turnAngle) > turnAngleThreshold)
                    {
                        string turnDirection = turnAngle > 0 ? "right" : "left";
                        nextWaypointDescription += "Prepare to turn " + turnDirection + ". ";
                    }
                }
            }
        }

        if (forceUpdate || !spokenDirections.Contains(directionMessage))
        {
            UpdateDebugText("Next point: " + distance.ToString("F1") + "m" +
                            "\nDirection: " + direction +
                            "\nAngle: " + angle.ToString("F0") + "°");

            spokenDirections.Add(directionMessage);
            if (spokenDirections.Count > 5)
                spokenDirections.RemoveAt(0);

            SpeakMessage(directionMessage);

            if (use3DAudio && audioSource != null)
            {
                audioSource.transform.position = nextPathPoint;
            }

            if (directionSound != null)
                PlaySound(directionSound);

            if (useVibration && isSignificantTurn)
            {
                if (hapticFeedbackMode == 2)
                {
                    if (angle < -turnAngleThreshold)
                    {
                        VibratePattern(new float[] { 0.8f, 0.1f, 0, 0.1f, 0.8f, 0.1f });
                    }
                    else if (angle > turnAngleThreshold)
                    {
                        VibratePattern(new float[] { 0.8f, 0.3f });
                    }
                }
                else
                {
                    Vibrate();
                }
            }
        }
    }

    private int CountNearbyObstacles(Vector3 position, float radius)
    {
        int count = 0;
        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                float distance = Vector3.Distance(
                    new Vector3(position.x, 0, position.z),
                    new Vector3(pose.position.x, 0, pose.position.z));

                if (distance < radius)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private void AnnounceWaypointReached()
    {
        PlaySound(waypointReachedSound);

        if (useVibration && hapticFeedbackMode >= 1)
        {
            Vibrate(0.6f, 0.15f);
        }

        if (!string.IsNullOrEmpty(nextWaypointDescription))
        {
            SpeakMessage(nextWaypointDescription);
        }

        waypointAnnouncementNeeded = false;
        nextWaypointDescription = "";
    }

    private string GetDirectionName(float angle)
    {
        if (useCoarseSpatialNavigation)
        {
            if (Mathf.Abs(angle) < 22.5f)
            {
                return "straight ahead";
            }
            else if (angle < -157.5f || angle > 157.5f)
            {
                return "behind you";
            }
            else if (angle < -112.5f)
            {
                return "behind you to the left";
            }
            else if (angle < -67.5f)
            {
                return "to your left";
            }
            else if (angle < -22.5f)
            {
                return "slightly left";
            }
            else if (angle < 67.5f)
            {
                return "slightly right";
            }
            else if (angle < 112.5f)
            {
                return "to your right";
            }
            else
            {
                return "behind you to the right";
            }
        }
        else
        {
            if (Mathf.Abs(angle) < 10f)
            {
                return "straight ahead";
            }
            else if (angle < -170f || angle > 170f)
            {
                return "behind you";
            }
            else if (angle < -135f)
            {
                return "behind you to the left";
            }
            else if (angle < -90f)
            {
                return "to your left";
            }
            else if (angle < -45f)
            {
                return "forward left";
            }
            else if (angle < -10f)
            {
                return "slightly left";
            }
            else if (angle < 45f)
            {
                return "slightly right";
            }
            else if (angle < 90f)
            {
                return "forward right";
            }
            else if (angle < 135f)
            {
                return "to your right";
            }
            else
            {
                return "behind you to the right";
            }
        }
    }

    private AudioClip GetDirectionSound(float angle)
    {
        if (Mathf.Abs(angle) < 15f)
        {
            return straightAheadSound;
        }
        else if (angle < 0f)
        {
            return turnLeftSound;
        }
        else
        {
            return turnRightSound;
        }
    }

    private void StartBeacon()
    {
        if (beaconAudioSource != null && beaconSound != null)
        {
            StopBeacon();

            isBeaconActive = true;
            beaconAudioSource.clip = beaconSound;
            beaconAudioSource.Play();

            beaconCoroutine = StartCoroutine(UpdateBeaconPosition());
        }
    }

    private void StopBeacon()
    {
        if (beaconAudioSource != null && isBeaconActive)
        {
            beaconAudioSource.Stop();
            isBeaconActive = false;
        }

        if (beaconCoroutine != null)
        {
            StopCoroutine(beaconCoroutine);
            beaconCoroutine = null;
        }
    }

    private IEnumerator UpdateBeaconPosition()
    {
        while (isBeaconActive && isNavigating)
        {
            if (safePathPlanner != null && safePathPlanner.GetPathCount() > safePathPlanner.GetCurrentPathIndex())
            {
                Vector3 nextPoint = safePathPlanner.GetNextPathPoint();

                int lookAheadCount = Mathf.Min(3, safePathPlanner.GetPathCount() - safePathPlanner.GetCurrentPathIndex() - 1);
                if (lookAheadCount > 0)
                {
                    Vector3 lookAheadPoint = safePathPlanner.GetPathPointAt(safePathPlanner.GetCurrentPathIndex() + lookAheadCount);
                    nextPoint = Vector3.Lerp(nextPoint, lookAheadPoint, 0.3f);
                }

                if (beaconAudioSource != null)
                {
                    beaconAudioSource.transform.position = nextPoint;
                }
            }

            yield return new WaitForSeconds(0.2f);
        }
    }

    private void UpdateBeacon()
    {
        if (!isBeaconActive || beaconAudioSource == null)
            return;

        if (safePathPlanner != null && safePathPlanner.GetPathCount() > safePathPlanner.GetCurrentPathIndex())
        {
            Vector3 nextPoint = safePathPlanner.GetNextPathPoint();
            Vector3 userPos = Camera.main.transform.position;

            Vector3 directionToPoint = nextPoint - userPos;
            directionToPoint.y = 0;
            directionToPoint.Normalize();

            Vector3 userForward = Camera.main.transform.forward;
            userForward.y = 0;
            userForward.Normalize();

            float facingDot = Vector3.Dot(userForward, directionToPoint);

            float targetVolume = Mathf.Lerp(beaconMinimumVolume, beaconMaximumVolume, (facingDot + 1) / 2);
            beaconAudioSource.volume = Mathf.Lerp(beaconAudioSource.volume, targetVolume, Time.deltaTime * 2);
        }
    }

    private void CheckDestinationReached(Vector3 userPosition)
    {
        PoseClass endPose = hitPointManager.poseClassList.FirstOrDefault(p => p.waypointType == WaypointType.EndPoint);
        if (endPose == null && hitPointManager.poseClassList.Count > 0)
        {
            endPose = hitPointManager.poseClassList[hitPointManager.poseClassList.Count - 1];
        }

        if (endPose != null)
        {
            float distanceToDestination = Vector3.Distance(
                new Vector3(userPosition.x, 0, userPosition.z),
                new Vector3(endPose.position.x, 0, endPose.position.z));

            if (distanceToDestination < waypointReachedDistance)
            {
                EndNavigation();
            }
        }
    }

    private void EndNavigation()
    {
        isNavigating = false;

        StopBeacon();

        PlaySound(destinationReachedSound);

        if (useVibration)
        {
            if (hapticFeedbackMode == 2)
            {
                VibratePattern(new float[] { 1.0f, 0.2f, 0, 0.1f, 1.0f, 0.2f, 0, 0.1f, 1.0f, 0.3f });
            }
            else
            {
                Vibrate(1.0f, 0.8f);
            }
        }

        SpeakMessage("You have reached your destination safely. Navigation completed.");
        UpdateDebugText("✅ Destination reached!");

        destinationAnnouncementStarted = false;
    }

    public void SpeakMessage(string message)
    {
        if (textToSpeech != null)
        {
            textToSpeech.Speak(message);
            UpdateDebugText("🔊 " + message);
        }
        else
        {
            Debug.LogWarning("TextToSpeech component not found. Message: " + message);
            UpdateDebugText("TTS not found: " + message);
        }
    }

    private void PlaySound(AudioClip clip, float volume = 1.0f)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip, volume);
        }
    }

    private void Vibrate(float intensity = 1.0f, float duration = -1.0f)
    {
        if (duration < 0)
            duration = vibrationDuration;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (intensity >= 1.0f)
        {
            Handheld.Vibrate();
        }
        else
        {
            long[] pattern = { 0, (long)(duration * 1000 * intensity) };
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    using (AndroidJavaObject vibrator = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                    {
                        vibrator.Call("vibrate", pattern, -1);
                    }
                }
            }
        }
#endif
    }

    private void VibratePattern(float[] pattern)
    {
        if (pattern == null || pattern.Length < 2)
            return;

#if UNITY_ANDROID && !UNITY_EDITOR
        long[] vibrationPattern = new long[pattern.Length];
        for (int i = 0; i < pattern.Length; i++)
        {
            vibrationPattern[i] = (long)(pattern[i] * 1000);
        }

        using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                using (AndroidJavaObject vibrator = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                {
                    vibrator.Call("vibrate", vibrationPattern, -1);
                }
            }
        }
#endif
    }

    private void UpdateDebugText(string text)
    {
        debugText = text;
    }

    public void ProvideNextInstruction()
    {
        if (isNavigating)
        {
            GiveDirectionToNextPathPoint(true);
        }
        else
        {
            if (navigationEnhancer != null)
            {
                navigationEnhancer.AnnounceWhatsAhead();
            }
            else
            {
                SpeakMessage("Navigation is not active. Tap and hold to start navigation or double tap to access the menu.");
            }
        }
    }

    public void ResetNavigation()
    {
        StopNavigation();
        destinationAnnouncementStarted = false;
        spokenDirections.Clear();
        lastDirectionUpdate = 0;
        lastObstacleWarningTime = 0;
    }
}
