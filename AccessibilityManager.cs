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
    public NavigationEnhancer navigationEnhancer;
    
    // NEW: Enhanced 3D Map Manager reference
    private Enhanced3DMapManager enhanced3DMapManager;

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

    // NEW: Voice Recognition Integration
    [Header("Voice Recognition")]
    public bool enableVoiceCommands = true;
    public bool enableAutoListening = false;
    public float voiceCommandTimeout = 5.0f;
    
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

    // NEW: Emergency state management
    private bool isEmergencyMode = false;
    private Coroutine emergencyAssistanceCoroutine;

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
            
        if (navigationEnhancer == null)
            navigationEnhancer = FindObjectOfType<NavigationEnhancer>();
            
        // NEW: Find Enhanced3DMapManager
        enhanced3DMapManager = FindObjectOfType<Enhanced3DMapManager>();
            
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
                voiceRecognitionHelper.OnCommandRecognized.AddListener(HandleVoiceCommand);
                
                // NEW: Set up auto-listening if enabled
                if (enableAutoListening)
                {
                    voiceRecognitionHelper.autoStartListening = true;
                    voiceRecognitionHelper.autoListenInterval = 8.0f; // Listen every 8 seconds
                }
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
            "Voice command settings",
            "Application help",
            "Exit menu"
        };

        if (accessibilityMenuPanel != null)
            accessibilityMenuPanel.SetActive(false);

        SetupButtonListeners();

        CacheUIElements();

        ApplyTextScaling();
        ApplyContrastMode();

        if (navigationManager != null)
        {
            navigationManager.useVibration = vibrationEnabled;
            navigationManager.useDetailedAudioDescriptions = verboseMode;
        }
            
        if (textToSpeech != null)
        {
            textToSpeech.speechRate = speechRate;
            textToSpeech.speechPitch = speechPitch;
        }

        if (announceFeatures)
            Invoke("AnnounceInitialInstructions", initialInstructionDelay);
    }

    void Update()
    {
        if (enableGestureControls)
            ProcessGestures();
    }

    private void ProcessGestures()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    touchStartPosition = touch.position;
                    longPressStart = Time.time;
                    isLongPressing = true;
                    break;

                case TouchPhase.Moved:
                    float touchDistance = Vector2.Distance(touch.position, touchStartPosition);
                    if (touchDistance > swipeMinDistance)
                    {
                        Vector2 direction = touch.position - touchStartPosition;
                        ProcessSwipe(direction);

                        isLongPressing = false;
                    }
                    break;

                case TouchPhase.Ended:
                    float touchDuration = Time.time - longPressStart;

                    if (isLongPressing)
                    {
                        if (touchDuration >= longPressTime)
                        {
                            ProcessLongPress(touch.position);
                        }
                        else
                        {
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
        float timeSinceLastTap = Time.time - lastTapTime;

        if (timeSinceLastTap <= doubleTapMaxDelay)
        {
            consecutiveTaps++;

            if (consecutiveTaps == 1)
            {
                OnDoubleTap(position);
            }
            else if (consecutiveTaps == 2)
            {
                OnTripleTap(position);
                consecutiveTaps = 0;
            }
        }
        else
        {
            consecutiveTaps = 0;
            OnSingleTap(position);
        }

        lastTapTime = Time.time;
    }

    private void ProcessLongPress(Vector2 position)
    {
        PlaySound(tapSound);

        // NEW: Long press now activates voice listening if available
        if (enableVoiceCommands && voiceRecognitionHelper != null && !isEmergencyMode)
        {
            if (!voiceRecognitionHelper.IsListening())
            {
                voiceRecognitionHelper.StartListening();
                SpeakMessage("Listening for voice command");
            }
            else
            {
                voiceRecognitionHelper.StopListening();
                SpeakMessage("Voice listening stopped");
            }
        }
        else
        {
            ToggleAccessibilityMenu();
        }
    }

    private void ProcessSwipe(Vector2 direction)
    {
        PlaySound(swipeSound);

        direction.Normalize();

        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
        {
            if (direction.x > 0)
            {
                OnSwipeRight();
            }
            else
            {
                OnSwipeLeft();
            }
        }
        else
        {
            if (direction.y > 0)
            {
                OnSwipeUp();
            }
            else
            {
                OnSwipeDown();
            }
        }
    }

    // Gesture handlers
    private void OnSingleTap(Vector2 position)
    {
        PlaySound(tapSound);

        if (navigationManager != null && navigationManager.isNavigating)
        {
            navigationManager.ProvideNextInstruction();
        }
        else
        {
            if (navigationEnhancer != null)
            {
                navigationEnhancer.AnnounceWhatsAhead();
            }
            else
            {
                AnnounceWhatsAhead();
            }
        }
    }

    private void OnDoubleTap(Vector2 position)
    {
        PlaySound(tapSound, 2);

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

        // NEW: Triple tap now provides enhanced status if available
        if (enhanced3DMapManager != null && enhanced3DMapManager.GetCurrentMap() != null)
        {
            var currentMap = enhanced3DMapManager.GetCurrentMap();
            string status = $"Enhanced map loaded: {currentMap.name}. ";
            status += $"Path has {currentMap.waypoints.Count} waypoints over {currentMap.totalPathLength:F1} meters. ";
            
            if (enhanced3DMapManager.IsNavigating())
            {
                status += $"Currently navigating. At waypoint {enhanced3DMapManager.GetCurrentWaypointIndex() + 1}.";
            }
            
            SpeakMessage(status);
        }
        else
        {
            AnnounceCurrentStatus();
        }
    }

    private void OnSwipeRight()
    {
        if (accessibilityMenuOpen)
        {
            IncreaseValue();
        }
        else
        {
            if (navigationManager != null && !navigationManager.isNavigating)
            {
                SpeakMessage("Skipping to next waypoint");
            }
        }
    }

    private void OnSwipeLeft()
    {
        if (accessibilityMenuOpen)
        {
            DecreaseValue();
        }
        else
        {
            if (navigationManager != null && !navigationManager.isNavigating)
            {
                SpeakMessage("Going back to previous waypoint");
            }
        }
    }

    private void OnSwipeUp()
    {
        if (accessibilityMenuOpen)
        {
            SelectPreviousOption();
        }
        else
        {
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
            SelectNextOption();
        }
        else
        {
            // NEW: Enhanced path saving
            if (enhanced3DMapManager != null && enhanced3DMapManager.IsNavigating())
            {
                SpeakMessage("Cannot save during active navigation");
                PlaySound(errorSound);
            }
            else
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
        
        UpdateAccessibilityStatusText();
    }

    private void SelectPreviousOption()
    {
        if (!accessibilityMenuOpen || menuOptions == null || menuOptions.Length == 0)
            return;

        selectedMenuOption = (selectedMenuOption - 1 + menuOptions.Length) % menuOptions.Length;
        SpeakMessage(menuOptions[selectedMenuOption]);
        PlaySound(tapSound);
        
        UpdateAccessibilityStatusText();
    }

    private void IncreaseValue()
    {
        if (!accessibilityMenuOpen)
            return;

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
            case 5: // Voice command settings
                ToggleVoiceCommandSettings();
                break;
            case 6: // Help
                ProvideEnhancedHelp();
                break;
            case 7: // Exit menu
                ToggleAccessibilityMenu();
                break;
        }
    }

    private void DecreaseValue()
    {
        if (!accessibilityMenuOpen)
            return;

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
            case 5: // Voice command settings
                ToggleVoiceCommandSettings();
                break;
            case 6: // Help
                ProvideEnhancedHelp();
                break;
            case 7: // Exit menu
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

    // NEW: Voice command settings toggle
    public void ToggleVoiceCommandSettings()
    {
        if (voiceRecognitionHelper != null)
        {
            enableAutoListening = !enableAutoListening;
            
            if (enableAutoListening)
            {
                voiceRecognitionHelper.StartAutoListening();
                SpeakMessage("Auto voice listening enabled. I will listen for commands automatically.");
            }
            else
            {
                voiceRecognitionHelper.StopAutoListening();
                SpeakMessage("Auto voice listening disabled. Use long press to activate voice commands.");
            }
        }
        else
        {
            SpeakMessage("Voice recognition not available");
        }
        
        PlaySound(tapSound);
        UpdateAccessibilityStatusText();
    }
    
    // NEW: Enhanced help with voice commands
    public void ProvideEnhancedHelp()
    {
        string helpText = "AR Navigation Assistant Help. ";
        helpText += "Gestures: Single tap for directions, double tap to start or stop navigation, triple tap for status, long press for voice commands. ";
        helpText += "Swipe up to load paths, down to save paths. ";
        
        if (enableVoiceCommands && voiceRecognitionHelper != null)
        {
            helpText += "Voice commands available: Say 'Start navigation', 'Stop navigation', 'Record path', 'Save path', 'Load path', ";
            helpText += "'Where am I', 'What's ahead', 'Help', 'Repeat', or 'Emergency' for assistance. ";
            helpText += "You can also use natural phrases like 'begin navigation' or 'tell me what's in front of me'. ";
            
            if (enableAutoListening)
            {
                helpText += "Auto listening is enabled, so I'm always ready for voice commands.";
            }
            else
            {
                helpText += "Use long press to activate voice listening.";
            }
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
            foreach (Image image in allImageElements)
            {
                if (image != null)
                {
                    Color color = image.color;
                    float luminance = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;

                    if (luminance > 0.5f)
                    {
                        color = Color.Lerp(color, Color.white, 0.3f);
                    }
                    else
                    {
                        color = Color.Lerp(color, Color.black, 0.3f);
                    }

                    image.color = color;
                }
            }

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

                    text.outlineWidth = 0.2f;
                    text.outlineColor = luminance > 0.5f ? Color.black : Color.white;
                }
            }
        }
        else
        {
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
        TextMeshProUGUI[] texts = FindObjectsOfType<TextMeshProUGUI>();
        allTextElements.AddRange(texts);

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
            status += "Verbose Mode: " + (verboseMode ? "On" : "Off") + "\n";
            
            // NEW: Voice command status
            if (enableVoiceCommands)
            {
                status += "Voice Commands: " + (enableVoiceCommands ? "On" : "Off") + "\n";
                status += "Auto Listening: " + (enableAutoListening ? "On" : "Off");
            }

            accessibilityStatusText.text = status;
        }
    }

    private void PlaySound(AudioClip clip, int repetitions = 1)
    {
        if (audioSource != null && clip != null)
        {
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
        instructions += "Triple tap for current status. Long press for voice commands. ";
        instructions += "Swipe up to load paths, down to save paths.";

        // NEW: Add voice command info
        if (enableVoiceCommands)
        {
            instructions += " Voice commands are enabled. Say 'Help' for available commands.";
        }

        SpeakMessage(instructions);
    }

    public void AnnounceWhatsAhead()
    {
        if (navigationEnhancer != null)
        {
            navigationEnhancer.AnnounceWhatsAhead();
            return;
        }
        
        if (hitPointManager == null || Camera.main == null)
            return;

        Vector3 rayStart = Camera.main.transform.position;
        Vector3 rayDirection = Camera.main.transform.forward;

        string message = "";
        bool foundSomething = false;

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

        float nearestObstacleDistance = float.MaxValue;
        Vector3 nearestObstacleDirection = Vector3.zero;

        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                float distance = Vector3.Distance(rayStart, pose.position);
                if (distance < 3.0f && distance < nearestObstacleDistance)
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

        if (hitPointManager.poseClassList.Count > 0)
        {
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
                
                string pointType = "path point";
                if (nearestPath.waypointType == WaypointType.StartPoint)
                    pointType = "start point";
                else if (nearestPath.waypointType == WaypointType.EndPoint)
                    pointType = "destination";
                
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
        if (navigationEnhancer != null)
        {
            navigationEnhancer.DescribePathQuality();
            return;
        }
        
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
    
    // NEW: Enhanced voice command handling
    public void HandleVoiceCommand(string command)
    {
        command = command.ToLower();
        
        PlaySound(confirmSound);

        if (command.Contains("start") && (command.Contains("navigation") || command.Contains("navigate")))
        {
            if (enhanced3DMapManager != null && enhanced3DMapManager.GetCurrentMap() != null)
            {
                enhanced3DMapManager.StartEnhancedNavigation();
            }
            else if (navigationManager != null && !navigationManager.isNavigating)
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
        else if (command.Contains("stop") && (command.Contains("navigation") || command.Contains("end")))
        {
            if (enhanced3DMapManager != null && enhanced3DMapManager.IsNavigating())
            {
                enhanced3DMapManager.StopNavigation();
                SpeakMessage("Enhanced navigation stopped");
            }
            else if (navigationManager != null && navigationManager.isNavigating)
            {
                navigationManager.StopNavigation();
                SpeakMessage("Navigation stopped");
            }
            else
            {
                SpeakMessage("Navigation is not active");
            }
        }
        else if (command.Contains("record") && command.Contains("path"))
        {
            if (enhanced3DMapManager != null)
            {
                enhanced3DMapManager.StartPathRecording();
                SpeakMessage("Starting enhanced path recording. Walk slowly along your desired route.");
            }
            else if (hitPointManager != null)
            {
                hitPointManager.StartEnhancedPathCreation();
            }
            else
            {
                SpeakMessage("Enhanced path recording not available");
            }
        }
        else if (command.Contains("save") && command.Contains("path"))
        {
            if (enhanced3DMapManager != null)
            {
                enhanced3DMapManager.CompletePathRecording();
            }
            else
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
        }
        else if (command.Contains("load") && command.Contains("path"))
        {
            if (enhanced3DMapManager != null)
            {
                var availableMaps = enhanced3DMapManager.GetAvailableMaps();
                if (availableMaps.Count > 0)
                {
                    string mapToLoad = availableMaps[availableMaps.Count - 1];
                    enhanced3DMapManager.LoadEnhanced3DMap(mapToLoad);
                    SpeakMessage($"Loading enhanced map: {mapToLoad}");
                }
                else
                {
                    SpeakMessage("No enhanced maps available");
                }
            }
            else
            {
                SpeakMessage("Loading saved paths");
                if (hitPointManager != null)
                {
                    hitPointManager.PromptFilenameToLoad();
                }
            }
        }
        else if (command.Contains("where") && command.Contains("am") && command.Contains("i"))
        {
            AnnounceCurrentStatus();
        }
        else if (command.Contains("what") && command.Contains("ahead"))
        {
            AnnounceWhatsAhead();
        }
        else if (command.Contains("repeat"))
        {
            if (navigationManager != null)
            {
                navigationManager.ProvideNextInstruction();
            }
        }
        else if (command.Contains("help"))
        {
            ProvideEnhancedHelp();
        }
        else if (command.Contains("emergency"))
        {
            HandleEmergencyCommand();
        }
        else
        {
            SpeakMessage("Command not recognized. Say 'help' for available commands.");
        }
    }

    // NEW: Emergency assistance system
    private void HandleEmergencyCommand()
    {
        isEmergencyMode = true;
        
        SpeakMessage("Emergency mode activated. I will help you get to safety.");
        
        if (enhanced3DMapManager != null && enhanced3DMapManager.IsNavigating())
        {
            enhanced3DMapManager.StopNavigation();
        }
        else if (navigationManager != null && navigationManager.isNavigating)
        {
            navigationManager.StopNavigation();
        }
        
        AnnounceCurrentStatus();
        
        if (emergencyAssistanceCoroutine != null)
        {
            StopCoroutine(emergencyAssistanceCoroutine);
        }
        emergencyAssistanceCoroutine = StartCoroutine(EmergencyAssistanceSequence());
    }

    private System.Collections.IEnumerator EmergencyAssistanceSequence()
    {
        yield return new WaitForSeconds(3.0f);
        
        SpeakMessage("Stay calm. I'm going to help you navigate to safety. First, let me scan the area around you.");
        
        yield return new WaitForSeconds(2.0f);
        
        AnnounceWhatsAhead();
        
        yield return new WaitForSeconds(5.0f);
        
        SpeakMessage("If you need immediate assistance, consider calling emergency services. Otherwise, I can help guide you to a safe location. Say 'help me navigate' to continue with guided assistance.");
        
        // Enable emergency voice listening mode
        if (voiceRecognitionHelper != null)
        {
            voiceRecognitionHelper.StartAutoListening();
        }
        
        yield return new WaitForSeconds(30.0f);
        
        // After 30 seconds, exit emergency mode if no further commands
        if (isEmergencyMode)
        {
            isEmergencyMode = false;
            SpeakMessage("Emergency mode deactivated. You can reactivate it anytime by saying 'Emergency'.");
        }
    }
}
