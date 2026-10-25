using BrutalShore.Gpu;
using BrutalShore.Voxel;

namespace BrutalShore.Render;

/// <summary>The graphics pipeline that draws greedy-meshed voxel chunks.</summary>
/// <remarks>
/// <para>
/// Opaque geometry only: no blending, depth written and tested, back faces culled. Every quad the
/// mesher emits is a solid block surface, so there is nothing to sort and nothing to see through —
/// which is also why the depth test is <c>Less</c> rather than <c>LessOrEqual</c>; nothing is
/// meant to draw twice at the same depth.
/// </para>
/// <para>
/// The vertex format is two unsigned integers, unpacked in the shader. Describing it to Vulkan as
/// two <c>R32UInt</c> attributes rather than, say, four normalised bytes keeps the bit layout
/// entirely in <see cref="VoxelVertex" /> and the shader, where it is documented, instead of
/// splitting it across a pipeline description as well.
/// </para>
/// </remarks>
public sealed unsafe class VoxelPipeline : IDisposable
{
    private readonly GpuContext _context;

    /// <summary>Compiles the pipeline for one render pass.</summary>
    /// <param name="context">The device the pipeline belongs to.</param>
    /// <param name="shaders">Where <c>voxel.vert</c> and <c>voxel.frag</c> are compiled from.</param>
    /// <param name="scene">The per-frame uniform block the shaders read.</param>
    /// <param name="renderPass">The render pass chunks are drawn in.</param>
    public VoxelPipeline(
        GpuContext context,
        ShaderLibrary shaders,
        SceneBindings scene,
        VkRenderPass renderPass
    )
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

        Span<VkPipelineShaderStageCreateInfo> stages =
        [
            Presets.ShaderStage.Vertex(shaders.Load("voxel.vert")),
            Presets.ShaderStage.Fragment(shaders.Load("voxel.frag")),
        ];

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
            new()
            {
                Location = 1,
                Binding = 0,
                Format = VkFormat.R32UInt,
                Offset = ByteSize.OffsetOf<VoxelVertex>(nameof(VoxelVertex.Packed1)),
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
            CullMode = VkCullModeFlags.BackBit,

            // Counter-clockwise, confirmed against the picture rather than argued from the two
            // conventions involved: the mesher's winding and the camera's negated y both reverse
            // the sense of "front", and getting this backwards hides every outward face and shows
            // the inside of the terrain instead.
            FrontFace = VkFrontFace.CounterClockwise,
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

        var colorAttachment = new VkPipelineColorBlendAttachmentState
        {
            BlendEnable = false,
            ColorWriteMask =
                VkColorComponentFlags.RBit
                | VkColorComponentFlags.GBit
                | VkColorComponentFlags.BBit
                | VkColorComponentFlags.ABit,
        };

        var colorBlend = new VkPipelineColorBlendStateCreateInfo { AttachmentCount = 1, Attachments = &colorAttachment };

        // Viewport and scissor are set every frame, so a resize does not rebuild the pipeline.
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

    /// <summary>The layout the push constants are written through.</summary>
    public VkPipelineLayout Layout { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Device.DestroyPipeline(Handle, null);
        _context.Device.DestroyPipelineLayout(Layout, null);
    }
}
