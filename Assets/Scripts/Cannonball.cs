using UnityEngine;

public class Cannonball : MonoBehaviour
{
    [Tooltip("Time in seconds before the ball destroys itself automatically")]
    public float lifeTime = 5f;

    void Start()
    {
        // Destroy the ball after 'lifeTime' seconds to keep the hierarchy clean
        Destroy(gameObject, lifeTime);
    }

    // This event triggers when the ball hits something solid
    void OnCollisionEnter(Collision collision)
    {
        // Check if the object we hit has the DestructibleWall script
        DestructibleWall wall = collision.gameObject.GetComponent<DestructibleWall>();

        if (wall != null)
        {
            // Trigger the wall break logic
            wall.BreakWall();

            // Optional: Destroy the ball immediately upon impact
            Destroy(gameObject);
        }
    }
}
