using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Real-time Mesh Slicing destruction script.
/// Uses planar slicing to recursively cut the mesh into smaller shards.
/// Heavily optimized for "Convex" shapes (like walls).
/// </summary>
public class DestructibleWallSlicing : MonoBehaviour
{
    [Header("Slicing Settings")]
    [Tooltip("Target number of shards to generate. Higher = more CPU cost.")]
    public int targetShards = 12;

    [Tooltip("Bias for cut planes to focus on the impact point. 0 = random, 1 = all cuts through impact.")]
    [Range(0f, 1f)]
    public float impactBias = 0.8f;

    [Tooltip("Randomness of the cut plane normal.")]
    [Range(0f, 1f)]
    public float planeChaos = 0.5f;

    [Header("Explosion Settings")]
    public float explosionForce = 600f;
    public float explosionRadius = 4f;

    [Header("Materials")]
    [Tooltip("Material for the outside surface")]
    public Material surfaceMaterial;
    [Tooltip("Material for the inside cut faces")]
    public Material interiorMaterial;

    private bool isBroken = false;
    private float originalMass = 1.0f;

    void Start()
    {
        // Cache original mass to distribute it later
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) originalMass = rb.mass;

        Renderer rend = GetComponent<Renderer>();
        if (rend != null && surfaceMaterial == null) surfaceMaterial = rend.sharedMaterial;
        if (interiorMaterial == null) interiorMaterial = surfaceMaterial;
    }

    public void BreakWall(Vector3 hitPoint)
    {
        if (isBroken) return;
        isBroken = true;

        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null) return;

        // 1. Setup Initial Mesh Wrapper
        SlicedMesh initialMesh = new SlicedMesh(mf.sharedMesh, transform.localToWorldMatrix, transform.worldToLocalMatrix);

        List<SlicedMesh> finalShards = new List<SlicedMesh>();
        List<SlicedMesh> processQueue = new List<SlicedMesh>();
        processQueue.Add(initialMesh);

        // 2. Recursive Slicing Loop
        // We loop until we have enough shards or run out of meaningful cuts
        int safetyCounter = 0;

        // Convert hit point to local space for logic
        Vector3 localHitPoint = transform.InverseTransformPoint(hitPoint);

        while (processQueue.Count < targetShards && processQueue.Count > 0 && safetyCounter < targetShards * 2)
        {
            safetyCounter++;

            // Pick the biggest mesh that is closest to impact to slice next
            // This ensures we get small details near the hit point
            int indexToSlice = GetBestMeshIndexToSlice(processQueue, localHitPoint);
            SlicedMesh target = processQueue[indexToSlice];
            processQueue.RemoveAt(indexToSlice);

            // Generate a Cut Plane
            Plane cutPlane = GenerateCutPlane(target.Bounds, localHitPoint);

            // Perform Slice
            SlicedMesh[] result = Slicer.Slice(target, cutPlane);

            if (result != null)
            {
                processQueue.Add(result[0]);
                processQueue.Add(result[1]);
            }
            else
            {
                // If slice failed (mesh didn't intersect plane), keep original
                finalShards.Add(target);
            }
        }

        // Add remaining queue items to final
        finalShards.AddRange(processQueue);

        // 3. Instantiate GameObjects
        CreateGameObjects(finalShards);

        // 4. Disable Original
        GetComponent<Renderer>().enabled = false;
        if (GetComponent<Collider>()) GetComponent<Collider>().enabled = false;
        Destroy(gameObject, 10f); // Cleanup parent after a while
    }

    private int GetBestMeshIndexToSlice(List<SlicedMesh> list, Vector3 localHit)
    {
        // Scoring: Higher is better. 
        // We want large volume + close proximity to hit point.
        float bestScore = -Mathf.Infinity;
        int bestIndex = 0;

        for (int i = 0; i < list.Count; i++)
        {
            float dist = Vector3.Distance(list[i].Bounds.center, localHit);
            // Avoid divide by zero
            if (dist < 0.1f) dist = 0.1f;

            // Score = Volume / Distance. 
            // We prioritize breaking big chunks near the center.
            float score = list[i].Volume / dist;

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

    private Plane GenerateCutPlane(Bounds bounds, Vector3 localHit)
    {
        // 1. Pick a point for the plane to pass through
        // Lerp between the mesh center and the hit point based on bias
        Vector3 targetPos = Vector3.Lerp(bounds.center, localHit, impactBias);

        // Add some jitter so we don't always slice perfectly through the center
        Vector3 jitter = Random.insideUnitSphere * (bounds.size.magnitude * 0.1f);
        Vector3 planePoint = targetPos + jitter;

        // 2. Pick a normal
        // Random direction
        Vector3 randomDir = Random.onUnitSphere;
        // Optional: Bias normal to face towards hit point for "shattering" effect? 
        // For now, pure random rotation is usually best for "rubble".

        return new Plane(randomDir, planePoint);
    }

    private void CreateGameObjects(List<SlicedMesh> shards)
    {
        GameObject root = new GameObject(name + "_Shards");
        root.transform.position = transform.position;
        root.transform.rotation = transform.rotation;
        root.transform.localScale = transform.localScale;

        float totalCalculatedVolume = 0f;
        foreach (var s in shards) totalCalculatedVolume += s.Volume;

        foreach (var shardData in shards)
        {
            GameObject go = new GameObject("Shard");
            go.transform.SetParent(root.transform, false);

            Mesh mesh = shardData.GenerateUnityMesh();

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.mesh = mesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.materials = new Material[] { surfaceMaterial, interiorMaterial };

            MeshCollider mc = go.AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = mesh;

            Rigidbody rb = go.AddComponent<Rigidbody>();
            // Calculate mass based on volume ratio
            float ratio = shardData.Volume / totalCalculatedVolume;
            rb.mass = Mathf.Max(originalMass * ratio, 0.01f);

            rb.AddExplosionForce(explosionForce, transform.position, explosionRadius);
        }
    }

    // --- Helper Classes for Slicing Logic ---

    private class SlicedMesh
    {
        public List<Vector3> vertices = new List<Vector3>();
        public List<Vector3> normals = new List<Vector3>();
        public List<Vector2> uvs = new List<Vector2>();

        // Submesh 0: Surface, Submesh 1: Cut Interior
        public List<int> trianglesSurface = new List<int>();
        public List<int> trianglesCut = new List<int>();

        public Bounds Bounds;
        public float Volume;

        public SlicedMesh() { }

        // Constructor from Unity Mesh
        public SlicedMesh(Mesh m, Matrix4x4 l2w, Matrix4x4 w2l)
        {
            m.GetVertices(vertices);
            m.GetNormals(normals);
            m.GetUVs(0, uvs);

            // Assume submesh 0 is everything for the start
            // If the original wall already has 2 submeshes, we merge them for simplicity here
            // or just take the first one. For this demo, we assume a simple wall.
            if (m.subMeshCount > 0) m.GetTriangles(trianglesSurface, 0);
            if (m.subMeshCount > 1) m.GetTriangles(trianglesCut, 1);

            RecalculateBoundsAndVolume();
        }

        public void AddVertex(Vector3 v, Vector3 n, Vector2 uv)
        {
            vertices.Add(v);
            normals.Add(n);
            uvs.Add(uv);
        }

        public void AddTriangle(int i1, int i2, int i3, bool isCutFace)
        {
            if (isCutFace)
            {
                trianglesCut.Add(i1);
                trianglesCut.Add(i2);
                trianglesCut.Add(i3);
            }
            else
            {
                trianglesSurface.Add(i1);
                trianglesSurface.Add(i2);
                trianglesSurface.Add(i3);
            }
        }

        public Mesh GenerateUnityMesh()
        {
            Mesh m = new Mesh();
            m.SetVertices(vertices);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.subMeshCount = 2;
            m.SetTriangles(trianglesSurface, 0);
            m.SetTriangles(trianglesCut, 1);
            m.RecalculateTangents();
            return m;
        }

        public void RecalculateBoundsAndVolume()
        {
            if (vertices.Count == 0) return;

            Bounds = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < vertices.Count; i++) Bounds.Encapsulate(vertices[i]);

            // Approximate volume using bounding box (fast) or tetrahedron sum (accurate)
            // Using Box volume is faster for real-time heuristics
            Volume = Bounds.size.x * Bounds.size.y * Bounds.size.z;
        }
    }

    private static class Slicer
    {
        /// <summary>
        /// Slices a mesh by a plane. Returns [PositiveMesh, NegativeMesh].
        /// Returns null if plane doesn't intersect mesh.
        /// </summary>
        public static SlicedMesh[] Slice(SlicedMesh src, Plane plane)
        {
            // Optimization: check bounds first
            // Get corners of bounds
            Vector3 min = src.Bounds.min;
            Vector3 max = src.Bounds.max;
            Vector3[] corners = new Vector3[] {
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z)
            };

            bool hasPos = false;
            bool hasNeg = false;
            foreach (var c in corners)
            {
                if (plane.GetSide(c)) hasPos = true; else hasNeg = true;
            }

            // Plane doesn't cut the box? Return null.
            if (!hasPos || !hasNeg) return null;

            SlicedMesh meshPos = new SlicedMesh();
            SlicedMesh meshNeg = new SlicedMesh();

            // Mapping from old vertex index to new vertex index in respective meshes
            int[] mapPos = new int[src.vertices.Count];
            int[] mapNeg = new int[src.vertices.Count];
            bool[] side = new bool[src.vertices.Count]; // true = positive

            // 1. Classify Vertices and Copy
            for (int i = 0; i < src.vertices.Count; i++)
            {
                Vector3 v = src.vertices[i];
                bool isPos = plane.GetSide(v);
                side[i] = isPos;

                if (isPos)
                {
                    mapPos[i] = meshPos.vertices.Count;
                    meshPos.AddVertex(v, src.normals[i], src.uvs[i]);
                }
                else
                {
                    mapNeg[i] = meshNeg.vertices.Count;
                    meshNeg.AddVertex(v, src.normals[i], src.uvs[i]);
                }
            }

            // List of cut edges (PointA, PointB) used for capping logic
            List<Vector3> cutVerts = new List<Vector3>();

            // 2. Process Triangles
            // We combine both submesh lists for processing, but keep track of which list they came from
            ProcessTriangleList(src.trianglesSurface, src, plane, side, mapPos, mapNeg, meshPos, meshNeg, cutVerts, false);
            ProcessTriangleList(src.trianglesCut, src, plane, side, mapPos, mapNeg, meshPos, meshNeg, cutVerts, true);

            // 3. Cap Holes
            CapMesh(meshPos, cutVerts, plane.normal); // Positive side uses plane normal
            CapMesh(meshNeg, cutVerts, -plane.normal); // Negative side uses opposite normal

            meshPos.RecalculateBoundsAndVolume();
            meshNeg.RecalculateBoundsAndVolume();

            return new SlicedMesh[] { meshPos, meshNeg };
        }

        private static void ProcessTriangleList(List<int> tris, SlicedMesh src, Plane plane, bool[] side, int[] mapPos, int[] mapNeg, SlicedMesh meshPos, SlicedMesh meshNeg, List<Vector3> cutVerts, bool isCutFace)
        {
            for (int i = 0; i < tris.Count; i += 3)
            {
                int i1 = tris[i];
                int i2 = tris[i + 1];
                int i3 = tris[i + 2];

                bool s1 = side[i1];
                bool s2 = side[i2];
                bool s3 = side[i3];

                if (s1 == s2 && s2 == s3)
                {
                    // All on one side
                    if (s1) meshPos.AddTriangle(mapPos[i1], mapPos[i2], mapPos[i3], isCutFace);
                    else meshNeg.AddTriangle(mapNeg[i1], mapNeg[i2], mapNeg[i3], isCutFace);
                }
                else
                {
                    // Split needed
                    // Find the single point on one side
                    int singleIndex, otherA, otherB;
                    bool singleSide;

                    if (s1 != s2 && s1 != s3) // i1 is alone
                    {
                        singleIndex = i1; otherA = i2; otherB = i3; singleSide = s1;
                    }
                    else if (s2 != s1 && s2 != s3) // i2 is alone
                    {
                        singleIndex = i2; otherA = i3; otherB = i1; singleSide = s2;
                    }
                    else // i3 is alone
                    {
                        singleIndex = i3; otherA = i1; otherB = i2; singleSide = s3;
                    }

                    // Intersection points
                    // Intersect Single-OtherA
                    InterpolatedVertex v1 = Intersect(src, plane, singleIndex, otherA);
                    // Intersect Single-OtherB
                    InterpolatedVertex v2 = Intersect(src, plane, singleIndex, otherB);

                    // Add intersect vertices to both meshes
                    int posV1 = meshPos.vertices.Count; meshPos.AddVertex(v1.pos, v1.norm, v1.uv);
                    int posV2 = meshPos.vertices.Count; meshPos.AddVertex(v2.pos, v2.norm, v2.uv);
                    int negV1 = meshNeg.vertices.Count; meshNeg.AddVertex(v1.pos, v1.norm, v1.uv);
                    int negV2 = meshNeg.vertices.Count; meshNeg.AddVertex(v2.pos, v2.norm, v2.uv);

                    // Store cut edge for capping (always v1 to v2)
                    cutVerts.Add(v1.pos);
                    cutVerts.Add(v2.pos);

                    if (singleSide) // Single point is Positive
                    {
                        // Positive side gets 1 triangle: Single -> V1 -> V2
                        meshPos.AddTriangle(mapPos[singleIndex], posV1, posV2, isCutFace);

                        // Negative side gets Quad: OtherA -> OtherB -> V2 -> V1
                        // Split quad into 2 tris: OtherA -> OtherB -> V2
                        meshNeg.AddTriangle(mapNeg[otherA], mapNeg[otherB], negV2, isCutFace);
                        // And OtherA -> V2 -> V1
                        meshNeg.AddTriangle(mapNeg[otherA], negV2, negV1, isCutFace);
                    }
                    else // Single point is Negative
                    {
                        // Negative side gets 1 triangle
                        meshNeg.AddTriangle(mapNeg[singleIndex], negV1, negV2, isCutFace);

                        // Positive side gets Quad
                        meshPos.AddTriangle(mapPos[otherA], mapPos[otherB], posV2, isCutFace);
                        meshPos.AddTriangle(mapPos[otherA], posV2, posV1, isCutFace);
                    }
                }
            }
        }

        struct InterpolatedVertex { public Vector3 pos; public Vector3 norm; public Vector2 uv; }

        private static InterpolatedVertex Intersect(SlicedMesh src, Plane plane, int i1, int i2)
        {
            Vector3 p1 = src.vertices[i1];
            Vector3 p2 = src.vertices[i2];

            float d1 = plane.GetDistanceToPoint(p1);
            float d2 = plane.GetDistanceToPoint(p2);
            float t = d1 / (d1 - d2); // Normalized distance (0 to 1)

            InterpolatedVertex v = new InterpolatedVertex();
            v.pos = Vector3.Lerp(p1, p2, t);
            v.norm = Vector3.Lerp(src.normals[i1], src.normals[i2], t).normalized;
            v.uv = Vector2.Lerp(src.uvs[i1], src.uvs[i2], t);
            return v;
        }

        private static void CapMesh(SlicedMesh mesh, List<Vector3> cutVerts, Vector3 faceNormal)
        {
            // Simple centroid fan triangulation.
            // This works well for convex slices (which box slicing produces).
            // For complex concave slices, this will produce artifacts, but for a wall breaking script it's sufficient and fast.

            if (cutVerts.Count < 3) return;

            // Calculate Centroid
            Vector3 center = Vector3.zero;
            foreach (var v in cutVerts) center += v;
            center /= cutVerts.Count;

            // Add center vertex
            int centerIdx = mesh.vertices.Count;
            // UV for cut face can be based on world position X/Y or just 0,0
            mesh.AddVertex(center, faceNormal, Vector2.zero);

            // Add all cut vertices again with the new Face Normal (flat shading for the cut)
            int startIdx = mesh.vertices.Count;
            for (int i = 0; i < cutVerts.Count; i++)
            {
                mesh.AddVertex(cutVerts[i], faceNormal, Vector2.zero);
            }

            // We need to order the vertices around the center to form a fan
            // Project to 2D plane defined by normal
            // Since we know they are pairs (from the triangle split step), we *could* traverse the graph.
            // But sorting by angle is easier for a centroid fan.

            // Create a list of indices 0..N
            List<int> indices = Enumerable.Range(0, cutVerts.Count).ToList();

            // Calculate a basis
            Vector3 tangent = Vector3.Cross(faceNormal, Vector3.up);
            if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(faceNormal, Vector3.right);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(faceNormal, tangent);

            indices.Sort((a, b) => {
                Vector3 da = cutVerts[a] - center;
                Vector3 db = cutVerts[b] - center;
                float angleA = Mathf.Atan2(Vector3.Dot(da, bitangent), Vector3.Dot(da, tangent));
                float angleB = Mathf.Atan2(Vector3.Dot(db, bitangent), Vector3.Dot(db, tangent));
                return angleA.CompareTo(angleB);
            });

            // Create Fan Triangles
            for (int i = 0; i < indices.Count; i++)
            {
                int current = startIdx + indices[i];
                int next = startIdx + indices[(i + 1) % indices.Count];

                // Note: Winding order matters. 
                // We check normal direction.
                mesh.AddTriangle(centerIdx, next, current, true);
            }
        }
    }
}