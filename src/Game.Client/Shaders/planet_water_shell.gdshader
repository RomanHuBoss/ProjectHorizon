shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec4 water_color : source_color = vec4(0.06, 0.30, 0.48, 1.0);
uniform float wave_strength : hint_range(0.0, 0.4) = 0.08;

void vertex() {
    float wave = sin(UV.x * 46.0 + TIME * 0.65) * cos(UV.y * 34.0 - TIME * 0.48);
    VERTEX += NORMAL * wave * wave_strength;
}

void fragment() {
    float depth_tint = clamp(0.70 + UV.y * 0.28, 0.0, 1.0);
    ALBEDO = water_color.rgb * depth_tint;
    ROUGHNESS = 0.18;
    METALLIC = 0.05;
}
