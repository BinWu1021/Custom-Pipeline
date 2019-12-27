using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class MyPipeline : RenderPipeline
{
    public MyPipeline(bool dynamicBatching, bool instancing, int shadowMapSize)
    {
        GraphicsSettings.lightsUseLinearIntensity = true;
        this.enableDynamicBatching = dynamicBatching;
        this.enableGPUInstancing = instancing;
        this.shadowMapSize = shadowMapSize;
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
    int shadowMapSize;

    CullResults cull;
    CommandBuffer cameraBuffer = new CommandBuffer() { name = "Render Camera" };
    CommandBuffer shadowBuffer = new CommandBuffer() { name = "Render Shadow" };
    Material errorMaterial;

    RenderTexture shadowMap = null;

    // Define Light Variables
    const int kMaxVisibleLights = 16;

    static int visibleLightColorsID = Shader.PropertyToID("_VisibleLightColors");
    static int visibleLightDirectionsOrPositionsID = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
    static int visibleLightAttenuationsID = Shader.PropertyToID("_VisibleLightAttenuations");
    static int visibleSpotLightDirectionsID = Shader.PropertyToID("_VisibleSpotLightDirections");
    static int visibleLightIndicesOffsetAndCountID = Shader.PropertyToID("unity_LightIndicesOffsetAndCount");
    static int shadowMapID = Shader.PropertyToID("_ShadowMap");
    static int worldToShadowMatrixID = Shader.PropertyToID("_WorldToShadowMatrix");
    static int shadowBiasID = Shader.PropertyToID("_ShadowBias");
    static int shadowStrengthID = Shader.PropertyToID("_ShadowStrength");

    Vector4[] visibleLightColors = new Vector4[kMaxVisibleLights];
    Vector4[] visibleLightDirectionsOrPositions = new Vector4[kMaxVisibleLights];
    Vector4[] visibleLightAttenuations = new Vector4[kMaxVisibleLights];
    Vector4[] visibleSpotLightDirections = new Vector4[kMaxVisibleLights];

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

        // Render Shadow Map
        RenderShadow(context);

        context.SetupCameraProperties(camera);

        // Clear Target
        cameraBuffer.ClearRenderTarget(true, false, Color.clear);

        // Configure Lights 
        if (cull.visibleLights.Count > 0)
        {
            ConfigureLights();
        }
        else
        {
            cameraBuffer.SetGlobalVector(visibleLightIndicesOffsetAndCountID, Vector4.zero);
        }

        cameraBuffer.BeginSample("Render Camera");
        // Set Lights array
        cameraBuffer.SetGlobalVectorArray(visibleLightColorsID, visibleLightColors);
        cameraBuffer.SetGlobalVectorArray(visibleLightDirectionsOrPositionsID, visibleLightDirectionsOrPositions);
        cameraBuffer.SetGlobalVectorArray(visibleLightAttenuationsID, visibleLightAttenuations);
        cameraBuffer.SetGlobalVectorArray(visibleSpotLightDirectionsID, visibleSpotLightDirections);
        context.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();

        // Draw Object
        var drawSettings = new DrawRendererSettings(camera, new ShaderPassName("SRPDefaultUnlit"));
        if (cull.visibleLights.Count > 0)
        {
            drawSettings.rendererConfiguration = RendererConfiguration.PerObjectLightIndices8;
        }
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

        if (shadowMap != null)
        {
            RenderTexture.ReleaseTemporary(shadowMap);
            shadowMap = null;
        }
    }

    void RenderShadow(ScriptableRenderContext context)
    {
        shadowMap = RenderTexture.GetTemporary(this.shadowMapSize, this.shadowMapSize, 16, RenderTextureFormat.Shadowmap);
        shadowMap.filterMode = FilterMode.Bilinear;
        shadowMap.wrapMode = TextureWrapMode.Clamp;

        CoreUtils.SetRenderTarget(shadowBuffer, shadowMap,
                                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, ClearFlag.Depth);
        shadowBuffer.BeginSample("Render Shadow");

        // Set View matrix and projection matrix
        Matrix4x4 viewMatrix, projMatrix;
        ShadowSplitData shadowSplitData;
        this.cull.ComputeSpotShadowMatricesAndCullingPrimitives(0, out viewMatrix, out projMatrix, out shadowSplitData);
        shadowBuffer.SetViewProjectionMatrices(viewMatrix, projMatrix);
        shadowBuffer.SetGlobalFloat(shadowBiasID, cull.visibleLights[0].light.shadowBias);

        context.ExecuteCommandBuffer(shadowBuffer);
        shadowBuffer.Clear();

        // Draw Shadow Casters
        var shadowSetting = new DrawShadowsSettings(cull, 0);
        context.DrawShadows(ref shadowSetting);

        // Set Shadow map and shadow space matrix
        if (SystemInfo.usesReversedZBuffer)
        {
            projMatrix.m20 = -projMatrix.m20;
			projMatrix.m21 = -projMatrix.m21;
			projMatrix.m22 = -projMatrix.m22;
			projMatrix.m23 = -projMatrix.m23;
        }

        Matrix4x4 scaleOffset = Matrix4x4.identity;
        scaleOffset.m00 = scaleOffset.m11 = scaleOffset.m22 = 0.5f;
        scaleOffset.m03 = scaleOffset.m13 = scaleOffset.m23 = 0.5f;
        Matrix4x4 worldToShadowMatrix = scaleOffset * projMatrix * viewMatrix;
        shadowBuffer.SetGlobalMatrix(worldToShadowMatrixID, worldToShadowMatrix);
        shadowBuffer.SetGlobalTexture(shadowMapID, shadowMap);
        shadowBuffer.SetGlobalFloat(shadowStrengthID, cull.visibleLights[0].light.shadowStrength);


        shadowBuffer.EndSample("Render Shadow");
        context.ExecuteCommandBuffer(shadowBuffer);
        shadowBuffer.Clear();
    }

    void ConfigureLights()
    {
        for (int i = 0; i < cull.visibleLights.Count; i++)
        {
            var visibleLight = this.cull.visibleLights[i];
            visibleLightColors[i] = visibleLight.finalColor;

            Vector4 attenuation = Vector4.zero;
            attenuation.w = 1;

            if (visibleLight.lightType == LightType.Directional)
            {
                var lightDir = visibleLight.localToWorld.GetColumn(2);
                lightDir.x = -lightDir.x;
                lightDir.y = -lightDir.y;
                lightDir.z = -lightDir.z;
                visibleLightDirectionsOrPositions[i] = lightDir;
            }
            else
            {
                visibleLightDirectionsOrPositions[i] = visibleLight.localToWorld.GetColumn(3);
                // Cacluate Range Attenuation
                attenuation.x = 1 / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);

                if (visibleLight.lightType == LightType.Spot)
                {
                    var lightDir = visibleLight.localToWorld.GetColumn(2);
                    lightDir.x = -lightDir.x;
                    lightDir.y = -lightDir.y;
                    lightDir.z = -lightDir.z;
                    visibleSpotLightDirections[i] = lightDir;

                    // ---------------------------------------------------------------------------------------------------
                    //
                    // tan(ri) = 46/64 * tan(ro)
                    // ri : half the inner spot angles in radians.
                    // ro : half the outer spot angles in radians.
                    // so cos(ri) = cos(arctan(46/64 * tan(ro))) ;
                    // The angle-based falloff is defined (Ds * Dl - cos(r0)) / (cos(ri) - cos(ro)). 
                    // Ds * Dl : dot product of the spot direction and light direction.
                    // So the expression can be simplified to (Ds * Dl)a + b, a = 1 / (cos(ri) - cos(ro)), b = -cos(ro)a;
                    //
                    // ---------------------------------------------------------------------------------------------------

                    float outerRad = Mathf.Deg2Rad * 0.5f * visibleLight.spotAngle;
                    float outerCos = Mathf.Cos(outerRad);
                    float outerTan = Mathf.Tan(outerRad);
                    float innerCos = Mathf.Cos(Mathf.Atan((46f / 64f) * outerTan));

                    attenuation.z = 1 / Mathf.Max((innerCos - outerCos), 0.001f);
                    attenuation.w = -outerCos * attenuation.z;

                }
            }

            visibleLightAttenuations[i] = attenuation;
        }

        if (cull.visibleLights.Count > kMaxVisibleLights)
        {
            int[] lightIndices = cull.GetLightIndexMap();
            for (int i = kMaxVisibleLights; i < lightIndices.Length; i++)
            {
                lightIndices[i] = -1;
            }
            cull.SetLightIndexMap(lightIndices);
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
