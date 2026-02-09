using UnityEngine;
using System.Collections.Generic;

public class DestructibleWallVoronoi : MonoBehaviour
{
    [Header("Voronoi Settings")]
    [Tooltip("How many pieces to break into. Higher = more lag but more detail.")]
    public int voronoiSiteCount = 50;

    [Tooltip("How much the pieces concentrate at the impact point (0 = uniform, 1 = highly clustered).")]
    [Range(0f, 1f)]
    public float impactBias = 0.75f;

    [Header("Explosion Settings")]
    public float explosionForce = 500f;
    public float explosionRadius = 3f;

    [Header("Materials")]
    [Tooltip("Material for the outside of the wall")]
    public Material surfaceMaterial;

    [Tooltip("Material for the inside cut faces")]
    public Material interiorMaterial;

    // Internal state
    private bool isBroken = false;
    private Renderer wallRenderer;
    private MeshFilter wallMeshFilter;

    void Start()
    {
        wallRenderer = GetComponent<Renderer>();
        wallMeshFilter = GetComponent<MeshFilter>();

        // Auto-assign surface material if missing
        if (surfaceMaterial == null && wallRenderer != null)
        {
            surfaceMaterial = wallRenderer.sharedMaterial;
        }

        // Fallback: If no interior material is set, use the surface material
        if (interiorMaterial == null)
        {
            interiorMaterial = surfaceMaterial;
        }
    }

    /// <summary>
    /// Called by Cannonball.cs. Takes the specific point of impact in World Space.
    /// </summary>
    public void BreakWall(Vector3 worldHitPoint)
    {
        if (isBroken) return;
        isBroken = true;

        // 0. Get Mass from Rigidbody (if exists) to conserve mass
        float totalMass = 1.0f;
        Rigidbody wallRb = GetComponent<Rigidbody>();
        if (wallRb != null)
        {
            totalMass = wallRb.mass;
        }

        // 1. Get Local Dimensions from Mesh
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
        if (wallMeshFilter != null && wallMeshFilter.sharedMesh != null)
        {
            bounds = wallMeshFilter.sharedMesh.bounds;
        }

        // 2. Convert Hit Point to Local Space
        Vector3 localHitPos = transform.InverseTransformPoint(worldHitPoint);

        // Flatten hit point to the X/Y plane for 2D Voronoi calculation
        Vector2 center2D = new Vector2(
            Mathf.Clamp(localHitPos.x, bounds.min.x, bounds.max.x),
            Mathf.Clamp(localHitPos.y, bounds.min.y, bounds.max.y)
        );

        // 3. Generate Voronoi Sites (Biased towards center2D)
        List<Vector2> sites = GenerateBiasedSites(voronoiSiteCount, center2D, bounds);

        // 4. Generate Polygons (The Voronoi Logic)
        List<List<Vector2>> cells = GenerateVoronoiCells(sites, bounds);

        // 5. Create Shards (Extrude using Z bounds)
        CreateShards(cells, bounds, totalMass);

        // 6. Disable Original Wall
        if (wallRenderer != null) wallRenderer.enabled = false;
        if (GetComponent<Collider>() != null) GetComponent<Collider>().enabled = false;
        // Optional: Disable original RB physics to prevent ghost interactions
        if (wallRb != null) wallRb.isKinematic = true;

        // Cleanup original object later
        Destroy(gameObject, 10f);
    }

    // Overload for compatibility if called without args (defaults to center)
    public void BreakWall()
    {
        BreakWall(transform.position);
    }

    List<Vector2> GenerateBiasedSites(int count, Vector2 center, Bounds b)
    {
        List<Vector2> sites = new List<Vector2>();

        for (int i = 0; i < count; i++)
        {
            Vector2 pt = new Vector2(
                Random.Range(b.min.x, b.max.x),
                Random.Range(b.min.y, b.max.y)
            );

            // Bias towards the impact point
            pt = Vector2.Lerp(pt, center, Random.Range(0f, impactBias));
            sites.Add(pt);
        }
        return sites;
    }

    List<List<Vector2>> GenerateVoronoiCells(List<Vector2> sites, Bounds b)
    {
        List<List<Vector2>> cells = new List<List<Vector2>>();

        List<Vector2> boundaryPoly = new List<Vector2>()
        {
            new Vector2(b.min.x, b.min.y),
            new Vector2(b.max.x, b.min.y),
            new Vector2(b.max.x, b.max.y),
            new Vector2(b.min.x, b.max.y)
        };

        for (int i = 0; i < sites.Count; i++)
        {
            List<Vector2> poly = new List<Vector2>(boundaryPoly);
            Vector2 site = sites[i];

            for (int j = 0; j < sites.Count; j++)
            {
                if (i == j) continue;

                Vector2 other = sites[j];
                Vector2 halfPoint = (site + other) * 0.5f;
                Vector2 dir = (other - site).normalized;

                poly = ClipPolygon(poly, halfPoint, dir);
                if (poly.Count < 3) break;
            }

            if (poly.Count >= 3) cells.Add(poly);
        }

        return cells;
    }

    List<Vector2> ClipPolygon(List<Vector2> poly, Vector2 planeP, Vector2 planeN)
    {
        List<Vector2> newPoly = new List<Vector2>();
        if (poly.Count == 0) return newPoly;

        Vector2 S = poly[poly.Count - 1];
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 E = poly[i];

            bool sIn = Vector2.Dot(S - planeP, planeN) < 0;
            bool eIn = Vector2.Dot(E - planeP, planeN) < 0;

            if (sIn && eIn) newPoly.Add(E);
            else if (sIn && !eIn) newPoly.Add(Intersect(S, E, planeP, planeN));
            else if (!sIn && eIn)
            {
                newPoly.Add(Intersect(S, E, planeP, planeN));
                newPoly.Add(E);
            }
            S = E;
        }
        return newPoly;
    }

    Vector2 Intersect(Vector2 p1, Vector2 p2, Vector2 planeP, Vector2 planeN)
    {
        Vector2 lineDir = p2 - p1;
        float t = Vector2.Dot(planeP - p1, planeN) / Vector2.Dot(lineDir, planeN);
        return p1 + lineDir * t;
    }

    void CreateShards(List<List<Vector2>> cells, Bounds b, float totalMass)
    {
        GameObject shardsRoot = new GameObject("Shards_Root");
        shardsRoot.transform.position = transform.position;
        shardsRoot.transform.rotation = transform.rotation;
        shardsRoot.transform.localScale = transform.localScale;

        float zMin = b.min.z;
        float zMax = b.max.z;

        // 1. Calculate Areas for Mass Distribution (Volume based)
        List<float> cellAreas = new List<float>();
        float totalArea = 0f;

        foreach (var cell in cells)
        {
            float area = CalculatePolygonArea(cell);
            cellAreas.Add(area);
            totalArea += area;
        }

        // Prevent divide by zero if something goes wrong
        if (totalArea <= 1e-5f) totalArea = 1f;

        // 2. Create Shards
        for (int i = 0; i < cells.Count; i++)
        {
            // Mass is proportional to area (volume, since thickness is constant)
            float massPercentage = cellAreas[i] / totalArea;
            float shardMass = totalMass * massPercentage;

            // Safety clamp for very small shards to avoid physics glitches
            if (shardMass < 0.001f) shardMass = 0.001f;

            CreateShardMesh(cells[i], zMin, zMax, shardsRoot.transform, shardMass);
        }
    }

    // Shoelace formula to calculate 2D polygon area
    float CalculatePolygonArea(List<Vector2> poly)
    {
        if (poly.Count < 3) return 0f;
        float area = 0f;
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 p1 = poly[i];
            Vector2 p2 = poly[(i + 1) % poly.Count];
            area += (p1.x * p2.y) - (p2.x * p1.y);
        }
        return Mathf.Abs(area * 0.5f);
    }

    void CreateShardMesh(List<Vector2> poly, float zMin, float zMax, Transform parent, float mass)
    {
        List<Vector3> verts = new List<Vector3>();
        List<Vector3> norms = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();

        // Split triangles by submesh index
        List<int> surfaceTris = new List<int>(); // Submesh 0 (Front/Back)
        List<int> interiorTris = new List<int>(); // Submesh 1 (Sides)

        // 1. Front Face (Surface)
        int frontStart = verts.Count;
        for (int i = 0; i < poly.Count; i++)
        {
            verts.Add(new Vector3(poly[i].x, poly[i].y, zMin));
            norms.Add(Vector3.back);
            uvs.Add(new Vector2(poly[i].x, poly[i].y));
        }
        for (int i = 1; i < poly.Count - 1; i++)
        {
            surfaceTris.Add(frontStart);
            surfaceTris.Add(frontStart + i + 1);
            surfaceTris.Add(frontStart + i);
        }

        // 2. Back Face (Surface)
        int backStart = verts.Count;
        for (int i = 0; i < poly.Count; i++)
        {
            verts.Add(new Vector3(poly[i].x, poly[i].y, zMax));
            norms.Add(Vector3.forward);
            uvs.Add(new Vector2(poly[i].x, poly[i].y));
        }
        for (int i = 1; i < poly.Count - 1; i++)
        {
            surfaceTris.Add(backStart);
            surfaceTris.Add(backStart + i);
            surfaceTris.Add(backStart + i + 1);
        }

        // 3. Sides (Interior)
        int sideStart = verts.Count;
        for (int i = 0; i < poly.Count; i++)
        {
            int next = (i + 1) % poly.Count;
            Vector3 v1 = new Vector3(poly[i].x, poly[i].y, zMin);
            Vector3 v2 = new Vector3(poly[next].x, poly[next].y, zMin);
            Vector3 v3 = new Vector3(poly[next].x, poly[next].y, zMax);
            Vector3 v4 = new Vector3(poly[i].x, poly[i].y, zMax);
            Vector3 normal = Vector3.Cross(v2 - v1, v4 - v1).normalized;

            verts.Add(v1); norms.Add(normal); uvs.Add(Vector2.zero);
            verts.Add(v2); norms.Add(normal); uvs.Add(Vector2.right);
            verts.Add(v3); norms.Add(normal); uvs.Add(Vector2.one);
            verts.Add(v4); norms.Add(normal); uvs.Add(Vector2.up);

            int baseIdx = sideStart + (i * 4);
            // Add side triangles to interiorTris list
            // FIX: Swapped winding order (0->1->2 and 0->2->3) for correct outward facing
            interiorTris.Add(baseIdx); interiorTris.Add(baseIdx + 1); interiorTris.Add(baseIdx + 2);
            interiorTris.Add(baseIdx); interiorTris.Add(baseIdx + 2); interiorTris.Add(baseIdx + 3);
        }

        Mesh mesh = new Mesh();
        mesh.vertices = verts.ToArray();
        mesh.normals = norms.ToArray();
        mesh.uv = uvs.ToArray();

        // Define 2 SubMeshes
        mesh.subMeshCount = 2;
        mesh.SetTriangles(surfaceTris, 0); // SubMesh 0: Surface
        mesh.SetTriangles(interiorTris, 1); // SubMesh 1: Interior

        mesh.RecalculateBounds();

        GameObject shard = new GameObject("Shard");
        shard.transform.SetParent(parent, false);

        shard.AddComponent<MeshFilter>().mesh = mesh;
        MeshRenderer mr = shard.AddComponent<MeshRenderer>();

        // Assign Material Array
        mr.materials = new Material[] { surfaceMaterial, interiorMaterial };

        MeshCollider mc = shard.AddComponent<MeshCollider>();
        mc.convex = true;
        mc.sharedMesh = mesh;

        Rigidbody rb = shard.AddComponent<Rigidbody>();
        rb.mass = mass; // Set calculated mass

        rb.AddExplosionForce(explosionForce, transform.position, explosionRadius);
    }
}