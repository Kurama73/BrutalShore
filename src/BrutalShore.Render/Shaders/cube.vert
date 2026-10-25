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
    vec4 middle; // xyz: the cube's middle relative to the camera, in metres. w: half its side
    vec4 color;  // rgb: its colour, in sRGB. w: how far it is turned about its own y, in radians
} push;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;

layout(location = 0) out vec3 outColor;
layout(location = 1) out vec3 outNormal;
layout(location = 2) out vec3 outViewOffset;

vec3 srgbToLinear(vec3 encoded) {
    vec3 low = encoded / 12.92;
    vec3 high = pow((encoded + 0.055) / 1.055, vec3(2.4));
    return mix(low, high, step(vec3(0.04045), encoded));
}

void main() {
    // Turned about its own upright axis before it is placed. A dropped block that spins slowly
    // is the difference between an object lying in the world and a decal stuck to the ground.
    float turn = push.color.w;
    float sine = sin(turn);
    float cosine = cos(turn);

    mat3 spin = mat3(
        vec3(cosine, 0.0, -sine),
        vec3(0.0, 1.0, 0.0),
        vec3(sine, 0.0, cosine)
    );

    vec3 corner = spin * (inPosition * push.middle.w);

    outColor = srgbToLinear(push.color.rgb);
    outNormal = normalize(spin * inNormal);
    outViewOffset = push.middle.xyz + corner;

    gl_Position = scene.viewProjection * vec4(outViewOffset, 1.0);
}
