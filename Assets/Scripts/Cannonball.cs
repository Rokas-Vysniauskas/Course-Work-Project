using UnityEngine;

public class Cannonball : MonoBehaviour
{
    [Tooltip("Time in seconds before the ball destroys itself automatically")]
    public float lifeTime = 5f;

    [Tooltip("Optimization: Only check for the wall script if the object has this tag.")]
    public string targetTag = "Destructible";

    private Rigidbody rb;
    private Vector3 velocityBeforePhysics;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        Destroy(gameObject, lifeTime);
    }

    void FixedUpdate()
    {
        // Cache velocity every physics step. 
        // FixedUpdate runs before collision resolution, so this captures the speed 
        // right before it hits the wall and gets stopped by physics.
        if (rb != null)
        {
            // Updated for Unity 6+: velocity -> linearVelocity
            velocityBeforePhysics = rb.linearVelocity;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        // Debug: Verify collision is happening at all
        //Debug.Log($"Cannonball hit: {collision.gameObject.name}");

        // GetComponentInParent is expensive. We only run it if the tag matches.
        // Ensure the Wall objects are tagged "Destructible" (or match this string)
        //if (!collision.gameObject.CompareTag(targetTag)) return;

        // Improved: Look for the script on the object hit OR its parents
        // This fixes issues where the collider is on a child mesh
        DestructibleWall wall = collision.gameObject.GetComponentInParent<DestructibleWall>();

        if (wall != null)
        {
            wall.BreakWall();

            // Restore the velocity to what it was before hitting the solid wall.
            // This makes the ball "ignore" the stop caused by the solid wall 
            // and continue through to hit the debris.
            if (rb != null)
            {
                // Updated for Unity 6+: velocity -> linearVelocity
                rb.linearVelocity = velocityBeforePhysics;
            }
        }
    }
}
