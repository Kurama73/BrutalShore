#version 450

// Must match SceneUniforms on the C# side; see sky.vert.
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

layout(location = 0) in vec3 inDirection;

layout(location = 0) out vec4 outColor;

// Dave Hoskins, "Hash without Sine" (https://www.shadertoy.com/view/4djSRW). A sine-based hash
// gives different results per driver, which would make the stars move when the GPU changes.
float hash13(vec3 position) {
    vec3 scrambled = fract(position * 0.1031);
    scrambled += dot(scrambled, scrambled.zyx + 31.32);
    return fract((scrambled.x + scrambled.y) * scrambled.z);
}

// Shared with voxel.frag, which fogs towards the sky it would see in the same direction. The two
// have to agree or the far edge of the world shows as a band against the sky.
vec3 skyColorAt(float upwards) {
    // The exponent crowds the gradient into the lower half, which is where a real sky changes
    // fastest and where a player standing on the ground is looking.
    float height = pow(clamp(upwards, 0.0, 1.0), 0.42);
    vec3 above = mix(scene.horizonColor.rgb, scene.zenithColor.rgb, height);

    // Below the horizon, the same colours darkened: looking down should fade, not hit a seam.
    return mix(above * 0.42, above, smoothstep(-0.16, 0.0, upwards));
}

float starField(vec3 direction, float opacity) {
    if (opacity <= 0.0) {
        return 0.0;
    }

    // A grid on the direction vector, one candidate star per cell, jittered inside it so the
    // field reads as scattered rather than as the lattice it is.
    vec3 scaled = direction * 260.0;
    vec3 cell = floor(scaled);
    float pick = hash13(cell);
    if (pick < 0.974) {
        return 0.0;
    }

    vec3 jitter = vec3(hash13(cell + 11.0), hash13(cell + 23.0), hash13(cell + 37.0));
    float distance = length((scaled - cell) - jitter);
    float brightness = (pick - 0.974) / 0.026;
    return opacity * brightness * smoothstep(0.14, 0.0, distance);
}

void main() {
    vec3 direction = normalize(inDirection);
    vec3 color = skyColorAt(direction.y);

    color += vec3(starField(direction, scene.moonDirection.w));

    // The sun: a disc with a wide, weak halo around it. The disc comes out a few degrees across
    // rather than the real half degree, which at any sane resolution would be one pixel.
    float towardsSun = max(dot(direction, scene.sunDirection.xyz), 0.0);
    color += scene.sunColor.rgb * ((pow(towardsSun, 4500.0) * 9.0) + (pow(towardsSun, 10.0) * 0.30));

    // The moon only shows while it is above the horizon; it has no light of its own to give the
    // sky, so it is a disc and nothing else.
    float towardsMoon = max(dot(direction, scene.moonDirection.xyz), 0.0);
    float moonIsUp = smoothstep(-0.04, 0.06, scene.moonDirection.y);
    color += vec3(0.85, 0.86, 0.80) * pow(towardsMoon, 6000.0) * moonIsUp;

    // Under a liquid there is no sky to see: what the far plane shows is more of the liquid.
    // The same exposure voxel.frag applies, and it has to be the same number: the world fades
    // into this sky at its far edge, and any disagreement shows up as a band on the horizon.
    color *= scene.shadow.w;

    outColor = vec4(mix(color, scene.submerged.rgb, scene.submerged.a), 1.0);
}
