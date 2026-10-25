#version 450

// Unpacks the eight-byte vertex BrutalShore.Voxel.VoxelVertex packs. The bit layout is documented
// there; this has to agree with it, and the round-trip tests on the C# side are what pin it down.

layout(push_constant) uniform Push {
    mat4 viewProjection;
    vec4 chunkOrigin; // xyz: the chunk's origin relative to the camera, in metres
} push;

layout(location = 0) in uint inPacked0;
layout(location = 1) in uint inPacked1;

layout(location = 0) out vec3 outColor;
layout(location = 1) out vec3 outNormal;
layout(location = 2) out vec2 outSurface;
layout(location = 3) out float outOcclusion;

// Indexed by the face field, in the order of BrutalShore.Voxel.VoxelFace.
const vec3 NORMALS[6] = vec3[6](
    vec3( 1.0,  0.0,  0.0),
    vec3(-1.0,  0.0,  0.0),
    vec3( 0.0,  1.0,  0.0),
    vec3( 0.0, -1.0,  0.0),
    vec3( 0.0,  0.0,  1.0),
    vec3( 0.0,  0.0, -1.0)
);

// Block colours are authored as sRGB bytes, which is how anyone picking a colour thinks about it,
// but lighting has to be done in linear light or the shading comes out washed out and the ambient
// term reads far too bright.
vec3 srgbToLinear(vec3 encoded) {
    vec3 low = encoded / 12.92;
    vec3 high = pow((encoded + 0.055) / 1.055, vec3(2.4));
    return mix(low, high, step(vec3(0.04045), encoded));
}

void main() {
    vec3 local = vec3(
        float( inPacked0        & 0x3Fu),
        float((inPacked0 >>  6) & 0x3Fu),
        float((inPacked0 >> 12) & 0x3Fu)
    );
    uint face = (inPacked0 >> 18) & 0x7u;
    uint occlusion = (inPacked0 >> 21) & 0x3u;

    vec3 encoded = vec3(
        float( inPacked1        & 0xFFu),
        float((inPacked1 >>  8) & 0xFFu),
        float((inPacked1 >> 16) & 0xFFu)
    ) / 255.0;

    outColor = srgbToLinear(encoded);
    outNormal = NORMALS[face];
    outOcclusion = float(occlusion) / 3.0;

    // The surface coordinate is just the position in the plane of the face, which is why the
    // vertex carries no texture coordinate: for an axis-aligned quad the two components that are
    // not along the normal already run one unit per block, so a merged quad tiles per block
    // instead of stretching one block's worth of detail across the whole run.
    uint axis = face >> 1u;
    outSurface = axis == 0u ? local.zy : (axis == 1u ? local.xz : local.xy);

    gl_Position = push.viewProjection * vec4(push.chunkOrigin.xyz + local, 1.0);
}
