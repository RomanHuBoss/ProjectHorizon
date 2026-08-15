shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix;

uniform vec4 atmosphere_color : source_color = vec4(0.28, 0.56, 0.92, 1.0);
uniform vec4 sunset_color : source_color = vec4(0.95, 0.47, 0.22, 1.0);
uniform float density : hint_range(0.0, 3.0) = 0.8;
uniform vec3 star_direction_world = vec3(-0.35, 0.75, 0.55);

void fragment() {
    vec3 normal_view = normalize(NORMAL);
    vec3 view_dir = normalize(VIEW);
    float rim = pow(clamp(1.0 - abs(dot(normal_view, view_dir)), 0.0, 1.0), 2.2);
    vec3 star_dir_view = normalize((VIEW_MATRIX * vec4(star_direction_world, 0.0)).xyz);
    float day = clamp(dot(normal_view, star_dir_view) * 0.5 + 0.5, 0.0, 1.0);
    ALBEDO = mix(sunset_color.rgb, atmosphere_color.rgb, day);
    ALPHA = clamp(rim * (0.16 + density * 0.18), 0.0, 0.72);
}
