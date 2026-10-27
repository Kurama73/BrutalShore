using System.Runtime.InteropServices;
using BrutalShore.Core;
using BrutalShore.Gpu;

namespace BrutalShore.Render;

/// <summary>Draws solid cubes that are not part of a chunk.</summary>
/// <remarks>
/// <para>
/// The world is made of chunks, and a chunk is the right answer for anything that stays where it
/// is. What it is the wrong answer for is anything that moves: a block knocked loose and falling,
/// something walking about, a shower of fragments. Re-meshing a chunk to move one of those would
/// be a few milliseconds of work for a thing a metre across.
/// </para>
/// <para>
/// So there is this: one unit cube uploaded once, and everything that differs between two of them
/// — where, how large, what colour, how far turned — in a push constant. Nothing here knows what
/// a cube is for, which is why it is in the engine and the things made of them are not.
/// </para>
/// <para>
/// Lit from the sun and the sky with no shadow and no baked light. A loose cube is small and
/// close, and what an exact answer would cost is a shadow lookup and a flood fill it has no chunk
/// to belong to.
/// </para>
/// </remarks>
public sealed unsafe class CubeRenderer : IDisposable
{
    private readonly GpuContext _context;
    private readonly GpuRenderer _renderer;
    private readonly SceneBindings _scene;
    private readonly GpuBuffer _corners;
    private readonly VkPipeline _pipeline;
    private readonly VkPipelineLayout _layout;

    /// <summary>Builds the pipeline and uploads the one cube it ever draws.</summary>
    /// <param name="context">The device the pipeline belongs to.</param>
    /// <param name="renderer">The renderer whose frame the cubes are recorded into.</param>
    /// <param name="shaders">Where <c>cube.vert</c> and <c>cube.frag</c> are compiled from.</param>
    /// <param name="scene">The per-frame uniform block the shaders read.</param>
    /// <param name="renderPass">The render pass the cubes are drawn in.</param>
    public CubeRenderer(
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
        _corners = GpuBuffer.Create(context, UnitCube(), VkBufferUsageFlags.VertexBufferBit);

        var pushConstantRange = new VkPushConstantRange
        {
            StageFlags = VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit,
            Offset = ByteSize.Zero,
            Size = ByteSize.Of<CubePushConstants>(),
        };

        var setLayout = scene.Layout;
        _layout = context.Device.CreatePipelineLayout([setLayout], [pushConstantRange], null);

        Span<VkPipelineShaderStageCreateInfo> stages =
        [
            Presets.ShaderStage.Vertex(shaders.Load("cube.vert")),
            Presets.ShaderStage.Fragment(shaders.Load("cube.frag")),
        ];

        var binding = new VkVertexInputBindingDescription
        {
            Binding = 0,
            Stride = ByteSize.Of<CubeCorner>(),
            InputRate = VkVertexInputRate.Vertex,
        };

        Span<VkVertexInputAttributeDescription> attributes =
        [
            new()
            {
                Location = 0,
                Binding = 0,
                Format = VkFormat.R32G32B32SFloat,
                Offset = ByteSize.Zero,
            },
            new()
            {
                Location = 1,
                Binding = 0,
                Format = VkFormat.R32G32B32SFloat,
                Offset = ByteSize.Of<float3>(),
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
            FrontFace = VkFrontFace.CounterClockwise,
            LineWidth = 1f,
        };

        var multisample = new VkPipelineMultisampleStateCreateInfo { RasterizationSamples = VkSampleCountFlags._1Bit };

        // Written as well as tested, unlike the outline: a cube is a solid thing in the world and
        // whatever is behind it should be hidden by it.
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

    /// <summary>Binds the pipeline, once, before any number of <see cref="Draw" /> calls.</summary>
    /// <remarks>
    /// Split from the drawing because the pipeline and the descriptor set are the same for every
    /// cube: binding them once for thirty of them rather than thirty times is most of what makes
    /// a draw call each acceptable at all.
    /// </remarks>
    public void Begin()
    {
        var commands = _renderer.Commands;
        commands.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
        _scene.Bind(commands, _layout, _renderer.FrameIndex);
        commands.BindVertexBuffer(0, _corners.Handle);
    }

    /// <summary>Draws one box. <see cref="Begin" /> must have been called this frame.</summary>
    /// <param name="middleMeters">Where its middle is, in world metres.</param>
    /// <param name="sizeMeters">Its width, height and depth, in metres.</param>
    /// <param name="color">Its colour, in sRGB.</param>
    /// <param name="turnRadians">How far it is turned about its own upright axis.</param>
    /// <param name="cameraMeters">Where the camera is; the render-space origin.</param>
    /// <param name="textureLayer">Texture-array layer, or zero to use the supplied colour.</param>
    /// <param name="textureTint">The sRGB colour multiplied into the sampled texture, if any.</param>
    /// <param name="skin">Which part of the layer each face is cut from; the whole of it by default.</param>
    /// <param name="tiltRadians">How far it is nosed up about its own crosswise axis.</param>
    /// <remarks>
    /// Three sizes rather than one, because the things made of these are not all cubes: an animal
    /// is a long body on four thin legs, and drawing it out of cubes alone would be drawing it out
    /// of dice.
    /// </remarks>
    public void Draw(
        double3 middleMeters,
        float3 sizeMeters,
        float3 color,
        float turnRadians,
        double3 cameraMeters,
        byte textureLayer = 0,
        float3? textureTint = null,
        CubeSkin? skin = null,
        float tiltRadians = 0f
    )
    {
        var middle = RenderSpace.ToRenderSpace(middleMeters, cameraMeters, 1.0);

        _renderer.Commands.PushConstants(
            _layout,
            VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit,
            ByteSize.Zero,
            new CubePushConstants(middle, sizeMeters, color, turnRadians, textureLayer, textureTint, skin, tiltRadians)
        );
        _renderer.Commands.Draw(_corners.Count, 1, 0, 0);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Device.DestroyPipeline(_pipeline, null);
        _context.Device.DestroyPipelineLayout(_layout, null);
        _corners.Dispose();
    }

    /// <summary>One corner of the unit cube: where it is, and which way its face points.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CubeCorner(float3 Position, float3 Normal);

    /// <summary>
    /// The unit cube about its own middle, as thirty-six corners wound counter-clockwise seen
    /// from outside.
    /// </summary>
    /// <remarks>
    /// About the middle rather than from a corner, because a cube that turns has to turn about
    /// something, and the only point a cube has that is not arbitrary is its middle.
    /// </remarks>
    private static CubeCorner[] UnitCube()
    {
        var corners = new List<CubeCorner>(36);

        for (var axis = 0; axis < 3; axis++)
        for (var facing = 0; facing < 2; facing++)
        {
            var sign = facing == 0 ? 1f : -1f;
            var normal = Along(axis, sign);

            // The two axes across the face, in the order that makes the winding come out right
            // for both signs: swapping them for the far side is what reverses it.
            var across = Along((axis + 1) % 3, 1f);
            var up = Along((axis + 2) % 3, 1f);
            if (facing != 0)
                (across, up) = (up, across);

            var middle = normal * 0.5f;

            var lowLeft = new CubeCorner(middle - (across * 0.5f) - (up * 0.5f), normal);
            var lowRight = new CubeCorner(middle + (across * 0.5f) - (up * 0.5f), normal);
            var highRight = new CubeCorner(middle + (across * 0.5f) + (up * 0.5f), normal);
            var highLeft = new CubeCorner(middle - (across * 0.5f) + (up * 0.5f), normal);

            corners.AddRange([lowLeft, lowRight, highRight, lowLeft, highRight, highLeft]);
        }

        return [.. corners];
    }

    private static float3 Along(int axis, float sign) =>
        axis switch
        {
            0 => new float3(sign, 0f, 0f),
            1 => new float3(0f, sign, 0f),
            _ => new float3(0f, 0f, sign),
        };
}
