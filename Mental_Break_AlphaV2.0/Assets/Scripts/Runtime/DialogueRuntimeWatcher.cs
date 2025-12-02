using System;
using System.Collections;
using UnityEngine;
using Yarn.Unity;

/// <summary>
/// Keeps track of the active DialogueRunner and its VariableStorage.
/// Retries until the runtime is available, emits callbacks, and logs warnings
/// if the runtime fails to appear within the configured timeout.
/// </summary>
[DefaultExecutionOrder(-250)]
public class DialogueRuntimeWatcher : MonoBehaviour
{
    private static DialogueRuntimeWatcher instance;

    [Header("Lookup Settings")]
    [Tooltip("Delay (in seconds) before the watcher performs the first lookup.")]
    [SerializeField] private float initialDelay = 0.1f;

    [Tooltip("Additional delay for WebGL builds (WebGL loads more asynchronously).")]
    [SerializeField] private float webGLExtraDelay = 0.5f;

    [Tooltip("Interval (in seconds) between runtime lookups.")]
    [SerializeField] private float checkInterval = 0.25f;

    [Header("Diagnostics")]
    [Tooltip("How many seconds to wait before emitting a warning about a missing DialogueRunner.")]
    [SerializeField] private float warnAfterSeconds = 5f;

    [Tooltip("Emit verbose logs when the watcher acquires or loses the runtime.")]
    [SerializeField] private bool enableVerboseLogging = false;

    private Coroutine monitorRoutine;
    private float missingTimer = 0f;
    private bool warningRaised = false;

    /// <summary>
    /// Event invoked whenever both the DialogueRunner and its VariableStorage are available.
    /// </summary>
    public event Action<DialogueRunner, VariableStorageBehaviour> RuntimeAvailable;

    /// <summary>
    /// Event invoked when the DialogueRunner disappears from the scene.
    /// </summary>
    public event Action RuntimeLost;

    /// <summary>
    /// The currently discovered DialogueRunner, or null if none is active.
    /// </summary>
    public DialogueRunner CurrentRunner { get; private set; }

    /// <summary>
    /// The current VariableStorage associated with <see cref="CurrentRunner"/>.
    /// </summary>
    public VariableStorageBehaviour CurrentVariableStorage { get; private set; }

    /// <summary>
    /// Indicates whether both the runner and variable storage are ready for use.
    /// </summary>
    public bool HasRuntime => CurrentRunner != null && CurrentVariableStorage != null;

    /// <summary>
    /// Get the singleton instance, creating one in the scene if necessary.
    /// </summary>
    public static DialogueRuntimeWatcher Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<DialogueRuntimeWatcher>();
                if (instance == null)
                {
                    var watcherObject = new GameObject("DialogueRuntimeWatcher");
                    instance = watcherObject.AddComponent<DialogueRuntimeWatcher>();
                }
            }
            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            if (enableVerboseLogging)
            {
                Debug.LogWarning("DialogueRuntimeWatcher: Duplicate instance detected. Destroying the newer component.", this);
            }
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        if (monitorRoutine == null)
        {
            monitorRoutine = StartCoroutine(MonitorRuntime());
        }
    }

    private void OnDisable()
    {
        if (monitorRoutine != null)
        {
            StopCoroutine(monitorRoutine);
            monitorRoutine = null;
        }
    }

    private IEnumerator MonitorRuntime()
    {
        // Calculate total initial delay (add extra time for WebGL)
        float totalDelay = initialDelay;
#if UNITY_WEBGL && !UNITY_EDITOR
        totalDelay += webGLExtraDelay;
        if (enableVerboseLogging)
        {
            Debug.Log($"DialogueRuntimeWatcher: WebGL detected, using extended initial delay of {totalDelay}s");
        }
#endif
        
        if (totalDelay > 0f)
        {
            yield return new WaitForSeconds(totalDelay);
        }

        var wait = new WaitForSeconds(Mathf.Max(0.05f, checkInterval));

        while (true)
        {
            RefreshRuntimeReference();
            UpdateMissingTimer();
            yield return wait;
        }
    }

    private void RefreshRuntimeReference()
    {
        DialogueRunner foundRunner = CurrentRunner;
        if (foundRunner == null || !foundRunner.isActiveAndEnabled)
        {
            foundRunner = FindAnyObjectByType<DialogueRunner>();
        }

        if (foundRunner != CurrentRunner)
        {
            AssignRunner(foundRunner);
            return;
        }

        // Runner is the same; verify storage did not change.
        if (CurrentRunner != null)
        {
            var storage = CurrentRunner.VariableStorage;
            if (storage != CurrentVariableStorage)
            {
                CurrentVariableStorage = storage;
                if (storage != null)
                {
                    EmitRuntimeAvailable();
                }
            }
        }
    }

    private void AssignRunner(DialogueRunner newRunner)
    {
        bool hadRuntime = HasRuntime;

        CurrentRunner = newRunner;
        CurrentVariableStorage = newRunner != null ? newRunner.VariableStorage : null;

        if (HasRuntime)
        {
            if (enableVerboseLogging)
            {
                Debug.Log($"DialogueRuntimeWatcher: Runtime ready (runner '{CurrentRunner.name}').", CurrentRunner);
            }
            EmitRuntimeAvailable();
            ResetMissingTimer();
        }
        else
        {
            if (hadRuntime)
            {
                if (enableVerboseLogging)
                {
                    Debug.Log("DialogueRuntimeWatcher: DialogueRunner lost. Waiting for it to reappear.", this);
                }
                RuntimeLost?.Invoke();
            }
        }
    }

    private void EmitRuntimeAvailable()
    {
        if (!HasRuntime)
        {
            return;
        }

        try
        {
            RuntimeAvailable?.Invoke(CurrentRunner, CurrentVariableStorage);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
        }
    }

    private void UpdateMissingTimer()
    {
        if (HasRuntime)
        {
            ResetMissingTimer();
            return;
        }

        missingTimer += Mathf.Max(0.01f, checkInterval);
        if (!warningRaised && warnAfterSeconds > 0f && missingTimer >= warnAfterSeconds)
        {
            warningRaised = true;
            Debug.LogWarning($"DialogueRuntimeWatcher: DialogueRunner still missing after {missingTimer:0.0}s. UI elements will continue waiting.", this);
        }
    }

    private void ResetMissingTimer()
    {
        missingTimer = 0f;
        warningRaised = false;
    }

    /// <summary>
    /// Register callbacks for runtime availability and loss.
    /// If the runtime is already available, the callback is invoked immediately when requested.
    /// </summary>
    public void Register(Action<DialogueRunner, VariableStorageBehaviour> onAvailable, Action onLost = null, bool invokeImmediatelyIfReady = true)
    {
        if (onAvailable != null)
        {
            RuntimeAvailable += onAvailable;
            if (invokeImmediatelyIfReady && HasRuntime)
            {
                onAvailable.Invoke(CurrentRunner, CurrentVariableStorage);
            }
        }

        if (onLost != null)
        {
            RuntimeLost += onLost;
        }
    }

    /// <summary>
    /// Remove previously registered callbacks.
    /// </summary>
    public void Unregister(Action<DialogueRunner, VariableStorageBehaviour> onAvailable, Action onLost = null)
    {
        if (onAvailable != null)
        {
            RuntimeAvailable -= onAvailable;
        }

        if (onLost != null)
        {
            RuntimeLost -= onLost;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
}

