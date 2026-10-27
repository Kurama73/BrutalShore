#version 450

// Must match SceneUniforms on the C# side; shaders are compiled with no include path, so the
// block is written out again in every shader that reads it. See that type's remarks.
layout(set = 0, binding = 0, std140) uniform Scene {
    mat4 viewProjection;
    mat4 inverseViewProjection;
    mat4 lightViewProjection;
    vec4 sunDirection;
    vec4 sunColor;
    vec4 moonDirection;
    vec4 moonColor;
    vec4 ambientColor;
    vec4 zenithColor;
    vec4 horizonColor;
    vec4 fog;
    vec4 shadow;
    vec4 submerged;
} scene;

layout(push_constant) uniform Push {
    vec4 chunkOrigin; // xyz: the chunk's origin relative to the camera, in metres
    vec4 chunkWorld;  // xyz: the chunk's origin in the world, in blocks
} push;

layout(location = 0) in uint inPacked0;
layout(location = 1) in uint inPacked1;
layout(location = 2) in uint inPacked2;

layout(location = 0) out vec3 outColor;
layout(location = 1) out vec3 outNormal;
layout(location = 2) out vec2 outSurface;
layout(location = 3) out float outOcclusion;
layout(location = 4) out vec3 outViewOffset;
layout(location = 5) out float outOpacity;
layout(location = 6) out vec3 outWorldPosition;
layout(location = 7) out vec2 outLight;
layout(location = 8) flat out uint outPattern;
layout(location = 9) flat out uint outTextureLayer;
layout(location = 10) flat out vec3 outTextureTint;

const vec3 NORMALS[6] = vec3[6](
    vec3( 1.0,  0.0,  0.0),
    vec3(-1.0,  0.0,  0.0),
    vec3( 0.0,  1.0,  0.0),
    vec3( 0.0, -1.0,  0.0),
    vec3( 0.0,  0.0,  1.0),
    vec3( 0.0,  0.0, -1.0)
);

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

    // Worked out on the CPU while the chunk was meshed, averaged over the cells around this
    // corner, and interpolated across the quad for free on the way to the fragment shader.
    uint skyLight = (inPacked0 >> 23) & 0xFu;
    uint blockLight = (inPacked0 >> 27) & 0xFu;

    vec3 encoded = vec3(
        float( inPacked1        & 0xFFu),
        float((inPacked1 >>  8) & 0xFFu),
        float((inPacked1 >> 16) & 0xFFu)
    ) / 255.0;

    outColor = srgbToLinear(encoded);

    // Sixteenths rather than bytes, which is what made room for the pattern beside it.
    outOpacity = float((inPacked1 >> 24) & 0xFu) / 15.0;
    outPattern = (inPacked1 >> 28) & 0xFu;
    outTextureLayer = inPacked2 & 0xFFu;
    outTextureTint = vec3(
        float((inPacked2 >> 8) & 0xFFu),
        float((inPacked2 >> 16) & 0xFFu),
        float((inPacked2 >> 24) & 0xFFu)
    ) / 255.0;
    outNormal = NORMALS[face];
    outOcclusion = float(occlusion) / 3.0;
    outLight = vec2(float(skyLight), float(blockLight)) / 15.0;

    uint axis = face >> 1u;
    outSurface = axis == 0u ? local.zy : (axis == 1u ? local.xz : local.xy);

    // Render space has the camera at the origin, so the position is also the offset from the eye
    // — which is what the fragment shader needs for both the fog distance and the view ray.
    outViewOffset = push.chunkOrigin.xyz + local;

    // The same point in a frame that does not move with the camera. Only the look of a block
    // is decided from it, never its position, so f32 at world scale is enough.
    outWorldPosition = push.chunkWorld.xyz + local;

    gl_Position = scene.viewProjection * vec4(outViewOffset, 1.0);
}
