shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_opaque;

uniform sampler2D y_data : filter_linear, repeat_disable;
uniform sampler2D u_data : filter_linear, repeat_disable;
uniform sampler2D v_data : filter_linear, repeat_disable;
uniform vec2 yuv_resolution = vec2(1920.0, 1080.0);
uniform vec4 color_profile = vec4(1.5748, 0.1873, 0.4681, 1.8556);
uniform bool full_color_range = true;

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

void fragment() {
	float y = texture(y_data, UV).r;
	float u = texture(u_data, UV).r;
	float v = texture(v_data, UV).r;

	ALBEDO = yuv_to_rgb(y, u, v);
	ALPHA = 1.0;
}
