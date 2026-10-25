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
} scene;

layout(push_constant) uniform Push {
    vec4 chunkOrigin; // xyz: the chunk's origin relative to the camera, in metres
} push;

// The pipeline describes both vertex attributes because it shares the voxel vertex format; this
// pass only needs where the block is, so the colour and the occlusion are left unread.
layout(location = 0) in uint inPacked0;

void main() {
    vec3 local = vec3(
        float( inPacked0        & 0x3Fu),
        float((inPacked0 >>  6) & 0x3Fu),
        float((inPacked0 >> 12) & 0x3Fu)
    );

    gl_Position = scene.lightViewProjection * vec4(push.chunkOrigin.xyz + local, 1.0);
}
