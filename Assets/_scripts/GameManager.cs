using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Simple, persistent GameManager:
/// - DontDestroyOnLoad
/// - On Start: loads Run0 additively
/// - Positions the camera/player at StartPosition
/// - Enables music + AutoForward
/// </summary>
[DisallowMultipleComponent]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Lifecycle")]
    [SerializeField] private bool dontDestroyOnLoad = true;

    [Header("Initial Run")]
    [Tooltip("Optional: explicit list of run scene names. If empty, GameManager will use the pattern 'Run0', 'Run1', 'Run2', ...")]
    [SerializeField] private string[] runSceneNames;
    [Tooltip("Scene name for the first run to load additively (must be in Build Settings).")]
    [SerializeField] private string run0SceneName = "Run0";
    [Tooltip("If true, preloads Run0 additively on Start (positions camera, but does NOT start the run: no music + no auto-forward).")]
    [SerializeField] private bool loadRun0OnStart = true;
    [SerializeField] private bool logStartup = false;
    [Tooltip("If true, unloads the previous run scene BEFORE loading the next one (lower peak memory, but you may see a brief empty frame unless you have a loading UI).")]
    [SerializeField] private bool unloadPreviousBeforeLoad = false;

    [Header("Player / Camera")]
    [Tooltip("Optional: explicit player/camera rig to reposition. If null, GameManager will use Camera.main or AutoForwardCameraController.")]
    [SerializeField] private Transform playerRig;
    [Tooltip("Optional: AutoForwardCameraController reference. If null, GameManager will try to find it.")]
    [SerializeField] private AutoForwardCameraController autoForward;

    [Header("Per-Run Tuning (optional)")]
    [Tooltip("Optional: assign one RunTuning asset per run index (Run0 at index 0, Run1 at index 1, ...).")]
    [SerializeField] private RunTuning[] runTuningsByIndex;

    [Header("Camera FOV")]
    [Tooltip("When switching runs, force Camera.main FOV back to this value (e.g. 105) so end-sequence FOV doesn't carry over.")]
    [SerializeField] private float defaultFovOnRunLoad = 105f;

    [Header("Music")]
    [Tooltip("Optional: set active when the run starts (e.g., your Musicplayer GameObject).")]
    [SerializeField] private GameObject musicManagerRoot;
    [Tooltip("If true, forces musicManagerRoot inactive on Awake so music doesn't start at Play.")]
    [SerializeField] private bool disableMusicOnAwake = true;

    [Header("Loading Overlay (optional)")]
    [Tooltip("Optional UI overlay to hide scene transitions. Recommended: a full-screen black Image with a CanvasGroup.")]
    [SerializeField] private CanvasGroup loadingOverlay;
    [Tooltip("Seconds to fade the overlay OUT to invisible after the run finished loading/positioning. 0 = instant.")]
    [SerializeField] [Min(0f)] private float loadingOverlayFadeOutSeconds = 0.15f;

    [Header("Events")]
    [Tooltip("Fires after Run0 is loaded, player positioned, music and auto-forward enabled.")]
    [SerializeField] private UnityEvent onRunStarted;
    [Tooltip("Optional: fired when the maze end trigger is crossed.")]
    [SerializeField] private UnityEvent onEndSequenceStarted;

    private Coroutine _runRoutine;
    private int _currentRunIndex;
    private string _loadedRunSceneName;
    private bool _runStarted;
    private Scene _bootstrapScene;
    private Coroutine _overlayRoutine;

    public int CurrentRunIndex => _currentRunIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // Capture the bootstrap (startup) scene BEFORE moving this object into DontDestroyOnLoad.
        _bootstrapScene = SceneManager.GetActiveScene();

        Instance = this;
        if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);

        if (disableMusicOnAwake && musicManagerRoot != null)
        {
            musicManagerRoot.SetActive(false);
        }
    }

    private void Start()
    {
        if (!loadRun0OnStart) return;
        // Preload + position only (no music, no auto-forward).
        StartRunRoutine(runIndex: 0, startRun: false);
    }

    public void StartEndSequence()
    {
        onEndSequenceStarted?.Invoke();

        // Run progression: only unlock the next run on "good finish".
        // Rule: good finish when the music IS playing at the end trigger moment.
        if (LevelManager.Instance != null)
        {
            bool musicPlaying = false;
            BackgroundMusicPlayer.TryIsMusicPlaying(out musicPlaying);
            bool goodFinish = musicPlaying;
            LevelManager.Instance.ReportRunFinished(_currentRunIndex, goodFinish);
        }
    }

    public void StartRun0()
    {
        StartRunRoutine(runIndex: 0, startRun: true);
    }

    /// <summary>
    /// UI-friendly alias: call this from your Start button.
    /// </summary>
    public void StartRun()
    {
        // 1st press: start current run (usually Run0 if preloaded).
        if (!_runStarted)
        {
            StartRunRoutine(_currentRunIndex, startRun: true);
            return;
        }

        // Next presses: advance only if the next run is unlocked; otherwise repeat current.
        int nextIndex = _currentRunIndex + 1;
        string nextSceneName = GetRunSceneName(nextIndex);
        bool nextSceneLoadable = !string.IsNullOrWhiteSpace(nextSceneName) && Application.CanStreamedLevelBeLoaded(nextSceneName);

        // If we don't have a LevelManager (or the next scene doesn't exist in Build Settings),
        // be safe and just replay the current run.
        if (LevelManager.Instance == null || !nextSceneLoadable || !LevelManager.Instance.IsUnlocked(nextIndex))
        {
            // Repeat the same run with a hard reload to reset scene-local state
            // (end trigger, eye dilation, one-shot events, etc).
            StartRunRoutine(_currentRunIndex, startRun: true, forceReloadIfAlreadyLoaded: true);
            return;
        }

        StartRunRoutine(nextIndex, startRun: true);
    }

    /// <summary>
    /// Starts a specific run index (for UI buttons / level select).
    /// </summary>
    public void StartRunIndex(int runIndex)
    {
        bool forceReload = _runStarted && runIndex == _currentRunIndex;
        StartRunRoutine(runIndex, startRun: true, forceReloadIfAlreadyLoaded: forceReload);
    }

    private void StartRunRoutine(int runIndex, bool startRun)
    {
        StartRunRoutine(runIndex, startRun, forceReloadIfAlreadyLoaded: false);
    }

    private void StartRunRoutine(int runIndex, bool startRun, bool forceReloadIfAlreadyLoaded)
    {
        // If an overlay is assigned, immediately cover the screen in black at the start of a run transition.
        ShowLoadingOverlayBlackImmediate();

        // Reset FOV immediately (before any scene load/unload) so we don't carry the end-sequence FOV into transitions.
        TryResetCameraFov();

        if (_runRoutine != null) StopCoroutine(_runRoutine);
        _runRoutine = StartCoroutine(LoadRunRoutine(runIndex, startRun, forceReloadIfAlreadyLoaded));
    }

    /// <summary>
    /// Order:
    /// load scene -> find StartPosition -> move camera -> (optional) enable music -> (optional) enable auto-forward.
    /// </summary>
    private IEnumerator LoadRunRoutine(int runIndex, bool startRun, bool forceReloadIfAlreadyLoaded)
    {
        runIndex = Mathf.Max(0, runIndex);

        // Reset FOV again at the start of the routine (covers cases where StartRunRoutine wasn't used).
        TryResetCameraFov();

        // Disable movement while we load & teleport.
        ResolveAutoForwardReference();
        if (autoForward != null) autoForward.enabled = false;

        string runSceneName = GetRunSceneName(runIndex);
        if (string.IsNullOrWhiteSpace(runSceneName))
        {
            Debug.LogWarning($"[GameManager] Run scene name for index {runIndex} is empty/out of range.", this);
            yield break;
        }

        // If we're repeating the same run, the run scene may already be loaded (additive flow),
        // which means triggers/events keep their runtime state. Force an unload+reload when requested.
        if (forceReloadIfAlreadyLoaded)
        {
            Scene already = SceneManager.GetSceneByName(runSceneName);
            if (already.IsValid() && already.isLoaded)
            {
                TrySetActiveSceneForUnload(excludeSceneName: runSceneName);
                if (logStartup) Debug.Log($"[GameManager] Force reloading run scene '{runSceneName}' (unload+reload) to reset state.", this);
                AsyncOperation unloadSame = SceneManager.UnloadSceneAsync(already);
                if (unloadSame != null)
                {
                    while (!unloadSame.isDone) yield return null;
                }
            }
        }

        // Option: unload previous run BEFORE loading the next one (lower peak memory).
        if (unloadPreviousBeforeLoad &&
            !string.IsNullOrWhiteSpace(_loadedRunSceneName) &&
            _loadedRunSceneName != runSceneName)
        {
            Scene prev = SceneManager.GetSceneByName(_loadedRunSceneName);
            if (prev.IsValid() && prev.isLoaded)
            {
                TrySetActiveSceneForUnload(excludeSceneName: _loadedRunSceneName);

                if (logStartup) Debug.Log($"[GameManager] Unloading previous run scene '{_loadedRunSceneName}' (before load)", this);
                AsyncOperation unload = SceneManager.UnloadSceneAsync(prev);
                if (unload != null)
                {
                    while (!unload.isDone) yield return null;
                }
            }
        }

        Scene existing = SceneManager.GetSceneByName(runSceneName);
        if (!existing.isLoaded)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(runSceneName, LoadSceneMode.Additive);
            if (load != null)
            {
                while (!load.isDone) yield return null;
            }
        }

        Scene runScene = SceneManager.GetSceneByName(runSceneName);
        if (logStartup) Debug.Log($"[GameManager] Run scene '{runSceneName}' valid={runScene.IsValid()} loaded={runScene.isLoaded}", this);

        if (runScene.IsValid() && runScene.isLoaded)
        {
            SceneManager.SetActiveScene(runScene);
        }
        else
        {
            Debug.LogWarning($"[GameManager] Couldn't get loaded scene by name '{runSceneName}'. Check Build Settings and the exact scene name.", this);
        }

        // Bind the look-at target ("Eye") from the newly loaded run scene to the camera script (since scenes change).
        TryBindEyeTargetForCamera(runScene);
        TryResetCameraFov();

        // FIRST: Position the player/camera as soon as the scene is loaded (before any gameplay updates).
        bool positioned = TryPositionPlayerAtStart(runScene);
        if (logStartup) Debug.Log($"[GameManager] Position at StartPosition (immediate): {positioned}", this);
        TryResetCameraLookStateBaseline();
        ResetMazeEndTriggers(runScene);
        ApplyRunTuning(runIndex);

        // Safety: if something creates/moves StartPosition in Start(), try again next frame.
        yield return null;
        if (!positioned)
        {
            positioned = TryPositionPlayerAtStart(runScene);
            if (logStartup) Debug.Log($"[GameManager] Position at StartPosition (next frame retry): {positioned}", this);
        }
        TryResetCameraLookStateBaseline();
        ResetMazeEndTriggers(runScene);
        ApplyRunTuning(runIndex);

        if (startRun)
        {
            // Enable music (only when starting the run).
            if (musicManagerRoot != null) musicManagerRoot.SetActive(true);
            ApplyRunMusicTuningIfAny(runIndex);
            BackgroundMusicPlayer.TryPlayMusic(restart: true);

            // Enable auto-forward (only when starting the run).
            ResolveAutoForwardReference();
            if (autoForward != null) autoForward.enabled = true;

            onRunStarted?.Invoke();
        }

        // Default behavior: unload AFTER the new run is ready (safer transitions).
        if (!unloadPreviousBeforeLoad &&
            !string.IsNullOrWhiteSpace(_loadedRunSceneName) &&
            _loadedRunSceneName != runSceneName)
        {
            Scene prev = SceneManager.GetSceneByName(_loadedRunSceneName);
            if (prev.IsValid() && prev.isLoaded)
            {
                if (logStartup) Debug.Log($"[GameManager] Unloading previous run scene '{_loadedRunSceneName}' (after load)", this);
                AsyncOperation unload = SceneManager.UnloadSceneAsync(prev);
                if (unload != null)
                {
                    while (!unload.isDone) yield return null;
                }
            }
        }

        _loadedRunSceneName = runSceneName;
        _currentRunIndex = runIndex;
        if (startRun) _runStarted = true;

        _runRoutine = null;

        // Fade overlay out at the very end of the transition (after positioning + enabling).
        HideLoadingOverlayFadeOut();
    }

    private void ApplyRunTuning(int runIndex)
    {
        RunTuning tuning = GetRunTuning(runIndex);
        if (tuning == null) return;

        ResolveAutoForwardReference();
        if (autoForward == null) return;

        if (tuning.overrideMoveSpeed) autoForward.MoveSpeed = tuning.moveSpeed;
        if (tuning.overrideStopDistance) autoForward.StopDistance = tuning.stopDistance;
        if (tuning.overrideTurnDuration) autoForward.TurnDuration = tuning.turnDuration;
    }

    private void ApplyRunMusicTuningIfAny(int runIndex)
    {
        RunTuning tuning = GetRunTuning(runIndex);
        if (tuning == null) return;

        if (tuning.overrideMusicClip && tuning.musicClip != null)
        {
            BackgroundMusicPlayer.TrySetMusicClip(tuning.musicClip);
        }

        if (tuning.overrideMusicStartAtSeconds)
        {
            BackgroundMusicPlayer.TrySetStartAtSeconds(tuning.musicStartAtSeconds);
        }
    }

    private RunTuning GetRunTuning(int runIndex)
    {
        if (runTuningsByIndex == null) return null;
        if (runIndex < 0 || runIndex >= runTuningsByIndex.Length) return null;
        return runTuningsByIndex[runIndex];
    }

    private static void ResetMazeEndTriggers(Scene runScene)
    {
        if (!runScene.IsValid() || !runScene.isLoaded) return;

        GameObject[] roots = runScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root == null) continue;

            MazeEndTriggerLookAt[] triggers = root.GetComponentsInChildren<MazeEndTriggerLookAt>(true);
            for (int t = 0; t < triggers.Length; t++)
            {
                MazeEndTriggerLookAt trig = triggers[t];
                if (trig == null) continue;
                trig.ResetTriggerState();
            }
        }
    }

    private void TryBindEyeTargetForCamera(Scene runScene)
    {
        if (!runScene.IsValid() || !runScene.isLoaded) return;

        Transform eye = FindTransformByNameInScene(runScene, "Eye");
        if (eye == null)
        {
            if (logStartup) Debug.LogWarning($"[GameManager] No Transform named 'Eye' found in scene '{runScene.name}'.", this);
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            if (logStartup) Debug.LogWarning("[GameManager] Camera.main is null; can't bind Eye target.", this);
            return;
        }

        CameraLookAtOnKey look = cam.GetComponent<CameraLookAtOnKey>();
        if (look == null)
        {
            if (logStartup) Debug.LogWarning("[GameManager] CameraLookAtOnKey not found on Camera.main; can't bind Eye target.", this);
            return;
        }

        look.SetTarget(eye);
        if (logStartup) Debug.Log($"[GameManager] Bound CameraLookAtOnKey target to Eye='{eye.name}' in scene '{runScene.name}'.", this);
    }

    private void TryResetCameraFov()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        CameraLookAtOnKey look = cam.GetComponent<CameraLookAtOnKey>();
        if (look != null)
        {
            look.ForceResetFov(defaultFovOnRunLoad);
        }
        else
        {
            cam.fieldOfView = defaultFovOnRunLoad;
        }
    }

    private void TryResetCameraLookStateBaseline()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        CameraLookAtOnKey look = cam.GetComponent<CameraLookAtOnKey>();
        if (look == null) return;

        // Critical for multi-run: if the previous run ended in "looking at target" mode,
        // StartLookAtTarget() will no-op on subsequent runs. Reset to a clean baseline.
        look.ForceResetLookStateToCurrentPose();
    }

    private string GetRunSceneName(int runIndex)
    {
        if (runSceneNames != null && runSceneNames.Length > 0)
        {
            if (runIndex >= 0 && runIndex < runSceneNames.Length) return runSceneNames[runIndex];
            return null; // out of range
        }

        if (runIndex == 0) return run0SceneName;
        return $"Run{runIndex}";
    }

    private void ResolveAutoForwardReference()
    {
        if (autoForward != null) return;

        if (playerRig != null)
        {
            autoForward = playerRig.GetComponent<AutoForwardCameraController>();
            if (autoForward != null) return;
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            autoForward = cam.GetComponent<AutoForwardCameraController>();
            if (autoForward != null) return;
        }

        autoForward = FindObjectOfType<AutoForwardCameraController>();
    }

    private bool TryPositionPlayerAtStart(Scene runScene)
    {
        Transform startTransform = FindStartPositionTransformInScene(runScene);
        if (startTransform == null)
        {
            Debug.LogWarning(
                $"[GameManager] No StartPosition found in scene '{runScene.name}'. " +
                $"Add the StartPosition component OR create a GameObject named 'StartPosition' in that scene.",
                this
            );
            return false;
        }

        Transform target = playerRig;
        if (target == null)
        {
            Camera cam = Camera.main;
            if (cam != null) target = cam.transform;
        }
        if (target == null)
        {
            ResolveAutoForwardReference();
            if (autoForward != null) target = autoForward.transform;
        }
        if (target == null)
        {
            Debug.LogWarning("[GameManager] No playerRig, no Camera.main, and no AutoForwardCameraController found to reposition.", this);
            return false;
        }

        CharacterController cc = target.GetComponent<CharacterController>();
        if (cc == null && autoForward != null) cc = autoForward.GetComponent<CharacterController>();

        bool wasEnabled = false;
        if (cc != null)
        {
            wasEnabled = cc.enabled;
            cc.enabled = false;
        }

        if (logStartup)
        {
            Debug.Log(
                $"[GameManager] Teleporting '{target.name}' to StartPosition '{startTransform.name}' " +
                $"pos={startTransform.position} rot={startTransform.rotation.eulerAngles}",
                this
            );
        }

        target.SetPositionAndRotation(startTransform.position, startTransform.rotation);

        if (cc != null) cc.enabled = wasEnabled;
        return true;
    }

    private static Transform FindStartPositionTransformInScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;

        // Prefer by name (your requested workflow).
        Transform byName = FindTransformByNameInScene(scene, "StartPosition");
        if (byName != null) return byName;

        // No marker found.
        return null;
    }

    private static Transform FindTransformByNameInScene(Scene scene, string exactName)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        if (string.IsNullOrWhiteSpace(exactName)) return null;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root == null) continue;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int c = 0; c < children.Length; c++)
            {
                Transform t = children[c];
                if (t == null) continue;
                if (t.name == exactName) return t;
            }
        }

        return null;
    }

    private void ShowLoadingOverlayBlackImmediate()
    {
        if (loadingOverlay == null) return;

        if (_overlayRoutine != null)
        {
            StopCoroutine(_overlayRoutine);
            _overlayRoutine = null;
        }

        loadingOverlay.gameObject.SetActive(true);
        loadingOverlay.alpha = 1f;
        loadingOverlay.blocksRaycasts = true;
        loadingOverlay.interactable = true;
    }

    private void HideLoadingOverlayFadeOut()
    {
        if (loadingOverlay == null) return;

        if (_overlayRoutine != null)
        {
            StopCoroutine(_overlayRoutine);
            _overlayRoutine = null;
        }

        if (loadingOverlayFadeOutSeconds <= 0f)
        {
            loadingOverlay.alpha = 0f;
            loadingOverlay.blocksRaycasts = false;
            loadingOverlay.interactable = false;
            loadingOverlay.gameObject.SetActive(false);
            return;
        }

        _overlayRoutine = StartCoroutine(FadeOverlayOutRoutine(loadingOverlayFadeOutSeconds));
    }

    private IEnumerator FadeOverlayOutRoutine(float seconds)
    {
        if (loadingOverlay == null) yield break;

        float startAlpha = loadingOverlay.alpha;
        float t = 0f;
        float d = Mathf.Max(0.0001f, seconds);

        if (loadingOverlay != null)
        {
            // Block input while we're fading out.
            loadingOverlay.blocksRaycasts = true;
            loadingOverlay.interactable = true;
        }

        while (t < d && loadingOverlay != null)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / d);
            loadingOverlay.alpha = Mathf.Lerp(startAlpha, 0f, a);
            yield return null;
        }

        if (loadingOverlay != null)
        {
            loadingOverlay.alpha = 0f;
            loadingOverlay.blocksRaycasts = false;
            loadingOverlay.interactable = false;
            loadingOverlay.gameObject.SetActive(false);
        }

        _overlayRoutine = null;
    }

    private void TrySetActiveSceneForUnload(string excludeSceneName)
    {
        // Unity cannot set the internal DontDestroyOnLoad scene active.
        const string DontDestroyOnLoadSceneName = "DontDestroyOnLoad";

        Scene active = SceneManager.GetActiveScene();
        if (active.IsValid() && active.isLoaded && active.name != DontDestroyOnLoadSceneName && active.name != excludeSceneName)
        {
            SceneManager.SetActiveScene(active);
            return;
        }

        // Prefer the captured bootstrap scene if it's valid.
        if (_bootstrapScene.IsValid() && _bootstrapScene.isLoaded &&
            _bootstrapScene.name != DontDestroyOnLoadSceneName &&
            _bootstrapScene.name != excludeSceneName)
        {
            SceneManager.SetActiveScene(_bootstrapScene);
            return;
        }

        // Otherwise, pick any other loaded scene that's not excluded and not DDoL.
        int count = SceneManager.sceneCount;
        for (int i = 0; i < count; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (!s.IsValid() || !s.isLoaded) continue;
            if (s.name == DontDestroyOnLoadSceneName) continue;
            if (s.name == excludeSceneName) continue;
            SceneManager.SetActiveScene(s);
            return;
        }

        // If we get here, there is no safe scene to set active (rare). We'll just avoid SetActiveScene.
        if (logStartup) Debug.LogWarning("[GameManager] No safe loaded scene found to SetActiveScene before unload.", this);
    }
}

