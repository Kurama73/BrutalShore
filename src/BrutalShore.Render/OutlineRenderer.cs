using BrutalShore.Core;
using BrutalShore.Gpu;

namespace BrutalShore.Render;

/// <summary>Draws the twelve edges of a block, to show which one is being aimed at.</summary>
/// <remarks>
/// <para>
/// Small, and the difference between breaking a block and guessing. A crosshair says where the
/// middle of the screen is; it says nothing about which of the two blocks under it the next click
/// will take, and at a shallow angle those are rarely the same block.
/// </para>
/// <para>
/// Twenty-four vertices, uploaded once and never touched again: the box is always a unit cube,
/// and where it goes is a push constant. That is what makes this cost one draw call of twelve
/// lines rather than a buffer rewritten every time the player turns their head.
/// </para>
/// </remarks>
public sealed unsafe class OutlineRenderer : IDisposable
{
    /// <summary>How far the box is grown about its middle, in metres.</summary>
    /// <remarks>
    /// Two millimetres against a block a metre across. Enough that the lines are outside the
    /// surface they trace rather than in it, where the depth test would keep whichever of the two
    /// happened to be nearer and the outline would come out dashed.
    /// </remarks>
    private const float GrowthMeters = 0.004f;

    private static readonly float3 LineColor = new(0.05f, 0.05f, 0.06f);

    private readonly GpuContext _context;
    private readonly GpuRenderer _renderer;
    private readonly SceneBindings _scene;
    private readonly GpuBuffer _edges;
    private readonly VkPipeline _pipeline;
    private readonly VkPipelineLayout _layout;

    /// <summary>Builds the pipeline and uploads the one box it ever draws.</summary>
    /// <param name="context">The device the pipeline belongs to.</param>
    /// <param name="renderer">The renderer whose frame the outline is recorded into.</param>
    /// <param name="shaders">Where <c>outline.vert</c> and <c>outline.frag</c> are compiled from.</param>
    /// <param name="scene">The per-frame uniform block the shaders read.</param>
    /// <param name="renderPass">The render pass the outline is drawn in.</param>
    public OutlineRenderer(
        GpuContext context,
        GpuRenderer renderer,
        ShaderLibrary shaders,
        SceneBindings scene,
        VkRenderPass renderPass
    )
    {
        _context = context;
        _renderer = renderer;
        _scene = scene;
        _edges = GpuBuffer.Create(context, UnitCubeEdges(), VkBufferUsageFlags.VertexBufferBit);

        var pushConstantRange = new VkPushConstantRange
        {
            StageFlags = VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit,
            Offset = ByteSize.Zero,
            Size = ByteSize.Of<OutlinePushConstants>(),
        };

        var setLayout = scene.Layout;
        _layout = context.Device.CreatePipelineLayout([setLayout], [pushConstantRange], null);

        Span<VkPipelineShaderStageCreateInfo> stages =
        [
            Presets.ShaderStage.Vertex(shaders.Load("outline.vert")),
            Presets.ShaderStage.Fragment(shaders.Load("outline.frag")),
        ];

        var binding = new VkVertexInputBindingDescription
        {
            Binding = 0,
            Stride = ByteSize.Of<float3>(),
            InputRate = VkVertexInputRate.Vertex,
        };

        var attribute = new VkVertexInputAttributeDescription
        {
            Location = 0,
            Binding = 0,
            Format = VkFormat.R32G32B32SFloat,
            Offset = ByteSize.Zero,
        };

        var vertexInput = new VkPipelineVertexInputStateCreateInfo
        {
            VertexBindingDescriptionCount = 1,
            VertexBindingDescriptions = &binding,
            VertexAttributeDescriptionCount = 1,
            VertexAttributeDescriptions = &attribute,
        };

        var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { Topology = VkPrimitiveTopology.LineList };
        var viewportState = new VkPipelineViewportStateCreateInfo { ViewportCount = 1, ScissorCount = 1 };

        // A line width of one is the only one every device supports without a feature being asked
        // for; anything thicker would have to be geometry.
        var rasterization = new VkPipelineRasterizationStateCreateInfo
        {
            PolygonMode = VkPolygonMode.Fill,
            CullMode = VkCullModeFlags.None,
            FrontFace = VkFrontFace.CounterClockwise,
            LineWidth = 1f,
        };

        var multisample = new VkPipelineMultisampleStateCreateInfo { RasterizationSamples = VkSampleCountFlags._1Bit };

        // Tested but not written: the outline is a hint over the world, and a block behind the
        // one being aimed at should still hide it.
        var depthStencil = new VkPipelineDepthStencilStateCreateInfo
        {
            DepthTestEnable = true,
            DepthWriteEnable = false,
            DepthCompareOp = VkCompareOp.LessOrEqual,
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

        Span<VkDynamicState> dynamicStates = [VkDynamicState.Viewport, VkDynamicState.Scissor];
        var dynamicState = new VkPipelineDynamicStateCreateInfo
        {
            DynamicStateCount = dynamicStates.Length,
            DynamicStates = dynamicStates.AsPointer(),
        };

        _pipeline = context.Device.CreateGraphicsPipeline(
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
                Layout = _layout,
                RenderPass = renderPass,
                Subpass = 0,
            },
            null
        );
    }

    /// <summary>Outlines one block.</summary>
    /// <param name="blockCoordinate">Which block, in world coordinates.</param>
    /// <param name="cameraMeters">Where the camera is; the render-space origin.</param>
    public void Draw(int3 blockCoordinate, double3 cameraMeters)
    {
        var corner = RenderSpace.ToRenderSpace(
            new double3(blockCoordinate.X, blockCoordinate.Y, blockCoordinate.Z),
            cameraMeters,
            1.0
        );

        var commands = _renderer.Commands;
        commands.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
        _scene.Bind(commands, _layout, _renderer.FrameIndex);
        commands.BindVertexBuffer(0, _edges.Handle);
        commands.PushConstants(
            _layout,
            VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit,
            ByteSize.Zero,
            new OutlinePushConstants(corner, LineColor, GrowthMeters)
        );
        commands.Draw(_edges.Count, 1, 0, 0);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Device.DestroyPipeline(_pipeline, null);
        _context.Device.DestroyPipelineLayout(_layout, null);
        _edges.Dispose();
    }

    /// <summary>The twelve edges of the unit cube, as twenty-four corners.</summary>
    private static float3[] UnitCubeEdges()
    {
        var corners = new float3[8];
        for (var index = 0; index < 8; index++)
            corners[index] = new float3(index & 1, (index >> 1) & 1, (index >> 2) & 1);

        // Two corners are joined by an edge when their indices differ in exactly one bit, which
        // is the same as saying they differ along exactly one axis.
        var edges = new List<float3>(24);
        for (var from = 0; from < 8; from++)
        for (var axis = 0; axis < 3; axis++)
        {
            var to = from | (1 << axis);
            if (to == from)
                continue;

            edges.Add(corners[from]);
            edges.Add(corners[to]);
        }

        return [.. edges];
    }
}
