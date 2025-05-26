using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using System;

// Define StringEvent class
[System.Serializable]
public class StringEvent : UnityEvent<string> { }

[System.Serializable]
public class VoiceCommand
{
    public string command;
    public string[] alternateCommands;
    public string description;
}

public class VoiceRecognitionHelper : MonoBehaviour
{
    // Listeners
    public StringEvent OnCommandRecognized = new StringEvent();
    
    [Header("Voice Recognition Settings")]
    public VoiceCommand[] predefinedCommands;
    public float recognitionConfidenceThreshold = 0.7f;
    public bool showDebugInfo = true;
    public float listeningTimeout = 5.0f;
    public bool autoStartListening = false;
    public float autoListenInterval = 10.0f;
    
    [Header("Audio Feedback")]
    public AudioSource audioSource;
    public AudioClip startListeningSound;
    public AudioClip stopListeningSound;
    public AudioClip recognizedCommandSound;
    public AudioClip errorSound;
    
    // State tracking
    private bool isInitialized = false;
    private bool isListening = false;
    private string lastRecognizedCommand = "";
    private AndroidJavaObject speechRecognizer;
    private AndroidJavaObject speechRecognizerIntent;
    private bool isSupportedPlatform = false;
    private Coroutine autoListenCoroutine;
    
    // Default commands
    private VoiceCommand[] defaultCommands = new VoiceCommand[]
    {
        new VoiceCommand 
        { 
            command = "Start navigation", 
            alternateCommands = new string[] { "Begin navigation", "Navigate", "Start guiding", "Start", "Go" },
            description = "Start following the loaded path"
        },
        new VoiceCommand 
        { 
            command = "Stop navigation", 
            alternateCommands = new string[] { "End navigation", "Stop guiding", "Pause navigation", "Stop", "Halt" },
            description = "Stop the current navigation session"
        },
        new VoiceCommand 
        { 
            command = "Where am I", 
            alternateCommands = new string[] { "Current location", "My position", "Location status", "Status" },
            description = "Announce your current position and status"
        },
        new VoiceCommand 
        { 
            command = "What's ahead", 
            alternateCommands = new string[] { "Describe surroundings", "What's around me", "Scan area", "Look ahead" },
            description = "Describe what's in front of you"
        },
        new VoiceCommand 
        { 
            command = "Help", 
            alternateCommands = new string[] { "Instructions", "Commands", "How to use", "Assist me" },
            description = "Get help about how to use the app"
        },
        new VoiceCommand 
        { 
            command = "Open menu", 
            alternateCommands = new string[] { "Accessibility menu", "Settings menu", "Options", "Menu" },
            description = "Open the accessibility settings menu"
        },
        new VoiceCommand 
        { 
            command = "Close menu", 
            alternateCommands = new string[] { "Exit menu", "Hide menu", "Back" },
            description = "Close the currently open menu"
        },
        new VoiceCommand 
        { 
            command = "Load path", 
            alternateCommands = new string[] { "Open path", "Browse paths", "Select path", "Load route" },
            description = "Open the path loading screen"
        },
        new VoiceCommand 
        { 
            command = "Save path", 
            alternateCommands = new string[] { "Store path", "Save route", "Save current path", "Store route" },
            description = "Save the current path to storage"
        },
        new VoiceCommand 
        { 
            command = "Emergency help", 
            alternateCommands = new string[] { "Emergency", "Help me", "I'm lost", "SOS" },
            description = "Get immediate assistance in case of emergency"
        },
        new VoiceCommand 
        { 
            command = "Repeat", 
            alternateCommands = new string[] { "Say again", "Repeat that", "Again" },
            description = "Repeat the last instruction"
        },
        new VoiceCommand 
        { 
            command = "Next direction", 
            alternateCommands = new string[] { "Next instruction", "What's next", "Continue" },
            description = "Get the next navigation instruction"
        }
    };
    
    void Start()
    {
        // Check if we're on a supported platform
        isSupportedPlatform = Application.platform == RuntimePlatform.Android;
        
        // Set up audio source if needed
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
        
        // Set up default commands if none provided
        if (predefinedCommands == null || predefinedCommands.Length == 0)
        {
            predefinedCommands = defaultCommands;
        }
        
        // Initialize voice recognition
        Initialize();
        
        // Start auto-listening if enabled
        if (autoStartListening && isInitialized)
        {
            StartAutoListening();
        }
    }
    
    public void Initialize()
    {
        Debug.Log("VoiceRecognitionHelper Initialize called");
        
        if (!isSupportedPlatform)
        {
            Debug.LogWarning("Voice recognition is only supported on Android platforms.");
            // Still mark as initialized for editor testing
            isInitialized = true;
            return;
        }

        if (isInitialized)
        {
            return;
        }

        #if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    // Check if we have the RECORD_AUDIO permission
                    int permissionResult = activity.Call<int>("checkSelfPermission", "android.permission.RECORD_AUDIO");
                    if (permissionResult != 0) // PERMISSION_GRANTED = 0
                    {
                        Debug.LogWarning("RECORD_AUDIO permission not granted. Requesting permission...");
                        string[] permissions = new string[] { "android.permission.RECORD_AUDIO" };
                        activity.Call("requestPermissions", permissions, 1001);
                        
                        // Schedule re-initialization after a delay
                        StartCoroutine(RetryInitializationAfterPermission());
                        return;
                    }

                    // Create the speech recognizer
                    AndroidJavaClass speechRecognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer");

                    // Check if speech recognition is available
                    bool isRecognitionAvailable = speechRecognizerClass.CallStatic<bool>("isRecognitionAvailable", activity);
                    if (!isRecognitionAvailable)
                    {
                        Debug.LogError("Speech recognition is not available on this device.");
                        return;
                    }

                    speechRecognizer = speechRecognizerClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", activity);

                    // Create the recognition listener
                    AndroidJavaProxy listener = new SpeechRecognitionListener(this);
                    speechRecognizer.Call("setRecognitionListener", listener);
                    
                    // Create and configure the intent
                    AndroidJavaClass recognizerIntent = new AndroidJavaClass("android.speech.RecognizerIntent");
                    speechRecognizerIntent = new AndroidJavaObject("android.content.Intent", 
                        recognizerIntent.GetStatic<string>("ACTION_RECOGNIZE_SPEECH"));
                    
                    speechRecognizerIntent.Call<AndroidJavaObject>("putExtra",
                        recognizerIntent.GetStatic<string>("EXTRA_LANGUAGE_MODEL"),
                        recognizerIntent.GetStatic<string>("LANGUAGE_MODEL_FREE_FORM"));
                        
                    speechRecognizerIntent.Call<AndroidJavaObject>("putExtra",
                        recognizerIntent.GetStatic<string>("EXTRA_PARTIAL_RESULTS"),
                        true);
                        
                    speechRecognizerIntent.Call<AndroidJavaObject>("putExtra",
                        recognizerIntent.GetStatic<string>("EXTRA_MAX_RESULTS"),
                        5);
                        
                    speechRecognizerIntent.Call<AndroidJavaObject>("putExtra",
                        recognizerIntent.GetStatic<string>("EXTRA_CALLING_PACKAGE"),
                        activity.Call<string>("getPackageName"));
                }
            }
            
            isInitialized = true;
            Debug.Log("Voice recognition initialized successfully");
        }
        catch (Exception e)
        {
            Debug.LogError("Error initializing voice recognition: " + e.Message + "\n" + e.StackTrace);
            PlayErrorSound();
        }
        #else
        // Simulated initialization for editor testing
        isInitialized = true;
        Debug.Log("Voice recognition simulated in Unity Editor");
        #endif
    }
    
    private IEnumerator RetryInitializationAfterPermission()
    {
        yield return new WaitForSeconds(2.0f);
        Initialize();
    }

    public void StartListening()
    {
        Debug.Log("StartListening called. isInitialized: " + isInitialized + ", isListening: " + isListening);
        
        if (!isInitialized)
        {
            Debug.LogWarning("Voice recognition not initialized. Attempting to initialize...");
            Initialize();
            return;
        }
        
        if (isListening)
        {
            Debug.Log("Already listening, ignoring request");
            return;
        }
                
        // Play feedback sound
        if (audioSource != null && startListeningSound != null)
        {
            audioSource.PlayOneShot(startListeningSound);
        }
        
        #if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (speechRecognizer != null && speechRecognizerIntent != null)
            {
                // Start listening
                speechRecognizer.Call("startListening", speechRecognizerIntent);
                isListening = true;
                
                // Start timeout coroutine
                StartCoroutine(ListeningTimeout());
                
                if (showDebugInfo)
                    Debug.Log("Voice recognition started listening");
            }
            else
            {
                Debug.LogError("Speech recognizer or intent is null. Re-initializing...");
                Initialize();
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error starting speech recognition: " + e.Message + "\n" + e.StackTrace);
            PlayErrorSound();
            isListening = false;
        }
        #endif

        #if UNITY_EDITOR
        // Simulate listening in editor with a test command after a delay
        isListening = true;
        StartCoroutine(SimulateCommand());
        #endif
        
        Debug.Log("StartListening completed. isListening: " + isListening);
    }
    
    public void StopListening()
    {
        if (!isListening)
            return;
            
        // Play feedback sound
        if (audioSource != null && stopListeningSound != null)
        {
            audioSource.PlayOneShot(stopListeningSound);
        }
        
        #if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (speechRecognizer != null)
            {
                // Stop listening
                speechRecognizer.Call("stopListening");
                isListening = false;
                
                if (showDebugInfo)
                    Debug.Log("Voice recognition stopped listening");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error stopping speech recognition: " + e.Message);
            PlayErrorSound();
        }
        #endif

        #if UNITY_EDITOR
        // End simulated listening in editor
        isListening = false;
        #endif
    }
    
    public void StartAutoListening()
    {
        if (autoListenCoroutine == null)
        {
            autoListenCoroutine = StartCoroutine(AutoListenCoroutine());
        }
    }
    
    public void StopAutoListening()
    {
        if (autoListenCoroutine != null)
        {
            StopCoroutine(autoListenCoroutine);
            autoListenCoroutine = null;
        }
    }
    
    private IEnumerator AutoListenCoroutine()
    {
        while (true)
        {
            if (!isListening && isInitialized)
            {
                StartListening();
            }
            
            yield return new WaitForSeconds(autoListenInterval);
        }
    }
    
    private IEnumerator ListeningTimeout()
    {
        yield return new WaitForSeconds(listeningTimeout);
        
        if (isListening)
        {
            if (showDebugInfo)
                Debug.Log("Voice recognition timeout");
            StopListening();
        }
    }
    
    #if UNITY_EDITOR
    private IEnumerator SimulateCommand()
    {
        // Wait a random amount of time (1 to 3 seconds)
        yield return new WaitForSeconds(UnityEngine.Random.Range(1.0f, 3.0f));
        
        // Pick a random command for testing
        if (predefinedCommands != null && predefinedCommands.Length > 0)
        {
            int randomIndex = UnityEngine.Random.Range(0, predefinedCommands.Length);
            string simulatedCommand = predefinedCommands[randomIndex].command;
            
            Debug.Log("Simulated voice command: " + simulatedCommand);
            
            // Process the command
            OnRecognizedSpeech(simulatedCommand);
        }
        
        isListening = false;
    }
    #endif
    
    // Called when speech is recognized
    public void OnRecognizedSpeech(string rawCommand)
    {
        // Play feedback sound
        if (audioSource != null && recognizedCommandSound != null)
        {
            audioSource.PlayOneShot(recognizedCommandSound);
        }
        
        // Process and match the command
        string matchedCommand = ProcessCommand(rawCommand);
        
        lastRecognizedCommand = matchedCommand;
        isListening = false;
        
        if (showDebugInfo)
            Debug.Log("Voice command recognized: '" + rawCommand + "' -> matched: '" + matchedCommand + "'");
        
        // Trigger the event
        if (!string.IsNullOrEmpty(matchedCommand))
        {
            OnCommandRecognized.Invoke(matchedCommand);
        }
        else
        {
            Debug.Log("No matching command found for: " + rawCommand);
            PlayErrorSound();
        }
    }
    
    private string ProcessCommand(string rawCommand)
    {
        if (string.IsNullOrEmpty(rawCommand) || predefinedCommands == null)
            return "";
            
        // Convert to lowercase for comparison
        string lowerCommand = rawCommand.ToLower().Trim();
        
        // Try to match against predefined commands
        foreach (var command in predefinedCommands)
        {
            // Check main command
            if (lowerCommand.Contains(command.command.ToLower()))
            {
                return command.command;
            }
            
            // Check alternate commands
            if (command.alternateCommands != null)
            {
                foreach (var alt in command.alternateCommands)
                {
                    if (lowerCommand.Contains(alt.ToLower()))
                    {
                        return command.command;
                    }
                }
            }
        }
        
        // Try partial matching for better recognition
        foreach (var command in predefinedCommands)
        {
            string[] commandWords = command.command.ToLower().Split(' ');
            int matchedWords = 0;
            
            foreach (string word in commandWords)
            {
                if (lowerCommand.Contains(word))
                {
                    matchedWords++;
                }
            }
            
            // If we matched more than half the words, consider it a match
            if (matchedWords > 0 && (float)matchedWords / commandWords.Length >= 0.5f)
            {
                return command.command;
            }
        }
        
        return ""; // No match found
    }
    
    private void PlayErrorSound()
    {
        if (audioSource != null && errorSound != null)
        {
            audioSource.PlayOneShot(errorSound);
        }
    }
    
    void OnDestroy()
    {
        // Stop auto-listening
        StopAutoListening();
        
        // Clean up the speech recognizer
        if (isInitialized)
        {
            #if UNITY_ANDROID && !UNITY_EDITOR
            if (speechRecognizer != null)
            {
                try
                {
                    speechRecognizer.Call("destroy");
                    speechRecognizer.Dispose();
                    speechRecognizer = null;
                }
                catch (Exception e)
                {
                    Debug.LogError("Error destroying speech recognizer: " + e.Message);
                }
            }
            
            if (speechRecognizerIntent != null)
            {
                speechRecognizerIntent.Dispose();
                speechRecognizerIntent = null;
            }
            #endif
        }
    }
    
    // Utility methods
    public string GetAvailableCommandsText()
    {
        if (predefinedCommands == null || predefinedCommands.Length == 0)
            return "No voice commands available.";
            
        string commandsText = "Available voice commands:\n";
        
        foreach (var command in predefinedCommands)
        {
            commandsText += "• \"" + command.command + "\"";
            if (!string.IsNullOrEmpty(command.description))
            {
                commandsText += " - " + command.description;
            }
            commandsText += "\n";
        }
        
        return commandsText;
    }
    
    public bool IsListening()
    {
        return isListening;
    }
    
    public bool IsInitialized()
    {
        return isInitialized;
    }
    
    public string GetLastRecognizedCommand()
    {
        return lastRecognizedCommand;
    }
}

// Separate class for the Android speech recognition listener
#if UNITY_ANDROID && !UNITY_EDITOR
public class SpeechRecognitionListener : AndroidJavaProxy
{
    private VoiceRecognitionHelper helper;
    
    public SpeechRecognitionListener(VoiceRecognitionHelper helper) : base("android.speech.RecognitionListener")
    {
        this.helper = helper;
    }
    
    void onResults(AndroidJavaObject results)
    {
        try
        {
            AndroidJavaObject matches = results.Call<AndroidJavaObject>("getStringArrayList", "android.speech.RecognizerIntent.RESULTS");
            int size = matches.Call<int>("size");
            
            if (size > 0)
            {
                string command = matches.Call<string>("get", 0);
                
                // Use Unity's main thread dispatcher
                UnityMainThreadDispatcher.Instance().Enqueue(() => {
                    helper.OnRecognizedSpeech(command);
                });
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error in onResults: " + e.Message);
        }
    }
    
    void onError(int error)
    {
        string errorMessage = GetErrorMessage(error);
        Debug.LogError("Speech recognition error: " + error + " - " + errorMessage);
        
        UnityMainThreadDispatcher.Instance().Enqueue(() => {
            helper.StopListening();
        });
    }
    
    void onReadyForSpeech(AndroidJavaObject bundle) 
    {
        Debug.Log("Ready for speech");
    }
    
    void onBeginningOfSpeech() 
    {
        Debug.Log("Beginning of speech");
    }
    
    void onRmsChanged(float rmsdB) { }
    
    void onBufferReceived(byte[] buffer) { }
    
    void onEndOfSpeech() 
    {
        Debug.Log("End of speech");
    }
    
    void onPartialResults(AndroidJavaObject bundle) 
    {
        // Handle partial results if needed
    }
    
    void onEvent(int eventType, AndroidJavaObject bundle) { }
    
    private string GetErrorMessage(int error)
    {
        switch (error)
        {
            case 1: return "Network timeout";
            case 2: return "Network error";
            case 3: return "Audio error";
            case 4: return "Server error";
            case 5: return "Client error";
            case 6: return "Speech timeout";
            case 7: return "No match";
            case 8: return "Recognizer busy";
            case 9: return "Insufficient permissions";
            default: return "Unknown error";
        }
    }
}
#endif
