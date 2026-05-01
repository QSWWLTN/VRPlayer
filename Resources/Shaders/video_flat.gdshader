shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_opaque;

uniform samplerExternalOES video_texture : filter_linear, repeat_disable;

void fragment() {
    vec4 color = texture(video_texture, UV);
    ALBEDO = color.rgb;
    ALPHA = 1.0;
}
