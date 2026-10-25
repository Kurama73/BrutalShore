using BrutalShore.Gpu;

namespace BrutalShore.Render;

/// <summary>Draws the sky: the gradient, the sun, the moon and the stars.</summary>
/// <remarks>
/// <para>
/// One class rather than the pipeline-plus-renderer pair the voxels get, because there is no
/// geometry to keep: the sky is three vertices generated from their own index, so everything this
/// draws fits beside the pipeline that draws it.
/// </para>
/// <para>
/// It has to be the first thing drawn in a frame. The pipeline neither tests nor writes depth, so
/// anything drawn before it would be painted over and anything drawn after lands on top, which is
/// exactly the order wanted — and it means the colour the render pass clears to is never seen.
/// </para>
/// </remarks>
public sealed unsafe class SkyRenderer : IDisposable
{
    private readonly GpuContext _context;
    private readonly GpuRenderer _renderer;
    private readonly SceneBindings _scene;
    private readonly VkPipeline _pipeline;
    private readonly VkPipelineLayout _layout;

    /// <summary>Builds the sky pipeline.</summary>
    /// <param name="context">The device the pipeline belongs to.</param>
    /// <param name="renderer">The renderer whose frame the sky is recorded into.</param>
    /// <param name="shaders">Where <c>sky.vert</c> and <c>sky.frag</c> are compiled from.</param>
    /// <param name="scene">The per-frame uniform block the shaders read.</param>
    /// <param name="renderPass">The render pass the sky is drawn in.</param>
    public SkyRenderer(
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

        var setLayout = scene.Layout;
        _layout = context.Device.CreatePipelineLayout([setLayout], [], null);

        Span<VkPipelineShaderStageCreateInfo> stages =
        [
            Presets.ShaderStage.Vertex(shaders.Load("sky.vert")),
            Presets.ShaderStage.Fragment(shaders.Load("sky.frag")),
        ];

        // No vertex input at all: the shader builds its triangle from gl_VertexIndex.
        var vertexInput = new VkPipelineVertexInputStateCreateInfo();
        var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { Topology = VkPrimitiveTopology.TriangleList };
        var viewportState = new VkPipelineViewportStateCreateInfo { ViewportCount = 1, ScissorCount = 1 };

        // Culling nothing saves having to care which way round a generated triangle comes out.
        var rasterization = new VkPipelineRasterizationStateCreateInfo
        {
            PolygonMode = VkPolygonMode.Fill,
            CullMode = VkCullModeFlags.None,
            FrontFace = VkFrontFace.CounterClockwise,
            LineWidth = 1f,
        };

        var multisample = new VkPipelineMultisampleStateCreateInfo { RasterizationSamples = VkSampleCountFlags._1Bit };

        // Neither test nor write: see the remarks on the type for why that is the whole trick.
        var depthStencil = new VkPipelineDepthStencilStateCreateInfo
        {
            DepthTestEnable = false,
            DepthWriteEnable = false,
            DepthCompareOp = VkCompareOp.Always,
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

    /// <summary>Fills the frame with the sky.</summary>
    /// <remarks>
    /// Everything it needs is already in the scene uniform block, so it takes nothing: the sun
    /// moved when that block was filled, not here.
    /// </remarks>
    public void Draw()
    {
        var commands = _renderer.Commands;
        commands.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
        _scene.Bind(commands, _layout, _renderer.FrameIndex);
        commands.Draw(3, 1, 0, 0);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Device.DestroyPipeline(_pipeline, null);
        _context.Device.DestroyPipelineLayout(_layout, null);
    }
}
