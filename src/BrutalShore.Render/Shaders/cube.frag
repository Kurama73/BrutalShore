#version 450

// Must match SceneUniforms on the C# side; see cube.vert.
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
    vec4 middle;
    vec4 halfSize;
    vec4 color;
} push;

layout(location = 0) in vec3 inColor;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec3 inViewOffset;

layout(location = 0) out vec4 outColor;

// Shared with voxel.frag and sky.frag, and it has to stay identical: anything far enough away
// fades into the sky it would see in that direction.
vec3 skyColorAt(float upwards) {
    float height = pow(clamp(upwards, 0.0, 1.0), 0.42);
    vec3 above = mix(scene.horizonColor.rgb, scene.zenithColor.rgb, height);
    return mix(above * 0.42, above, smoothstep(-0.16, 0.0, upwards));
}

void main() {
    // No shadow lookup and no baked light. A loose cube is a small thing a metre or two from the
    // player, and what it costs to light it exactly is a shadow map sample and a flood fill it
    // has no chunk to belong to; what it buys is nothing anyone would see at that size.
    vec3 light = scene.ambientColor.rgb
        + (scene.sunColor.rgb * max(dot(inNormal, scene.sunDirection.xyz), 0.0))
        + (scene.moonColor.rgb * max(dot(inNormal, scene.moonDirection.xyz), 0.0));

    vec3 lit = inColor * light * scene.shadow.w;

    float distance = length(inViewOffset);
    float fog = smoothstep(scene.fog.x, scene.fog.y, distance);
    vec3 distant = mix(skyColorAt(normalize(inViewOffset).y) * scene.shadow.w, scene.submerged.rgb, scene.submerged.a);

    outColor = vec4(mix(lit, distant, fog), 1.0);
}
