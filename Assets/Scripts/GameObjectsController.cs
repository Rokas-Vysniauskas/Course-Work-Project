using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem; // Requires Input System Package

/// <summary>
/// Controls the activation state of various GameObjects via keyboard shortcuts.
/// Handles finding objects in both Master and Additive scenes, even if they are currently inactive.
/// </summary>
public class GameObjectsController : MonoBehaviour
{
    [System.Serializable]
    public class ControlledObject
    {
        [Tooltip("The name of the GameObject to control. Useful for objects in additive scenes.")]
        public string objectName;

        [Tooltip("Optional: Direct reference for objects in the Master Scene.")]
        public GameObject directReference;

        [Tooltip("Key to toggle the object on/off.")]
        public Key toggleKey;

        [HideInInspector]
        public GameObject cachedObject;
    }

    [Header("Configuration")]
    public List<ControlledObject> objectsToControl = new List<ControlledObject>();

    void Update()
    {
        if (Keyboard.current == null) return;

        foreach (var item in objectsToControl)
        {
            if (Keyboard.current[item.toggleKey].wasPressedThisFrame)
            {
                ToggleObject(item);
            }
        }
    }

    void ToggleObject(ControlledObject item)
    {
        GameObject target = GetTargetObject(item);

        if (target != null)
        {
            bool newState = !target.activeSelf;
            target.SetActive(newState);
            Debug.Log($"[GameObjectsController] {(newState ? "Enabled" : "Disabled")}: {target.name}");
        }
        else
        {
            Debug.LogWarning($"[GameObjectsController] Could not find GameObject with name: '{item.objectName}'");
        }
    }

    GameObject GetTargetObject(ControlledObject item)
    {
        // 1. Check Direct Reference (Fastest)
        if (item.directReference != null)
        {
            return item.directReference;
        }

        // 2. Check Cached Reference (Fast)
        if (item.cachedObject != null)
        {
            return item.cachedObject;
        }

        // 3. Find by Name (Slow - searches all loaded scenes including inactive objects)
        // Standard GameObject.Find only works on active objects, so we must iterate root objects manually.
        if (!string.IsNullOrEmpty(item.objectName))
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                GameObject found = FindInSceneRecursive(scene, item.objectName);
                if (found != null)
                {
                    item.cachedObject = found;
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Deep search for a specific object name within a scene, including inactive objects.
    /// </summary>
    GameObject FindInSceneRecursive(Scene scene, string name)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject root in roots)
        {
            if (root.name == name) return root;

            // Search children
            Transform found = root.transform.Find(name); // Only finds immediate child
            if (found != null) return found.gameObject;

            // Deep recursive search
            Transform deepFound = FindDeepChild(root.transform, name);
            if (deepFound != null) return deepFound.gameObject;
        }
        return null;
    }

    Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;

            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }
}