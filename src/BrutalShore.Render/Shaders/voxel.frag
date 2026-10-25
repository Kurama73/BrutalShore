#version 450

// Must match SceneUniforms on the C# side; see voxel.vert.
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

layout(location = 0) in vec3 inColor;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inSurface;
layout(location = 3) in float inOcclusion;
layout(location = 4) in vec3 inViewOffset;

layout(location = 0) out vec4 outColor;

const float OCCLUSION_FLOOR = 0.42;
const float EDGE_DARKENING = 0.14;

// Shared with sky.frag, and it has to stay identical: the far edge of the world fades into the
// sky it would see in that direction, and any disagreement shows up as a band on the horizon.
vec3 skyColorAt(float upwards) {
    float height = pow(clamp(upwards, 0.0, 1.0), 0.42);
    vec3 above = mix(scene.horizonColor.rgb, scene.zenithColor.rgb, height);
    return mix(above * 0.42, above, smoothstep(-0.16, 0.0, upwards));
}

void main() {
    // Two lights, one of which is always off: the sun while it is up, the moon while it is not.
    // Both terms are kept because dawn and dusk have the sun setting while the moon is already
    // risen, and dropping either would make the light jump at the horizon.
    vec3 light = scene.ambientColor.rgb
        + scene.sunColor.rgb * max(dot(inNormal, scene.sunDirection.xyz), 0.0)
        + scene.moonColor.rgb * max(dot(inNormal, scene.moonDirection.xyz), 0.0);

    float occlusion = mix(OCCLUSION_FLOOR, 1.0, inOcclusion);
    vec3 lit = inColor * light * occlusion;

    // A thin darker line along every block edge, drawn from the surface coordinate rather than a
    // texture so it stays one pixel wide at any distance.
    vec2 toEdge = 0.5 - abs(fract(inSurface) - 0.5);
    vec2 pixels = toEdge / (fwidth(inSurface) + 0.0001);
    float edge = 1.0 - clamp(min(pixels.x, pixels.y), 0.0, 1.0);
    lit *= 1.0 - EDGE_DARKENING * edge;

    // Fog towards the sky in this direction, which is what hides the edge of the loaded world.
    float distance = length(inViewOffset);
    float fog = smoothstep(scene.fog.x, scene.fog.y, distance);
    lit = mix(lit, skyColorAt(normalize(inViewOffset).y), fog);

    outColor = vec4(lit, 1.0);
}
