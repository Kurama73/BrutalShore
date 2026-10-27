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
    vec4 middle;   // xyz: the box's middle relative to the camera, in metres. w: its turn, in radians
    vec4 halfSize; // xyz: half its width, height and depth, in metres. w: its tilt, in radians
    vec4 color;    // rgb: its colour, in sRGB. w: texture-array layer
    vec4 textureTint;
    vec4 sideRect;  // xy: corner, zw: size, as a fraction of the layer
    vec4 topRect;
    vec4 frontRect;
    vec4 backRect;
} push;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;

layout(location = 0) out vec3 outColor;
layout(location = 1) out vec3 outNormal;
layout(location = 2) out vec3 outViewOffset;
layout(location = 3) out vec2 outSurface;
layout(location = 4) flat out uint outTextureLayer;
layout(location = 5) flat out vec4 outRect;

vec3 srgbToLinear(vec3 encoded) {
    vec3 low = encoded / 12.92;
    vec3 high = pow((encoded + 0.055) / 1.055, vec3(2.4));
    return mix(low, high, step(vec3(0.04045), encoded));
}

void main() {
    // Turned about its own upright axis before it is placed. A dropped block that spins slowly
    // is the difference between an object lying in the world and a decal stuck to the ground.
    float turn = push.middle.w;
    float sine = sin(turn);
    float cosine = cos(turn);

    mat3 spin = mat3(
        vec3(cosine, 0.0, -sine),
        vec3(0.0, 1.0, 0.0),
        vec3(sine, 0.0, cosine)
    );

    // Nosed up or down about its own crosswise axis, before it is turned. Only one thing in the
    // game uses it — the arm in front of the camera, which has to point somewhere other than
    // along the ground — and a box that can only yaw is a box that cannot be held.
    float tilt = push.halfSize.w;
    float tiltSine = sin(tilt);
    float tiltCosine = cos(tilt);

    mat3 nose = mat3(
        vec3(1.0, 0.0, 0.0),
        vec3(0.0, tiltCosine, tiltSine),
        vec3(0.0, -tiltSine, tiltCosine)
    );

    spin = spin * nose;

    vec3 corner = spin * (inPosition * (push.halfSize.xyz * 2.0));

    outColor = srgbToLinear(push.color.rgb);
    // Normals turn with the box but are not scaled with it: a long thin box scaled normal is a
    // normal that no longer points away from the face it belongs to.
    outNormal = normalize(spin * inNormal);
    outViewOffset = push.middle.xyz + corner;
    // Where on the face this corner is, with zero at its top left seen from outside. A texture's
    // first row is its top one, so an upright face has to count down from the top of the box.
    outSurface = inNormal.x != 0.0
        ? inPosition.zy + 0.5
        : (inNormal.y != 0.0 ? inPosition.xz + 0.5 : inPosition.xy + 0.5);
    if (inNormal.y == 0.0) {
        outSurface.y = 1.0 - outSurface.y;
    }

    // The face's own rectangle on the layer. Facing and away are told apart rather than shared,
    // which is the difference between a creature with a face and one with two.
    outRect = inNormal.x != 0.0
        ? push.sideRect
        : (inNormal.y != 0.0 ? push.topRect : (inNormal.z > 0.0 ? push.frontRect : push.backRect));
    outTextureLayer = uint(push.color.w);

    gl_Position = scene.viewProjection * vec4(outViewOffset, 1.0);
}
