using UnityEngine;
using UnityEngine.InputSystem; // Required for the new Input System

public class CannonController : MonoBehaviour
{
    [Header("Cannon Settings")]
    [Tooltip("The actual ball prefab to spawn and shoot")]
    public GameObject cannonballPrefab;

    [Tooltip("The point at the tip of the barrel where the ball spawns")]
    public Transform firePoint;

    [Tooltip("The force applied to the ball when shooting")]
    public float shootForce = 1000f; // Lowered default since Impulse is much stronger

    void Update()
    {
        // Check for Spacebar using the New Input System
        // We check 'Keyboard.current != null' to prevent errors if no keyboard is connected
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Shoot();
        }
    }

    void Shoot()
    {
        if (firePoint == null)
        {
            Debug.LogError("FirePoint is not assigned!");
            return;
        }

        // 1. Instantiate the ball at the fire point
        // Using the firePoint's rotation ensures it shoots in the direction the cannon is facing
        GameObject currentBall = Instantiate(cannonballPrefab, firePoint.position, firePoint.rotation);

        // 2. Get the Rigidbody component from the new ball
        Rigidbody rb = currentBall.GetComponent<Rigidbody>();

        if (rb != null)
        {
            // 3. Add explosive force in the forward direction
            // Changed to ForceMode.Impulse for instant velocity change
            rb.AddForce(firePoint.forward * shootForce, ForceMode.Impulse);
        }
        else
        {
            Debug.LogWarning("Cannonball Prefab is missing a Rigidbody component!");
        }
    }
}