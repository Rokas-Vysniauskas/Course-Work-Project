using UnityEngine;

public class CannonController : MonoBehaviour
{
    [Header("Cannon Settings")]
    [Tooltip("The actual ball prefab to spawn and shoot")]
    public GameObject cannonballPrefab;

    [Tooltip("The point at the tip of the barrel where the ball spawns")]
    public Transform firePoint;

    [Tooltip("The force applied to the ball when shooting")]
    public float shootForce = 1000f;

    void Update()
    {
        // Check for Spacebar input
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Shoot();
        }
    }

    void Shoot()
    {
        // 1. Instantiate the ball at the fire point
        // Using the firePoint's rotation ensures it shoots in the direction the cannon is facing
        GameObject currentBall = Instantiate(cannonballPrefab, firePoint.position, firePoint.rotation);

        // 2. Get the Rigidbody component from the new ball
        Rigidbody rb = currentBall.GetComponent<Rigidbody>();

        if (rb != null)
        {
            // 3. Add explosive force in the forward direction
            rb.AddForce(firePoint.forward * shootForce);
        }
        else
        {
            Debug.LogWarning("Cannonball Prefab is missing a Rigidbody component!");
        }
    }
}
