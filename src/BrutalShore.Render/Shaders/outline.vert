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
    vec4 corner; // xyz: the block's lowest corner relative to the camera, in metres
    vec4 color;  // rgb: the line's colour. w: how far the box is grown, in metres
} push;

layout(location = 0) in vec3 inPosition;

void main() {
    // Grown a little about its middle, so the lines sit just outside the surface they trace
    // rather than in the same plane as it, where the depth test would take half of them.
    vec3 grown = (inPosition - 0.5) * (1.0 + push.color.w) + 0.5;
    gl_Position = scene.viewProjection * vec4(push.corner.xyz + grown, 1.0);
}
