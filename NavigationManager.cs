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
    public NavigationEnhancer navigationEnhancer; // Changed from NavigationHelper to NavigationEnhancer

    [Header("Audio Feedback")]
    public AudioClip waypointReachedSound;
    public AudioClip turnLeftSound;
    public AudioClip turnRightSound;
    public AudioClip straightAheadSound;
    public AudioClip destinationReachedSound;
    public AudioClip obstacleNearbySound;
    public AudioClip pathStartedSound;
    public AudioClip errorSound;
    public AudioClip beaconSound; // Continuous audio to indicate path direction
    
    [Header("3D Audio")]
    public bool use3DAudio = true;
    public float maxAudioDistance = 20f;
    public Transform audioSourceTransform;
    public AudioSource beaconAudioSource; // Separate source for continuous guidance sound

    [Header("Haptic Feedback")]
    public bool useVibration = true;
    public float vibrationDuration = 0.5f;
    public float vibrationIntensity = 1.0f;

    [Header("Navigation Settings")]
    public float waypointReachedDistance = 1.0f;
    public float directionUpdateFrequency = 2.0f;
    public float obstacleWarningDistance = 2.5f;
    public bool isNavigating = false;
    public bool useDistanceBasedGuidance = true;
    public float minDistanceForUpdate = 0.5f;
    public float turnAngleThreshold = 15f; // Degrees threshold to identify a turn

    [Header("Accessibility")]
    public bool useDetailedAudioDescriptions = true;
    public bool useCoarseSpatialNavigation = false; // Simpler directions for users with severe visual impairment
    public TextMeshProUGUI debugTextDisplay;
    public bool enableTapForNextDirection = true;
    public float hazardProximityWarningFrequency = 0.5f; // Seconds between warnings when near obstacles
    public int hapticFeedbackMode = 1; // 0 = off, 1 = basic, 2 = advanced patterns
    public bool useContinuousBeacon = true; // Use beacon sound to indicate path direction
    public bool useRightOrLeftCalls = true; // Call out right or left for directions

    [Header("Advanced Settings")]
    public bool useProgressiveGuidance = true; // More detailed guidance when moving slowly
    public float userSpeedThreshold = 0.5f; // m/s, threshold to determine if user is moving slowly
    public float pathCompletionAnnouncementDistance = 5.0f; // Start announcing distance to destination
    public float beaconMinimumVolume = 0.1f;
    public float beaconMaximumVolume = 0.7f;
    public bool announceLandmarks = true;
    public float environmentAnnouncementInterval = 20f; // Seconds between environment descriptions

    // Private state variables
    private float lastDirectionUpdate = 0f;
    private float lastEnvironmentAnnouncement = 0f;
    private Vector3 lastUpdatePosition;
    private Vector3 lastUserPosition;
    private float lastUserSpeed = 0f;
    private float lastObstacleWarningTime = 0f;
    private string debugText = "";
    private List<string> spokenDirections = new List<string>(); // Avoid repeating the same instruction
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
            beaconAudioSource.spatialBlend = 1.0f; // Always use 3D audio for beacon
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

            // Update navigation
            NavigateAlongPath();

            // Update audio beacon
            UpdateBeacon();

            // Check for tap input if enabled
            if (enableTapForNextDirection && Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                // Check if tap is on UI element
                bool isTapOnUI = UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);

                // If not on UI, provide next direction
                if (!isTapOnUI)
                {
                    GiveDirectionToNextPathPoint(true); // Force an update
                }
            }

            // Periodic environment descriptions - use NavigationEnhancer if available
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
        // Use NavigationEnhancer if available and map is validated
        if (navigationEnhancer != null && navigationEnhancer.IsMapValidForNavigation())
        {
            navigationEnhancer.StartEnhancedNavigation();
            return;
        }

        // Regular navigation start
        if (hitPointManager.poseClassList.Count > 0)
        {
            // First check if we have at least start and end points
            bool hasStartPoint = hitPointManager.poseClassList.Any(p => p.waypointType == WaypointType.StartPoint);
            bool hasEndPoint = hitPointManager.poseClassList.Any(p => p.waypointType == WaypointType.EndPoint);

            if (!hasStartPoint || !hasEndPoint)
            {
                // If not explicitly defined, check if we can use first and last points
                if (hitPointManager.poseClassList.Count >= 2)
                {
                    // Use first point as start and last point as end
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

            // Plan safe path
            bool pathPlanned = safePathPlanner.PlanSafePath();

            if (pathPlanned)
            {
                isNavigating = true;
                lastUpdatePosition = Camera.main.transform.position;
                destinationAnnouncementStarted = false;
                lastEnvironmentAnnouncement = Time.time;

                // Clear previous spoken directions
                spokenDirections.Clear();

                // Play start sound
                PlaySound(pathStartedSound);

                // Announce start of navigation
                SpeakMessage("Navigation started. Follow the audio cues to safely reach your destination.");

                // Start beacon if enabled
                if (useContinuousBeacon)
                {
                    StartBeacon();
                }

                // Vibrate to signal start
                if (useVibration)
                    Vibrate();

                // First direction update
                GiveDirectionToNextPathPoint(true);

                UpdateDebugText("Navigation active - follow audio cues.");
                
                // Provide path description
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
        
        // Use NavigationEnhancer for path description if available
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

            // Stop audio beacon
            StopBeacon();

            // Stop any ongoing speech
            if (textToSpeech != null)
                textToSpeech.StopSpeaking();

            SpeakMessage("Navigation stopped.");
            UpdateDebugText("Navigation stopped");
        }
    }

    private void NavigateAlongPath()
    {
        Vector3 userPosition = Camera.main.transform.position;

        // Update which path point we're heading toward
        int previousPathIndex = safePathPlanner.GetCurrentPathIndex();
        safePathPlanner.UpdateCurrentPathIndex(userPosition);
        int currentPathIndex = safePathPlanner.GetCurrentPathIndex();

        // Check if we've progressed to a new waypoint
        if (currentPathIndex > previousPathIndex && waypointAnnouncementNeeded)
        {
            AnnounceWaypointReached();
        }

        // Get the next path point to move toward
        Vector3 nextPathPoint = safePathPlanner.GetNextPathPoint();

        // Calculate distance to current target (horizontal plane only)
        Vector3 horizontalUserPosition = new Vector3(userPosition.x, 0, userPosition.z);
        Vector3 horizontalPathPoint = new Vector3(nextPathPoint.x, 0, nextPathPoint.z);
        float distance = Vector3.Distance(horizontalUserPosition, horizontalPathPoint);

        UpdateDebugText("Distance to next point: " + distance.ToString("F2") + "m\n" +
                        "Speed: " + lastUserSpeed.ToString("F2") + "m/s");

        // Check for nearby obstacles and warn if needed
        if (Time.time - lastObstacleWarningTime > hazardProximityWarningFrequency)
        {
            WarnAboutNearbyObstacles(userPosition);
            lastObstacleWarningTime = Time.time;
        }

        // Update direction guidance based on configured conditions
        bool shouldUpdate = false;

        if (useDistanceBasedGuidance)
        {
            // Update based on how far the user has moved
            float distanceMoved = Vector3.Distance(lastUpdatePosition, userPosition);

            // Update if we've moved enough or if we're moving slowly and using progressive guidance
            if (distanceMoved > minDistanceForUpdate ||
                (useProgressiveGuidance && lastUserSpeed < userSpeedThreshold && Time.time - lastDirectionUpdate > directionUpdateFrequency * 2))
            {
                shouldUpdate = true;
                lastUpdatePosition = userPosition;
            }
        }
        else
        {
            // Update based on time frequency
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

        // Check if destination reached
        CheckDestinationReached(userPosition);

        // Check if we should start announcing approach to destination
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
        bool obstacleWarningGiven = false;
        List<PoseClass> nearbyObstacles = new List<PoseClass>();

        // Check distance to all obstacles
        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                float obstacleDistance = Vector3.Distance(
                    new Vector3(userPosition.x, 0, userPosition.z),
                    new Vector3(pose.position.x, 0, pose.position.z));

                if (obstacleDistance < obstacleWarningDistance)
                {
                    // Add to nearby obstacles
                    nearbyObstacles.Add(pose);
                }
            }
        }

        // If multiple obstacles are nearby, warn about the closest one
        if (nearbyObstacles.Count > 0)
        {
            // Sort by distance
            nearbyObstacles.Sort((a, b) =>
                Vector3.Distance(userPosition, a.position).CompareTo(
                Vector3.Distance(userPosition, b.position)));

            // Get closest obstacle
            PoseClass closestObstacle = nearbyObstacles[0];
            float obstacleDistance = Vector3.Distance(userPosition, closestObstacle.position);

            // Determine direction to obstacle
            Vector3 directionToObstacle = closestObstacle.position - userPosition;
            directionToObstacle.y = 0;

            // Get user's forward direction
            Vector3 userForward = Camera.main.transform.forward;
            userForward.y = 0;
            userForward.Normalize();

            // Calculate angle between user's forward and obstacle
            float angle = Vector3.SignedAngle(userForward, directionToObstacle, Vector3.up);

            // Determine verbal direction
            string direction = GetDirectionName(angle);

            // Only warn if obstacle is somewhat in front of user (within about 120 degrees)
            if (Mathf.Abs(angle) < 60f)
            {
                // Play obstacle warning sound with volume based on proximity
                float warningVolume = Mathf.Lerp(0.3f, 1.0f, 1.0f - (obstacleDistance / obstacleWarningDistance));
                
                // Set 3D position of sound if using 3D audio
                if (use3DAudio && audioSource != null)
                {
                    audioSource.transform.position = closestObstacle.position;
                }
                
                PlaySound(obstacleNearbySound, warningVolume);
                obstacleWarningGiven = true;

                // Vibrate with intensity based on proximity
                if (useVibration)
                {
                    float intensity = Mathf.Lerp(0.3f, 1.0f, 1.0f - (obstacleDistance / obstacleWarningDistance));
                    
                    // Use different patterns based on obstacle direction
                    if (hapticFeedbackMode == 2)
                    {
                        if (angle < -30f) // Left
                            VibratePattern(new float[] { intensity, 0.1f, 0, 0.1f, intensity, 0.2f });
                        else if (angle > 30f) // Right
                            VibratePattern(new float[] { intensity, 0.2f, 0, 0.1f, intensity, 0.1f });
                        else // Center
                            Vibrate(intensity);
                    }
                    else
                    {
                        Vibrate(intensity);
                    }
                }

                // Speak warning message
                string intensityWord = "";
                if (obstacleDistance < 1.0f)
                    intensityWord = "very close ";
                else if (obstacleDistance < 1.5f)
                    intensityWord = "close ";

                SpeakMessage("Caution! " + intensityWord + "Obstacle " + direction + ", " +
                             obstacleDistance.ToString("F1") + " meters away.");

                // Update debug text
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
            
            // Get user's forward direction
            Vector3 enhancedUserFwd = Camera.main.transform.forward;
            enhancedUserFwd.y = 0;
            enhancedUserFwd.Normalize();
            
            // Get direction to next point
            Vector3 directionToPoint = nextPoint - enhancedUserPos;
            directionToPoint.y = 0;
            
            if (directionToPoint.magnitude > 0.1f)
            {
                directionToPoint.Normalize();
                
                // Calculate angle and distance
                float enhancedAngle = Vector3.SignedAngle(enhancedUserFwd, directionToPoint, Vector3.up);
                float enhancedDistance = Vector3.Distance(enhancedUserPos, nextPoint);
                
                // Use enhanced directions
                navigationEnhancer.ProvideEnhancedDirections(enhancedUserPos, nextPoint, enhancedAngle, enhancedDistance);
                return;
            }
        }

        // Fall back to regular direction guidance
        Vector3 nextPathPoint = safePathPlanner.GetNextPathPoint();
        int currentPathIndex = safePathPlanner.GetCurrentPathIndex();
        bool isLastPoint = currentPathIndex >= safePathPlanner.GetPathCount() - 1;

        // Get user's current position and forward direction
        Transform cameraTransform = Camera.main.transform;
        Vector3 userPosition = cameraTransform.position;
        Vector3 userForward = cameraTransform.forward;
        userForward.y = 0; // Ignore vertical component for direction calculation
        userForward.Normalize();

        // Get direction to next path point
        Vector3 directionToPathPoint = nextPathPoint - userPosition;
        directionToPathPoint.y = 0; // Ignore vertical component

        // Skip if direction is zero (we're at the point)
        if (directionToPathPoint.magnitude < 0.1f && !forceUpdate)
            return;

        directionToPathPoint.Normalize();

        // Calculate angle between user's forward direction and path point direction
        float angle = Vector3.SignedAngle(userForward, directionToPathPoint, Vector3.up);

        // Determine verbal direction based on angle
        string direction = GetDirectionName(angle);
        AudioClip directionSound = GetDirectionSound(angle);

        // Calculate distance to path point
        float distance = Vector3.Distance(userPosition, nextPathPoint);
        string distanceStr = distance.ToString("F1");

        // Determine if this is a significant turn
        bool isSignificantTurn = Mathf.Abs(angle) > turnAngleThreshold;

        // Create directional message
        string directionMessage;

        // Different message format for last point (destination)
        if (isLastPoint)
        {
            directionMessage = "Your destination is " + direction + ", " + distanceStr + " meters away.";
        }
        else
        {
            // Get turns and obstacles along the path
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
                // Simpler directions for severe visual impairment
                directionMessage = "Go " + direction + ", " + distanceStr + " meters.";
            }
            else if (isSignificantTurn)
            {
                // More emphasis on the turn
                directionMessage = "Turn " + direction + " and continue for " + distanceStr + " meters." + pathDescription;
            }
            else
            {
                // Standard directional guidance
                directionMessage = "Continue " + direction + " for " + distanceStr + " meters." + pathDescription;
            }
            
            // Store waypoint description for announcement when reached
            if (currentPathIndex < safePathPlanner.GetPathCount() - 1)
            {
                waypointAnnouncementNeeded = true;
                nextWaypointDescription = "Waypoint reached. ";
                
                // Check if we have any obstacles to note
                int nextObstacleCount = CountNearbyObstacles(nextPathPoint, 3.0f);
                if (nextObstacleCount > 0)
                {
                    nextWaypointDescription += nextObstacleCount == 1 
                        ? "There is an obstacle nearby. " 
                        : "There are " + nextObstacleCount + " obstacles nearby. ";
                }
                
                // Check if next segment has a significant turn
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

        // Check if this is identical to the last direction given, if so don't repeat unless forced
        if (forceUpdate || !spokenDirections.Contains(directionMessage))
        {
            // Update debug information
            UpdateDebugText("Next point: " + distance.ToString("F1") + "m" +
                            "\nDirection: " + direction +
                            "\nAngle: " + angle.ToString("F0") + "°");

            // Add to spoken directions history (limit to last 5 directions)
            spokenDirections.Add(directionMessage);
            if (spokenDirections.Count > 5)
                spokenDirections.RemoveAt(0);

            // Speak direction to user
            SpeakMessage(directionMessage);

            // If using 3D audio, position the sound at the next waypoint
            if (use3DAudio && audioSource != null)
            {
                audioSource.transform.position = nextPathPoint;
            }

            // Play direction sound
            if (directionSound != null)
                PlaySound(directionSound);

            // Vibrate for significant turns
            if (useVibration && isSignificantTurn)
            {
                if (hapticFeedbackMode == 2)
                {
                    // Different vibration patterns based on direction
                    if (angle < -turnAngleThreshold)
                    {
                        // Left turn - two short vibrations
                        VibratePattern(new float[] { 0.8f, 0.1f, 0, 0.1f, 0.8f, 0.1f });
                    }
                    else if (angle > turnAngleThreshold)
                    {
                        // Right turn - one long vibration
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
        // Play waypoint reached sound
        PlaySound(waypointReachedSound);
        
        // Vibrate if enabled
        if (useVibration && hapticFeedbackMode >= 1)
        {
            Vibrate(0.6f, 0.15f);
        }
        
        // Speak the waypoint announcement
        if (!string.IsNullOrEmpty(nextWaypointDescription))
        {
            SpeakMessage(nextWaypointDescription);
        }
        
        // Reset flag
        waypointAnnouncementNeeded = false;
        nextWaypointDescription = "";
    }

    private string GetDirectionName(float angle)
    {
        if (useCoarseSpatialNavigation)
        {
            // Simpler 8-point directions for severe visual impairment
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
            // More precise directions
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
            StopBeacon(); // Stop any existing beacon
            
            isBeaconActive = true;
            beaconAudioSource.clip = beaconSound;
            beaconAudioSource.Play();
            
            // Start coroutine to update beacon position
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
            // Update position of beacon to next waypoint
            if (safePathPlanner != null && safePathPlanner.GetPathCount() > safePathPlanner.GetCurrentPathIndex())
            {
                Vector3 nextPoint = safePathPlanner.GetNextPathPoint();
                
                // Look ahead to further waypoints for a more stable beacon
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
            
        // Adjust volume based on whether user is facing the right direction
        if (safePathPlanner != null && safePathPlanner.GetPathCount() > safePathPlanner.GetCurrentPathIndex())
        {
            Vector3 nextPoint = safePathPlanner.GetNextPathPoint();
            Vector3 userPos = Camera.main.transform.position;
            
            // Direction to next point
            Vector3 directionToPoint = nextPoint - userPos;
            directionToPoint.y = 0;
            directionToPoint.Normalize();
            
            // User's forward direction
            Vector3 userForward = Camera.main.transform.forward;
            userForward.y = 0;
            userForward.Normalize();
            
            // Calculate how well user is facing the next point
            float facingDot = Vector3.Dot(userForward, directionToPoint);
            
            // Adjust volume - louder when facing the right direction
            float targetVolume = Mathf.Lerp(beaconMinimumVolume, beaconMaximumVolume, (facingDot + 1) / 2);
            beaconAudioSource.volume = Mathf.Lerp(beaconAudioSource.volume, targetVolume, Time.deltaTime * 2);
        }
    }

    private void CheckDestinationReached(Vector3 userPosition)
    {
        // Find end point
        PoseClass endPose = hitPointManager.poseClassList.FirstOrDefault(p => p.waypointType == WaypointType.EndPoint);
        if (endPose == null && hitPointManager.poseClassList.Count > 0)
        {
            // Use last point if no explicit end point
            endPose = hitPointManager.poseClassList[hitPointManager.poseClassList.Count - 1];
        }

        if (endPose != null)
        {
            float distanceToDestination = Vector3.Distance(
                new Vector3(userPosition.x, 0, userPosition.z),
                new Vector3(endPose.position.x, 0, endPose.position.z));

            if (distanceToDestination < waypointReachedDistance)
            {
                // Reached destination
                EndNavigation();
            }
        }
    }

    private void EndNavigation()
    {
        isNavigating = false;

        // Stop beacon
        StopBeacon();

        // Play destination reached sound
        PlaySound(destinationReachedSound);

        // Vibrate device if enabled
        if (useVibration)
        {
            if (hapticFeedbackMode == 2)
            {
                // Special completion pattern
                VibratePattern(new float[] { 1.0f, 0.2f, 0, 0.1f, 1.0f, 0.2f, 0, 0.1f, 1.0f, 0.3f });
            }
            else
            {
                Vibrate(1.0f, 0.8f);
            }
        }

        SpeakMessage("You have reached your destination safely. Navigation completed.");
        UpdateDebugText("✅ Destination reached!");

        // Reset variables
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
            // On Android, there's no direct way to control vibration intensity
            // So we use a pattern of short vibrations to simulate lower intensity
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
        // Convert float pattern to long[] pattern expected by Android
        // Pattern format: [delay1, duration1, delay2, duration2, ...]
        long[] vibrationPattern = new long[pattern.Length];
        for (int i = 0; i < pattern.Length; i++)
        {
            vibrationPattern[i] = (long)(pattern[i] * 1000); // Convert to milliseconds
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

    // Method to provide next instruction on demand (for manual triggering)
    public void ProvideNextInstruction()
    {
        if (isNavigating)
        {
            GiveDirectionToNextPathPoint(true);
        }
        else
        {
            // If not navigating but NavigationEnhancer is available, use it
            if (navigationEnhancer != null)
            {
                navigationEnhancer.AnnounceWhatsAhead();
            }
            else
            {
                // Otherwise provide a basic message
                SpeakMessage("Navigation is not active. Tap and hold to start navigation or double tap to access the menu.");
            }
        }
    }

    // Function to reset all navigation data
    public void ResetNavigation()
    {
        StopNavigation();
        destinationAnnouncementStarted = false;
        spokenDirections.Clear();
        lastDirectionUpdate = 0;
        lastObstacleWarningTime = 0;
    }
}