using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.ARFoundation;
using System;

public class AccessibilityManager : MonoBehaviour
{
    [Header("References")]
    public HitPointManager hitPointManager;
    public NavigationManager navigationManager;
    public TextToSpeech textToSpeech;
    public Canvas accessibilityCanvas;
    public NavigationEnhancer navigationEnhancer; // Changed from NavigationHelper to NavigationEnhancer

    [Header("Gesture Settings")]
    public bool enableGestureControls = true;
    public float doubleTapMaxDelay = 0.3f;
    public float longPressTime = 0.8f;
    public float swipeMinDistance = 50f;

    [Header("Audio Feedback")]
    public AudioSource audioSource;
    public AudioClip tapSound;
    public AudioClip swipeSound;
    public AudioClip menuOpenSound;
    public AudioClip menuCloseSound;
    public AudioClip errorSound;
    public AudioClip successSound;
    public AudioClip confirmSound;

    [Header("Speech Feedback")]
    public bool announceFeatures = true;
    public bool verboseMode = false;
    public float initialInstructionDelay = 2.0f;
    public float speechRate = 1.0f;
    [Range(0.5f, 1.5f)]
    public float speechPitch = 1.0f;

    [Header("Accessibility UI")]
    public GameObject accessibilityMenuPanel;
    public Button increaseFontSizeButton;
    public Button decreaseFontSizeButton;
    public Button increaseSpeechRateButton;
    public Button decreaseSpeechRateButton;
    public Button highContrastModeButton;
    public Button enableVibrationButton;
    public Button enableVerboseModeButton;
    public Button selfVoicingToggleButton;
    public TextMeshProUGUI accessibilityStatusText;

    // Gesture state tracking
    private bool isLongPressing = false;
    private float longPressStart = 0f;
    private Vector2 touchStartPosition;
    private float lastTapTime = 0f;
    private int consecutiveTaps = 0;

    // Accessibility state
    [SerializeField] private float textScale = 1.0f;
    [SerializeField] private bool highContrastMode = false;
    [SerializeField] private bool vibrationEnabled = true;
    private bool accessibilityMenuOpen = false;
    private int selectedMenuOption = 0;
    private string[] menuOptions;

    // UI element cache
    private List<TextMeshProUGUI> allTextElements = new List<TextMeshProUGUI>();
    private List<Image> allImageElements = new List<Image>();
    
    // Voice recognition helper
    private VoiceRecognitionHelper voiceRecognitionHelper;
    public bool enableVoiceCommands = true;

    void Start()
    {
        // Find references if not set
        if (hitPointManager == null)
            hitPointManager = FindObjectOfType<HitPointManager>();

        if (navigationManager == null)
            navigationManager = FindObjectOfType<NavigationManager>();

        if (textToSpeech == null)
            textToSpeech = FindObjectOfType<TextToSpeech>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
            
        // Find NavigationEnhancer
        if (navigationEnhancer == null)
            navigationEnhancer = FindObjectOfType<NavigationEnhancer>();
            
        // Initialize voice recognition if enabled
        if (enableVoiceCommands)
        {
            voiceRecognitionHelper = GetComponent<VoiceRecognitionHelper>();
            if (voiceRecognitionHelper == null)
            {
                voiceRecognitionHelper = gameObject.AddComponent<VoiceRecognitionHelper>();
            }
            
            if (voiceRecognitionHelper != null)
            {
                //voiceRecognitionHelper.Initialize();
                voiceRecognitionHelper.OnCommandRecognized.AddListener(HandleVoiceCommand);
            }
        }

        // Initialize menu options
        menuOptions = new string[] 
        {
            "Adjust speech rate",
            "Change voice pitch",
            "Toggle high contrast mode",
            "Toggle vibration feedback",
            "Toggle verbose mode",
            "Application help",
            "Exit menu"
        };

        // Initialize UI elements
        if (accessibilityMenuPanel != null)
            accessibilityMenuPanel.SetActive(false);

        // Set up button listeners
        SetupButtonListeners();

        // Cache all text and image elements for accessibility adjustments
        CacheUIElements();

        // Initialize accessibility settings
        ApplyTextScaling();
        ApplyContrastMode();

        // Set vibration mode on navigation manager
        if (navigationManager != null)
        {
            navigationManager.useVibration = vibrationEnabled;
            navigationManager.useDetailedAudioDescriptions = verboseMode;
        }
            
        // Set speech rate and pitch on TTS
        if (textToSpeech != null)
        {
            textToSpeech.speechRate = speechRate;
            textToSpeech.speechPitch = speechPitch;
        }

        // Announce initial instructions after a delay
        if (announceFeatures)
            Invoke("AnnounceInitialInstructions", initialInstructionDelay);
    }

    void Update()
    {
        // Process gesture controls if enabled
        if (enableGestureControls)
            ProcessGestures();
    }

    private void ProcessGestures()
    {
        // Only process gestures if no UI elements are being interacted with
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        // Get touch input
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);

            // Handle touch phases
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    touchStartPosition = touch.position;
                    longPressStart = Time.time;
                    isLongPressing = true;
                    break;

                case TouchPhase.Moved:
                    // Check if movement exceeds threshold for a swipe
                    float touchDistance = Vector2.Distance(touch.position, touchStartPosition);
                    if (touchDistance > swipeMinDistance)
                    {
                        // Determine swipe direction
                        Vector2 direction = touch.position - touchStartPosition;
                        ProcessSwipe(direction);

                        // Reset long press detection
                        isLongPressing = false;
                    }
                    break;

                case TouchPhase.Ended:
                    // Check for tap
                    float touchDuration = Time.time - longPressStart;

                    if (isLongPressing)
                    {
                        if (touchDuration >= longPressTime)
                        {
                            // Long press detected
                            ProcessLongPress(touch.position);
                        }
                        else
                        {
                            // Short tap detected
                            ProcessTap(touch.position);
                        }
                    }

                    isLongPressing = false;
                    break;
            }
        }
    }

    private void ProcessTap(Vector2 position)
    {
        // Check for double/triple tap
        float timeSinceLastTap = Time.time - lastTapTime;

        if (timeSinceLastTap <= doubleTapMaxDelay)
        {
            consecutiveTaps++;

            if (consecutiveTaps == 1)
            {
                // Double tap
                OnDoubleTap(position);
            }
            else if (consecutiveTaps == 2)
            {
                // Triple tap
                OnTripleTap(position);
                consecutiveTaps = 0; // Reset after triple tap
            }
        }
        else
        {
            // Single tap
            consecutiveTaps = 0;
            OnSingleTap(position);
        }

        lastTapTime = Time.time;
    }

    private void ProcessLongPress(Vector2 position)
    {
        PlaySound(tapSound);

        // Toggle accessibility menu
        ToggleAccessibilityMenu();
    }

    private void ProcessSwipe(Vector2 direction)
    {
        PlaySound(swipeSound);

        // Normalize direction
        direction.Normalize();

        // Determine swipe direction (simplified to 4 directions)
        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
        {
            // Horizontal swipe
            if (direction.x > 0)
            {
                // Right swipe
                OnSwipeRight();
            }
            else
            {
                // Left swipe
                OnSwipeLeft();
            }
        }
        else
        {
            // Vertical swipe
            if (direction.y > 0)
            {
                // Up swipe
                OnSwipeUp();
            }
            else
            {
                // Down swipe
                OnSwipeDown();
            }
        }
    }

    // Gesture handlers
    private void OnSingleTap(Vector2 position)
    {
        PlaySound(tapSound);

        // Request next navigation instruction or environmental info
        if (navigationManager != null && navigationManager.isNavigating)
        {
            navigationManager.ProvideNextInstruction();
        }
        else
        {
            // If not navigating, announce what's ahead using NavigationEnhancer if available
            if (navigationEnhancer != null)
            {
                navigationEnhancer.AnnounceWhatsAhead();
            }
            else
            {
                AnnounceWhatsAhead(); // Fallback to original implementation
            }
        }
    }

    private void OnDoubleTap(Vector2 position)
    {
        PlaySound(tapSound, 2);

        // Start/stop navigation
        if (navigationManager != null)
        {
            if (navigationManager.isNavigating)
            {
                navigationManager.StopNavigation();
                SpeakMessage("Navigation stopped");
            }
            else if (hitPointManager.poseClassList.Count > 0)
            {
                navigationManager.StartNavigation();
            }
            else
            {
                SpeakMessage("No path available. Please load or create a path first.");
                PlaySound(errorSound);
            }
        }
    }

    private void OnTripleTap(Vector2 position)
    {
        PlaySound(tapSound, 3);

        // Announce current location and status
        AnnounceCurrentStatus();
    }

    private void OnSwipeRight()
    {
        if (accessibilityMenuOpen)
        {
            // In menu: increase value
            IncreaseValue();
        }
        else
        {
            // Not in menu: next path/waypoint
            if (navigationManager != null && !navigationManager.isNavigating)
            {
                SpeakMessage("Skipping to next waypoint");
                // Logic to skip to next waypoint would go here
            }
        }
    }

    private void OnSwipeLeft()
    {
        if (accessibilityMenuOpen)
        {
            // In menu: decrease value
            DecreaseValue();
        }
        else
        {
            // Not in menu: previous path/waypoint
            if (navigationManager != null && !navigationManager.isNavigating)
            {
                SpeakMessage("Going back to previous waypoint");
                // Logic to go back to previous waypoint would go here
            }
        }
    }

    private void OnSwipeUp()
    {
        if (accessibilityMenuOpen)
        {
            // In menu: previous option
            SelectPreviousOption();
        }
        else
        {
            // Load path
            SpeakMessage("Loading saved paths");
            if (hitPointManager != null)
            {
                hitPointManager.PromptFilenameToLoad();
            }
        }
    }

    private void OnSwipeDown()
    {
        if (accessibilityMenuOpen)
        {
            // In menu: next option
            SelectNextOption();
        }
        else
        {
            // Save current path
            SpeakMessage("Saving current path");
            if (hitPointManager != null && hitPointManager.poseClassList.Count > 0)
            {
                hitPointManager.SaveAllTheInformationToFile();
            }
            else
            {
                SpeakMessage("No path to save");
                PlaySound(errorSound);
            }
        }
    }

    // Menu operations
    public void ToggleAccessibilityMenu()
    {
        accessibilityMenuOpen = !accessibilityMenuOpen;

        if (accessibilityMenuPanel != null)
            accessibilityMenuPanel.SetActive(accessibilityMenuOpen);

        PlaySound(accessibilityMenuOpen ? menuOpenSound : menuCloseSound);

        if (accessibilityMenuOpen)
        {
            selectedMenuOption = 0;
            SpeakMessage("Accessibility menu opened. Swipe up or down to navigate options, left or right to change values. Current option: " + menuOptions[selectedMenuOption]);
            UpdateAccessibilityStatusText();
        }
        else
        {
            SpeakMessage("Accessibility menu closed");
        }
    }

    private void SelectNextOption()
    {
        if (!accessibilityMenuOpen || menuOptions == null || menuOptions.Length == 0)
            return;

        selectedMenuOption = (selectedMenuOption + 1) % menuOptions.Length;
        SpeakMessage(menuOptions[selectedMenuOption]);
        PlaySound(tapSound);
        
        // Update UI to show selected option
        UpdateAccessibilityStatusText();
    }

    private void SelectPreviousOption()
    {
        if (!accessibilityMenuOpen || menuOptions == null || menuOptions.Length == 0)
            return;

        selectedMenuOption = (selectedMenuOption - 1 + menuOptions.Length) % menuOptions.Length;
        SpeakMessage(menuOptions[selectedMenuOption]);
        PlaySound(tapSound);
        
        // Update UI to show selected option
        UpdateAccessibilityStatusText();
    }

    private void IncreaseValue()
    {
        if (!accessibilityMenuOpen)
            return;

        // Handle based on selected option
        switch (selectedMenuOption)
        {
            case 0: // Speech rate
                IncreaseSpeechRate();
                break;
            case 1: // Voice pitch
                IncreaseSpeechPitch();
                break;
            case 2: // High contrast
                ToggleHighContrastMode();
                break;
            case 3: // Vibration
                ToggleVibration();
                break;
            case 4: // Verbose mode
                ToggleVerboseMode();
                break;
            case 5: // Help
                ProvideHelp();
                break;
            case 6: // Exit menu
                ToggleAccessibilityMenu();
                break;
        }
    }

    private void DecreaseValue()
    {
        if (!accessibilityMenuOpen)
            return;

        // Handle based on selected option
        switch (selectedMenuOption)
        {
            case 0: // Speech rate
                DecreaseSpeechRate();
                break;
            case 1: // Voice pitch
                DecreaseSpeechPitch();
                break;
            case 2: // High contrast
                ToggleHighContrastMode();
                break;
            case 3: // Vibration
                ToggleVibration();
                break;
            case 4: // Verbose mode
                ToggleVerboseMode();
                break;
            case 5: // Help
                ProvideHelp();
                break;
            case 6: // Exit menu
                ToggleAccessibilityMenu();
                break;
        }
    }

    // Accessibility features
    public void IncreaseTextSize()
    {
        textScale += 0.1f;
        textScale = Mathf.Clamp(textScale, 1.0f, 2.0f);
        ApplyTextScaling();

        SpeakMessage("Text size increased to " + (textScale * 100).ToString("F0") + " percent");
        PlaySound(tapSound);
        UpdateAccessibilityStatusText();
    }

    public void DecreaseTextSize()
    {
        textScale -= 0.1f;
        textScale = Mathf.Clamp(textScale, 1.0f, 2.0f);
        ApplyTextScaling();

        SpeakMessage("Text size decreased to " + (textScale * 100).ToString("F0") + " percent");
        PlaySound(tapSound);
        UpdateAccessibilityStatusText();
    }

    public void IncreaseSpeechRate()
    {
        if (textToSpeech != null)
        {
            speechRate += 0.1f;
            speechRate = Mathf.Clamp(speechRate, 0.5f, 2.0f);
            textToSpeech.UpdateSpeechRate(speechRate);

            SpeakMessage("Speech rate increased to " + (speechRate * 100).ToString("F0") + " percent");
            PlaySound(tapSound);
            UpdateAccessibilityStatusText();
        }
    }

    public void DecreaseSpeechRate()
    {
        if (textToSpeech != null)
        {
            speechRate -= 0.1f;
            speechRate = Mathf.Clamp(speechRate, 0.5f, 2.0f);
            textToSpeech.UpdateSpeechRate(speechRate);

            SpeakMessage("Speech rate decreased to " + (speechRate * 100).ToString("F0") + " percent");
            PlaySound(tapSound);
            UpdateAccessibilityStatusText();
        }
    }
    
    public void IncreaseSpeechPitch()
    {
        if (textToSpeech != null)
        {
            speechPitch += 0.1f;
            speechPitch = Mathf.Clamp(speechPitch, 0.5f, 1.5f);
            textToSpeech.UpdateSpeechPitch(speechPitch);

            SpeakMessage("Voice pitch increased");
            PlaySound(tapSound);
            UpdateAccessibilityStatusText();
        }
    }

    public void DecreaseSpeechPitch()
    {
        if (textToSpeech != null)
        {
            speechPitch -= 0.1f;
            speechPitch = Mathf.Clamp(speechPitch, 0.5f, 1.5f);
            textToSpeech.UpdateSpeechPitch(speechPitch);

            SpeakMessage("Voice pitch decreased");
            PlaySound(tapSound);
            UpdateAccessibilityStatusText();
        }
    }

    public void ToggleHighContrastMode()
    {
        highContrastMode = !highContrastMode;
        ApplyContrastMode();

        SpeakMessage(highContrastMode ? "High contrast mode enabled" : "High contrast mode disabled");
        PlaySound(tapSound);
        UpdateAccessibilityStatusText();
    }

    public void ToggleVibration()
    {
        vibrationEnabled = !vibrationEnabled;

        if (navigationManager != null)
            navigationManager.useVibration = vibrationEnabled;

        SpeakMessage(vibrationEnabled ? "Vibration enabled" : "Vibration disabled");
        PlaySound(tapSound);
        UpdateAccessibilityStatusText();
    }
    
    public void ToggleVerboseMode()
    {
        verboseMode = !verboseMode;

        if (navigationManager != null)
            navigationManager.useDetailedAudioDescriptions = verboseMode;

        SpeakMessage(verboseMode ? "Verbose mode enabled. You will hear more detailed descriptions." : "Verbose mode disabled. You will hear basic descriptions.");
        PlaySound(tapSound);
        UpdateAccessibilityStatusText();
    }
    
    public void ProvideHelp()
    {
        string helpText = "AR Navigation Assistant Help. Use the following gestures: ";
        helpText += "Single tap to get current direction. ";
        helpText += "Double tap to start or stop navigation. ";
        helpText += "Triple tap for current status. ";
        helpText += "Long press to open accessibility menu. ";
        helpText += "Swipe up to load paths. ";
        helpText += "Swipe down to save current path. ";
        
        if (enableVoiceCommands && voiceRecognitionHelper != null)
        {
            helpText += "Voice commands are enabled. You can say 'Start Navigation', 'Stop Navigation', 'Where am I', or 'Help' at any time.";
        }
        
        SpeakMessage(helpText);
    }

    // Helper methods
    private void ApplyTextScaling()
    {
        foreach (TextMeshProUGUI text in allTextElements)
        {
            if (text != null)
                text.fontSize = text.fontSize * textScale / text.transform.localScale.x;
        }
    }

    private void ApplyContrastMode()
    {
        if (highContrastMode)
        {
            // Apply high contrast theme
            foreach (Image image in allImageElements)
            {
                if (image != null)
                {
                    // Increase contrast by making dark colors darker and light colors lighter
                    Color color = image.color;
                    float luminance = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;

                    if (luminance > 0.5f)
                    {
                        // Make light colors lighter
                        color = Color.Lerp(color, Color.white, 0.3f);
                    }
                    else
                    {
                        // Make dark colors darker
                        color = Color.Lerp(color, Color.black, 0.3f);
                    }

                    image.color = color;
                }
            }

            // Make text more contrasty
            foreach (TextMeshProUGUI text in allTextElements)
            {
                if (text != null)
                {
                    Color color = text.color;
                    float luminance = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;

                    if (luminance > 0.5f)
                    {
                        text.color = Color.white;
                    }
                    else
                    {
                        text.color = Color.black;
                    }

                    // Increase outline for better readability
                    text.outlineWidth = 0.2f;
                    text.outlineColor = luminance > 0.5f ? Color.black : Color.white;
                }
            }
        }
        else
        {
            // Reset to normal contrast
            // Would need to store original colors to properly implement
            // For now, just remove outlines
            foreach (TextMeshProUGUI text in allTextElements)
            {
                if (text != null)
                {
                    text.outlineWidth = 0;
                }
            }
        }
    }

    private void CacheUIElements()
    {
        // Find all text elements
        TextMeshProUGUI[] texts = FindObjectsOfType<TextMeshProUGUI>();
        allTextElements.AddRange(texts);

        // Find all image elements
        Image[] images = FindObjectsOfType<Image>();
        allImageElements.AddRange(images);
    }

    private void SetupButtonListeners()
    {
        if (increaseFontSizeButton != null)
            increaseFontSizeButton.onClick.AddListener(IncreaseTextSize);

        if (decreaseFontSizeButton != null)
            decreaseFontSizeButton.onClick.AddListener(DecreaseTextSize);

        if (increaseSpeechRateButton != null)
            increaseSpeechRateButton.onClick.AddListener(IncreaseSpeechRate);

        if (decreaseSpeechRateButton != null)
            decreaseSpeechRateButton.onClick.AddListener(DecreaseSpeechRate);

        if (highContrastModeButton != null)
            highContrastModeButton.onClick.AddListener(ToggleHighContrastMode);

        if (enableVibrationButton != null)
            enableVibrationButton.onClick.AddListener(ToggleVibration);
            
        if (enableVerboseModeButton != null)
            enableVerboseModeButton.onClick.AddListener(ToggleVerboseMode);
    }

    private void UpdateAccessibilityStatusText()
    {
        if (accessibilityStatusText != null)
        {
            string status = "";
            
            if (accessibilityMenuOpen && menuOptions != null && selectedMenuOption < menuOptions.Length)
            {
                status += "Selected option: " + menuOptions[selectedMenuOption] + "\n\n";
            }
            
            status += "Text Size: " + (textScale * 100).ToString("F0") + "%\n";

            if (textToSpeech != null)
            {
                status += "Speech Rate: " + (speechRate * 100).ToString("F0") + "%\n";
                status += "Voice Pitch: " + (speechPitch * 100).ToString("F0") + "%\n";
            }

            status += "High Contrast: " + (highContrastMode ? "On" : "Off") + "\n";
            status += "Vibration: " + (vibrationEnabled ? "On" : "Off") + "\n";
            status += "Verbose Mode: " + (verboseMode ? "On" : "Off");

            accessibilityStatusText.text = status;
        }
    }

    private void PlaySound(AudioClip clip, int repetitions = 1)
    {
        if (audioSource != null && clip != null)
        {
            // Play sound the specified number of times
            StartCoroutine(PlaySoundRepeatedly(clip, repetitions));
        }
    }

    private IEnumerator PlaySoundRepeatedly(AudioClip clip, int repetitions)
    {
        for (int i = 0; i < repetitions; i++)
        {
            audioSource.PlayOneShot(clip);

            if (i < repetitions - 1)
                yield return new WaitForSeconds(clip.length * 0.5f);
        }
    }

    private void SpeakMessage(string message)
    {
        if (textToSpeech != null)
        {
            textToSpeech.Speak(message);
        }
    }

    private void AnnounceInitialInstructions()
    {
        string instructions = "Welcome to the AR Navigation Assistant. ";
        instructions += "Single tap to get directions. Double tap to start or stop navigation. ";
        instructions += "Triple tap for current status. Long press to open accessibility menu. ";
        instructions += "Swipe up to load paths, down to save paths.";

        SpeakMessage(instructions);
    }

    public void AnnounceWhatsAhead()
    {
        // Use NavigationEnhancer if available
        if (navigationEnhancer != null)
        {
            navigationEnhancer.AnnounceWhatsAhead();
            return;
        }
        
        // Original implementation if NavigationEnhancer not available
        if (hitPointManager == null || Camera.main == null)
            return;

        // Cast a ray forward to detect obstacles
        Vector3 rayStart = Camera.main.transform.position;
        Vector3 rayDirection = Camera.main.transform.forward;

        string message = "";
        bool foundSomething = false;

        // Check for obstacles in front
        RaycastHit hit;
        if (Physics.Raycast(rayStart, rayDirection, out hit, 10f))
        {
            message = "There is ";

            if (hit.collider.CompareTag("Obstacle"))
            {
                message += "an obstacle about " + hit.distance.ToString("F1") + " meters ahead.";
            }
            else if (hit.collider.CompareTag("Waypoint"))
            {
                message += "a waypoint about " + hit.distance.ToString("F1") + " meters ahead.";
            }
            else
            {
                message += "something about " + hit.distance.ToString("F1") + " meters ahead.";
            }

            foundSomething = true;
        }

        // Check for nearby obstacles in any direction
        float nearestObstacleDistance = float.MaxValue;
        Vector3 nearestObstacleDirection = Vector3.zero;

        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                float distance = Vector3.Distance(rayStart, pose.position);
                if (distance < 3.0f && distance < nearestObstacleDistance) // Within 3 meters
                {
                    nearestObstacleDistance = distance;
                    nearestObstacleDirection = pose.position - rayStart;
                }
            }
        }

        if (nearestObstacleDistance < float.MaxValue)
        {
            if (foundSomething)
                message += " Also, ";
            else
                message = "Caution, ";

            // Determine direction to nearest obstacle
            nearestObstacleDirection.y = 0;
            nearestObstacleDirection.Normalize();

            Vector3 forward = Camera.main.transform.forward;
            forward.y = 0;
            forward.Normalize();

            float angle = Vector3.SignedAngle(forward, nearestObstacleDirection, Vector3.up);
            string direction = GetRelativeDirection(angle);

            message += "obstacle " + direction + ", " + nearestObstacleDistance.ToString("F1") + " meters away.";
            foundSomething = true;
        }

        // Also check for path information
        if (hitPointManager.poseClassList.Count > 0)
        {
            // Find nearest path point
            PoseClass nearestPath = null;
            float nearestPathDistance = float.MaxValue;
            
            foreach (var pose in hitPointManager.poseClassList)
            {
                if (pose.waypointType == WaypointType.PathPoint || 
                    pose.waypointType == WaypointType.StartPoint || 
                    pose.waypointType == WaypointType.EndPoint)
                {
                    float distance = Vector3.Distance(rayStart, pose.position);
                    if (distance < 5.0f && distance < nearestPathDistance)
                    {
                        nearestPathDistance = distance;
                        nearestPath = pose;
                    }
                }
            }
            
            if (nearestPath != null)
            {
                if (foundSomething)
                    message += " ";
                
                // Determine type of point
                string pointType = "path point";
                if (nearestPath.waypointType == WaypointType.StartPoint)
                    pointType = "start point";
                else if (nearestPath.waypointType == WaypointType.EndPoint)
                    pointType = "destination";
                
                // Determine direction
                Vector3 pathDirection = nearestPath.position - rayStart;
                pathDirection.y = 0;
                pathDirection.Normalize();
                
                float pathAngle = Vector3.SignedAngle(Camera.main.transform.forward, pathDirection, Vector3.up);
                string pathDirectionName = GetRelativeDirection(pathAngle);
                
                message += "There is a " + pointType + " " + pathDirectionName + ", " + 
                          nearestPathDistance.ToString("F1") + " meters away.";
                          
                foundSomething = true;
            }
        }

        if (!foundSomething)
        {
            message = "No obstacles detected nearby. The path appears clear.";
        }

        SpeakMessage(message);
    }

    public void AnnounceCurrentStatus()
    {
        // Use NavigationEnhancer if available
        if (navigationEnhancer != null)
        {
            navigationEnhancer.DescribePathQuality();
            return;
        }
        
        // Original implementation if NavigationEnhancer not available
        string status = "Current status: ";

        if (navigationManager != null && navigationManager.isNavigating)
        {
            status += "Currently navigating. ";
        }
        else
        {
            status += "Not navigating. ";
        }

        if (hitPointManager != null)
        {
            int pathPoints = 0;
            int obstacles = 0;
            bool hasStartPoint = false;
            bool hasEndPoint = false;

            foreach (var pose in hitPointManager.poseClassList)
            {
                switch (pose.waypointType)
                {
                    case WaypointType.PathPoint:
                        pathPoints++;
                        break;
                    case WaypointType.Obstacle:
                        obstacles++;
                        break;
                    case WaypointType.StartPoint:
                        hasStartPoint = true;
                        break;
                    case WaypointType.EndPoint:
                        hasEndPoint = true;
                        break;
                }
            }

            if (hitPointManager.poseClassList.Count > 0)
            {
                status += "Path loaded with " + pathPoints + " path points, " + obstacles + " obstacles. ";
                status += hasStartPoint ? "Start point defined. " : "No start point defined. ";
                status += hasEndPoint ? "End point defined. " : "No end point defined. ";
            }
            else
            {
                status += "No path loaded. ";
            }
        }

        SpeakMessage(status);
    }

    private string GetRelativeDirection(float angle)
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
            return "slightly to your left";
        }
        else if (angle < 67.5f)
        {
            return "slightly to your right";
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
    
    // Voice command handling
    public void HandleVoiceCommand(string command)
    {
        // Convert command to lowercase for easier comparison
        command = command.ToLower();
        
        // Play acknowledgment sound
        PlaySound(confirmSound);

        // Process command - use NavigationEnhancer for enhanced handling if available
        if (navigationEnhancer != null)
        {
            // Pass voice command to NavigationEnhancer for enhanced processing
            // This would ideally call a method like navigationEnhancer.HandleVoiceCommand(command)
            // but we'll handle common commands here

            if (command.Contains("save") && command.Contains("map"))
            {
                navigationEnhancer.SaveEnhancedMap();
                return;
            }
            else if (command.Contains("load") && command.Contains("map"))
            {
                SpeakMessage("Please select a map to load");
                hitPointManager.PromptFilenameToLoad();
                return;
            }
            else if (command.Contains("describe") && command.Contains("environment"))
            {
                navigationEnhancer.DescribeSurroundings();
                return;
            }
        }

        // Legacy command handling
        if (command.Contains("start") && command.Contains("navigation") || command.Contains("navigate"))
        {
            if (navigationManager != null && !navigationManager.isNavigating)
            {
                if (hitPointManager.poseClassList.Count > 0)
                {
                    SpeakMessage("Starting navigation");
                    navigationManager.StartNavigation();
                }
                else
                {
                    SpeakMessage("No path available. Please load or create a path first.");
                    PlaySound(errorSound);
                }
            }
            else if (navigationManager != null && navigationManager.isNavigating)
            {
                SpeakMessage("Navigation is already active");
            }
        }
        else if (command.Contains("stop") && command.Contains("navigation") || command.Contains("end") && command.Contains("navigation"))
        {
            if (navigationManager != null && navigationManager.isNavigating)
            {
                navigationManager.StopNavigation();
                SpeakMessage("Navigation stopped");
            }
            else
            {
                SpeakMessage("Navigation is not active");
            }
        }
        else if (command.Contains("where") && command.Contains("am") && command.Contains("i") || 
                 command.Contains("current") && command.Contains("location"))
        {
            AnnounceCurrentStatus();
        }
        else if (command.Contains("what") && command.Contains("ahead") || 
                 command.Contains("describe") && command.Contains("surroundings"))
        {
            AnnounceWhatsAhead();
        }
        else if (command.Contains("help") || command.Contains("instructions"))
        {
            ProvideHelp();
        }
        else if (command.Contains("open") && command.Contains("menu") || 
                 command.Contains("accessibility") && command.Contains("menu"))
        {
            if (!accessibilityMenuOpen)
            {
                ToggleAccessibilityMenu();
            }
            else
            {
                SpeakMessage("Menu is already open");
            }
        }
        else if (command.Contains("close") && command.Contains("menu"))
        {
            if (accessibilityMenuOpen)
            {
                ToggleAccessibilityMenu();
            }
            else
            {
                SpeakMessage("Menu is already closed");
            }
        }
        else if (command.Contains("load") && command.Contains("path"))
        {
            SpeakMessage("Loading saved paths");
            if (hitPointManager != null)
            {
                hitPointManager.PromptFilenameToLoad();
            }
        }
        else if (command.Contains("save") && command.Contains("path"))
        {
            SpeakMessage("Saving current path");
            if (hitPointManager != null && hitPointManager.poseClassList.Count > 0)
            {
                hitPointManager.SaveAllTheInformationToFile();
            }
            else
            {
                SpeakMessage("No path to save");
                PlaySound(errorSound);
            }
        }
        else
        {
            // Unknown command
            SpeakMessage("Command not recognized. Please try again.");
        }
    }
}