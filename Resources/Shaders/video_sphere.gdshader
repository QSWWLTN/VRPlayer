shader_type spatial;
render_mode cull_front, unshaded, depth_draw_never, blend_mix;

uniform sampler2D y_data : filter_linear, repeat_disable;
uniform sampler2D u_data : filter_linear, repeat_disable;
uniform sampler2D v_data : filter_linear, repeat_disable;
uniform vec2 yuv_resolution = vec2(1920.0, 1080.0);
uniform vec4 color_profile = vec4(1.5748, 0.1873, 0.4681, 1.8556);
uniform bool full_color_range = true;
uniform int projection_mode = 3;

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
	vec3 world_pos = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 dir = normalize(world_pos - CAMERA_POSITION_WORLD);

	float theta = atan(dir.x, -dir.z);
	float phi = acos(clamp(dir.y, -1.0, 1.0));
	vec2 uv = vec2(0.0);
	bool show = false;

	if (projection_mode == 1) {
		if (theta >= -1.5707963 && theta <= 1.5707963) {
			uv = vec2((theta + 1.5707963) / 3.14159265, phi / 3.14159265);
			show = true;
		}
	} else if (projection_mode == 2) {
		vec3 fwd = vec3(0.0, 0.0, -1.0);
		float cos_angle = dot(dir, fwd);
		if (cos_angle >= 0.0) {
			float angle = acos(cos_angle);
			if (angle <= 1.5707963) {
				float phi2 = atan(dir.y, dir.x);
				float r = (angle / 1.5707963) * 0.5;
				uv = vec2(0.5, 0.5) + r * vec2(cos(phi2), sin(phi2));
				if (length(uv - vec2(0.5, 0.5)) <= 0.5) {
					show = true;
				}
			}
		}
	} else if (projection_mode == 3) {
		uv = vec2(theta / 6.2831853 + 0.5, phi / 3.14159265);
		show = true;
	} else if (projection_mode == 4) {
		uv = vec2(theta / 6.2831853 + 0.5, phi / 3.14159265);
		if (int(VIEW_INDEX) == 0) {
			uv.y = uv.y * 0.5;
		} else {
			uv.y = uv.y * 0.5 + 0.5;
		}
		show = true;
	} else if (projection_mode == 5) {
		if (theta >= -1.5707963 && theta <= 1.5707963) {
			uv = vec2((theta + 1.5707963) / 3.14159265, phi / 3.14159265);
			if (int(VIEW_INDEX) == 0) {
				uv.x = uv.x * 0.5;
			} else {
				uv.x = uv.x * 0.5 + 0.5;
			}
			show = true;
		}
	}

	if (show) {
		float y = texture(y_data, uv).r;
		float u = texture(u_data, uv).r;
		float v = texture(v_data, uv).r;
		ALBEDO = yuv_to_rgb(y, u, v);
		ALPHA = 1.0;
	} else {
		ALBEDO = vec3(0.0);
		ALPHA = 0.0;
	}
}
