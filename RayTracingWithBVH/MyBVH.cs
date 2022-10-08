using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MyBVH
{
    public class BVHTriangle
    {
        // Vertices
        public Vector3 v0;
        public Vector3 v1;
        public Vector3 v2;
        // Vertex start index
        public int i0;
        public int i1;
        public int i2;
        // Bounding box
        public Bounds bounds;

        // Constructor
        public BVHTriangle(Vector3 v0, Vector3 v1, Vector3 v2, int i0, int i1, int i2)
        {
            this.v0 = v0;
            this.v1 = v1;
            this.v2 = v2;
            this.i0 = i0;
            this.i1 = i1;
            this.i2 = i2;
            Vector3 max = new Vector3(
                Mathf.Max(Mathf.Max(v0.x, v1.x), v2.x),
                Mathf.Max(Mathf.Max(v0.y, v1.y), v2.y),
                Mathf.Max(Mathf.Max(v0.z, v1.z), v2.z)
            );
            Vector3 min = new Vector3(
                Mathf.Min(Mathf.Min(v0.x, v1.x), v2.x),
                Mathf.Min(Mathf.Min(v0.y, v1.y), v2.y),
                Mathf.Min(Mathf.Min(v0.z, v1.z), v2.z)
            );
            bounds = new Bounds((max + min) / 2, max - min);
        }

        public override string ToString()
        {
            return v0.ToString() + ", " + v1.ToString() + ", " + v2.ToString();
        }
    }

    public class BVHNode
    {
        public Bounds bounds;
        public BVHNode left;
        public BVHNode right;
        public BVHTriangle triangle;
        public MeshFilter meshFilter;
        public int meshFilterID;

        public BVHNode()
        {
            this.bounds = new Bounds();
            this.meshFilterID = -1;
        }

        public BVHNode(Bounds bounds)
        {
            this.bounds = bounds;
            this.meshFilterID = -1;
        }
    }

    public class BVHAccelerator
    {
        public static BVHNode MeshRecursiveBuild(List<MeshFilter> meshFilters, ref int meshRecursiveCount)
        {
            meshRecursiveCount++;

            // Compute bounds of all meshes and assign bounds to node
            Bounds bounds = meshFilters[0].GetComponent<MeshRenderer>().bounds;
            Bounds centroidBounds = new Bounds(bounds.center, Vector3.zero);
            for (int i = 1; i < meshFilters.Count; i++)
            {
                Bounds temp = meshFilters[i].GetComponent<MeshRenderer>().bounds;
                bounds.Encapsulate(temp);
                centroidBounds.Encapsulate(temp.center);
            }

            if (meshFilters.Count == 1)
            {
                // If only one left, build mesh to triangles and create BVH.
                // List<BVHTriangle> triangles = MeshToTriangles(meshFilters[0].sharedMesh, meshFilters[0].transform.localToWorldMatrix);
                // BVHNode leaf = MeshTriangleRecursiveBuild(triangles, ref triangleRecursiveCount);
                BVHNode leaf = new BVHNode(bounds);
                leaf.meshFilterID = meshFilters[0].GetInstanceID();
                leaf.meshFilter = meshFilters[0];
                return leaf;
            }
            
            BVHNode node = new BVHNode(bounds);
            if (meshFilters.Count == 2)
            {
                // If only two left, recursively build left and right node.
                node.left = MeshRecursiveBuild(meshFilters.GetRange(0, 1), ref meshRecursiveCount);
                node.right = MeshRecursiveBuild(meshFilters.GetRange(1, 1), ref meshRecursiveCount);
            }
            else
            {
                // More than two left.
                // Sort along the longest axis of the bounding box based on each bounding box center
                if (centroidBounds.size.x > centroidBounds.size.y && centroidBounds.size.x > centroidBounds.size.z)
                {
                    // Along x axis
                    meshFilters.Sort((mf1, mf2) => {
                        return mf1.GetComponent<MeshRenderer>().bounds.center.x <
                               mf2.GetComponent<MeshRenderer>().bounds.center.x ? -1 : 1;
                    });
                }
                else if (centroidBounds.size.y > centroidBounds.size.z)
                {
                    // Along y axis
                    meshFilters.Sort((mf1, mf2) => {
                        return mf1.GetComponent<MeshRenderer>().bounds.center.y <
                               mf2.GetComponent<MeshRenderer>().bounds.center.y ? -1 : 1;
                    });
                }
                else
                {
                    // Along z axis
                    meshFilters.Sort((mf1, mf2) => {
                        return mf1.GetComponent<MeshRenderer>().bounds.center.z <
                               mf2.GetComponent<MeshRenderer>().bounds.center.z ? -1 : 1;
                    });
                }

                // Evenly seperate and recursively build left and right node
                int mid = meshFilters.Count / 2;
                List<MeshFilter> leftMeshFilters = meshFilters.GetRange(0, mid);
                List<MeshFilter> rightMeshFilters = meshFilters.GetRange(mid, meshFilters.Count - mid);
                node.left = MeshRecursiveBuild(leftMeshFilters, ref meshRecursiveCount);
                node.right = MeshRecursiveBuild(rightMeshFilters, ref meshRecursiveCount);
            }

            return node;
        }

        public static List<BVHTriangle> MeshToTriangles(Mesh mesh, int vertexOffset = 0)
        {
            List<BVHTriangle> result = new List<BVHTriangle>();
            
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.GetIndices(0);
            for (int i = 0; i < indices.Length; i += 3)
            {
                // result.Add(new BVHTriangle(
                //     localToWorldMatrix.MultiplyPoint3x4(vertices[indices[i]]),
                //     localToWorldMatrix.MultiplyPoint3x4(vertices[indices[i+1]]),
                //     localToWorldMatrix.MultiplyPoint3x4(vertices[indices[i+2]])
                // ));
                result.Add(new BVHTriangle(
                    vertices[indices[i]],
                    vertices[indices[i+1]],
                    vertices[indices[i+2]],
                    vertexOffset + indices[i],
                    vertexOffset + indices[i+1],
                    vertexOffset + indices[i+2]
                ));
            }

            return result;
        }

        public static BVHNode MeshTriangleRecursiveBuild(List<BVHTriangle> triangles, ref int triangleRecursiveCount)
        {
            triangleRecursiveCount++;

            // Compute bounds of all triangles and assign bounds to node
            Bounds bounds = triangles[0].bounds;
            Bounds centroidBounds = new Bounds(bounds.center, Vector3.zero);
            for (int i = 1; i < triangles.Count; i++)
            {
                Bounds temp = triangles[i].bounds;
                bounds.Encapsulate(temp);
                centroidBounds.Encapsulate(temp.center);
            }

            BVHNode node = new BVHNode(bounds);
            if (triangles.Count == 1)
            {
                // If only one left, set triangle and return.
                node.triangle = triangles[0];
            }
            else if (triangles.Count == 2)
            {
                // If only two left, recursively build left and right node.
                node.left = MeshTriangleRecursiveBuild(triangles.GetRange(0, 1), ref triangleRecursiveCount);
                node.right = MeshTriangleRecursiveBuild(triangles.GetRange(1, 1), ref triangleRecursiveCount);
            }
            else
            {
                // More than two left.
                // Sort along the longest axis of the bounding box based on each bounding box center
                if (centroidBounds.size.x > centroidBounds.size.y && centroidBounds.size.x > centroidBounds.size.z)
                {
                    // Along x axis
                    triangles.Sort((t1, t2) => { return t1.bounds.center.x < t2.bounds.center.x ? -1 : 1; });
                }
                else if (centroidBounds.size.y > centroidBounds.size.z)
                {
                    // Along y axis
                    triangles.Sort((t1, t2) => { return t1.bounds.center.y < t2.bounds.center.y ? -1 : 1; });
                }
                else
                {
                    // Along z axis
                    triangles.Sort((t1, t2) => { return t1.bounds.center.z < t2.bounds.center.z ? -1 : 1; });
                }

                // Evenly seperate and recursively build left and right node
                int mid = triangles.Count / 2;
                List<BVHTriangle> leftTriangles = triangles.GetRange(0, mid);
                List<BVHTriangle> rightTriangles = triangles.GetRange(mid, triangles.Count - mid);
                node.left = MeshTriangleRecursiveBuild(leftTriangles, ref triangleRecursiveCount);
                node.right = MeshTriangleRecursiveBuild(rightTriangles, ref triangleRecursiveCount);
            }

            return node;
        }
    }
}
