using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RayTracingPostProcess : MonoBehaviour
{
    /* -------------------- Variables Declaration -------------------- */
    // Compute Shader
    public ComputeShader rayTracingShader;
    // Skybox Texture
    public Texture skyboxTexture;
    // Light Source
    public Light directionalLight;

    // Camera
    private Camera _camera;
    // Target Render Texture
    private RenderTexture target;

    // Mesh Objects
    public bool useBVH = false;
    private bool previousBVH = false;
    public int  rayTracingDepth = 1;
    public static bool meshObjectsNeedRebuilding = true;
    private RayTracingObjectRoot rayTracingObjectRoot;

    // Dummy Buffer for Compute Shader
    private ComputeBuffer dummyBuffer;
    
    /* -------------------- Lifecycle Functions -------------------- */
    private void Awake()
    {
        _camera = GetComponent<Camera>();
        GameObject[] rootGameObejcts = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (GameObject obj in rootGameObejcts)
            if (obj.activeSelf && obj.TryGetComponent<RayTracingObjectRoot>(out rayTracingObjectRoot))
                break;

        // Dummy buffer init
        dummyBuffer = new ComputeBuffer(1, 4);
        previousBVH = useBVH;
    }
    
    private void OnEnable() {}

    private void Update()
    {
        if (useBVH != previousBVH)
        {
            previousBVH = useBVH;
            meshObjectsNeedRebuilding = true;
        }
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        RebuildMeshObjectBuffers();
        SetShaderParameters();
        Render(source, destination);
    }

    private void OnDisable()
    {
        meshObjectsNeedRebuilding = true;
        dummyBuffer.Release();
    }

    /* -------------------- Custom Functions -------------------- */
    // Set Parameters For Compute Shader
    private void SetShaderParameters()
    {
        // Common matrices
        rayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        rayTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);
        // 0 is the kernel index
        rayTracingShader.SetTexture(0, "_SkyboxTexture", skyboxTexture);
        // Light
        Vector3 light = directionalLight.transform.forward;
        rayTracingShader.SetVector("_DirectionalLight", new Vector4(light.x, light.y, light.z, directionalLight.intensity));
    }
    
    // Render From "source" to "destination"
    private void Render(RenderTexture source, RenderTexture destination)
    {
        // Init current render target
        if (target == null || target.width != Screen.width || target.height != Screen.height)
        {
            // Release render texture if we already have one
            if (target != null) target.Release();
            // Get a render target for Ray Tracing
            target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.enableRandomWrite = true;
            target.Create();
        }
        // Set textures
        rayTracingShader.SetTexture(0, "_Source", source);
        rayTracingShader.SetTexture(0, "_Result", target);
        rayTracingShader.SetInt("_RayTracingDepth", rayTracingDepth >= 1 ? rayTracingDepth : 1);
        // Set compute shader thread groups
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        // Dispatch compute shader, 0 is the kernel index, 1 is the threadGroupsZ
        rayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        // Blit the result texture to the screen
        Graphics.Blit(target, destination);
    }

    // Rebuild mesh objects buffer for compute shader to render
    private void RebuildMeshObjectBuffers()
    {
        if (!meshObjectsNeedRebuilding) return;
        meshObjectsNeedRebuilding = false;

        // Get all objects with mesh filters and create object buffers
        if (rayTracingObjectRoot != null)
        {
            if (useBVH)
                rayTracingObjectRoot.BuildBVHBuffers();
            else
                rayTracingObjectRoot.BuildMeshObjectBuffers();

            rayTracingShader.SetBool("_UseBVH", useBVH);
            // Buffers for basic
            SetBufferToShader(0, "_MeshObjects", rayTracingObjectRoot.GetMeshObjectBuffer());
            SetBufferToShader(0, "_Vertices",    rayTracingObjectRoot.GetVertexBuffer());
            SetBufferToShader(0, "_Indices",     rayTracingObjectRoot.GetIndexBuffer());
            rayTracingShader.SetInt("_MeshObjectsCount",  rayTracingObjectRoot.GetMeshObjectCount());

            // Buffers for BVH
            SetBufferToShader(0, "_BVHMeshObjects",  rayTracingObjectRoot.GetBVHMeshObjectBuffer());
            SetBufferToShader(0, "_BVHNodeList",     rayTracingObjectRoot.GetBVHNodeListBuffer());
            SetBufferToShader(0, "_BVHVertices",     rayTracingObjectRoot.GetBVHVerticesBuffer());
            SetBufferToShader(0, "_BVHNormals",      rayTracingObjectRoot.GetBVHNormalsBuffer());
            rayTracingShader.SetInt("_BVHLeafCount", rayTracingObjectRoot.GetBVHLeafCount());
        }
    }

    // Buffer Set Helper
    private void SetBufferToShader(int kernelID, string name, ComputeBuffer buffer)
    {
        rayTracingShader.SetBuffer(kernelID, name, buffer == null ? dummyBuffer : buffer);
    }
}
