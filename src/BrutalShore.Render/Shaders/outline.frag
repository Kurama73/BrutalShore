#version 450

layout(push_constant) uniform Push {
    vec4 corner;
    vec4 color;
} push;

layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(push.color.rgb, 1.0);
}
