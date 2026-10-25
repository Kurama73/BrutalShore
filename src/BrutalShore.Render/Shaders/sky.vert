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

layout(location = 0) out vec3 outDirection;

void main() {
    // One triangle big enough to cover the screen, built from the vertex index alone: the draw
    // binds no vertex buffer, so there is no geometry to keep anywhere for the sky.
    vec2 corner = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
    vec2 device = corner * 2.0 - 1.0;

    // Depth 1 is the far plane. The pipeline neither tests nor writes depth and the sky is drawn
    // first, so every other draw simply lands on top of it.
    gl_Position = vec4(device, 1.0, 1.0);

    // Unprojecting both ends of the pixel's ray is what keeps this honest about the Y flip the
    // projection carries: whatever the camera matrix does, its inverse undoes.
    vec4 near = scene.inverseViewProjection * vec4(device, 0.0, 1.0);
    vec4 far = scene.inverseViewProjection * vec4(device, 1.0, 1.0);
    outDirection = (far.xyz / far.w) - (near.xyz / near.w);
}
