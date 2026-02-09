using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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
    [Header("UI References (Optional - Auto-creates if null)")]
    public Canvas mainCanvas;
    public TextMeshProUGUI statsText;

    [Header("Settings")]
    [Tooltip("How often to update the UI (in seconds).")]
    public float uiRefreshRate = 0.5f;
    [Tooltip("Number of frames to keep for 1% low calculations.")]
    public int sampleSize = 1000;

    [Header("Geometry Settings")]
    [Tooltip("Tag to search for geometry calculations (e.g., Destructible walls).")]
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

        if (string.IsNullOrEmpty(targetTag)) return;

        GameObject[] targets;
        try
        {
            targets = GameObject.FindGameObjectsWithTag(targetTag);
        }
        catch (UnityException)
        {
            // Tag likely doesn't define in TagManager
            return;
        }

        foreach (var obj in targets)
        {
            // Optional: Strict check to ensure we only count objects in the Additive Scene
            // If you want to count ALL destructibles regardless of scene, remove this check.
            if (obj.scene.name != _currentSceneName && _currentSceneName != "Waiting...") continue;

            MeshFilter[] meshFilters = obj.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh != null)
                {
                    // FIXED: Using GetIndexCount instead of .triangles allows access 
                    // on meshes where "Read/Write Enabled" is false.
                    // Loop through submeshes (materials) to get total indices.
                    for (int i = 0; i < mf.sharedMesh.subMeshCount; i++)
                    {
                        _currentSceneTriangles += (mf.sharedMesh.GetIndexCount(i) / 3);
                    }
                    _currentSceneVertices += mf.sharedMesh.vertexCount;
                }
            }

            SkinnedMeshRenderer[] skinnedMeshes = obj.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in skinnedMeshes)
            {
                if (smr.sharedMesh != null)
                {
                    // FIXED: Same fix for Skinned Meshes
                    for (int i = 0; i < smr.sharedMesh.subMeshCount; i++)
                    {
                        _currentSceneTriangles += (smr.sharedMesh.GetIndexCount(i) / 3);
                    }
                    _currentSceneVertices += smr.sharedMesh.vertexCount;
                }
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

        // 1% Low = The frame time that is slower than 99% of all other frames
        var sortedTimes = _frameTimes.OrderBy(t => t).ToList();
        int index1Percent = Mathf.FloorToInt(sortedTimes.Count * 0.99f);
        if (index1Percent >= sortedTimes.Count) index1Percent = sortedTimes.Count - 1;
        float frameTime1PercentLow = sortedTimes[index1Percent];

        float fpsMax = 1000.0f / (sortedTimes[0] > 0 ? sortedTimes[0] : 0.001f);
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
        _sb.Append($"  Curr: <color=white>{currentFps:F0}</color> | Avg: <color=yellow>{fpsAvg:F0}</color> | 1% Low: <color=red>{fps1PercentLow:F0}</color>\n");

        _sb.Append("<b>Frame Time (ms):</b>\n");
        _sb.Append($"  Curr: {currentDtMs:F2} | Avg: {avgFrameTime:F2} | Max: {frameTime1PercentLow:F2}\n");

        _sb.Append($"\n<b>Geometry (Tag: {targetTag}):</b>\n");
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
            GameObject textObj = new GameObject("StatsText");
            textObj.transform.SetParent(mainCanvas.transform, false);
            statsText = textObj.AddComponent<TextMeshProUGUI>();
            statsText.fontSize = 18;
            statsText.color = Color.white;
            statsText.alignment = TextAlignmentOptions.TopLeft;

            RectTransform rt = statsText.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0, 1);
            rt.offsetMin = new Vector2(20, 20);
            rt.offsetMax = new Vector2(-20, -20);

            // Readable Background
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(textObj.transform, false);
            bgObj.transform.SetAsFirstSibling();
            Image bg = bgObj.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.6f);
            RectTransform bgRt = bg.rectTransform;
            bgRt.anchorMin = new Vector2(0, 0);
            bgRt.anchorMax = new Vector2(0.3f, 0.6f); // Top left corner area
            bgRt.pivot = new Vector2(0, 1);
            bgRt.anchoredPosition = Vector2.zero;
        }
    }
}