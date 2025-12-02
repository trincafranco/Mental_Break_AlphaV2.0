using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Yarn.Unity;

/// <summary>
/// Manages the main menu scene, including button states and scene transitions.
/// </summary>
public class MenuManager : MonoBehaviour
{
    [Header("UI Elements")]
    [Tooltip("The button that starts or continues the game")]
    public Button startButton;

    [Tooltip("Text component of the start button (TextMeshPro or Text)")]
    public Component startButtonText;

    [Header("Game Settings")]
    [Tooltip("Name of the game scene to load")]
    public string gameSceneName = "MVPScene";

    [Tooltip("Name of the start node in the Yarn dialogue")]
    public string startNodeName = "R1_Start";

    [Header("Audio")]
    [Tooltip("AudioSource for menu background music")]
    public AudioSource menuBGMSource;

    [Tooltip("Main theme music clip for the menu")]
    public AudioClip mainThemeClip;

    private void Start()
    {
        UpdateButtonText();
        
        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartButtonClicked);
        }
        
        // Play main theme music
        PlayMenuMusic();
    }

    private void PlayMenuMusic()
    {
        if (mainThemeClip != null)
        {
            // Create AudioSource if not assigned
            if (menuBGMSource == null)
            {
                menuBGMSource = gameObject.AddComponent<AudioSource>();
            }
            
            menuBGMSource.clip = mainThemeClip;
            menuBGMSource.loop = true;
            menuBGMSource.volume = 0.5f;
            menuBGMSource.Play();
        }
    }

    private void UpdateButtonText()
    {
        if (startButtonText == null) return;

        // Check game state from PlayerPrefs or a saved state
        int currentRun = PlayerPrefs.GetInt("CurrentRun", 0);
        bool runInProgress = PlayerPrefs.GetInt("RunInProgress", 0) == 1;

        string buttonText;
        if (currentRun == 0)
        {
            // First time playing
            buttonText = "Get to Work";
        }
        else if (runInProgress)
        {
            // Run is in progress
            buttonText = "Get Back to Work";
        }
        else
        {
            // Run completed - show appropriate message
            if (currentRun == 1)
            {
                buttonText = "TRAINING RUN ONE COMPLETE.\nBEGIN RUN TWO?";
            }
            else if (currentRun == 2)
            {
                buttonText = "RUN TWO COMPLETE.\nBEGIN RUN THREE?";
            }
            else if (currentRun == 3)
            {
                buttonText = "RUN THREE COMPLETE.\nBEGIN RUN FOUR?";
            }
            else if (currentRun >= 4)
            {
                buttonText = "PLAYTHROUGH COMPLETE.\nTRY AGAIN?";
            }
            else
            {
                buttonText = $"Try Again - Run #{currentRun}";
            }
        }

        // Set text based on component type
#if USE_TMP
        if (startButtonText is TMPro.TextMeshProUGUI tmpText)
        {
            tmpText.text = buttonText;
        }
#endif
        if (startButtonText is UnityEngine.UI.Text regularText)
        {
            regularText.text = buttonText;
        }
    }

    private void OnStartButtonClicked()
    {
        // Load the game scene
        SceneManager.LoadScene(gameSceneName);
    }

    /// <summary>
    /// Called from GameManager when a run completes
    /// </summary>
    public static void MarkRunCompleted(int runNumber)
    {
        PlayerPrefs.SetInt("CurrentRun", runNumber);
        PlayerPrefs.SetInt("RunInProgress", 0);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Called when starting a new run
    /// </summary>
    public static void MarkRunStarted()
    {
        PlayerPrefs.SetInt("RunInProgress", 1);
        PlayerPrefs.Save();
    }
}
