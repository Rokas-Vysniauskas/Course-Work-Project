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
            // Unity 6+ uses linearVelocity
            velocityBeforePhysics = rb.linearVelocity;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        bool hitSomething = false;
        Vector3 hitPoint = collision.GetContact(0).point;

        // 1. Check for the Voronoi Wall (Previous script)
        DestructibleWallVoronoi vWall = collision.gameObject.GetComponentInParent<DestructibleWallVoronoi>();
        if (vWall != null)
        {
            vWall.BreakWall(hitPoint);
            hitSomething = true;
        }
        else
        {
            // 2. Check for the Slicing Wall (NEW script)
            DestructibleWallSlicing sWall = collision.gameObject.GetComponentInParent<DestructibleWallSlicing>();
            if (sWall != null)
            {
                sWall.BreakWall(hitPoint);
                hitSomething = true;
            }
            else
            {
                // 3. Check for Basic Wall (Legacy)
                DestructibleWall wall = collision.gameObject.GetComponentInParent<DestructibleWall>();
                if (wall != null)
                {
                    wall.BreakWall();
                    hitSomething = true;
                }
            }
        }

        // Shared Logic: Restore velocity if we broke a wall so the ball keeps flying through
        if (hitSomething && rb != null)
        {
            rb.linearVelocity = velocityBeforePhysics;
        }
    }
}