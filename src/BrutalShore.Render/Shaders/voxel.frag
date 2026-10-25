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
layout(location = 6) in vec3 inWorldPosition;

// x: how much of the sky reaches here, y: how much light from glowing blocks does. Both already
// averaged per corner by the mesher; see LightVolume.
layout(location = 7) in vec2 inLight;

// Compares rather than reads: each fetch answers "is this fragment nearer the light than what
// was stored", and with a linear filter the hardware averages four of those answers for free.
layout(set = 0, binding = 1) uniform sampler2DShadow shadowMap;

layout(location = 0) out vec4 outColor;

const float OCCLUSION_FLOOR = 0.42;
const float EDGE_DARKENING = 0.14;
const float VARIATION = 0.13;

// What a glowing block's light looks like: a flame, not a window. Warm enough to read as another
// kind of light than the sky's, so a lit cave does not merely look like a shallow one.
const vec3 GLOW_COLOR = vec3(1.0, 0.72, 0.42);

// How dark the inside of an unlit cave gets. Not zero: a world where walking into a cave means
// walking into a black rectangle is a world nobody finds their way out of, and the point of the
// dark is to make a lamp worth placing rather than to end the game.
const float CAVE_AMBIENT = 0.035;

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

// Dave Hoskins, "Hash without Sine" (https://www.shadertoy.com/view/4djSRW), the same one the
// stars use. A sine-based hash differs between drivers, and a world that looked different on two
// machines would be a world the seed no longer describes.
float hash13(vec3 position) {
    vec3 scrambled = fract(position * 0.1031);
    scrambled += dot(scrambled, scrambled.zyx + 31.32);
    return fract((scrambled.x + scrambled.y) * scrambled.z);
}

void main() {
    // Two lights, one of which is always off: the sun while it is up, the moon while it is not.
    // Both terms are kept because dawn and dusk have the sun setting while the moon is already
    // risen, and dropping either would make the light jump at the horizon.
    // One shadow map is cast per frame, by whichever of the two is above the horizon, so the
    // same factor applies to both terms — the one that is down contributes nothing anyway.
    float reaching = sunlightReaching(inViewOffset, inNormal);

    // How much of the sky gets here at all, which gates both of those lights and the bounce from
    // the sky as well. Squared because the levels are a count of blocks travelled and brightness
    // is not: linear, the fifth metre of a tunnel still looks like the open air.
    float daylight = inLight.x * inLight.x;

    vec3 light = scene.ambientColor.rgb * max(daylight, CAVE_AMBIENT)
        + daylight * reaching * scene.sunColor.rgb * max(dot(inNormal, scene.sunDirection.xyz), 0.0)
        + daylight * reaching * scene.moonColor.rgb * max(dot(inNormal, scene.moonDirection.xyz), 0.0)
        + GLOW_COLOR * inLight.y * inLight.y;

    // The player's own exposure, applied to the light rather than to the finished pixel so that
    // the fog in front of it keeps its strength. sky.frag scales by the same number.
    light *= scene.shadow.w;

    float occlusion = mix(OCCLUSION_FLOOR, 1.0, inOcclusion);

    // Which block this fragment belongs to, in world coordinates. Stepping half a block back
    // along the normal is what turns a position on a face into the block behind it, and the whole
    // point of using the world frame is that the answer does not change as the camera moves.
    vec3 cell = floor(inWorldPosition - inNormal * 0.5);

    // A few per cent either way, per block. Nothing in this world has a texture, so without it a
    // field of grass is one flat colour however large it is, and the shape of the ground is
    // carried entirely by the lighting.
    float shade = 1.0 + (hash13(cell) - 0.5) * VARIATION;

    vec3 lit = inColor * light * occlusion * shade;

    // A thin darker line along every block edge, drawn from the surface coordinate rather than a
    // texture so it stays one pixel wide at any distance.
    vec2 toEdge = 0.5 - abs(fract(inSurface) - 0.5);
    vec2 pixels = toEdge / (fwidth(inSurface) + 0.0001);
    float edge = 1.0 - clamp(min(pixels.x, pixels.y), 0.0, 1.0);
    lit *= 1.0 - EDGE_DARKENING * edge;

    // Fog towards the sky in this direction, which is what hides the edge of the loaded world.
    float distance = length(inViewOffset);
    float fog = smoothstep(scene.fog.x, scene.fog.y, distance);
    vec3 distant =
        mix(skyColorAt(normalize(inViewOffset).y) * scene.shadow.w, scene.submerged.rgb, scene.submerged.a);
    lit = mix(lit, distant, fog);

    // Fog has to reach the alpha too, or a see-through surface stays sharp against a sky the
    // solid world has already faded into.
    outColor = vec4(lit, mix(inOpacity, 1.0, fog));
}
