using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TextToSpeech : MonoBehaviour
{
    [Header("Speech Settings")]
    [Range(0.5f, 2.0f)]
    public float speechRate = 1.0f;
    [Range(0.1f, 2.0f)]
    public float speechPitch = 1.0f;
    public int speakerIndex = 0; // Voice to use (if available)
    public string preferredLanguage = ""; // Leave empty for device default

    [Header("Queue Management")]
    public bool useHighPriority = true; // Force immediate playback (interrupt current speech)
    public float pauseBetweenMessages = 0.3f;
    public int maxQueueLength = 10; // Limit queue to prevent backlog
    public bool mergeConsecutiveMessages = true; // Combine messages if they arrive quickly

    [Header("Advanced Options")]
    public bool speakPunctuation = false;
    public bool useCompactTTS = false; // Less detailed speech for quicker delivery
    public float mergeTimeThreshold = 0.5f; // Time threshold to merge messages

    // Internal state
    private Queue<string> speechQueue = new Queue<string>();
    private bool isSpeaking = false;
    private float lastMessageTime = 0f;
    private string lastMessage = "";
    private AndroidJavaObject tts;

    void Start()
    {
        InitializeTTS();
    }

    void OnDisable()
    {
        StopSpeaking();
    }

    void OnDestroy()
    {
        ShutdownTTS();
    }

    private void InitializeTTS()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, new TTSInitListener(this));
                }
            }
            
            // Wait a moment for TTS to initialize
            StartCoroutine(SetTTSParameters());
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error initializing TTS: " + e.Message);
        }
#else
        Debug.Log("TextToSpeech initialized in editor mode (simulated)");
#endif
    }

    private IEnumerator SetTTSParameters()
    {
        yield return new WaitForSeconds(1.0f);

#if UNITY_ANDROID && !UNITY_EDITOR
        if (tts != null)
        {
            // Set speech rate and pitch
            tts.Call<int>("setSpeechRate", speechRate);
            tts.Call<int>("setPitch", speechPitch);
            
            // Set language
            if (!string.IsNullOrEmpty(preferredLanguage))
            {
                try
                {
                    // Try to set specific language
                    using (AndroidJavaClass localeClass = new AndroidJavaClass("java.util.Locale"))
                    {
                        // Parse language tag (e.g., "en-US" -> language "en", country "US")
                        string[] parts = preferredLanguage.Split('-');
                        AndroidJavaObject locale;
                        
                        if (parts.Length > 1)
                        {
                            locale = new AndroidJavaObject("java.util.Locale", parts[0], parts[1]);
                        }
                        else
                        {
                            locale = new AndroidJavaObject("java.util.Locale", preferredLanguage);
                        }
                        
                        int result = tts.Call<int>("setLanguage", locale);
                        if (result == -2) // Language missing data
                        {
                            Debug.LogWarning("TTS: Language data missing for " + preferredLanguage);
                            
                            // Fall back to device default
                            using (AndroidJavaObject defaultLocale = localeClass.CallStatic<AndroidJavaObject>("getDefault"))
                            {
                                tts.Call<int>("setLanguage", defaultLocale);
                            }
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError("Error setting TTS language: " + e.Message);
                    
                    // Fallback to default
                    using (AndroidJavaClass localeClass = new AndroidJavaClass("java.util.Locale"))
                    {
                        using (AndroidJavaObject defaultLocale = localeClass.CallStatic<AndroidJavaObject>("getDefault"))
                        {
                            tts.Call<int>("setLanguage", defaultLocale);
                        }
                    }
                }
            }
            else
            {
                // Use device default language
                using (AndroidJavaClass localeClass = new AndroidJavaClass("java.util.Locale"))
                {
                    using (AndroidJavaObject defaultLocale = localeClass.CallStatic<AndroidJavaObject>("getDefault"))
                    {
                        tts.Call<int>("setLanguage", defaultLocale);
                    }
                }
            }
            
            // Try to set voice if specified
            if (speakerIndex > 0)
            {
                try
                {
                    // Get available voices
                    AndroidJavaObject voiceSet = tts.Call<AndroidJavaObject>("getVoices");
                    AndroidJavaObject voiceIterator = voiceSet.Call<AndroidJavaObject>("iterator");
                    
                    int currentIndex = 0;
                    AndroidJavaObject selectedVoice = null;
                    
                    while (voiceIterator.Call<bool>("hasNext"))
                    {
                        AndroidJavaObject voice = voiceIterator.Call<AndroidJavaObject>("next");
                        currentIndex++;
                        
                        if (currentIndex == speakerIndex)
                        {
                            selectedVoice = voice;
                            break;
                        }
                    }
                    
                    if (selectedVoice != null)
                    {
                        tts.Call<int>("setVoice", selectedVoice);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("Error setting TTS voice: " + e.Message);
                }
            }
        }
#endif

        Debug.Log("TTS parameters set");
    }

    public void Speak(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        // Format message for better TTS results
        message = FormatMessage(message);

        // Check if we should merge with last message
        if (mergeConsecutiveMessages &&
            Time.time - lastMessageTime < mergeTimeThreshold &&
            !string.IsNullOrEmpty(lastMessage))
        {
            // Combine messages with a comma between
            message = lastMessage + ", " + message;

            // Remove the last message from queue if possible
            if (speechQueue.Count > 0)
            {
                string[] queueArray = speechQueue.ToArray();
                if (queueArray[queueArray.Length - 1] == lastMessage)
                {
                    // Recreate queue without the last message
                    speechQueue.Clear();
                    for (int i = 0; i < queueArray.Length - 1; i++)
                    {
                        speechQueue.Enqueue(queueArray[i]);
                    }
                }
            }
        }

        lastMessage = message;
        lastMessageTime = Time.time;

        // Check queue limits
        if (speechQueue.Count >= maxQueueLength)
        {
            // Remove oldest message if using FIFO approach
            if (!useHighPriority)
            {
                speechQueue.Dequeue();
            }
            else
            {
                // With high priority, just clear queue and speak this message
                speechQueue.Clear();
            }
        }

        speechQueue.Enqueue(message);

        if (!isSpeaking)
            StartCoroutine(ProcessSpeechQueue());
    }

    private IEnumerator ProcessSpeechQueue()
    {
        isSpeaking = true;

        while (speechQueue.Count > 0)
        {
            string message = speechQueue.Dequeue();
            bool success = SpeakImmediate(message);

            if (success)
            {
                // Wait until speech is done
                float waitTime = 0;
                float maxWaitTime = message.Length * 0.05f + 3.0f; // Estimate based on message length

                while (waitTime < maxWaitTime)
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    if (tts != null && !tts.Call<bool>("isSpeaking"))
                        break;
#endif
                    yield return new WaitForSeconds(0.1f);
                    waitTime += 0.1f;
                }

                // Add a small pause between messages
                yield return new WaitForSeconds(pauseBetweenMessages);
            }
        }

        isSpeaking = false;
    }

    private bool SpeakImmediate(string message)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (tts != null)
        {
            try
            {
                // Create HashMap for speech parameters
                using (AndroidJavaObject hashMap = new AndroidJavaObject("java.util.HashMap"))
                {
                    // Set queue mode (add to queue or flush queue)
                    int queueMode = useHighPriority ? 0 : 1; // QUEUE_FLUSH = 0, QUEUE_ADD = 1
                    
                    // Set additional parameters
                    int result = tts.Call<int>("speak", message, queueMode, hashMap);
                    
                    return result == 0; // SUCCESS = 0
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error during TTS speak: " + e.Message);
                return false;
            }
        }
        return false;
#else
        // Simulated speech in editor
        Debug.Log("TTS: " + message);
        return true;
#endif
    }

    public void StopSpeaking()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (tts != null)
        {
            tts.Call<int>("stop");
        }
#endif

        speechQueue.Clear();
        isSpeaking = false;
        StopAllCoroutines();
    }

    private void ShutdownTTS()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (tts != null)
        {
            tts.Call("shutdown");
            tts.Dispose();
            tts = null;
        }
#endif
    }

    private string FormatMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        // Apply transformations based on settings
        if (useCompactTTS)
        {
            // Remove less important words for quicker speech
            message = message.Replace(" please", "");
            message = message.Replace(" the ", " ");
            message = message.Replace(" a ", " ");
            message = message.Replace(" an ", " ");
            message = message.Replace(" that ", " ");
            message = message.Replace(" there ", " ");
        }

        // Ensure proper spacing around punctuation
        if (!speakPunctuation)
        {
            // Add spaces around punctuation to improve speech
            message = message.Replace(".", ". ");
            message = message.Replace(",", ", ");
            message = message.Replace(":", ": ");
            message = message.Replace(";", "; ");
            message = message.Replace("!", "! ");
            message = message.Replace("?", "? ");
        }

        // Clean up extra spaces
        while (message.Contains("  "))
        {
            message = message.Replace("  ", " ");
        }

        return message.Trim();
    }

    public bool IsSpeaking()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (tts != null)
        {
            return tts.Call<bool>("isSpeaking") || isSpeaking;
        }
#endif
        return isSpeaking;
    }

    public void UpdateSpeechRate(float rate)
    {
        speechRate = Mathf.Clamp(rate, 0.5f, 2.0f);

#if UNITY_ANDROID && !UNITY_EDITOR
        if (tts != null)
        {
            tts.Call<int>("setSpeechRate", speechRate);
        }
#endif
    }

    public void UpdateSpeechPitch(float pitch)
    {
        speechPitch = Mathf.Clamp(pitch, 0.1f, 2.0f);

#if UNITY_ANDROID && !UNITY_EDITOR
        if (tts != null)
        {
            tts.Call<int>("setPitch", speechPitch);
        }
#endif
    }

    // Get available languages for the UI
    public string[] GetAvailableLanguages()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (tts != null)
        {
            try
            {
                List<string> languages = new List<string>();
                
                // Get available locales
                AndroidJavaObject localeSet = tts.Call<AndroidJavaObject>("getAvailableLanguages");
                AndroidJavaObject iterator = localeSet.Call<AndroidJavaObject>("iterator");
                
                while (iterator.Call<bool>("hasNext"))
                {
                    AndroidJavaObject locale = iterator.Call<AndroidJavaObject>("next");
                    string langTag = locale.Call<string>("toLanguageTag");
                    languages.Add(langTag);
                }
                
                return languages.ToArray();
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error getting available languages: " + e.Message);
            }
        }
#endif

        // Default fallback
        return new string[] { "en-US", "en-GB", "es-ES", "fr-FR", "de-DE", "it-IT", "ja-JP" };
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private class TTSInitListener : AndroidJavaProxy
    {
        private TextToSpeech ttsComponent;
        
        public TTSInitListener(TextToSpeech tts) : base("android.speech.tts.TextToSpeech$OnInitListener") 
        { 
            ttsComponent = tts;
        }
        
        public void onInit(int status)
        {
            // Status code 0 indicates success
            if (status == 0)
            {
                Debug.Log("TTS initialized successfully");
                
                // Speak a silent message to initialize the engine
                if (ttsComponent != null)
                {
                    ttsComponent.Speak("");
                }
            }
            else
            {
                Debug.LogError("Failed to initialize TTS, status code: " + status);
            }
        }
    }
#endif
}