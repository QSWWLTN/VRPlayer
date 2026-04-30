shader_type spatial;
render_mode unshaded;

uniform sampler2D video_texture_left;
uniform sampler2D video_texture_right;
uniform bool enabled = true;
uniform int display_mode = 0;

void fragment() {
    vec4 color;
    
    if (!enabled) {
        color = texture(video_texture_left, UV);
    } else if (display_mode == 0) {
        bool is_left_side = UV.x < 0.5;
        vec2 local_uv = vec2(
            is_left_side ? UV.x * 2.0 : (UV.x - 0.5) * 2.0,
            UV.y
        );
        
        if (is_left_side) {
            color = texture(video_texture_left, local_uv);
        } else {
            color = texture(video_texture_right, local_uv);
        }
    } else if (display_mode == 1) {
        bool is_top_half = UV.y < 0.5;
        vec2 local_uv = vec2(
            UV.x,
            is_top_half ? UV.y * 2.0 : (UV.y - 0.5) * 2.0
        );
        
        if (is_top_half) {
            color = texture(video_texture_left, local_uv);
        } else {
            color = texture(video_texture_right, local_uv);
        }
    } else {
        color = texture(video_texture_left, UV);
    }
    
    ALBEDO = color.rgb;
    ALPHA = color.a;
}
