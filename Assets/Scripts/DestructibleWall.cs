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
        // Ensure correct initial state
        if (solidWall != null) solidWall.SetActive(true);
        if (fracturedWall != null) fracturedWall.SetActive(false);
    }

    public void BreakWall()
    {
        if (isBroken) return; // Prevent double breaking
        isBroken = true;

        // 1. Swap the models
        if (solidWall != null) solidWall.SetActive(false);
        if (fracturedWall != null) fracturedWall.SetActive(true);

        // 2. Add physics force to the broken pieces
        // This assumes the fracturedWall has children with Rigidbodies
        foreach (Transform piece in fracturedWall.transform)
        {
            Rigidbody rb = piece.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // Add explosion force to make the wall pieces fly outward
                rb.AddExplosionForce(explosionForce, transform.position, explosionRadius);
            }
        }
    }
}
