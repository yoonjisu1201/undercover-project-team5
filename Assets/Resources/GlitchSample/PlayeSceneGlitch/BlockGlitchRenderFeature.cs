using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace GlitchSample
{
    public sealed class BlockGlitchRenderFeature : ScriptableRendererFeature
    {
        public Shader shader;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        private BlockGlitchRenderPass _pass;

        public override void Create()
        {
            _pass = new BlockGlitchRenderPass(shader)
            {
                renderPassEvent = renderPassEvent
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (shader == null)
            {
                Debug.LogWarning($"{name} shader is null and will be skipped.");
                return;
            }

            if (renderingData.cameraData.cameraType == CameraType.Game)
            {
                renderer.EnqueuePass(_pass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
        }
    }

    internal sealed class BlockGlitchRenderPass : ScriptableRenderPass
    {
        private const string PassName = "Block Glitch Render Pass";

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int BlockDensityId = Shader.PropertyToID("_BlockDensity");
        private static readonly int BarDensityId = Shader.PropertyToID("_BarDensity");
        private static readonly int MaxOffsetId = Shader.PropertyToID("_MaxOffset");
        private static readonly int BarOpacityId = Shader.PropertyToID("_BarOpacity");
        private static readonly int SpeedId = Shader.PropertyToID("_Speed");

        private static readonly Vector4 ScaleBias = new(1f, 1f, 0f, 0f);

        private readonly Material _material;

        public BlockGlitchRenderPass(Shader shader)
        {
            if (shader != null)
            {
                _material = CoreUtils.CreateEngineMaterial(shader);
            }

            requiresIntermediateTexture = true;
        }

        private sealed class PassData
        {
            internal TextureHandle source;
            internal Material material;
        }

        private static void ExecutePass(PassData data, RasterGraphContext context)
        {
            Blitter.BlitTexture(context.cmd, data.source, ScaleBias, data.material, 0);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null)
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            if (!cameraData.postProcessEnabled || cameraData.isSceneViewCamera || resourceData.isActiveTargetBackBuffer)
            {
                return;
            }

            UndercoverBlockGlitchVolume volume = VolumeManager.instance.stack.GetComponent<UndercoverBlockGlitchVolume>();
            if (volume == null || !volume.active || volume.intensity.value <= 0f)
            {
                return;
            }

            _material.SetFloat(IntensityId, volume.intensity.value);
            _material.SetFloat(BlockDensityId, volume.blockDensity.value);
            _material.SetFloat(BarDensityId, volume.barDensity.value);
            _material.SetFloat(MaxOffsetId, volume.maxOffset.value);
            _material.SetFloat(BarOpacityId, volume.barOpacity.value);
            _material.SetFloat(SpeedId, volume.speed.value);

            TextureHandle source = resourceData.activeColorTexture;
            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = $"CameraColor-{PassName}";
            destinationDesc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(PassName, out PassData passData, profilingSampler))
            {
                passData.source = source;
                passData.material = _material;
                builder.UseTexture(source);
                builder.SetRenderAttachment(destination, 0);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
            }

            resourceData.cameraColor = destination;
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_material);
        }
    }
}
