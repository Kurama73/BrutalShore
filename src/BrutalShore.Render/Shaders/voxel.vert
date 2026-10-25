#version 450

// Must match SceneUniforms on the C# side; shaders are compiled with no include path, so the
// block is written out again in every shader that reads it. See that type's remarks.
layout(set = 0, binding = 0, std140) uniform Scene {
    mat4 viewProjection;
    mat4 inverseViewProjection;
    vec4 sunDirection;
    vec4 sunColor;
    vec4 moonDirection;
    vec4 moonColor;
    vec4 ambientColor;
    vec4 zenithColor;
    vec4 horizonColor;
    vec4 fog;
} scene;

layout(push_constant) uniform Push {
    vec4 chunkOrigin; // xyz: the chunk's origin relative to the camera, in metres
} push;

layout(location = 0) in uint inPacked0;
layout(location = 1) in uint inPacked1;

layout(location = 0) out vec3 outColor;
layout(location = 1) out vec3 outNormal;
layout(location = 2) out vec2 outSurface;
layout(location = 3) out float outOcclusion;
layout(location = 4) out vec3 outViewOffset;

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

    vec3 encoded = vec3(
        float( inPacked1        & 0xFFu),
        float((inPacked1 >>  8) & 0xFFu),
        float((inPacked1 >> 16) & 0xFFu)
    ) / 255.0;

    outColor = srgbToLinear(encoded);
    outNormal = NORMALS[face];
    outOcclusion = float(occlusion) / 3.0;

    uint axis = face >> 1u;
    outSurface = axis == 0u ? local.zy : (axis == 1u ? local.xz : local.xy);

    // Render space has the camera at the origin, so the position is also the offset from the eye
    // — which is what the fragment shader needs for both the fog distance and the view ray.
    outViewOffset = push.chunkOrigin.xyz + local;

    gl_Position = scene.viewProjection * vec4(outViewOffset, 1.0);
}
