using BrutalShore.Gpu;
using BrutalShore.Voxel;

namespace BrutalShore.Render;

/// <summary>The depth-only pipeline that fills the sun's shadow map with voxel chunks.</summary>
/// <remarks>
/// <para>
/// The same vertex buffers as <see cref="VoxelPipeline" />, read through a shader that keeps only
/// the position: the map stores how far the light got, and a block's colour has no bearing on
/// that. There is no fragment stage at all, which is allowed because the pass has no colour
/// attachment to write.
/// </para>
/// <para>
/// Nothing is culled. Drawing a block's far side as well as its near one costs twice the
/// triangles and changes no result — the depth test keeps whichever is nearer the light either
/// way — and it buys not having to reason about which way round a face appears once the
/// projection is the light's rather than the camera's.
/// </para>
/// </remarks>
public sealed unsafe class ShadowPipeline : IDisposable
{
    /// <summary>A flat offset applied to every depth written, in depth-buffer units.</summary>
    /// <remarks>
    /// Small, because the normal offset the fragment shader applies does most of the work. This
    /// is here for the case the normal offset cannot help with: a face almost edge-on to the
    /// light, where one texel of the map spans a long way along the surface.
    /// </remarks>
    private const float DepthBiasConstant = 1.0f;

    /// <summary>How much the offset grows with the surface's slope relative to the light.</summary>
    private const float DepthBiasSlope = 1.75f;

    private readonly GpuContext _context;

    /// <summary>Compiles the pipeline for a depth target's render pass.</summary>
    /// <param name="context">The device the pipeline belongs to.</param>
    /// <param name="shaders">Where <c>shadow.vert</c> is compiled from.</param>
    /// <param name="scene">The per-frame uniform block the shader reads the light matrix from.</param>
    /// <param name="renderPass">The depth target's render pass.</param>
    public ShadowPipeline(GpuContext context, ShaderLibrary shaders, SceneBindings scene, VkRenderPass renderPass)
    {
        _context = context;

        var pushConstantRange = new VkPushConstantRange
        {
            StageFlags = VkShaderStageFlags.VertexBit,
            Offset = ByteSize.Zero,
            Size = ByteSize.Of<VoxelPushConstants>(),
        };

        var setLayout = scene.Layout;
        Layout = context.Device.CreatePipelineLayout([setLayout], [pushConstantRange], null);

        Span<VkPipelineShaderStageCreateInfo> stages = [Presets.ShaderStage.Vertex(shaders.Load("shadow.vert"))];

        var binding = new VkVertexInputBindingDescription
        {
            Binding = 0,
            Stride = ByteSize.Of<VoxelVertex>(),
            InputRate = VkVertexInputRate.Vertex,
        };

        Span<VkVertexInputAttributeDescription> attributes =
        [
            new()
            {
                Location = 0,
                Binding = 0,
                Format = VkFormat.R32UInt,
                Offset = ByteSize.OffsetOf<VoxelVertex>(nameof(VoxelVertex.Packed0)),
            },
        ];

        var vertexInput = new VkPipelineVertexInputStateCreateInfo
        {
            VertexBindingDescriptionCount = 1,
            VertexBindingDescriptions = &binding,
            VertexAttributeDescriptionCount = attributes.Length,
            VertexAttributeDescriptions = attributes.AsPointer(),
        };

        var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { Topology = VkPrimitiveTopology.TriangleList };
        var viewportState = new VkPipelineViewportStateCreateInfo { ViewportCount = 1, ScissorCount = 1 };

        var rasterization = new VkPipelineRasterizationStateCreateInfo
        {
            PolygonMode = VkPolygonMode.Fill,
            CullMode = VkCullModeFlags.None,
            FrontFace = VkFrontFace.CounterClockwise,
            DepthBiasEnable = true,
            DepthBiasConstantFactor = DepthBiasConstant,
            DepthBiasSlopeFactor = DepthBiasSlope,
            LineWidth = 1f,
        };

        var multisample = new VkPipelineMultisampleStateCreateInfo { RasterizationSamples = VkSampleCountFlags._1Bit };

        var depthStencil = new VkPipelineDepthStencilStateCreateInfo
        {
            DepthTestEnable = true,
            DepthWriteEnable = true,
            DepthCompareOp = VkCompareOp.Less,
            MinDepthBounds = 0f,
            MaxDepthBounds = 1f,
        };

        // No colour attachment in this render pass, so no blend state to describe.
        var colorBlend = new VkPipelineColorBlendStateCreateInfo { AttachmentCount = 0 };

        Span<VkDynamicState> dynamicStates = [VkDynamicState.Viewport, VkDynamicState.Scissor];
        var dynamicState = new VkPipelineDynamicStateCreateInfo
        {
            DynamicStateCount = dynamicStates.Length,
            DynamicStates = dynamicStates.AsPointer(),
        };

        Handle = context.Device.CreateGraphicsPipeline(
            default,
            new VkGraphicsPipelineCreateInfo
            {
                StageCount = stages.Length,
                Stages = stages.AsPointer(),
                VertexInputState = &vertexInput,
                InputAssemblyState = &inputAssembly,
                ViewportState = &viewportState,
                RasterizationState = &rasterization,
                MultisampleState = &multisample,
                DepthStencilState = &depthStencil,
                ColorBlendState = &colorBlend,
                DynamicState = &dynamicState,
                Layout = Layout,
                RenderPass = renderPass,
                Subpass = 0,
            },
            null
        );
    }

    /// <summary>The pipeline to bind.</summary>
    public VkPipeline Handle { get; }

    /// <summary>The layout its push constants and descriptor set are bound through.</summary>
    public VkPipelineLayout Layout { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Device.DestroyPipeline(Handle, null);
        _context.Device.DestroyPipelineLayout(Layout, null);
    }
}
