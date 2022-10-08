using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MyBVH;

public class RayTracingObjectRoot : MonoBehaviour
{
    /* -------------------- Structure Definitions -------------------- */
    private struct MeshObject
    {
        public Matrix4x4 localToWorldMatrix;
        public Vector4   albedo;
        public Vector3   specular;
        public int       indices_offset;
        public int       indices_count;
    };

    private struct BVHMeshObject
    {
        public Matrix4x4 localToWorldMatrix;
        public Vector4   albedo;
        public Vector3   specular;
    };

    private struct BVHListNode
    {
        public Vector3 bmin;  // bounding box min
        public Vector3 bmax;  // bounding box max
        public int left;      // left node index, vertex0 index if leaf
        public int right;     // right node index, vertex1 index if leaf
        public int isLeaf;    // -1 if internal node, vertex2 index if leaf
        public int extra;     // -1 if mesh bbox, BVHMeshObject index if triangle bbox
    };

    /* -------------------- Variables Declaration -------------------- */
    // Variables for basic ray tracing
    private static List<MeshObject> meshObjects = new List<MeshObject>();
    private static List<Vector3> vertices = new List<Vector3>();
    private static List<int> indices = new List<int>();
    private ComputeBuffer meshObjectBuffer;
    private ComputeBuffer vertexBuffer;
    private ComputeBuffer indexBuffer;
    private int meshObjectCount = 0;

    // Variables for ray tracing with BVH
    private static Dictionary<int, BVHNode> meshBVHNodesDictionary = new Dictionary<int, BVHNode>();
    private static List<BVHMeshObject> BVHMeshObjects = new List<BVHMeshObject>();
    private static List<BVHListNode> BVHNodeList = new List<BVHListNode>();
    private static List<Vector3> BVHVertices = new List<Vector3>();
    private ComputeBuffer BVHMeshObjectBuffer;
    private ComputeBuffer BVHNodeListBuffer;
    private ComputeBuffer BVHVerticesBuffer;
    private int BVHLeafCount;

    // Show BVH Gizmos or not
    public enum GizmosMode{ Disable, BoundingBoxesOnly, RayIntercect }
    public GizmosMode currentGizmosMode;
    public int GizmosBVHLevel = 1;  // 1 is the outer most bounding box
    // Camera for GizmosMode RayIntercect
    public Camera testCamera;
    [Range(-1.0f, 1.0f)]
    public float cameraRayDirectionX = 0.0f;
    [Range(-1.0f, 1.0f)]
    public float cameraRayDirectionY = 0.0f;
    public float cameraRayLength = 10.0f;

    /* -------------------- Interface Functions -------------------- */
    private void OnEnable()
    {
        ClearData();
        BuildMeshBVHNodes();
    }

    private void OnDisable()
    {
        ClearData();
    }

    public void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) return;
        if (currentGizmosMode == GizmosMode.Disable) return;

        // Build BVH
        BuildMeshBVHNodes();
        BuildBVHBuffers();

        // Set Gizmos Matrix
        Gizmos.matrix = Matrix4x4.identity;
        // Variables declaration
        Vector3 bmin, bmax;
        BVHListNode node;
        switch (currentGizmosMode)
        {
            case GizmosMode.BoundingBoxesOnly:
                Gizmos.color = Color.yellow;
                // Traverse the List to draw all nodes
                Queue<BVHListNode> queue = new Queue<BVHListNode>();
                queue.Enqueue(BVHNodeList[0]);
                int level = 1;
                int currentLevelCount = 1;
                int nextLevelCount = 0;
                while (queue.Count > 0 && level < 20)
                {
                    node = queue.Dequeue();
                    // Draw Bounding Boxes Gizmos
                    if ((GizmosBVHLevel <= -1 && node.isLeaf != -1) ||  // show leaf node
                        GizmosBVHLevel == 0 ||    // show all nodes
                        GizmosBVHLevel == level)  // show current level nodes
                    {
                        bmin = node.extra == -1 ? node.bmin : BVHMeshObjects[node.extra].localToWorldMatrix.MultiplyPoint3x4(node.bmin);
                        bmax = node.extra == -1 ? node.bmax : BVHMeshObjects[node.extra].localToWorldMatrix.MultiplyPoint3x4(node.bmax);
                        Gizmos.DrawWireCube((bmin + bmax) / 2, bmax - bmin);
                    }

                    if (node.isLeaf == -1)
                    {
                        queue.Enqueue(BVHNodeList[node.left]);
                        queue.Enqueue(BVHNodeList[node.right]);
                        nextLevelCount += 2;
                    }

                    currentLevelCount--;
                    if (currentLevelCount == 0)
                    {
                        level++;
                        currentLevelCount = nextLevelCount;
                        nextLevelCount = 0;
                    }
                }
                break;
            case GizmosMode.RayIntercect:
                // Create ray from camera
                Matrix4x4 cameraToWorldMatrix = testCamera.cameraToWorldMatrix;
                Matrix4x4 cameraInverseProjection = testCamera.projectionMatrix.inverse;
                Vector3 origin = cameraToWorldMatrix.MultiplyPoint(Vector3.zero);
                Vector3 direction = cameraInverseProjection.MultiplyPoint(new Vector3(cameraRayDirectionX, cameraRayDirectionY, 0.0f));
                direction = cameraToWorldMatrix.MultiplyVector(direction).normalized;
                // Calculate helper variables
                Vector3 dirInv = new Vector3(1.0f / direction.x, 1.0f / direction.y, 1.0f / direction.z);
                bool[] dirIsNeg = new bool[]{ direction.x < 0, direction.y < 0, direction.z < 0 };
                // Show camera ray
                if (currentGizmosMode == GizmosMode.RayIntercect)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(origin, origin + direction * cameraRayLength);
                }

                // Use Manual Stack to Traverse BVH, which is used in compute shader. Easy for debug.
                int[] stackIndex = new int[64];
                stackIndex[0] = 0;
                int ptr = 0;
                while (ptr >= 0)
                {
                    node = BVHNodeList[stackIndex[ptr--]];
                    bmin = node.extra == -1 ? node.bmin : BVHMeshObjects[node.extra].localToWorldMatrix.MultiplyPoint3x4(node.bmin);
                    bmax = node.extra == -1 ? node.bmax : BVHMeshObjects[node.extra].localToWorldMatrix.MultiplyPoint3x4(node.bmax);
                    Gizmos.color = Color.blue;
                    Gizmos.DrawWireCube((bmin + bmax) / 2, bmax - bmin);

                    if (node.isLeaf == -1)
                    {
                        // Internal node: test left and right intersection
                        BVHListNode left  = BVHNodeList[node.left];
                        BVHListNode right = BVHNodeList[node.right];
                        // Left
                        bmin = left.extra == -1 ? left.bmin : BVHMeshObjects[left.extra].localToWorldMatrix.MultiplyPoint3x4(left.bmin);
                        bmax = left.extra == -1 ? left.bmax : BVHMeshObjects[left.extra].localToWorldMatrix.MultiplyPoint3x4(left.bmax);
                        if (IntersectBBox(origin, dirInv, dirIsNeg, bmin, bmax))
                            stackIndex[++ptr] = node.left;
                        // Right
                        bmin = right.extra == -1 ? right.bmin : BVHMeshObjects[right.extra].localToWorldMatrix.MultiplyPoint3x4(right.bmin);
                        bmax = right.extra == -1 ? right.bmax : BVHMeshObjects[right.extra].localToWorldMatrix.MultiplyPoint3x4(right.bmax);
                        if (IntersectBBox(origin, dirInv, dirIsNeg, bmin, bmax))
                            stackIndex[++ptr] = node.right;
                    }
                    else
                    {
                        // Leaf node
                        Gizmos.color = Color.yellow;
                        Matrix4x4 ltwm = BVHMeshObjects[node.extra].localToWorldMatrix;
                        Vector3 v0 = ltwm.MultiplyPoint3x4(BVHVertices[node.left]);
                        Vector3 v1 = ltwm.MultiplyPoint3x4(BVHVertices[node.right]);
                        Vector3 v2 = ltwm.MultiplyPoint3x4(BVHVertices[node.isLeaf]);
                        Gizmos.DrawLine(v0, v1);
                        Gizmos.DrawLine(v1, v2);
                        Gizmos.DrawLine(v2, v0);
                    }
                }
                break;
        }

        // Release and Clear Buffers
        ClearData();
        return;
    }

    /* -------------------- Custom Functions -------------------- */
    // Getters
    public ComputeBuffer GetMeshObjectBuffer() { return meshObjectBuffer; }
    public ComputeBuffer GetVertexBuffer()     { return vertexBuffer; }
    public ComputeBuffer GetIndexBuffer()      { return indexBuffer; }
    public int           GetMeshObjectCount()  { return meshObjectCount; }

    public ComputeBuffer GetBVHMeshObjectBuffer() { return BVHMeshObjectBuffer; }
    public ComputeBuffer GetBVHNodeListBuffer()   { return BVHNodeListBuffer; }
    public ComputeBuffer GetBVHVerticesBuffer()   { return BVHVerticesBuffer; }
    public int           GetBVHLeafCount()        { return BVHLeafCount; }

    // Clear data
    public void ClearData()
    {
        meshObjects.Clear();
        vertices.Clear();
        indices.Clear();
        meshBVHNodesDictionary.Clear();
        BVHVertices.Clear();
        BVHMeshObjects.Clear();
        BVHNodeList.Clear();
        BVHLeafCount = 0;
        if (meshObjectBuffer != null) meshObjectBuffer.Release();
        if (vertexBuffer != null)     vertexBuffer.Release();
        if (indexBuffer != null)      indexBuffer.Release();
        meshObjectBuffer = null;
        vertexBuffer = null;
        indexBuffer = null;

        if (BVHMeshObjectBuffer != null) BVHMeshObjectBuffer.Release();
        if (BVHNodeListBuffer != null)   BVHNodeListBuffer.Release();
        if (BVHVerticesBuffer != null)   BVHVerticesBuffer.Release();
        BVHMeshObjectBuffer = null;
        BVHNodeListBuffer = null;
        BVHVerticesBuffer = null;
    }

    // Collect all mesh filters in children and build computer buffers for compute shader
    public void BuildMeshObjectBuffers()
    {
        // Clear all lists
        meshObjects.Clear();
        vertices.Clear();
        indices.Clear();

        // Find all mesh filters
        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (!meshFilter.gameObject.activeSelf) continue;
            Mesh mesh = meshFilter.sharedMesh;

            // Add vertex data
            int firstVertex = vertices.Count;
            vertices.AddRange(mesh.vertices);

            // Add index data
            // If the vertex buffer is not empty, the indices need to be offset
            int firstIndex = indices.Count;
            int[] tempIndices = mesh.GetIndices(0);  // 0 is the sub-mesh index
            for (int i = 0; i < tempIndices.Length; i++) tempIndices[i] += firstVertex;
            indices.AddRange(tempIndices);

            // Add mesh object
            Material mat = meshFilter.transform.GetComponent<MeshRenderer>().material;
            float metallic = mat.GetFloat("_Metallic");
            meshObjects.Add(new MeshObject()
            {
                localToWorldMatrix = meshFilter.transform.localToWorldMatrix,
                albedo = mat.GetColor("_Color"),
                specular = new Vector3(metallic, metallic, metallic),
                indices_offset = firstIndex,
                indices_count = tempIndices.Length
            });
        }

        CreateComputeBuffer(ref meshObjectBuffer, meshObjects, 100);
        CreateComputeBuffer(ref vertexBuffer, vertices, 12);
        CreateComputeBuffer(ref indexBuffer, indices, 4);
        meshObjectCount = meshObjects.Count;
    }

    // Build BVHNode for each mesh filter so that no further builds are needed for triangles in a mesh
    public void BuildMeshBVHNodes()
    {
        // Reset mesh data
        meshBVHNodesDictionary.Clear();
        BVHVertices.Clear();
        // Get all mesh filters and create BVH
        List<MeshFilter> mfs = new List<MeshFilter>(GetComponentsInChildren<MeshFilter>());
        foreach (MeshFilter mf in mfs)
        {
            // Add vertices to list
            int vertexOffset = BVHVertices.Count;
            BVHVertices.AddRange(mf.sharedMesh.vertices);

            // Create BVHTriangles from mesh
            List<BVHTriangle> triangles = BVHAccelerator.MeshToTriangles(mf.sharedMesh, vertexOffset);
            int triangleRecursiveConut = 0;
            meshBVHNodesDictionary.Add(mf.GetInstanceID(), BVHAccelerator.MeshTriangleRecursiveBuild(triangles, ref triangleRecursiveConut));
            int depth = (int)Mathf.Log(triangleRecursiveConut, 2) + 1;
        }
    }

    // Build compute buffers for BVH
    public void BuildBVHBuffers()
    {
        // Clear all lists
        BVHMeshObjects.Clear();
        BVHNodeList.Clear();
        BVHLeafCount = 0;

        // Build BVH
        int meshRecursiveCount = 0;
        BVHNode root = BVHAccelerator.MeshRecursiveBuild(new List<MeshFilter>(GetComponentsInChildren<MeshFilter>()), ref meshRecursiveCount);
        
        // Record leaf node for building mesh BVH
        List<BVHNode> leafs = new List<BVHNode>();
        List<int> leafIndex = new List<int>();
        // Traverse tree and prepare data for compute buffer
        Queue<BVHNode> queue = new Queue<BVHNode>();
        queue.Enqueue(root);
        int nodeIndex = 0;
        while (queue.Count > 0)
        {
            BVHNode current = queue.Dequeue();
            BVHListNode listNode = new BVHListNode()
            {
                bmin   = current.bounds.min,
                bmax   = current.bounds.max,
                left   = current.meshFilterID == -1 ? ++nodeIndex : -1,
                right  = current.meshFilterID == -1 ? ++nodeIndex : -1,
                isLeaf = -1,
                extra  = -1
            };
            BVHNodeList.Add(listNode);
            if (current.meshFilterID == -1)
            {
                queue.Enqueue(current.left);
                queue.Enqueue(current.right);
            }
            else
            {
                leafs.Add(current);
                leafIndex.Add(BVHNodeList.Count-1);
            }
        }

        // Build mesh BVH for each leaf node
        for (int i = 0; i < leafs.Count; i++)
        {
            // Create BVHMeshObject
            Material mat = leafs[i].meshFilter.transform.GetComponent<MeshRenderer>().sharedMaterial;
            float metallic = mat.GetFloat("_Metallic");
            int meshObjectIndex = BVHMeshObjects.Count;
            BVHMeshObjects.Add(new BVHMeshObject()
            {
                localToWorldMatrix = leafs[i].meshFilter.transform.localToWorldMatrix,
                albedo = mat.GetColor("_Color"),
                specular = new Vector3(1.0f, 0.78f, 0.34f),
                // specular = new Vector3(1.0f, 1.0f, 1.0f),
            });

            // Traverse mesh BVH
            BVHNode triNode = meshBVHNodesDictionary[leafs[i].meshFilterID];
            Queue<BVHNode> triQueue = new Queue<BVHNode>();
            // Update corresponding ListNode left and right
            BVHNodeList[leafIndex[i]] = new BVHListNode()
            {
                bmin   = leafs[i].bounds.min,
                bmax   = leafs[i].bounds.max,
                left   = ++nodeIndex,
                right  = ++nodeIndex,
                isLeaf = -1,
                extra  = -1
            };
            triQueue.Enqueue(triNode.left);
            triQueue.Enqueue(triNode.right);
            while (triQueue.Count > 0)
            {
                BVHNode triCurrent = triQueue.Dequeue();
                bool triLeaf = triCurrent.triangle != null;
                BVHListNode listNode = new BVHListNode()
                {
                    bmin   = triCurrent.bounds.min,
                    bmax   = triCurrent.bounds.max,
                    left   = triLeaf ? triCurrent.triangle.i0 : ++nodeIndex,
                    right  = triLeaf ? triCurrent.triangle.i1 : ++nodeIndex,
                    isLeaf = triLeaf ? triCurrent.triangle.i2 : -1,
                    extra  = meshObjectIndex
                };
                BVHNodeList.Add(listNode);
                if (triLeaf) BVHLeafCount++;
                else
                {
                    triQueue.Enqueue(triCurrent.left);
                    triQueue.Enqueue(triCurrent.right);
                }
            }
        }

        // Create compute buffer
        CreateComputeBuffer(ref BVHMeshObjectBuffer, BVHMeshObjects, 92);
        CreateComputeBuffer(ref BVHNodeListBuffer, BVHNodeList, 40);
        CreateComputeBuffer(ref BVHVerticesBuffer, BVHVertices, 12);
    }

    // Create the corresponding compute buffer
    private static void CreateComputeBuffer<T>(ref ComputeBuffer buffer, List<T> data, int stride) where T : struct
    {
        // Do we already have a compute buffer?
        if (buffer != null)
        {
            // If no data or buffer doesn't match the given criteria, release it
            if (data.Count == 0 || buffer.count != data.Count || buffer.stride != stride)
            {
                buffer.Release();
                buffer = null;
            }
        }
        if (data.Count != 0)
        {
            // If the buffer has been released or wasn't there to
            // begin with, create it
            if (buffer == null)
            {
                buffer = new ComputeBuffer(data.Count, stride);
            }
            // Set data on the buffer
            buffer.SetData(data);
        }
    }
    
    /* -------------------- Temporary/Test Functions -------------------- */
    // Build BVH and return root node from all current meshes
    public void TestBuildBVH() 
    {
        // Calculate runtime
        float begin = Time.realtimeSinceStartup;
        // Build BVH
        BuildMeshBVHNodes();
        Debug.Log("Runtime1: " + (Time.realtimeSinceStartup - begin));
        BuildBVHBuffers();
        Debug.Log("Runtime2: " + (Time.realtimeSinceStartup - begin));
        Debug.Log("BVHMeshObjects size: " + BVHMeshObjects.Count);
        Debug.Log("BVHNodeList size: " + BVHNodeList.Count);
        Debug.Log("BVHVertices size: " + BVHVertices.Count);

        ClearData();
    }

    // Temporary test function
    public void Test()
    {
        // Build BVH
        BuildMeshBVHNodes();
        BuildBVHBuffers();

        for (int i = 0; i < BVHNodeList.Count; i++)
        {
            if (BVHNodeList[i].isLeaf == -1)
                Debug.Log("Node #" + i + ": left->" + BVHNodeList[i].left + ", right->" + BVHNodeList[i].right);
        }

        ClearData();
    }

    public void TestRay()
    {
        // Create ray from camera
        Matrix4x4 cameraToWorldMatrix = GetComponent<Camera>().cameraToWorldMatrix;
        Matrix4x4 cameraInverseProjection = GetComponent<Camera>().projectionMatrix.inverse;
        Vector3 origin = cameraToWorldMatrix.MultiplyPoint(Vector3.zero);
        Vector3 direction = cameraInverseProjection.MultiplyPoint(new Vector3(cameraRayDirectionX, cameraRayDirectionY, 0.0f));
        direction = cameraToWorldMatrix.MultiplyVector(direction).normalized;

        Vector3 dirInv = new Vector3(1.0f / direction.x, 1.0f / direction.y, 1.0f / direction.z);
        bool[] dirIsNeg = new bool[]{ direction.x < 0, direction.y < 0, direction.z < 0 };

        Vector3 bmin = new Vector3(-0.15f, 0.0f, -0.5f);
        Vector3 bmax = new Vector3(-0.0f, 2.0f, -0.44f);
        // (-0.50, 0.50, -0.50), (0.50, 0.50, 0.50)
        //IntersectBBox
        bool result = IntersectBBox(origin, dirInv, dirIsNeg, bmin, bmax);
        Debug.Log("DEBUG: origin -> " + origin);
        Debug.Log("DEBUG: direction -> " + direction);
        Debug.Log("DEBUG: result -> " + result);
    }

    private void MySwap(ref float a, ref float b)
    {
        float temp = a;
        a = b;
        b = temp;
    }

    private bool IntersectBBox(Vector3 origin, Vector3 dirInv, bool[] dirIsNeg, Vector3 bmin, Vector3 bmax)
    {
        // x slab
        float xmin = (bmin.x - origin.x) * dirInv.x;
        float xmax = (bmax.x - origin.x) * dirInv.x;
        if (dirIsNeg[0]) MySwap(ref xmin, ref xmax);

        // y slab
        float ymin = (bmin.y - origin.y) * dirInv.y;
        float ymax = (bmax.y - origin.y) * dirInv.y;
        if (dirIsNeg[1]) MySwap(ref ymin, ref ymax);

        // z slab
        float zmin = (bmin.z - origin.z) * dirInv.z;
        float zmax = (bmax.z - origin.z) * dirInv.z;
        if (dirIsNeg[2]) MySwap(ref zmin, ref zmax);

        float t_enter = Mathf.Max(zmin, Mathf.Max(xmin, ymin));
        float t_exit  = Mathf.Min(zmax, Mathf.Min(xmax, ymax));
        return t_exit >= 0 && t_enter <= t_exit;
    }
}
