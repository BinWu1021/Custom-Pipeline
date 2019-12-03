using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class MyPipeline : RenderPipeline
{
    public MyPipeline(bool dynamicBatching, bool instancing)
    {
        GraphicsSettings.lightsUseLinearIntensity = true;
        this.enableDynamicBatching = dynamicBatching;
        this.enableGPUInstancing = instancing;
    }

    public override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
    {
        base.Render(renderContext, cameras);

        foreach (var camera in cameras)
        {
            Render(renderContext, camera);
        }
    }

    bool enableDynamicBatching;
    bool enableGPUInstancing;
    CullResults cull;
    CommandBuffer cameraBuffer = new CommandBuffer() { name = "Render Camera" };
    Material errorMaterial;


    // Define Light Variables
    const int kMaxVisibleLights = 4;

    static int visibleLightColorsID = Shader.PropertyToID("_VisibleLightColors");
    static int visibleLightDirectionsOrPositionsID = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");

    Vector4[] visibleLightColors = new Vector4[kMaxVisibleLights];
    Vector4[] visibleLightDirectionsOrPositions = new Vector4[kMaxVisibleLights];

    void Render(ScriptableRenderContext context, Camera camera)
    {
        // Culling
        ScriptableCullingParameters cullingParameters;

        if (!CullResults.GetCullingParameters(camera, out cullingParameters))
            return;

#if UNITY_EDITOR
        ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        if (camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
#endif

        CullResults.Cull(ref cullingParameters, context, ref cull);

        context.SetupCameraProperties(camera);

        // Clear Target
        cameraBuffer.ClearRenderTarget(true, false, Color.clear);

        // Configer Lights 
        ConfigureLights();

        cameraBuffer.BeginSample("Render Camera");
        // Set Lights array
        cameraBuffer.SetGlobalVectorArray(visibleLightColorsID, visibleLightColors);
        cameraBuffer.SetGlobalVectorArray(visibleLightDirectionsOrPositionsID, visibleLightDirectionsOrPositions);
        context.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();

        // Draw Object
        var drawSettings = new DrawRendererSettings(camera, new ShaderPassName("SRPDefaultUnlit"));
        drawSettings.sorting.flags = SortFlags.CommonOpaque;

        // Enable Dynamic batching
        if (enableDynamicBatching)
            drawSettings.flags = DrawRendererFlags.EnableDynamicBatching;
        if (enableGPUInstancing)
            drawSettings.flags |= DrawRendererFlags.EnableInstancing;

        var filterSettings = new FilterRenderersSettings(true) { renderQueueRange = RenderQueueRange.opaque };

        context.DrawRenderers(cull.visibleRenderers, ref drawSettings, filterSettings);

        DrawDefaultPipeline(context, camera);

        // Draw Skybox
        context.DrawSkybox(camera);

        // Draw Transparent Objects
        drawSettings.sorting.flags = SortFlags.CommonTransparent;
        filterSettings.renderQueueRange = RenderQueueRange.transparent;
        context.DrawRenderers(cull.visibleRenderers, ref drawSettings, filterSettings);

        cameraBuffer.EndSample("Render Camera");
        context.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();

        context.Submit();
    }

    void ConfigureLights()
    {
        for (int i = 0; i < kMaxVisibleLights; i++)
        {
            if (this.cull.visibleLights.Count <= i)
            {
                visibleLightColors[i] = Color.clear;
                continue;
            }

            var visibleLight = this.cull.visibleLights[i];
            visibleLightColors[i] = visibleLight.finalColor;

            if (visibleLight.lightType == LightType.Directional)
            {
                var lightDir = visibleLight.localToWorld.GetColumn(2);
                lightDir.x = -lightDir.x;
                lightDir.y = -lightDir.y;
                lightDir.z = -lightDir.z;
                visibleLightDirectionsOrPositions[i] = lightDir;
            }
            else if (visibleLight.lightType == LightType.Point)
            {
                visibleLightDirectionsOrPositions[i] = visibleLight.localToWorld.GetColumn(3);
            }
        }
    }

    void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
    {
        if (errorMaterial == null)
        {
            Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
            errorMaterial = new Material(errorShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        var drawSettings = new DrawRendererSettings(camera, new ShaderPassName("ForwardBase"));
        drawSettings.SetShaderPassName(1, new ShaderPassName("PrepassBase"));
        drawSettings.SetShaderPassName(2, new ShaderPassName("Always"));
        drawSettings.SetShaderPassName(3, new ShaderPassName("Vertex"));
        drawSettings.SetShaderPassName(4, new ShaderPassName("VertexLMRGBM"));
        drawSettings.SetShaderPassName(5, new ShaderPassName("VertexLM"));
        drawSettings.SetOverrideMaterial(errorMaterial, 0);
        var filterSettings = new FilterRenderersSettings(true);

        context.DrawRenderers(cull.visibleRenderers, ref drawSettings, filterSettings);
    }
}
