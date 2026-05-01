shader_type spatial;
render_mode cull_front, unshaded, depth_draw_never, blend_mix;

uniform sampler2D video_texture : filter_linear, repeat_disable;
uniform int projection_mode = 3;

void fragment() {
	vec3 world_pos = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 dir = normalize(world_pos - CAMERA_POSITION_WORLD);

	float theta = atan(dir.x, -dir.z);
	float phi = acos(clamp(dir.y, -1.0, 1.0));
	vec2 uv = vec2(0.0);
	bool show = false;

	// Mode 1: 180° 2D (单眼)
	if (projection_mode == 1) {
		if (theta >= -1.5707963 && theta <= 1.5707963) {
			uv = vec2((theta + 1.5707963) / 3.14159265, phi / 3.14159265);
			show = true;
		}
	// Mode 2: 鱼眼 180°
	} else if (projection_mode == 2) {
		vec3 fwd = vec3(0.0, 0.0, -1.0);
		float cos_angle = dot(dir, fwd);
		if (cos_angle >= 0.0) {
			float angle = acos(cos_angle);
			if (angle <= 1.5707963) {
				float phi2 = atan(dir.y, dir.x);
				float r = (angle / 1.5707963) * 0.5;
				uv = vec2(0.5, 0.5) + r * vec2(cos(phi2), sin(phi2));
				if (length(uv - vec2(0.5, 0.5)) <= 0.5) show = true;
			}
		}
	// Mode 3: 360° 2D (单眼)
	} else if (projection_mode == 3) {
		uv = vec2(theta / 6.2831853 + 0.5, phi / 3.14159265);
		show = true;
	// Mode 4: 360° 3D 立体 (上下格式 Top-Bottom)
	} else if (projection_mode == 4) {
		uv = vec2(theta / 6.2831853 + 0.5, phi / 3.14159265);
		// 关键修复：使用 int() 强制转换，避免 OpenGL ES 的严格类型报错
		if (int(VIEW_INDEX) == 0) {
			uv.y = uv.y * 0.5; // 左眼读取视频上半部分
		} else {
			uv.y = uv.y * 0.5 + 0.5; // 右眼读取视频下半部分
		}
		show = true;
	// Mode 5: 180° 3D 立体 (左右格式 Side-by-Side)
	} else if (projection_mode == 5) {
		if (theta >= -1.5707963 && theta <= 1.5707963) {
			uv = vec2((theta + 1.5707963) / 3.14159265, phi / 3.14159265);
			// 关键修复：使用 int() 强制转换
			if (int(VIEW_INDEX) == 0) {
				uv.x = uv.x * 0.5; // 左眼读取视频左半部分
			} else {
				uv.x = uv.x * 0.5 + 0.5; // 右眼读取视频右半部分
			}
			show = true;
		}
	}

	if (show) {
		vec4 color = texture(video_texture, uv);
		ALBEDO = color.rgb;
		ALPHA = 1.0;
	} else {
		ALBEDO = vec3(0.0);
		ALPHA = 0.0;
	}
}