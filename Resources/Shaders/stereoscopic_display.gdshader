shader_type spatial;
render_mode unshaded;

uniform sampler2D y_data : filter_linear, repeat_disable;
uniform sampler2D u_data : filter_linear, repeat_disable;
uniform sampler2D v_data : filter_linear, repeat_disable;
uniform vec2 yuv_resolution = vec2(1920.0, 1080.0);
uniform vec4 color_profile = vec4(1.5748, 0.1873, 0.4681, 1.8556);
uniform bool full_color_range = true;
uniform bool enabled = true;
uniform int display_mode = 0;

vec3 yuv_to_rgb(float y, float u, float v) {
	float Y, U, V;
	if (full_color_range) {
		Y = y;
		U = u - 0.5;
		V = v - 0.5;
	} else {
		Y = (y * 255.0 - 16.0) / 219.0;
		U = (u * 255.0 - 128.0) / 224.0;
		V = (v * 255.0 - 128.0) / 224.0;
	}
	float R = Y + color_profile.x * V;
	float G = Y - color_profile.y * U - color_profile.z * V;
	float B = Y + color_profile.w * U;
	return vec3(R, G, B);
}

vec3 sample_yuv(vec2 uv) {
	float y = texture(y_data, uv).r;
	float u = texture(u_data, uv).r;
	float v = texture(v_data, uv).r;
	return yuv_to_rgb(y, u, v);
}

void fragment() {
	if (!enabled) {
		ALBEDO = sample_yuv(UV);
	} else if (display_mode == 0) {
		bool is_left_side = UV.x < 0.5;
		vec2 local_uv = vec2(
			is_left_side ? UV.x * 2.0 : (UV.x - 0.5) * 2.0,
			UV.y
		);
		ALBEDO = sample_yuv(local_uv);
	} else if (display_mode == 1) {
		bool is_top_half = UV.y < 0.5;
		vec2 local_uv = vec2(
			UV.x,
			is_top_half ? UV.y * 2.0 : (UV.y - 0.5) * 2.0
		);
		ALBEDO = sample_yuv(local_uv);
	} else {
		ALBEDO = sample_yuv(UV);
	}
	ALPHA = 1.0;
}
