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
    vec4 heldLight;
    vec4 heldLightColor;
    vec4 field;
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

// Which of BlockPattern's surfaces this face is. Flat, because a pattern belongs to a whole quad
// and interpolating between two of them would mean a surface that is half stone and half wood.
layout(location = 8) flat in uint inPattern;

// Compares rather than reads: each fetch answers "is this fragment nearer the light than what
// was stored", and with a linear filter the hardware averages four of those answers for free.
layout(set = 0, binding = 1) uniform sampler2DShadow shadowMap;

// The world around the camera, one texel a block: rgb is the block's colour and a says what kind
// it is — nothing, see-through, or solid. This is what a ray walks through. See VoxelField.
layout(set = 0, binding = 2) uniform sampler3D voxelField;

layout(location = 0) out vec4 outColor;

const float OCCLUSION_FLOOR = 0.42;
const float EDGE_DARKENING = 0.14;
const float VARIATION = 0.13;

// What a glowing block's light looks like: a flame, not a window. Warm enough to read as another
// kind of light than the sky's, so a lit cave does not merely look like a shallow one.
const vec3 GLOW_COLOR = vec3(1.0, 0.72, 0.42);

// What the cube's last channel says a cell is. See VoxelField, which writes them.
const float SEE_THROUGH = 0.502;

vec3 srgbToLinear(vec3 encoded) {
    vec3 low = encoded / 12.92;
    vec3 high = pow((encoded + 0.055) / 1.055, vec3(2.4));
    return mix(low, high, step(vec3(0.04045), encoded));
}

// How sharply a carried light falls off. Two is the inverse square the real thing obeys, which
// over fifteen metres of corridor is almost all darkness; this is gentler on purpose, because a
// player wants to see the room they are standing in rather than their own boots.
const float HELD_FALLOFF = 1.9;

// How many blocks a reflected ray walks before giving up. Ninety-six is the cube's own width, so
// a ray can cross it corner to corner only along an axis; anything longer is paying for steps
// that have already left the cube.
const int RAY_STEPS = 96;

// What a surface reflects when looked at straight down, before the angle is taken into account.
// Water is about two per cent at normal incidence and almost a mirror at a grazing angle, and
// that spread is the whole reason a lake looks like a lake.
const float WATER_REFLECTANCE = 0.02;

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

// What the light in the player's hand adds here.
//
// No shadow term, and it does not need one: the light is a hand's length from the eye, so
// anything that could block it from a fragment would be blocking the fragment from view. What
// costs the geometry its flatness is the angle, not an occlusion test.
vec3 heldLightReaching(vec3 viewOffset, vec3 normal) {
    if (scene.heldLight.w <= 0.0 || scene.heldLightColor.a <= 0.0) {
        return vec3(0.0);
    }

    vec3 toLight = scene.heldLight.xyz - viewOffset;
    float distance = length(toLight);
    if (distance >= scene.heldLight.w) {
        return vec3(0.0);
    }

    // Smooth to nothing at the range rather than clipping: a lit circle with an edge on it reads
    // as a spotlight painted on the floor, which is the one thing a carried lamp never looks like.
    float reach = 1.0 - (distance / scene.heldLight.w);
    float falloff = pow(clamp(reach, 0.0, 1.0), HELD_FALLOFF);

    float facing = max(dot(normal, toLight / max(distance, 1e-4)), 0.0);

    return scene.heldLightColor.rgb * scene.heldLightColor.a * falloff * facing;
}

// What a cell of the cube holds, given a cell coordinate measured from the cube's own corner.
vec4 fieldAt(ivec3 cell) {
    if (any(lessThan(cell, ivec3(0))) || any(greaterThanEqual(cell, ivec3(int(scene.field.w))))) {
        return vec4(0.0);
    }

    return texture(voxelField, (vec3(cell) + 0.5) / scene.field.w);
}

// Walks a ray through the cube a block at a time and returns what it hit, or nothing.
//
// Amanatides and Woo, the same traversal the game uses on the CPU to work out which block the
// crosshair is on. A voxel world needs no acceleration structure for this: the geometry is a
// regular grid, so finding the next surface along a ray is arithmetic rather than a search, which
// is why hardware ray tracing would be the slower answer here even where it exists.
//
// Walked in the cube's own coordinates, where a block boundary is an integer. Render space is the
// world shifted by wherever the camera happens to be standing, so its boundaries are not.
vec4 marchField(vec3 fromRender, vec3 direction, float ignoreKind) {
    if (scene.field.w <= 0.0) {
        return vec4(0.0);
    }

    vec3 origin = fromRender - scene.field.xyz;
    ivec3 cell = ivec3(floor(origin));
    ivec3 stepDirection = ivec3(sign(direction));
    vec3 perBlock = 1.0 / max(abs(direction), vec3(1e-6));

    // How far along the ray each axis is from its next boundary. An axis the ray does not move
    // along never reaches one, which is what the large value stands in for.
    vec3 next;
    next.x = direction.x == 0.0 ? 1e9 : (direction.x > 0.0 ? float(cell.x) + 1.0 - origin.x : origin.x - float(cell.x)) * perBlock.x;
    next.y = direction.y == 0.0 ? 1e9 : (direction.y > 0.0 ? float(cell.y) + 1.0 - origin.y : origin.y - float(cell.y)) * perBlock.y;
    next.z = direction.z == 0.0 ? 1e9 : (direction.z > 0.0 ? float(cell.z) + 1.0 - origin.z : origin.z - float(cell.z)) * perBlock.z;

    for (int taken = 0; taken < RAY_STEPS; taken++) {
        // Always along whichever axis reaches its next boundary first.
        if (next.x < next.y && next.x < next.z) {
            cell.x += stepDirection.x;
            next.x += perBlock.x;
        } else if (next.y < next.z) {
            cell.y += stepDirection.y;
            next.y += perBlock.y;
        } else {
            cell.z += stepDirection.z;
            next.z += perBlock.z;
        }

        vec4 found = fieldAt(cell);
        if (found.a > 0.0 && abs(found.a - ignoreKind) > 0.01) {
            return vec4(found.rgb, 1.0);
        }
    }

    return vec4(0.0);
}

// What a see-through surface reflects: whatever a ray bounced off it runs into, or the sky.
//
// Schlick's approximation for how much of it is reflected at all, which is what makes water look
// like water — almost nothing looking straight down into it, almost everything across a lake.
vec3 reflectionOn(vec3 viewOffset, vec3 normal, out float amount) {
    vec3 towardsEye = normalize(-viewOffset);
    float facing = clamp(dot(towardsEye, normal), 0.0, 1.0);
    amount = WATER_REFLECTANCE + ((1.0 - WATER_REFLECTANCE) * pow(1.0 - facing, 5.0));

    vec3 bounced = reflect(-towardsEye, normal);
    vec3 sky = skyColorAt(bounced.y);

    // Start the ray a little off the surface, or its first step finds the block it came from.
    vec4 hit = marchField(viewOffset + (normal * 0.05), bounced, SEE_THROUGH);
    if (hit.a <= 0.0) {
        return sky;
    }

    // Lit roughly rather than exactly: what the ray found is a block's colour, and the shading it
    // would have had needs its own normal, its own occlusion and its own shadow lookup. At the
    // angles a reflection is visible from, a tint of the sky it sits under is what is read.
    vec3 lit = srgbToLinear(hit.rgb) * (scene.ambientColor.rgb + (scene.sunColor.rgb * 0.5));
    return mix(lit, sky, 0.25);
}

// Value noise over the face, smooth between its lattice points. Hashed from the position in the
// world rather than from the position on the quad, so the detail belongs to the block and does
// not slide across it when a merged quad changes shape.
float surfaceNoise(vec3 cell, vec2 across, float scale) {
    vec2 scaled = across * scale;
    vec2 whole = floor(scaled);
    vec2 part = fract(scaled);

    // Smoothstep between the four corners: cheaper than a gradient noise and, at the size of a
    // speck of stone, indistinguishable from one.
    vec2 eased = part * part * (3.0 - (2.0 * part));

    float lowLeft = hash13(cell + vec3(whole, 0.0));
    float lowRight = hash13(cell + vec3(whole + vec2(1.0, 0.0), 0.0));
    float highLeft = hash13(cell + vec3(whole + vec2(0.0, 1.0), 0.0));
    float highRight = hash13(cell + vec3(whole + vec2(1.0, 1.0), 0.0));

    return mix(mix(lowLeft, lowRight, eased.x), mix(highLeft, highRight, eased.x), eased.y);
}

// How much lighter or darker this point on the face is than the block's own colour.
//
// Every one of these is a function of where the fragment is and nothing else: no texture, no
// atlas, no mip levels, and detail as fine as the screen rather than as fine as a texel. What a
// pattern is allowed to change is brightness, never hue — the colour is the block's, and a
// pattern that tinted it would make two blocks of the same type look like two different ones.
float patternShade(uint pattern, vec2 across, vec3 cell) {
    vec2 within = fract(across);

    // Mottled: broad patches of rock a few per cent apart, with finer grit over them.
    if (pattern == 1u) {
        return 1.0
            + ((surfaceNoise(cell, across, 3.0) - 0.5) * 0.18)
            + ((surfaceNoise(cell, across, 11.0) - 0.5) * 0.07);
    }

    // Crumbly: lumps the size of a pebble, with the gaps between them darker than the lumps are
    // light, because earth is holes rather than bumps.
    if (pattern == 2u) {
        float lumps = surfaceNoise(cell, across, 6.0);
        return 1.0 + ((lumps - 0.55) * 0.34);
    }

    // Bladed: short strokes in columns, each one starting at its own height. Strokes that ran
    // the whole way across would read as corduroy, and strokes that wandered would read as
    // marble; what makes it grass is that they are short, upright, and all different lengths.
    if (pattern == 3u) {
        float columns = across.x * 9.0;
        float column = floor(columns);
        float sideways = fract(columns);

        float offset = hash13(cell + vec3(column, 0.0, 0.0));
        float along = fract((across.y * 4.0) + (offset * 4.0));

        float stroke = smoothstep(0.08, 0.3, along) * smoothstep(0.86, 0.6, along);
        float width = smoothstep(0.12, 0.34, sideways) * smoothstep(0.88, 0.66, sideways);

        return 1.0 + (((stroke * width) - 0.28) * 0.2);
    }

    // Grained: lines along the face with the odd knot in them. The lines bend slightly, which is
    // the difference between wood and a barcode.
    if (pattern == 4u) {
        float bend = (surfaceNoise(cell, across, 1.5) - 0.5) * 0.6;
        float rings = fract((within.y + bend) * 5.0);
        float line = smoothstep(0.0, 0.25, rings) * smoothstep(0.65, 0.4, rings);
        return 1.0 + ((line - 0.45) * 0.22);
    }

    // Scattered: clumps of leaf with gaps between them, dark enough to read as depth into the
    // canopy rather than as dirt on it.
    if (pattern == 5u) {
        float clumps = surfaceNoise(cell, across, 5.0);
        float gaps = smoothstep(0.52, 0.3, clumps);
        return 1.0 + ((clumps - 0.5) * 0.3) - (gaps * 0.3);
    }

    // Glinting: almost flat, with the occasional bright speck. Snow is a surface with nothing on
    // it except the few points where it catches the light.
    if (pattern == 6u) {
        float specks = surfaceNoise(cell, across, 14.0);
        return 1.0 + ((surfaceNoise(cell, across, 4.0) - 0.5) * 0.05) + (smoothstep(0.88, 1.0, specks) * 0.35);
    }

    // Rippled: two sets of waves at an angle to each other, so the pattern they make together
    // does not repeat on anything the eye can follow.
    if (pattern == 7u) {
        float first = sin((across.x + (across.y * 0.6)) * 9.0);
        float second = sin(((across.x * -0.5) + across.y) * 13.0);
        return 1.0 + ((first * second) * 0.045);
    }

    // Radiant: bright in the middle of each face and banded towards its edges, which is what
    // makes a lantern read as a thing that is lit rather than a thing that is pale.
    if (pattern == 8u) {
        vec2 fromMiddle = abs(within - 0.5) * 2.0;
        float middle = 1.0 - max(fromMiddle.x, fromMiddle.y);
        return 0.78 + (smoothstep(0.1, 0.75, middle) * 0.5);
    }

    // Grainy: even dots at the size of a grain of sand, and nothing else.
    if (pattern == 9u) {
        return 1.0 + ((surfaceNoise(cell, across, 16.0) - 0.5) * 0.14);
    }

    return 1.0;
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

    light += heldLightReaching(inViewOffset, inNormal);

    // The player's own exposure, applied to the light rather than to the finished pixel so that
    // the fog in front of it keeps its strength. sky.frag scales by the same number.
    light *= scene.shadow.w;

    float occlusion = mix(OCCLUSION_FLOOR, 1.0, inOcclusion);

    // Which block this fragment belongs to, in world coordinates. Stepping half a block back
    // along the normal is what turns a position on a face into the block behind it, and the whole
    // point of using the world frame is that the answer does not change as the camera moves.
    vec3 cell = floor(inWorldPosition - inNormal * 0.5);

    // A few per cent either way, per block, on top of whatever the pattern does within one. The
    // two are different scales of the same job: the pattern says what a surface is made of, and
    // this says that no two blocks of it are quite the same.
    float shade = 1.0 + (hash13(cell) - 0.5) * VARIATION;

    // Faded out once a block is small on screen. A pattern finer than a pixel is noise that
    // crawls as the camera moves, and there are no mip levels to fall back on when the detail
    // is a function rather than an image — so the fade is the whole of the answer. fwidth is how
    // much the surface coordinate changes between neighbouring pixels, which is exactly how
    // large a block is on screen.
    float onScreen = max(fwidth(inSurface).x, fwidth(inSurface).y);
    float detail = clamp(1.0 - (onScreen * 7.0), 0.0, 1.0);
    shade *= mix(1.0, patternShade(inPattern, inSurface, cell), detail);

    vec3 lit = inColor * light * occlusion * shade;

    // A thin darker line along every block edge, drawn from the surface coordinate rather than a
    // texture so it stays one pixel wide at any distance.
    vec2 toEdge = 0.5 - abs(fract(inSurface) - 0.5);
    vec2 pixels = toEdge / (fwidth(inSurface) + 0.0001);
    float edge = 1.0 - clamp(min(pixels.x, pixels.y), 0.0, 1.0);
    lit *= 1.0 - EDGE_DARKENING * edge;

    // The top of a see-through surface shows what is reflected in it as well as what is behind
    // it, and how much of each depends on the angle it is looked at from. Only upward faces:
    // what reads as a mirror is a lake seen from above it, and the side of a block of water seen
    // edge on is both a sliver and a surface no one expects anything of.
    if (inOpacity < 1.0 && inNormal.y > 0.5) {
        float reflected;
        vec3 mirrored = reflectionOn(inViewOffset, inNormal, reflected);
        lit = mix(lit, mirrored * scene.shadow.w, reflected);
    }

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
