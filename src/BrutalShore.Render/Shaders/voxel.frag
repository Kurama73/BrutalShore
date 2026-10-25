#version 450

layout(location = 0) in vec3 inColor;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inSurface;
layout(location = 3) in float inOcclusion;

layout(location = 0) out vec4 outColor;

// A fixed sun, deliberately off every axis so the three face orientations all get a different
// amount of light. With flat colours and no textures, that difference is most of what makes the
// shape of the terrain readable.
const vec3 SUN_DIRECTION = normalize(vec3(0.38, 0.84, 0.39));
const float AMBIENT = 0.34;
const float SUN_STRENGTH = 0.78;

// How dark a fully occluded corner gets. Ambient occlusion is the only contact shadow here, so it
// is carrying the weight of every crease and overhang in the world.
const float OCCLUSION_FLOOR = 0.42;

void main() {
    float lambert = max(dot(inNormal, SUN_DIRECTION), 0.0);
    float occlusion = mix(OCCLUSION_FLOOR, 1.0, inOcclusion);
    vec3 lit = inColor * (AMBIENT + SUN_STRENGTH * lambert) * occlusion;

    // One block's edge, darkened. Greedy meshing turns a flat field into a handful of large quads,
    // and without this the block grid vanishes along with them. Derived from the surface
    // coordinate's screen-space derivative so the line stays about a pixel wide at any distance
    // and fades out instead of aliasing when blocks get smaller than a pixel.
    vec2 toEdge = 0.5 - abs(fract(inSurface) - 0.5);
    vec2 pixels = toEdge / (fwidth(inSurface) + 0.0001);
    float edge = 1.0 - clamp(min(pixels.x, pixels.y), 0.0, 1.0);
    lit *= 1.0 - 0.14 * edge;

    outColor = vec4(lit, 1.0);
}
