using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using System;

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
    private bool permissionGranted = false;

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
            command = "Record path",
            alternateCommands = new string[] { "Start recording", "Create path", "Record route" },
            description = "Start recording a new path"
        },
        new VoiceCommand
        {
            command = "Save path",
            alternateCommands = new string[] { "Stop recording", "Save route", "Finish path" },
            description = "Save the current path"
        },
        new VoiceCommand
        {
            command = "Load path",
            alternateCommands = new string[] { "Open path", "Browse paths", "Select path", "Load route" },
            description = "Open the path loading screen"
        },
        new VoiceCommand
        {
            command = "Emergency",
            alternateCommands = new string[] { "Help me", "I'm lost", "SOS" },
            description = "Get immediate assistance"
        },
        new VoiceCommand
        {
            command = "Repeat",
            alternateCommands = new string[] { "Say again", "Repeat that", "Again" },
            description = "Repeat the last instruction"
        }
    };

    void Start()
    {
        Debug.Log("VoiceRecognitionHelper: Starting initialization");

        // Check platform support
        isSupportedPlatform = Application.platform == RuntimePlatform.Android;

        // Set up audio source
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        // Set up default commands
        if (predefinedCommands == null || predefinedCommands.Length == 0)
        {
            predefinedCommands = defaultCommands;
        }

        // Initialize voice recognition
        StartCoroutine(InitializeWithDelay());
    }

    private IEnumerator InitializeWithDelay()
    {
        // Wait a bit for the app to fully initialize
        yield return new WaitForSeconds(1.0f);

        Initialize();

        // Start auto-listening if enabled and initialized
        if (autoStartListening && isInitialized)
        {
            yield return new WaitForSeconds(2.0f);
            StartAutoListening();
        }
    }

    public void Initialize()
    {
        Debug.Log("VoiceRecognitionHelper: Initialize called");

        if (isInitialized)
        {
            Debug.Log("Already initialized");
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!isSupportedPlatform)
        {
            Debug.LogWarning("Voice recognition only supported on Android");
            return;
        }

        try
        {
            // Check and request permissions first
            if (!CheckPermissions())
            {
                RequestPermissions();
                return;
            }
            
            InitializeAndroidSpeechRecognizer();
        }
        catch (Exception e)
        {
            Debug.LogError("Error initializing voice recognition: " + e.Message);
            PlayErrorSound();
        }
#else
        // Editor mode - simulate initialization
        isInitialized = true;
        Debug.Log("Voice recognition simulated in Unity Editor");
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private bool CheckPermissions()
    {
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    int permissionResult = activity.Call<int>("checkSelfPermission", "android.permission.RECORD_AUDIO");
                    permissionGranted = (permissionResult == 0); // PERMISSION_GRANTED = 0
                    Debug.Log("Permission check result: " + (permissionGranted ? "GRANTED" : "DENIED"));
                    return permissionGranted;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error checking permissions: " + e.Message);
            return false;
        }
    }
    
    private void RequestPermissions()
    {
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    string[] permissions = new string[] { "android.permission.RECORD_AUDIO" };
                    activity.Call("requestPermissions", permissions, 1001);
                    
                    Debug.Log("Permission requested, will retry initialization");
                    StartCoroutine(RetryInitializationAfterPermission());
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error requesting permissions: " + e.Message);
        }
    }
    
    private IEnumerator RetryInitializationAfterPermission()
    {
        yield return new WaitForSeconds(3.0f);
        
        if (CheckPermissions())
        {
            InitializeAndroidSpeechRecognizer();
        }
        else
        {
            Debug.LogError("Permission still not granted after request");
        }
    }
    
    private void InitializeAndroidSpeechRecognizer()
    {
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    AndroidJavaClass speechRecognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer");
                    
                    bool isRecognitionAvailable = speechRecognizerClass.CallStatic<bool>("isRecognitionAvailable", activity);
                    if (!isRecognitionAvailable)
                    {
                        Debug.LogError("Speech recognition not available on this device");
                        return;
                    }
                    
                    speechRecognizer = speechRecognizerClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", activity);
                    
                    if (speechRecognizer == null)
                    {
                        Debug.LogError("Failed to create speech recognizer");
                        return;
                    }
                    
                    // Create recognition listener
                    SpeechRecognitionListener listener = new SpeechRecognitionListener(this);
                    speechRecognizer.Call("setRecognitionListener", listener);
                    
                    // Create and configure intent
                    AndroidJavaClass recognizerIntent = new AndroidJavaClass("android.speech.RecognizerIntent");
                    speechRecognizerIntent = new AndroidJavaObject("android.content.Intent", 
                        recognizerIntent.GetStatic<string>("ACTION_RECOGNIZE_SPEECH"));
                    
                    speechRecognizerIntent.Call<AndroidJavaObject>("putExtra",
                        recognizerIntent.GetStatic<string>("EXTRA_LANGUAGE_MODEL"),
                        recognizerIntent.GetStatic<string>("LANGUAGE_MODEL_FREE_FORM"));
                        
                    speechRecognizerIntent.Call<AndroidJavaObject>("putExtra",
                        recognizerIntent.GetStatic<string>("EXTRA_PARTIAL_RESULTS"), true);
                        
                    speechRecognizerIntent.Call<AndroidJavaObject>("putExtra",
                        recognizerIntent.GetStatic<string>("EXTRA_MAX_RESULTS"), 5);
                        
                    speechRecognizerIntent.Call<AndroidJavaObject>("putExtra",
                        recognizerIntent.GetStatic<string>("EXTRA_CALLING_PACKAGE"),
                        activity.Call<string>("getPackageName"));
                    
                    isInitialized = true;
                    Debug.Log("Android speech recognizer initialized successfully");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error initializing Android speech recognizer: " + e.Message);
            PlayErrorSound();
        }
    }
#endif

    public void StartListening()
    {
        Debug.Log("StartListening called. Initialized: " + isInitialized + ", Listening: " + isListening);

        if (!isInitialized)
        {
            Debug.LogWarning("Voice recognition not initialized");
            Initialize();
            return;
        }

        if (isListening)
        {
            Debug.Log("Already listening");
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
                Debug.Log("Starting Android speech recognition");
                speechRecognizer.Call("startListening", speechRecognizerIntent);
                isListening = true;
                
                StartCoroutine(ListeningTimeout());
                
                if (showDebugInfo)
                    Debug.Log("Voice recognition started listening");
            }
            else
            {
                Debug.LogError("Speech recognizer or intent is null");
                Initialize();
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error starting speech recognition: " + e.Message);
            PlayErrorSound();
            isListening = false;
        }
#else
        // Editor simulation
        isListening = true;
        StartCoroutine(SimulateCommand());
#endif
    }

    public void StopListening()
    {
        if (!isListening)
            return;

        if (audioSource != null && stopListeningSound != null)
        {
            audioSource.PlayOneShot(stopListeningSound);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (speechRecognizer != null)
            {
                speechRecognizer.Call("stopListening");
                isListening = false;
                
                if (showDebugInfo)
                    Debug.Log("Voice recognition stopped");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error stopping speech recognition: " + e.Message);
        }
#else
        isListening = false;
#endif
    }

    public void StartAutoListening()
    {
        if (autoListenCoroutine == null && isInitialized)
        {
            autoListenCoroutine = StartCoroutine(AutoListenCoroutine());
            Debug.Log("Auto-listening started");
        }
    }

    public void StopAutoListening()
    {
        if (autoListenCoroutine != null)
        {
            StopCoroutine(autoListenCoroutine);
            autoListenCoroutine = null;
            Debug.Log("Auto-listening stopped");
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
        yield return new WaitForSeconds(UnityEngine.Random.Range(2.0f, 4.0f));

        if (predefinedCommands != null && predefinedCommands.Length > 0)
        {
            int randomIndex = UnityEngine.Random.Range(0, predefinedCommands.Length);
            string simulatedCommand = predefinedCommands[randomIndex].command;

            Debug.Log("Simulated voice command: " + simulatedCommand);
            OnRecognizedSpeech(simulatedCommand);
        }

        isListening = false;
    }
#endif

    public void OnRecognizedSpeech(string rawCommand)
    {
        if (audioSource != null && recognizedCommandSound != null)
        {
            audioSource.PlayOneShot(recognizedCommandSound);
        }

        string matchedCommand = ProcessCommand(rawCommand);
        lastRecognizedCommand = matchedCommand;
        isListening = false;

        if (showDebugInfo)
            Debug.Log("Voice command: '" + rawCommand + "' -> '" + matchedCommand + "'");

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

        string lowerCommand = rawCommand.ToLower().Trim();

        // Direct matches first
        foreach (var command in predefinedCommands)
        {
            if (lowerCommand.Contains(command.command.ToLower()))
            {
                return command.command;
            }

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

        // Partial matching
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

            if (matchedWords > 0 && (float)matchedWords / commandWords.Length >= 0.5f)
            {
                return command.command;
            }
        }

        return "";
    }

    private void PlayErrorSound()
    {
        if (audioSource != null && errorSound != null)
        {
            audioSource.PlayOneShot(errorSound);
        }
    }

    public bool IsListening() { return isListening; }
    public bool IsInitialized() { return isInitialized; }
    public string GetLastRecognizedCommand() { return lastRecognizedCommand; }

    void OnDestroy()
    {
        StopAutoListening();

#if UNITY_ANDROID && !UNITY_EDITOR
        if (speechRecognizer != null)
        {
            try
            {
                speechRecognizer.Call("destroy");
                speechRecognizer.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogError("Error destroying speech recognizer: " + e.Message);
            }
        }
        
        if (speechRecognizerIntent != null)
        {
            speechRecognizerIntent.Dispose();
        }
#endif
    }
}

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
                Debug.Log("Speech recognized: " + command);
                
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
        UnityMainThreadDispatcher.Instance().Enqueue(() => {
            helper.StopListening();
        });
    }
    void onPartialResults(AndroidJavaObject bundle) { }
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
