using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using Unity.Profiling;
using System.Text;
using System.Linq;

/// <summary>
/// Passive performance monitor. 
/// Automatically detects newly loaded scenes to calculate geometry and tracks hardware stats.
/// To disable profiling completely, simply Disable this GameObject.
/// </summary>
public class PerformanceOverlayController : MonoBehaviour
{
    [Header("Input Settings")]
    [Tooltip("Key to reset the FPS and Memory statistics.")]
    public Key resetKey = Key.O;

    [Header("UI References (Optional - Auto-creates if null)")]
    public Canvas mainCanvas;
    public TextMeshProUGUI statsText;

    [Header("Settings")]
    [Tooltip("How often to update the UI (in seconds).")]
    public float uiRefreshRate = 0.5f;
    [Tooltip("Number of frames to keep for 1% low calculations.")]
    public int sampleSize = 1000;

    [Header("Geometry Settings")]
    [Tooltip("If true, only counts objects with the specified tag. If false, counts ALL geometry in the additive scene (catches untagged shards).")]
    public bool useTagFilter = false;
    [Tooltip("Tag to search for geometry calculations (e.g., Destructible walls). Only used if 'Use Tag Filter' is true.")]
    public string targetTag = "Destructible";
    [Tooltip("How often to recalculate geometry (in seconds). Set to 0 to disable auto-refresh.")]
    public float geometryRefreshRate = 5.0f;

    // --- State ---
    private string _currentSceneName = "Waiting...";
    private float _uiTimer;
    private float _geometryTimer;
    private StringBuilder _sb = new StringBuilder(500);

    // --- Profiler Recorders ---
    private ProfilerRecorder _totalReservedMemoryRecorder;
    private ProfilerRecorder _gcReservedMemoryRecorder;
    private ProfilerRecorder _textureMemoryRecorder;
    private ProfilerRecorder _mainThreadTimeRecorder;
    private ProfilerRecorder _gpuFrameTimeRecorder;

    // --- Metrics Storage ---
    private List<float> _frameTimes = new List<float>();
    private long _currentSceneTriangles = 0;
    private long _currentSceneVertices = 0;

    // RAM/VRAM Stats (tracking min/max/avg over the session)
    private long _ramMin = long.MaxValue, _ramMax = 0;
    private double _ramAvgSum = 0; private int _ramSampleCount = 0;

    private long _vramMin = long.MaxValue, _vramMax = 0;
    private double _vramAvgSum = 0; private int _vramSampleCount = 0;

    void OnEnable()
    {
        // Initialize Profiler Recorders (Low overhead, Unity 2022/6+ standard)
        _totalReservedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Reserved Memory");
        _gcReservedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
        _textureMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Texture Memory");
        _mainThreadTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
        _gpuFrameTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Gpu Frame Time", 15);

        SceneManager.sceneLoaded += OnSceneLoaded;

        // Ensure UI is visible when this component is enabled
        if (mainCanvas != null) mainCanvas.gameObject.SetActive(true);

        // Immediate update in case we were enabled mid-game
        if (SceneManager.GetActiveScene().isLoaded)
        {
            _currentSceneName = SceneManager.GetActiveScene().name;
            CalculateDestructibleGeometry();
        }
    }

    void OnDisable()
    {
        // Clean up recorders to stop overhead
        _totalReservedMemoryRecorder.Dispose();
        _gcReservedMemoryRecorder.Dispose();
        _textureMemoryRecorder.Dispose();
        _mainThreadTimeRecorder.Dispose();
        _gpuFrameTimeRecorder.Dispose();

        SceneManager.sceneLoaded -= OnSceneLoaded;

        // Hide UI when this component is disabled
        if (mainCanvas != null) mainCanvas.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        // If the script is destroyed, destroy the canvas we created
        if (mainCanvas != null) Destroy(mainCanvas.gameObject);
    }

    void Start()
    {
        EnsureUI();
    }

    void Update()
    {
        // 0. Handle Input (Reset Stats)
        if (Keyboard.current != null && Keyboard.current[resetKey].wasPressedThisFrame)
        {
            ResetStats();
        }

        // 1. Collect Frame Metrics
        float dt = Time.unscaledDeltaTime;
        float dtMs = dt * 1000.0f;

        if (_frameTimes.Count >= sampleSize) _frameTimes.RemoveAt(0);
        _frameTimes.Add(dtMs);

        // 2. Update Memory Stats
        UpdateMemoryStats();

        // 3. Geometry Refresh Logic
        if (geometryRefreshRate > 0)
        {
            _geometryTimer += Time.unscaledDeltaTime;
            if (_geometryTimer >= geometryRefreshRate)
            {
                CalculateDestructibleGeometry();
                _geometryTimer = 0;
            }
        }

        // 4. Refresh UI
        _uiTimer += Time.unscaledDeltaTime;
        if (_uiTimer >= uiRefreshRate)
        {
            UpdateUI(dtMs); // Pass current frame ms explicitly for real-time display
            _uiTimer = 0;
        }
    }

    // --- State Management ---

    public void ResetStats()
    {
        _frameTimes.Clear();

        _ramMin = long.MaxValue; _ramMax = 0;
        _ramAvgSum = 0; _ramSampleCount = 0;

        _vramMin = long.MaxValue; _vramMax = 0;
        _vramAvgSum = 0; _vramSampleCount = 0;

        // Trigger an immediate UI refresh to show cleared stats (optional)
        _uiTimer = uiRefreshRate;
    }

    // --- Event Handling ---

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // We assume the MasterScene is persistent and any NEW scene loading is the "content"
        _currentSceneName = scene.name;

        // Immediate calculation on load
        CalculateDestructibleGeometry();
        _geometryTimer = 0;
    }

    // --- Geometry Calculation ---

    void CalculateDestructibleGeometry()
    {
        _currentSceneTriangles = 0;
        _currentSceneVertices = 0;

        // METHOD 1: Filter by Tag (Original Method)
        if (useTagFilter)
        {
            if (string.IsNullOrEmpty(targetTag)) return;

            GameObject[] targets;
            try
            {
                targets = GameObject.FindGameObjectsWithTag(targetTag);
            }
            catch (UnityException)
            {
                return;
            }

            foreach (var obj in targets)
            {
                // Strict check: only count objects in the Additive Scene
                if (obj.scene.name != _currentSceneName && _currentSceneName != "Waiting...") continue;

                CountObjectGeometry(obj);
            }
        }
        // METHOD 2: Scan Additive Scene Roots (Catches untagged shards)
        else
        {
            if (_currentSceneName == "Waiting...") return;

            Scene scene = SceneManager.GetSceneByName(_currentSceneName);
            if (!scene.IsValid() || !scene.isLoaded) return;

            GameObject[] roots = scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                CountObjectGeometry(root);
            }
        }
    }

    void CountObjectGeometry(GameObject obj)
    {
        // CHANGED: Passed 'false' to GetComponentsInChildren to only include Active objects.
        // This ensures stats reflect the current scene state (e.g. after a wall fractures and shards enable).
        MeshFilter[] meshFilters = obj.GetComponentsInChildren<MeshFilter>(false);
        foreach (var mf in meshFilters)
        {
            if (mf.sharedMesh != null)
            {
                // Using GetIndexCount allows access on non-readable meshes
                for (int i = 0; i < mf.sharedMesh.subMeshCount; i++)
                {
                    _currentSceneTriangles += (mf.sharedMesh.GetIndexCount(i) / 3);
                }
                _currentSceneVertices += mf.sharedMesh.vertexCount;
            }
        }

        // CHANGED: Passed 'false' here as well for consistency.
        SkinnedMeshRenderer[] skinnedMeshes = obj.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        foreach (var smr in skinnedMeshes)
        {
            if (smr.sharedMesh != null)
            {
                for (int i = 0; i < smr.sharedMesh.subMeshCount; i++)
                {
                    _currentSceneTriangles += (smr.sharedMesh.GetIndexCount(i) / 3);
                }
                _currentSceneVertices += smr.sharedMesh.vertexCount;
            }
        }
    }

    // --- Metrics Logic ---

    void UpdateMemoryStats()
    {
        if (!_totalReservedMemoryRecorder.Valid) return;

        long currentRam = _totalReservedMemoryRecorder.LastValue / (1024 * 1024);
        long currentVram = _textureMemoryRecorder.LastValue / (1024 * 1024);

        // RAM Stats
        if (currentRam < _ramMin) _ramMin = currentRam;
        if (currentRam > _ramMax) _ramMax = currentRam;
        _ramAvgSum += currentRam;
        _ramSampleCount++;

        // VRAM Stats
        if (currentVram < _vramMin) _vramMin = currentVram;
        if (currentVram > _vramMax) _vramMax = currentVram;
        _vramAvgSum += currentVram;
        _vramSampleCount++;
    }

    void UpdateUI(float currentDtMs)
    {
        if (_frameTimes.Count == 0) return;

        // --- FPS Calculations ---
        float currentFps = 1000.0f / (currentDtMs > 0 ? currentDtMs : 0.001f);
        float avgFrameTime = _frameTimes.Average();

        // Sort for Min/Max/1% Low
        var sortedTimes = _frameTimes.OrderBy(t => t).ToList();

        // 1% Low FPS = Frame Time High (Slowest frames)
        int index1Percent = Mathf.FloorToInt(sortedTimes.Count * 0.99f);
        if (index1Percent >= sortedTimes.Count) index1Percent = sortedTimes.Count - 1;
        float frameTime1PercentLow = sortedTimes[index1Percent];

        // Frame Time Min (Fastest frame)
        float frameTimeMin = sortedTimes[0];

        // FPS Metrics
        float fpsMax = 1000.0f / (frameTimeMin > 0 ? frameTimeMin : 0.001f);
        float fpsAvg = 1000.0f / avgFrameTime;
        float fps1PercentLow = 1000.0f / frameTime1PercentLow;

        // --- Hardware Usage ---
        double cpuTimeMs = _mainThreadTimeRecorder.Valid ? _mainThreadTimeRecorder.LastValue * (1e-6f) : 0;
        double gpuTimeMs = _gpuFrameTimeRecorder.Valid ? _gpuFrameTimeRecorder.LastValue * (1e-6f) : 0;

        float targetMs = 16.66f; // 60 FPS standard for % calc
        float cpuLoad = (float)(cpuTimeMs / targetMs) * 100f;
        float gpuLoad = (float)(gpuTimeMs / targetMs) * 100f;

        long ramAvg = _ramSampleCount > 0 ? (long)(_ramAvgSum / _ramSampleCount) : 0;
        long vramAvg = _vramSampleCount > 0 ? (long)(_vramAvgSum / _vramSampleCount) : 0;

        // --- Text Generation ---
        _sb.Clear();
        _sb.Append($"<b><size=120%>{_currentSceneName}</size></b>\n\n");

        _sb.Append("<b>FPS:</b>\n");
        _sb.Append($"  Max: <color=green>{fpsMax:F0}</color> | Curr: <color=white>{currentFps:F0}</color> | Avg: <color=yellow>{fpsAvg:F0}</color> | 1% Low: <color=red>{fps1PercentLow:F0}</color>\n");

        _sb.Append("<b>Frame Time (ms):</b>\n");
        _sb.Append($"  Min: {frameTimeMin:F2} | Curr: {currentDtMs:F2} | Avg: {avgFrameTime:F2} | Max (1%): {frameTime1PercentLow:F2}\n");

        _sb.Append($"\n<b>Geometry ({(useTagFilter ? $"Tag: {targetTag}" : "Active Only")}):</b>\n");
        _sb.Append($"  Tris: {_currentSceneTriangles:N0}\n");
        _sb.Append($"  Verts: {_currentSceneVertices:N0}\n");

        _sb.Append("\n<b>Hardware Load (~60fps):</b>\n");
        _sb.Append($"  CPU: {cpuTimeMs:F2}ms ({cpuLoad:F0}%)\n");
        _sb.Append($"  GPU: {gpuTimeMs:F2}ms ({gpuLoad:F0}%)\n");

        _sb.Append("\n<b>Memory (MB):</b>\n");
        _sb.Append($"  RAM:  Min: {_ramMin} | Avg: {ramAvg} | Max: {_ramMax}\n");
        _sb.Append($"  VRAM: Min: {_vramMin} | Avg: {vramAvg} | Max: {_vramMax} (Tex)\n");

        if (statsText != null)
        {
            statsText.text = _sb.ToString();
        }
    }

    // --- Helpers ---

    private void EnsureUI()
    {
        if (mainCanvas == null)
        {
            GameObject canvasObj = new GameObject("PerformanceCanvas");
            mainCanvas = canvasObj.AddComponent<Canvas>();
            mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            mainCanvas.sortingOrder = 999; // Ensure it's on top
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            DontDestroyOnLoad(canvasObj); // Keep across scene loads if MasterScene isn't persistent
        }

        if (statsText == null)
        {
            // 1. Create Background Container (Parent) - Controls size and rendering order
            // The Background is the PARENT, so it draws FIRST (behind text).
            GameObject bgObj = new GameObject("StatsBackground");
            bgObj.transform.SetParent(mainCanvas.transform, false);

            Image bg = bgObj.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.5f); // Semi-transparent grey

            // 2. Add Layout Components to Auto-Size Background to Text
            // This forces the background to hug the text content + padding.
            VerticalLayoutGroup layout = bgObj.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            ContentSizeFitter csf = bgObj.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 3. Position Top-Left
            RectTransform bgRt = bgObj.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0, 1); // Top Left
            bgRt.anchorMax = new Vector2(0, 1); // Top Left
            bgRt.pivot = new Vector2(0, 1);     // Pivot Top Left
            bgRt.anchoredPosition = new Vector2(10, -10); // Slight offset from corner

            // 4. Create Text Object (Child of Background)
            GameObject textObj = new GameObject("StatsText");
            textObj.transform.SetParent(bgObj.transform, false);

            statsText = textObj.AddComponent<TextMeshProUGUI>();
            statsText.fontSize = 18;
            statsText.color = Color.white;
            statsText.alignment = TextAlignmentOptions.TopLeft;
        }
    }
}