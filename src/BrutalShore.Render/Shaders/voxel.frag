#version 450

// Must match SceneUniforms on the C# side; see voxel.vert.
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

layout(location = 0) in vec3 inColor;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inSurface;
layout(location = 3) in float inOcclusion;
layout(location = 4) in vec3 inViewOffset;
layout(location = 5) in float inOpacity;

// Compares rather than reads: each fetch answers "is this fragment nearer the light than what
// was stored", and with a linear filter the hardware averages four of those answers for free.
layout(set = 0, binding = 1) uniform sampler2DShadow shadowMap;

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

// How much of the direct light reaches this fragment, from 0 in full shadow to 1 in the open.
float sunlightReaching(vec3 viewOffset, vec3 normal) {
    // Nudging the lookup along the surface's own normal, rather than biasing the depth it is
    // compared against, is what suits a world of axis-aligned planes: the offset is the same
    // everywhere and cannot pull a shadow away from the block casting it.
    vec4 inLight = scene.lightViewProjection * vec4(viewOffset + normal * scene.shadow.y, 1.0);
    vec3 projected = inLight.xyz / inLight.w;

    // Past the far side of the light's box there is nothing stored to compare against.
    if (projected.z > 1.0) {
        return 1.0;
    }

    vec2 mapCoordinate = projected.xy * 0.5 + 0.5;

    // Nine taps a texel apart, on top of the four the hardware already averages per tap. That is
    // what turns the map's grid into an edge a few centimetres wide instead of a staircase.
    float texel = scene.shadow.z;
    float reaching = 0.0;
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 at = mapCoordinate + vec2(float(x), float(y)) * texel;
            reaching += texture(shadowMap, vec3(at, projected.z));
        }
    }
    reaching /= 9.0;

    // Fade the shadow out before the edge of the box rather than letting it stop dead there.
    float fromCentre = max(abs(projected.x), abs(projected.y));
    float inside = 1.0 - smoothstep(0.82, 1.0, fromCentre);

    return 1.0 - (1.0 - reaching) * inside * scene.shadow.x;
}

void main() {
    // Two lights, one of which is always off: the sun while it is up, the moon while it is not.
    // Both terms are kept because dawn and dusk have the sun setting while the moon is already
    // risen, and dropping either would make the light jump at the horizon.
    // One shadow map is cast per frame, by whichever of the two is above the horizon, so the
    // same factor applies to both terms — the one that is down contributes nothing anyway.
    float reaching = sunlightReaching(inViewOffset, inNormal);

    vec3 light = scene.ambientColor.rgb
        + reaching * scene.sunColor.rgb * max(dot(inNormal, scene.sunDirection.xyz), 0.0)
        + reaching * scene.moonColor.rgb * max(dot(inNormal, scene.moonDirection.xyz), 0.0);

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
    vec3 distant = mix(skyColorAt(normalize(inViewOffset).y), scene.submerged.rgb, scene.submerged.a);
    lit = mix(lit, distant, fog);

    // Fog has to reach the alpha too, or a see-through surface stays sharp against a sky the
    // solid world has already faded into.
    outColor = vec4(lit, mix(inOpacity, 1.0, fog));
}
