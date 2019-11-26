using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class MyPipeline : RenderPipeline
{
    public MyPipeline(bool dynamicBatching, bool instancing)
    {
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
    CommandBuffer clearBuffer = new CommandBuffer() { name = "Render Camera"} ;
    Material errorMaterial;
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
        clearBuffer.ClearRenderTarget(true, false, Color.clear);
        clearBuffer.BeginSample("Render Camera");
        context.ExecuteCommandBuffer(clearBuffer);
        clearBuffer.Clear();

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

        clearBuffer.EndSample("Render Camera");
		context.ExecuteCommandBuffer(clearBuffer);
		clearBuffer.Clear();

        context.Submit();
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
