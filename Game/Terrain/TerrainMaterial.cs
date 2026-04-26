using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

// Terrain shading. Geometry is locked to faceted 4-unshared-corners-per-tile,
// so the mesh ships flat per-tile vertex normals — but shading uses a SMOOTH
// normal computed in the fragment shader from a heightmap texture (one
// 32-bit-float Image per chunk, central-difference sampled). Net: silhouette
// stays AoE2-blocky, lighting is gradient-smooth across each tile, no
// 1-low-3-high "knife edge" on shadowed faces.
//
// One ShaderMaterial per chunk (each carries its own heightmap texture +
// chunk-size uniforms). The Shader resource is cached + shared.
public static class TerrainMaterial
{
    private static Shader? _cachedShader;

    private const string ShaderCode = """
shader_type spatial;
render_mode cull_disabled;

uniform sampler2D heightmap : filter_linear, repeat_disable;
uniform vec2 chunk_local_size;
uniform vec2 vert_count;
uniform float units_per_quanta;
uniform float units_per_tile;

uniform vec3 grass_dark : source_color = vec3(0.22, 0.34, 0.18);
uniform vec3 grass_mid  : source_color = vec3(0.36, 0.50, 0.24);
uniform vec3 grass_light: source_color = vec3(0.55, 0.66, 0.32);
uniform float fine_scale  = 0.6;
uniform float patch_scale = 0.05;
uniform float roughness_v = 0.95;

// VERTEX in fragment() is view-space in Godot 4. Capture model-space and
// world-space positions in vertex() and ship them down via varyings so
// the noise + heightmap UV stay locked to the mesh, not to the camera.
varying vec3 v_local;
varying vec3 v_world;

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

void vertex() {
    v_local = VERTEX;
    v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    vec2 local_xz = v_local.xz;
    vec2 uv = clamp(local_xz / chunk_local_size, vec2(0.0), vec2(1.0));

    vec2 step_uv = 1.0 / vert_count;
    float hL = textureLod(heightmap, uv - vec2(step_uv.x, 0.0), 0.0).r;
    float hR = textureLod(heightmap, uv + vec2(step_uv.x, 0.0), 0.0).r;
    float hD = textureLod(heightmap, uv - vec2(0.0, step_uv.y), 0.0).r;
    float hU = textureLod(heightmap, uv + vec2(0.0, step_uv.y), 0.0).r;

    float dHx = (hR - hL) * units_per_quanta / (2.0 * units_per_tile);
    float dHz = (hU - hD) * units_per_quanta / (2.0 * units_per_tile);
    vec3 n_local = normalize(vec3(-dHx, 1.0, -dHz));
    NORMAL = (VIEW_MATRIX * MODEL_MATRIX * vec4(n_local, 0.0)).xyz;

    float fine  = vnoise(v_world.xz * fine_scale);
    float patch = vnoise(v_world.xz * patch_scale);
    vec3 grass = mix(grass_dark, grass_mid, fine);
    grass = mix(grass, grass_light, smoothstep(0.55, 0.9, patch));
    ALBEDO = grass;
    ROUGHNESS = roughness_v;
    METALLIC = 0.0;
}
""";

    public static ShaderMaterial CreateForField(Heightfield field, float unitsPerTile)
    {
        _cachedShader ??= new Shader { Code = ShaderCode };

        var tex = BuildHeightmapTexture(field);
        var unitsPerQuanta = TerrainConstants.VerticalQuantumMetres
                           * (CowColonySim.Sim.SimConstants.GodotUnitsPerTile / CowColonySim.Sim.SimConstants.MetersPerTile);

        var mat = new ShaderMaterial { Shader = _cachedShader };
        mat.SetShaderParameter("heightmap", tex);
        mat.SetShaderParameter("chunk_local_size", new Vector2(
            (field.VertWidth - 1) * unitsPerTile,
            (field.VertHeight - 1) * unitsPerTile));
        mat.SetShaderParameter("vert_count", new Vector2(field.VertWidth, field.VertHeight));
        mat.SetShaderParameter("units_per_quanta", unitsPerQuanta);
        mat.SetShaderParameter("units_per_tile", unitsPerTile);
        return mat;
    }

    private static ImageTexture BuildHeightmapTexture(Heightfield field)
    {
        var w = field.VertWidth;
        var h = field.VertHeight;
        var bytes = new byte[w * h * sizeof(float)];
        var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes);
        var src = field.AsReadOnlySpan();
        for (var i = 0; i < src.Length; i++)
        {
            floats[i] = src[i];
        }
        var img = Image.CreateFromData(w, h, false, Image.Format.Rf, bytes);
        return ImageTexture.CreateFromImage(img);
    }
}
