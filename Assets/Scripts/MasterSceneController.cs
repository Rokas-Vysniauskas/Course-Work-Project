using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

/// <summary>
/// Manages additive scene loading for Unity 6.
/// Attach this to a GameObject in your persistent Master Scene (Build Index 0).
/// </summary>
public class MasterSceneController : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("The Build Index of this Master Scene (usually 0).")]
    [SerializeField] private int masterSceneIndex = 0;

    [Tooltip("The Build Index of the first content scene to load on Start.")]
    [SerializeField] private int firstContentSceneIndex = 1;

    [Header("Controls (New Input System)")]
    [SerializeField] private Key nextSceneKey = Key.D;
    [SerializeField] private Key previousSceneKey = Key.A;
    [SerializeField] private Key sceneReloadKey = Key.R;

    // Internal state
    private int _currentContentSceneIndex = -1;
    private bool _isTransitioning = false;

    private void Start()
    {
        // 1. Validation: Ensure we have scenes to load
        int totalScenes = SceneManager.sceneCountInBuildSettings;
        if (totalScenes <= 1)
        {
            Debug.LogError("MasterSceneController: Not enough scenes in Build Settings! Add your Master scene and at least one content scene.");
            return;
        }

        // 2. Load the initial content scene
        _currentContentSceneIndex = firstContentSceneIndex;

        // Check if it's already loaded (useful for play-mode editing) to avoid duplicates
        Scene currentScene = SceneManager.GetSceneByBuildIndex(_currentContentSceneIndex);
        if (!currentScene.isLoaded)
        {
            StartCoroutine(LoadSceneRoutine(_currentContentSceneIndex, LoadSceneMode.Additive));
        }
    }

    private void Update()
    {
        // Prevent spamming input while a scene is loading/unloading
        if (_isTransitioning) return;

        // Ensure Keyboard is present (New Input System safety)
        if (Keyboard.current == null) return;

        if (Keyboard.current[nextSceneKey].wasPressedThisFrame)
        {
            SwitchScene(1); // Move forward
        }
        else if (Keyboard.current[previousSceneKey].wasPressedThisFrame)
        {
            SwitchScene(-1); // Move backward
        }
        else if (Keyboard.current[sceneReloadKey].wasPressedThisFrame)
        {
            SwitchScene(0); // Reload current scene
        }
    }

    /// <summary>
    /// Calculates the next index and starts the transition.
    /// </summary>
    /// <param name="direction">1 for next, -1 for previous</param>
    private void SwitchScene(int direction)
    {
        int totalScenes = SceneManager.sceneCountInBuildSettings;

        // We assume Master Scene is index 0. Content scenes are 1 to (total-1).
        // If your setup is different, adjust this range logic.
        int contentSceneCount = totalScenes - 1;

        // Calculate relative index (0-based relative to content scenes)
        int currentRelativeIndex = _currentContentSceneIndex - 1;

        // Calculate next relative index with wrap-around (Modulo arithmetic)
        int nextRelativeIndex = (currentRelativeIndex + direction) % contentSceneCount;

        // Handle negative modulo result for backward wrapping (C# modulo can return negative)
        if (nextRelativeIndex < 0) nextRelativeIndex += contentSceneCount;

        // Convert back to absolute Build Index
        int nextAbsoluteIndex = nextRelativeIndex + 1;

        // Start the swap
        StartCoroutine(SwapScenesRoutine(_currentContentSceneIndex, nextAbsoluteIndex));
    }

    /// <summary>
    /// Unloads the old scene and loads the new one sequentially.
    /// </summary>
    private IEnumerator SwapScenesRoutine(int sceneToUnload, int sceneToLoad)
    {
        _isTransitioning = true;
        Debug.Log($"Switching from Index {sceneToUnload} to {sceneToLoad}...");

        // 1. Unload the current content scene
        AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(sceneToUnload);

        // Wait until unload is finished
        while (!unloadOp.isDone)
        {
            yield return null;
        }

        // 2. Load the new content scene
        yield return StartCoroutine(LoadSceneRoutine(sceneToLoad, LoadSceneMode.Additive));

        _currentContentSceneIndex = sceneToLoad;
        _isTransitioning = false;
    }

    /// <summary>
    /// Helper to load a scene and set it as active.
    /// </summary>
    private IEnumerator LoadSceneRoutine(int index, LoadSceneMode mode)
    {
        AsyncOperation loadOp = SceneManager.LoadSceneAsync(index, mode);

        // Wait until load is finished
        while (!loadOp.isDone)
        {
            yield return null;
        }

        // Optional: Set the new scene as "Active" so instantiated objects go there by default,
        // and lighting settings from that scene are used (important for URP).
        Scene newScene = SceneManager.GetSceneByBuildIndex(index);
        SceneManager.SetActiveScene(newScene);
    }
}