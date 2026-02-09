using UnityEngine;

public class CannonballWithHitPoint : MonoBehaviour
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
        if (rb != null)
        {
            // Updated for Unity 6+: velocity -> linearVelocity
            velocityBeforePhysics = rb.linearVelocity;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        bool hitSomething = false;

        // 1. Check for the Voronoi Wall first (specific hit point support)
        DestructibleWallVoronoi vWall = collision.gameObject.GetComponentInParent<DestructibleWallVoronoi>();
        if (vWall != null)
        {
            Vector3 hitPoint = collision.GetContact(0).point;
            vWall.BreakWall(hitPoint);
            hitSomething = true;
        }
        else
        {
            // 2. Check for the OLD Wall (Backwards compatibility for other scenes)
            DestructibleWall wall = collision.gameObject.GetComponentInParent<DestructibleWall>();
            if (wall != null)
            {
                wall.BreakWall();
                hitSomething = true;
            }
        }

        // Shared Logic: Restore velocity if we broke either type of wall
        if (hitSomething && rb != null)
        {
            rb.linearVelocity = velocityBeforePhysics;
        }
    }
}