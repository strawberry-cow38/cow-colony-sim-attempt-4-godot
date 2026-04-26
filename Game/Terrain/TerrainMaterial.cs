using Godot;

namespace CowColonySim.Game.Terrain;

// Procedural grass material — value-noise lookup over world XZ blends two
// green tones, plus a sparser noise for darker patches. No external texture
// assets; the shader is the asset. All terrain (main + LOD background)
// shares one cached instance so material params can be tweaked in one place.
public static class TerrainMaterial
{
    private static ShaderMaterial? _cached;

    private const string ShaderCode = """
shader_type spatial;
render_mode cull_disabled;

uniform vec3 grass_dark : source_color = vec3(0.22, 0.34, 0.18);
uniform vec3 grass_mid  : source_color = vec3(0.36, 0.50, 0.24);
uniform vec3 grass_light: source_color = vec3(0.55, 0.66, 0.32);
uniform float fine_scale  = 0.6;
uniform float patch_scale = 0.05;
uniform float roughness_v = 0.95;

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

void fragment() {
    vec3 wp = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float fine  = vnoise(wp.xz * fine_scale);
    float patch = vnoise(wp.xz * patch_scale);
    vec3 grass = mix(grass_dark, grass_mid, fine);
    grass = mix(grass, grass_light, smoothstep(0.55, 0.9, patch));
    ALBEDO = grass;
    ROUGHNESS = roughness_v;
    METALLIC = 0.0;
}
""";

    public static ShaderMaterial Get()
    {
        if (_cached is not null) return _cached;
        var shader = new Shader { Code = ShaderCode };
        _cached = new ShaderMaterial { Shader = shader };
        return _cached;
    }
}
