using BrutalShore.Gpu;

namespace BrutalShore.Render;

/// <summary>
/// The descriptor set every pipeline in this layer binds: one uniform block per frame in flight.
/// </summary>
/// <remarks>
/// <para>
/// What belongs here is anything the whole frame shares — the camera's matrices, the sun, the
/// sky's colours, the fog. What does not is anything that changes between draw calls within a
/// frame; that is what the push constants are for, and a chunk's origin is the only one so far.
/// </para>
/// <para>
/// There is one buffer and one set per frame in flight rather than one of each, because the
/// previous frame may still be reading its copy while this one is being filled. The fence
/// <see cref="GpuRenderer.BeginFrame" /> waits on is what proves the copy for
/// <see cref="GpuRenderer.FrameIndex" /> is free.
/// </para>
/// </remarks>
public sealed unsafe class SceneBindings : IDisposable
{
    private readonly GpuContext _context;
    private readonly GpuBuffer[] _buffers = new GpuBuffer[GpuRenderer.MaxFramesInFlight];
    private readonly VkDescriptorSet[] _sets = new VkDescriptorSet[GpuRenderer.MaxFramesInFlight];
    private readonly VkDescriptorPool _pool;
    private readonly DepthTarget _shadowMap;

    /// <summary>Creates the layout, the pool and one buffer and set per frame in flight.</summary>
    /// <param name="context">The device everything is allocated from.</param>
    /// <param name="shadowMap">The depth target the fragment shaders sample for shadows.</param>
    public SceneBindings(GpuContext context, DepthTarget shadowMap)
    {
        _context = context;
        _shadowMap = shadowMap;

        // Both stages read the block: the vertex shader needs the view-projection, the fragment
        // shader needs the lighting. Splitting it in two would cost a second binding to save
        // nothing measurable.
        Span<VkDescriptorSetLayoutBinding> bindings =
        [
            new()
            {
                Binding = 0,
                DescriptorType = VkDescriptorType.UniformBuffer,
                DescriptorCount = 1,
                StageFlags = VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit,
            },

            // Only the fragment shader looks up a shadow; the depth-only pass that fills the map
            // declares the same layout but never touches this binding.
            new()
            {
                Binding = 1,
                DescriptorType = VkDescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = VkShaderStageFlags.FragmentBit,
            },
        ];

        Layout = context.Device.CreateDescriptorSetLayout(
            new VkDescriptorSetLayoutCreateInfo
            {
                BindingCount = bindings.Length,
                Bindings = bindings.AsPointer(),
            },
            null
        );

        Span<VkDescriptorPoolSize> poolSizes =
        [
            new() { Type = VkDescriptorType.UniformBuffer, DescriptorCount = GpuRenderer.MaxFramesInFlight },
            new()
            {
                Type = VkDescriptorType.CombinedImageSampler,
                DescriptorCount = GpuRenderer.MaxFramesInFlight,
            },
        ];

        _pool = context.Device.CreateDescriptorPool(
            new VkDescriptorPoolCreateInfo
            {
                MaxSets = GpuRenderer.MaxFramesInFlight,
                PoolSizeCount = poolSizes.Length,
                PoolSizes = poolSizes.AsPointer(),
            },
            null
        );

        var empty = new SceneUniforms();
        for (var frame = 0; frame < GpuRenderer.MaxFramesInFlight; frame++)
        {
            _buffers[frame] = GpuBuffer.Create(
                context,
                new ReadOnlySpan<SceneUniforms>(in empty),
                VkBufferUsageFlags.UniformBufferBit
            );
            _sets[frame] = Allocate();
            Point(_sets[frame], _buffers[frame]);
        }
    }

    /// <summary>The set layout a pipeline has to declare to read the block.</summary>
    public VkDescriptorSetLayout Layout { get; }

    /// <summary>Fills in this frame's copy of the block.</summary>
    /// <param name="frameIndex">The frame being recorded; see <see cref="GpuRenderer.FrameIndex" />.</param>
    /// <param name="frame">Everything the shaders read once for this frame.</param>
    public void Update(int frameIndex, in SceneFrame frame)
    {
        var shadow = new float4(
            frame.ShadowStrength,
            frame.ShadowNormalOffsetMeters,
            1f / _shadowMap.Resolution,
            0f
        );

        var uniforms = new SceneUniforms(
            frame.ViewProjection,
            frame.LightViewProjection,
            frame.Sky,
            frame.FogStartMeters,
            frame.FogEndMeters,
            shadow,
            frame.Submerged
        );
        _buffers[frameIndex].Rewrite(new ReadOnlySpan<SceneUniforms>(in uniforms));
    }

    /// <summary>Binds this frame's set so the next draw calls can read the block.</summary>
    /// <param name="commands">The command buffer being recorded.</param>
    /// <param name="pipelineLayout">The layout of the pipeline about to draw.</param>
    /// <param name="frameIndex">The frame being recorded; see <see cref="GpuRenderer.FrameIndex" />.</param>
    /// <remarks>
    /// Called once per pipeline rather than once per frame. Vulkan keeps a bound set across a
    /// pipeline change only while the two layouts are compatible, and these pipelines differ in
    /// their push constant ranges; rebinding costs nothing and removes the question.
    /// </remarks>
    public void Bind(CommandBuffer commands, VkPipelineLayout pipelineLayout, int frameIndex) =>
        commands.BindDescriptorSets(
            VkPipelineBindPoint.Graphics,
            pipelineLayout,
            0,
            [_sets[frameIndex]],
            []
        );

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var buffer in _buffers)
            buffer.Dispose();

        // The sets go with the pool; freeing them one by one would need the free-descriptor-set
        // flag on it, which costs allocator bookkeeping for nothing.
        _context.Device.DestroyDescriptorPool(_pool, null);
        _context.Device.DestroyDescriptorSetLayout(Layout, null);
    }

    private VkDescriptorSet Allocate()
    {
        var layout = Layout;
        Span<VkDescriptorSet> allocated = stackalloc VkDescriptorSet[1];
        _context.Device.AllocateDescriptorSets(
            new VkDescriptorSetAllocateInfo
            {
                DescriptorPool = _pool,
                DescriptorSetCount = 1,
                SetLayouts = &layout,
            },
            allocated
        );
        return allocated[0];
    }

    private void Point(VkDescriptorSet set, GpuBuffer buffer)
    {
        var bufferInfo = new VkDescriptorBufferInfo
        {
            Buffer = buffer.Handle,
            Offset = ByteSize.Zero,
            Range = ByteSize.Of<SceneUniforms>(),
        };

        // The shadow map is one image for the whole renderer, so every frame's set points at the
        // same view; what differs between frames is only the block beside it.
        var imageInfo = new VkDescriptorImageInfo
        {
            Sampler = _shadowMap.Sampler,
            ImageView = _shadowMap.View,
            ImageLayout = VkImageLayout.ShaderReadOnlyOptimal,
        };

        _context.Device.UpdateDescriptorSets(
            [
                new VkWriteDescriptorSet
                {
                    DstSet = set,
                    DstBinding = 0,
                    DescriptorCount = 1,
                    DescriptorType = VkDescriptorType.UniformBuffer,
                    BufferInfo = &bufferInfo,
                },
                new VkWriteDescriptorSet
                {
                    DstSet = set,
                    DstBinding = 1,
                    DescriptorCount = 1,
                    DescriptorType = VkDescriptorType.CombinedImageSampler,
                    ImageInfo = &imageInfo,
                },
            ],
            []
        );
    }
}
