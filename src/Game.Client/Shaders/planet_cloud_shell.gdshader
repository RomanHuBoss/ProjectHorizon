shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix;

uniform vec4 cloud_color : source_color = vec4(0.92, 0.95, 1.0, 1.0);
uniform float cloud_density : hint_range(0.0, 1.0) = 0.55;
uniform float scroll_speed : hint_range(-0.2, 0.2) = 0.015;
uniform float layer_phase : hint_range(0.0, 12.0) = 0.0;

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 345.45));
    p += dot(p, p + 34.345);
    return fract(p.x * p.y);
}

float value_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

void fragment() {
    vec2 p = UV * 10.0 + vec2(TIME * scroll_speed, layer_phase);
    float n = value_noise(p) * 0.58 + value_noise(p * 2.13 + 7.1) * 0.30 +
        value_noise(p * 4.07 + 13.7) * 0.12;
    float threshold = mix(0.78, 0.34, cloud_density);
    float alpha = smoothstep(threshold, threshold + 0.18, n);
    ALBEDO = cloud_color.rgb;
    ALPHA = alpha * 0.62;
}
