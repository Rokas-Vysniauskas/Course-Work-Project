using UnityEngine;

public class DestructibleWall : MonoBehaviour
{
    [Header("Wall States")]
    [Tooltip("The solid, unbroken wall GameObject")]
    public GameObject solidWall;

    [Tooltip("The pre-fractured wall GameObject (initially disabled)")]
    public GameObject fracturedWall;

    [Header("Explosion Settings")]
    [Tooltip("Force applied to the broken pieces")]
    public float explosionForce = 500f;
    [Tooltip("Radius of the explosion")]
    public float explosionRadius = 2f;

    private bool isBroken = false;

    void Start()
    {
        // Reset state on start
        if (solidWall != null) solidWall.SetActive(true);
        if (fracturedWall != null) fracturedWall.SetActive(false);
    }

    public void BreakWall()
    {
        if (isBroken) return; // Prevent double breaking
        isBroken = true;

        Debug.Log("Wall logic triggered: Breaking now!");

        // 1. Hide solid wall
        if (solidWall != null) solidWall.SetActive(false);

        // 2. Show fractured wall and apply force
        if (fracturedWall != null)
        {
            fracturedWall.SetActive(true);

            // Apply explosion force to all children (the debris pieces)
            foreach (Transform piece in fracturedWall.transform)
            {
                Rigidbody rb = piece.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.AddExplosionForce(explosionForce, transform.position, explosionRadius);
                }
            }
        }
    }
}
