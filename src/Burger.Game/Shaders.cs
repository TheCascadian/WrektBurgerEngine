namespace Burger.Runtime;

/// <summary>GLSL 330 shaders for the map.</summary>
internal static class Shaders
{
    // Shared uniforms plus the swell that lifts the water vertices: smooth value noise that repeats a whole number of times across the
    // map width (the three copies of the flat map meet without a crack), bent by a slow warp so the sea never settles into a pattern.
    private const string WaveGlsl = @"
uniform float time;
uniform vec2 worldSize;
float whash(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float pn(vec2 p, float freq)
{
    float period = max(2.0, floor(worldSize.x * freq + 0.5));
    vec2 q = vec2(p.x * period / worldSize.x, p.y * freq);
    vec2 i = floor(q), f = fract(q);
    vec2 u = f * f * (3.0 - 2.0 * f);
    float x0 = mod(i.x, period), x1 = mod(i.x + 1.0, period);
    return mix(mix(whash(vec2(x0, i.y)), whash(vec2(x1, i.y)), u.x), mix(whash(vec2(x0, i.y + 1.0)), whash(vec2(x1, i.y + 1.0)), u.x), u.y);
}
float waveLift(vec2 p)
{
    float t = time * 0.35;
    p += (vec2(pn(p + vec2(0.0, t), 0.02), pn(p + vec2(t, 5.0), 0.02)) - 0.5) * 9.0;
    return (pn(p + vec2(t * 1.3, t * 0.7), 0.07) - 0.5) * 1.6 + (pn(p - vec2(t * 0.8, t * 1.1), 0.16) - 0.5) * 0.8;
}
";

    public const string TerrainVertex = "#version 330\n" + WaveGlsl + @"
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;
uniform mat4 mvp;
uniform mat4 matModel;
uniform mat4 matNormal;
uniform float globe;        // 1: bend the flat map onto a sphere (the fragment shader still reads the flat map position)
uniform float globeRadius;
uniform float heightScale;
uniform sampler2D texture0; // map data (alpha: coast closeness, 245 on lake water)
uniform vec2 mapSize;
uniform float seaH;      // sea level in world height
uniform float waveAmp;   // swell height in world units (0: flat)
out vec2 fragTexCoord;
out vec3 fragNormal;
out vec3 fragPos;    // flat map position: every map lookup, border and highlight uses this, in both modes
out vec3 fragWorld;  // where the fragment really is (the sphere in globe mode)
out vec3 fragE;      // east, up and south of the map surface here, in world space (flat: the world axes)
out vec3 fragU;
out vec3 fragS;
void main()
{
    fragTexCoord = vertexTexCoord;
    fragNormal = normalize(vec3(matNormal * vec4(vertexNormal, 0.0)));
    vec3 fp = vec3(matModel * vec4(vertexPosition, 1.0));
    fragPos = fp;
    // water is the same mesh as the land: vertices at sea level ride the swell; land and the coast fade it out
    float gap = 2.0 / 255.0 * heightScale;
    float lift = 0.0;
    float vy = vertexPosition.y + matModel[3].y - seaH;
    float ww = waveAmp > 0.0 ? 1.0 - smoothstep(0.25 * gap, 0.75 * gap, vy) : 0.0;
    if (ww > 0.0)
    {
        // open sea: full swell. Near a coast it calms, and on a lake it is gone (the endless sea mesh sits below sea level and has no map data)
        float calm = 1.0;
        if (vy > -0.015)
        {
            float a = texelFetch(texture0, ivec2(clamp(vertexTexCoord * mapSize, vec2(0.0), mapSize - 1.0)), 0).a * 255.0;
            calm = abs(a - 245.0) < 2.0 ? 0.0 : 1.0 - 0.7 * clamp(a / 240.0, 0.0, 1.0);
        }
        if (calm > 0.0) lift = waveAmp * waveLift(fp.xz) * ww * calm;
    }
    vec3 vp = vertexPosition + vec3(0.0, lift, 0.0);
    fp.y += lift;
    if (globe > 0.5)
    {
        float lon = (fp.x / worldSize.x - 0.5) * 6.28318530718;
        float lat = (0.5 - fp.z / worldSize.y) * 3.14159265359;
        float cl = cos(lat), sl = sin(lat), co = cos(lon), so = sin(lon);
        fragU = vec3(cl * so, sl, cl * co);
        fragE = vec3(co, 0.0, -so);
        fragS = vec3(sl * so, -cl, sl * co);
        fragWorld = fragU * (globeRadius + fp.y);
        // the map's columns are oversampled towards the poles, so east-west height differences there are noise: fade that tilt out
        fragNormal = normalize(vec3(fragNormal.x * clamp(cl * 2.0, 0.0, 1.0), fragNormal.y, fragNormal.z));
        gl_Position = mvp * vec4(fragWorld, 1.0);
    }
    else
    {
        fragE = vec3(1.0, 0.0, 0.0);
        fragU = vec3(0.0, 1.0, 0.0);
        fragS = vec3(0.0, 0.0, 1.0);
        fragWorld = fp;
        gl_Position = mvp * vec4(vp, 1.0);
    }
}
";

    public const string TerrainFragment = "#version 330\n" + WaveGlsl + @"
in vec2 fragTexCoord;
in vec3 fragNormal;
in vec3 fragPos;
in vec3 fragWorld;
in vec3 fragE;
in vec3 fragU;
in vec3 fragS;
uniform vec3 sunDirU;     // direction to the sun, set from the game date and hour
uniform float globe;

uniform sampler2D texture0; // map data: RG = location index + 1, B = terrain index, A = land: river (255) / water: closeness to the coast
uniform sampler2D texture1; // per-location lookup: block0 color, block1 owner/state ids, block2 stripe color + flags
uniform sampler2D texture2; // terrain texture atlas
uniform sampler2D texture3; // optional normal map (only read when hasNormalMap is 1)
uniform sampler2D texture4; // river coverage at half the map resolution (mipmapped)
uniform sampler2D texture5; // terrain weights, ids 0-3, at half the map resolution (the blend kernel baked in)
uniform sampler2D texture6; // terrain weights, ids 4-7
uniform float hasBlendTex;
uniform float hasNormalMap;
uniform float heightScale;  // world height of the highest point (heightmap value 255)
uniform vec2 latRange;      // latitude (degrees) of the top and bottom edge of the map
uniform vec4 colDiffuse;
uniform vec2 mapSize;
uniform vec2 lutSize;
uniform float blockRows;
uniform float atlasGrid;
uniform float tileScale;
uniform vec2 atlasTile;   // texture size in pixels, wrapped border in pixels
uniform vec3 waterColor;
uniform vec3 riverColor;
uniform float borderScale;
uniform float stateBorders; // 1 up close, fades to 0 when zoomed out
uniform float showProvinceBorders;
uniform float geoBase;       // 1: draw the start-layout province borders over the map (lut block 3)
uniform float tintStrength;
uniform float stripePeriod;  // occupation stripes: width in world units, steps by powers of two with the zoom (never a per pixel value)
uniform float fogStrength;   // 0 when zoomed far out (strategic view)   // how strongly map mode colors cover the terrain (stronger when zoomed out)
uniform vec3 viewPos;

out vec4 finalColor;

// flags (lut block 2 alpha): 1 water, 2 occupied stripes, 4 selected, 8 hovered, 16 lake, 32 hidden by fog of war, 64 highlighted state, 128 demilitarized zone

// the map wraps around east-west (the flat map repeats, the globe closes), north and south end at the poles
ivec2 clampPix(ivec2 p)
{
    int w = int(mapSize.x);
    int x = p.x - (p.x / w) * w;
    if (x < 0) x += w;
    return ivec2(x, clamp(p.y, 0, int(mapSize.y) - 1));
}

int locAt(ivec2 p)
{
    vec4 c = texelFetch(texture0, clampPix(p), 0);
    return int(c.r * 255.0 + 0.5) + int(c.g * 255.0 + 0.5) * 256 - 1;
}

vec4 lut(int idx, int block)
{
    if (idx < 0) return vec4(0.0);
    int w = int(lutSize.x);
    ivec2 p = ivec2(idx - (idx / w) * w, idx / w + block * int(blockRows));
    return texelFetch(texture1, p, 0);
}

int ownerOf(int idx) { vec4 v = lut(idx, 1); return int(v.r * 255.0 + 0.5) + int(v.g * 255.0 + 0.5) * 256; }
int stateOf(int idx) { vec4 v = lut(idx, 1); return int(v.b * 255.0 + 0.5) + int(v.a * 255.0 + 0.5) * 256; }
int baseOf(int idx) { vec4 v = lut(idx, 3); return int(v.r * 255.0 + 0.5) + int(v.g * 255.0 + 0.5) * 256; }
int flagsOf(int idx) { if (idx < 0) return 1; return int(lut(idx, 2).a * 255.0 + 0.5); }

float terrainAt(ivec2 p) { return floor(texelFetch(texture0, clampPix(p), 0).b * 255.0 + 0.5); }

vec3 atlas(float t, vec2 uv)
{
    vec2 tile = vec2(mod(t, atlasGrid), floor(t / atlasGrid));
    float cell = atlasTile.x + 2.0 * atlasTile.y;
    vec2 local = (fract(uv) * atlasTile.x + atlasTile.y) / cell;
    vec2 g = uv * (atlasTile.x / cell) / atlasGrid;
    return textureGrad(texture2, (tile + local) / atlasGrid, dFdx(g), dFdy(g)).rgb;
}

// hash without sine (stable on every GPU)
float hash(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float noise(vec2 p)
{
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}
float vhash3(vec3 p) { p = fract(p * 0.1031); p += dot(p, p.zyx + 31.32); return fract((p.x + p.y) * p.z); }
float vnoise3(vec3 p)
{
    vec3 i = floor(p), f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(vhash3(i), vhash3(i + vec3(1, 0, 0)), u.x), mix(vhash3(i + vec3(0, 1, 0)), vhash3(i + vec3(1, 1, 0)), u.x), u.y),
               mix(mix(vhash3(i + vec3(0, 0, 1)), vhash3(i + vec3(1, 0, 1)), u.x), mix(vhash3(i + vec3(0, 1, 1)), vhash3(i + vec3(1, 1, 1)), u.x), u.y), u.z);
}

// ripples stretched along the flow: x runs downstream, y across the channel
float streak(vec2 q, float t, float speed)
{
    return noise(vec2(q.x * 1.3 - t * speed, q.y * 4.5)) * 0.6 + noise(vec2(q.x * 3.2 - t * speed * 1.7, q.y * 10.0)) * 0.4;
}

// Water after the Water + Terrain shader by Frank Hugenroth (Shadertoy, CC BY-NC-SA 3.0): crossing ocean swells plus folded-noise
// ripples; the slope of this field is the water normal. T is the water clock (0 on a lake: it never moves).
const mat2 stM = mat2(0.72, -1.60, 1.60, 0.72);
// value noise with its slope: x is the value, yz the gradient
vec3 noised(vec2 p)
{
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f), du = 6.0 * f * (1.0 - f);
    float a = hash(i), b = hash(i + vec2(1, 0)), c = hash(i + vec2(0, 1)), d = hash(i + vec2(1, 1));
    float k1 = b - a, k2 = c - a, k4 = a - b - c + d;
    return vec3(a + k1 * u.x + k2 * u.y + k4 * u.x * u.y, du * vec2(k1 + k4 * u.y, k2 + k4 * u.x));
}
// The slope of stWater, worked out exactly in one pass (the three evaluations of a finite difference cost three times as much)
vec2 stWaterGrad(vec2 p, float detail, float T)
{
    vec3 n0 = noised(p), n1 = noised(p * stM), n2 = noised(p * stM * stM);
    float f = 0.6 * n0.x + 0.25 * n1.x + 0.1666 * n2.x;
    vec2 gf = 0.6 * n0.yz + 0.25 * (stM * n1.yz) + 0.1666 * ((stM * stM) * n2.yz);
    vec3 nm = noised(p * 0.05 + 4.0);
    float mA = nm.x; vec2 gm = 0.05 * nm.yz;
    vec3 na = noised(p * 0.3), nb = noised(p * 0.27 + 9.0);
    float a1 = 2.48 * (p.x + p.y) + T * 1.6 + na.x * 6.0;
    float a2 = 4.0 * dot(p, vec2(-0.74, 0.43)) - T * 1.1 + nb.x * 6.0;
    vec2 ga1 = vec2(2.48) + 1.8 * na.yz, ga2 = 4.0 * vec2(-0.74, 0.43) + 1.62 * nb.yz;
    float s1 = sin(a1), s2 = sin(a2);
    float mix12 = s1 * mA + s2 * (1.0 - mA);
    vec2 gmix = cos(a1) * ga1 * mA + s1 * gm + cos(a2) * ga2 * (1.0 - mA) - s2 * gm;
    vec2 g = 0.5 * (gf * mix12 + f * gmix);
    // ripples: folded noise; the chain rule carries the slope back through the rotations and scalings of the octaves
    vec2 shift1 = 0.001 * vec2(T * 320.0, T * 240.0);
    mat2 J = mat2(0.5);
    vec2 q = p * 0.5;
    float amp = 1.0, sp = 0.5;
    int n = 4 + int(detail * 3.0 + 0.5);
    vec2 gr = vec2(0.0);
    for (int i = 0; i < 7; i++)
    {
        if (i >= n) break;
        q = stM * q * 0.947; J = (stM * J) * 0.947;
        vec3 nn = noised(q + shift1 * sp);
        float sa = (nn.x - 0.5) * 2.0;
        gr -= amp * sign(sin(sa)) * cos(sa) * 2.0 * (nn.yz * J);
        amp *= 0.59; sp *= -1.329;
    }
    return g + gr * 0.6;
}
float stNautic(vec2 p, float T)
{
    p *= 18.0;
    float f = 0.0, amp = 1.0, sp = 0.5;
    for (int i = 0; i < 3; i++) { p = stM * p * 1.2; f += amp * abs(smoothstep(0.0, 1.0, noise(p + T * sp)) - 0.5); amp *= 0.5; sp *= -1.227; }
    return pow(1.0 - f, 5.0);
}

// Distance in screen pixels from a smoothed border: m0..m3 say whether each of the 4 nearest map pixels
// belongs to the same region as this fragment. The bilinear blend of those masks crosses 0.5 on a smooth
// curve instead of the pixel staircase, so borders look drawn rather than pixelated.
float edgePx(float m0, float m1, float m2, float m3, vec2 f, float texelsPerPixel)
{
    float w = mix(mix(m0, m1, f.x), mix(m2, m3, f.x), f.y);
    if (w > 0.999) return 1e4;
    vec2 g = vec2((1.0 - f.y) * (m1 - m0) + f.y * (m3 - m2), (1.0 - f.x) * (m2 - m0) + f.x * (m3 - m1));
    return max(w - 0.5, 0.0) / max(length(g), 0.05) / texelsPerPixel;
}
float line(float d, float halfWidth) { return 1.0 - smoothstep(halfWidth - 0.7, halfWidth + 0.7, d); }

void main()
{
    vec2 pixU = fragPos.xz / worldSize * mapSize;
    // on the globe a map column narrows with the cosine of the latitude, so east-west map pixels per screen pixel shrink by that much (this is what pinched the poles)
    float cosLat = globe > 0.5 ? max(cos((0.5 - fragPos.z / worldSize.y) * 3.14159265), 0.02) : 1.0;
    float tpp = max(max(fwidth(pixU.x) * cosLat, fwidth(pixU.y)), 1e-4); // map pixels per screen pixel (from the unwrapped position: no jump at the seam)
    vec2 pix = vec2(pixU.x - mapSize.x * floor(pixU.x / mapSize.x), pixU.y);

    // the 4 map pixels around this fragment
    vec2 pp = pix - 0.5;
    ivec2 b = ivec2(floor(pp));
    vec2 f = fract(pp);
    ivec2 o4[4] = ivec2[4](ivec2(0, 0), ivec2(1, 0), ivec2(0, 1), ivec2(1, 1));
    float wt[4] = float[4]((1.0 - f.x) * (1.0 - f.y), f.x * (1.0 - f.y), (1.0 - f.x) * f.y, f.x * f.y);
    int L[4]; int O[4]; int ST[4]; int WA[4]; int FL[4];
    L[0] = locAt(b); L[1] = locAt(b + o4[1]); L[2] = locAt(b + o4[2]); L[3] = locAt(b + o4[3]);
    if (L[1] == L[0] && L[2] == L[0] && L[3] == L[0])
    {
        // all four map pixels are one location (most of the screen): one lookup of its data instead of four
        vec4 v1 = lut(L[0], 1);
        int fl = flagsOf(L[0]);
        int ow = int(v1.r * 255.0 + 0.5) + int(v1.g * 255.0 + 0.5) * 256, sg = int(v1.b * 255.0 + 0.5) + int(v1.a * 255.0 + 0.5) * 256;
        for (int k = 0; k < 4; k++) { O[k] = ow; ST[k] = sg; FL[k] = fl; WA[k] = fl & 1; }
    }
    else
    {
        for (int k = 0; k < 4; k++)
        {
            vec4 v1 = lut(L[k], 1);
            O[k] = int(v1.r * 255.0 + 0.5) + int(v1.g * 255.0 + 0.5) * 256;
            ST[k] = int(v1.b * 255.0 + 0.5) + int(v1.a * 255.0 + 0.5) * 256;
            FL[k] = flagsOf(L[k]);
            WA[k] = FL[k] & 1;
        }
    }
    // this fragment belongs to the location with the most weight (smooth outlines instead of pixel steps)
    int best = 0; float bestW = -1.0;
    for (int k = 0; k < 4; k++)
    {
        float s = 0.0;
        for (int j = 0; j < 4; j++) if (L[j] == L[k]) s += wt[j];
        if (s > bestW) { bestW = s; best = k; }
    }
    int loc = L[best];
    int owner = O[best];
    int st = ST[best];
    int flags = FL[best];
    // outside the map (the endless sea): open water, fogged like the map edge next to it
    bool outside = pix.x < 0.0 || pix.y < 0.0 || pix.x > mapSize.x || pix.y > mapSize.y;
    if (outside) flags = 1 | (flags & 32);
    bool water = (flags & 1) != 0;

    float mProv[4]; float mCoast[4]; float mCountry[4]; float mState[4];
    for (int k = 0; k < 4; k++)
    {
        mProv[k] = L[k] == loc ? 1.0 : 0.0;
        mCoast[k] = WA[k] == WA[best] ? 1.0 : 0.0;
        bool otherLand = WA[k] == 0;
        mCountry[k] = (otherLand && O[k] != owner) ? 0.0 : 1.0;
        mState[k] = (otherLand && ST[k] != st) ? 0.0 : 1.0;
    }
    float dProv = edgePx(mProv[0], mProv[1], mProv[2], mProv[3], f, tpp);
    float dCoast = edgePx(mCoast[0], mCoast[1], mCoast[2], mCoast[3], f, tpp);
    float dCountry = water ? 1e4 : edgePx(mCountry[0], mCountry[1], mCountry[2], mCountry[3], f, tpp);
    float dState = water ? 1e4 : edgePx(mState[0], mState[1], mState[2], mState[3], f, tpp);
    if (outside) { dProv = 1e4; dCoast = 1e4; }
    float dBase = 1e4;
    if (geoBase > 0.5 && !water && !outside)
    {
        int bb = baseOf(loc);
        float mB[4];
        for (int k = 0; k < 4; k++) mB[k] = (WA[k] == 0 && baseOf(L[k]) != bb) ? 0.0 : 1.0;
        dBase = edgePx(mB[0], mB[1], mB[2], mB[3], f, tpp);
    }

    // zoomed far out several map pixels share one screen pixel: look further away so borders stay visible
    float far = smoothstep(0.6, 1.4, tpp);
    if (far > 0.0 && !outside)
    {
        ivec2 ip = ivec2(floor(pix));
        ivec2 offs[4] = ivec2[4](ivec2(1, 0), ivec2(-1, 0), ivec2(0, 1), ivec2(0, -1));
        int d = max(1, int(tpp + 0.5));
        for (int k = 0; k < 4; k++)
        {
            int o = locAt(ip + offs[k] * d);
            if (o == loc) continue;
            dProv = min(dProv, 0.5);
            bool otherWater = (flagsOf(o) & 1) != 0;
            if (water != otherWater) dCoast = min(dCoast, 0.5);
            else if (!water && ownerOf(o) != owner) dCountry = min(dCountry, 0.5);
            else if (!water && stateOf(o) != st) dState = min(dState, 0.5);
        }
    }

    // globe: the sun sits over the viewer's shoulder so the half you see is lit like the flat map
    vec3 sunDir = sunDirU; // fixed in the world and driven by the game clock only: the camera never moves the sun
    vec3 viewDir = normalize(viewPos - fragWorld);
    // how far the sun is above this spot's horizon: the unlit half of the world stays dark whatever the camera does
    float sunUp = smoothstep(-0.12, 0.3, globe > 0.5 ? dot(fragU, sunDir) : sunDir.y);
    vec4 data = texelFetch(texture0, clampPix(ivec2(floor(pix))), 0);
    float detail = 1.0 - smoothstep(0.02, 0.12, fwidth(fragPos.x) * cosLat); // fades small details when zoomed out

    vec3 col;
    if (water)
    {
        // deep water is dark blue, water near the coast lighter and greener
        bool lake = (flags & 16) != 0;
        // closeness to the coast, 1 at the shore and 0 in the open sea, blended between the four map pixels so it has no steps
        float cl = 0.0;
        if (!outside && !lake && tpp > 2.5) cl = clamp(data.a * 255.0 / 240.0, 0.0, 1.0); // far out: one fetch instead of four
        else if (!outside && !lake)
            for (int k = 0; k < 4; k++)
                cl += wt[k] * (WA[k] == 0 ? 1.0 : clamp(texelFetch(texture0, clampPix(b + o4[k]), 0).a * 255.0 / 240.0, 0.0, 1.0));
        float shallow = lake ? 0.25 : cl * cl;
        vec3 shore = mix(waterColor, vec3(0.30, 0.62, 0.66), 0.55) * 1.15;
        vec2 wp = fragPos.xz;
        if (globe > 0.5)
        {
            vec3 an = abs(normalize(fragWorld)); // surface coordinates instead of map coordinates (those stretch into rays at the poles)
            wp = (an.y >= an.x && an.y >= an.z) ? fragWorld.xz : (an.x >= an.z ? fragWorld.yz : fragWorld.xy);
        }
        float day = sunUp;
        // the water clock: slow, and stopped on a lake
        float T = lake ? 0.0 : time * 0.5;
        vec3 wn = fragU;
        if (detail > 0.02 && !lake)
        {
            // open water is bent by a slow warp so its pattern never repeats; the ripples are strongest at the coast and fade
            // smoothly into calm open sea; a lake is a flat mirror
            vec2 warp = (vec2(noise(wp * 0.045 + vec2(T * 0.02, 3.3)), noise(wp * 0.045 + vec2(8.1, -T * 0.017))) - 0.5) * 10.0;
            vec2 wpos = (wp + warp) * 0.35;
            vec2 gw = stWaterGrad(wpos, detail, T);
            float tilt = (0.25 + 0.75 * detail) * (0.55 + 0.45 * smoothstep(0.0, 1.0, cl));
            vec3 wl = vec3(-gw.x * 0.02 * tilt, 0.125, -gw.y * 0.02 * tilt);
            wn = normalize(fragE * wl.x + fragU * wl.y + fragS * wl.z);
        }
        // depth: 0 at the coast, 1 in the open sea; the seabed shows through the shallows
        float depth = 1.0 - shallow;
        float coastfade = smoothstep(0.0, 1.0, clamp(depth / 0.5, 0.0, 1.0));
        float coastfade2 = clamp(depth / 0.9, 0.0, 1.0);
        vec3 bed = vec3(0.52, 0.47, 0.38) * (0.55 + 0.45 * day);
        float lum = dot(bed, vec3(0.2126, 0.7152, 0.0722));
        vec3 wc = mix(mix(vec3(0.43, 0.60, 0.66) * lum, waterColor * 0.45, 0.5), waterColor * 0.30, smoothstep(0.0, 1.0, coastfade2));
        float grad = dot(wn, sunDir);
        float spec = pow(max(grad, 0.0), 15.0), spec2 = pow(max(grad, 0.0), 120.0);
        float spec1 = smoothstep(0.0, 1.0, pow(max(dot(fragU, sunDir), 0.0), 5.0));
        wc *= 2.8;
        wc += ((grad - 1.0) * 0.5 + spec) * 0.25 * day;
        wc /= (1.0 + spec1 * 1.25);
        wc += spec2 * vec3(1.3, 1.3, 0.9) * day * (0.3 + 0.7 * detail);
        // a faint drifting streak in the shallows (none on a lake)
        if (!lake && detail > 0.3) wc += coastfade * (1.0 - coastfade2) * vec3(0.5, 0.6, 0.7) * stNautic(wp * 0.35 / 15.0, T) * 0.10 * day;
        wc *= 0.12 + 0.88 * day;
        col = mix(bed * (0.5 + 0.5 * day), wc, mix(0.6, 1.0, coastfade));
        // foam along the coast, broken up by noise so it laps in and out
        if (!lake)
        {
            float lap = noise(wp * 0.9 + vec2(T * 0.2, -T * 0.15));
            col = mix(col, vec3(0.80, 0.86, 0.86) * (0.10 + 0.90 * day), line(dCoast + (lap - 0.5) * 3.0, 1.5) * 0.14);
        }
        if (lake) col = mix(col, shore, 0.4);
    }
    else
    {
        // blend the four nearest terrain texels so terrain types fade into each other
        vec2 tuv = fragPos.xz * tileScale;
        if (globe > 0.5)
        {
            // the map's columns collapse at the poles, so tile the texture on the true surface: project along the sphere's dominant axis
            vec3 an = abs(normalize(fragWorld));
            vec2 pl = (an.y >= an.x && an.y >= an.z) ? fragWorld.xz : (an.x >= an.z ? fragWorld.yz : fragWorld.xy);
            tuv = pl * tileScale;
        }
        // gather the terrain of a wide neighborhood (6x6 map pixels) with a smooth kernel, around a position that noise has bent, so the
        // edge is neither a map pixel staircase nor a straight fade; weights are summed per terrain, then each terrain is sampled once
        // zoomed far out (many map pixels per screen pixel) the blend cannot be seen: take the one terrain under the pixel (mip-mapped)
        float farMix = smoothstep(1.5, 3.0, tpp);
        col = vec3(0.0);
        if (farMix < 1.0)
        {
            vec2 wq = pp + (vec2(noise(pix * 0.8 + 3.1), noise(pix * 0.8 + 17.7)) - 0.5) * 2.6
                         + (vec2(noise(pix * 2.1 + 5.0), noise(pix * 2.1 + 9.0)) - 0.5) * 1.1;
            float tId[8]; float tW[8]; int tN = 0;
            if (hasBlendTex > 0.5)
            {
                // the kernel below, already summed per terrain for every half-resolution texel: two filtered lookups at the bent position
                vec2 buv = (wq + 0.5) / mapSize;
                vec4 w0 = texture(texture5, buv), w1 = texture(texture6, buv);
                float bw[8] = float[8](w0.r, w0.g, w0.b, w0.a, w1.r, w1.g, w1.b, w1.a);
                for (int k = 0; k < 8; k++)
                    if (bw[k] > 0.012) { tId[tN] = float(k); tW[tN] = bw[k]; tN++; }
            }
            else
            {
            ivec2 qb = ivec2(floor(wq));
            for (int j = -2; j <= 3; j++)
                for (int i = -2; i <= 3; i++)
                {
                    ivec2 c = qb + ivec2(i, j);
                    vec2 d = (vec2(c) - wq) / 2.7;
                    float kw = max(1.0 - dot(d, d), 0.0);
                    kw *= kw;
                    if (kw <= 0.0) continue;
                    float id = terrainAt(c);
                    int k = 0;
                    while (k < tN && tId[k] != id) k++;
                    if (k == tN) { if (tN >= 8) continue; tId[tN] = id; tW[tN] = 0.0; tN++; }
                    tW[k] += kw;
                }
            }
            float tot = 0.0;
            for (int k = 0; k < tN; k++) tot += tW[k];
            vec3 tC[8]; float tH[8]; float tMax = 0.0;
            for (int k = 0; k < tN; k++)
            {
                tW[k] /= max(tot, 1e-4);
                if (tW[k] < 0.004) { tW[k] = 0.0; continue; }
                tC[k] = atlas(tId[k], tuv);
                // a texture's own brightness lets its bumps push through its neighbor (height blending)
                tH[k] = tW[k] + dot(tC[k], vec3(0.3, 0.59, 0.11)) * 0.25;
                tMax = max(tMax, tH[k]);
            }
            col = vec3(0.0); float cw = 0.0;
            for (int k = 0; k < tN; k++)
            {
                if (tW[k] <= 0.0) continue;
                float w = max(tH[k] - (tMax - 0.5), 0.0);
                col += tC[k] * w; cw += w;
            }
            col /= max(cw, 1e-4);
        }
        if (farMix > 0.0)
        {
            vec3 farCol = atlas(terrainAt(ivec2(floor(pix))), tuv);
            col = farMix >= 1.0 ? farCol : mix(col, farCol, farMix);
        }
        // large scale variation hides the texture repeating
        // (on the globe this noise is taken in 3D: in map coordinates it stretches into rays at the poles)
        if (farMix >= 1.0) { }
        else if (globe > 0.5) col *= 0.90 + 0.20 * vnoise3(fragWorld * 0.11) * (0.5 + 0.5 * vnoise3(fragWorld * 0.37));
        else col *= 0.90 + 0.20 * noise(fragPos.xz * 0.11) * (0.5 + 0.5 * noise(fragPos.xz * 0.37));
        vec3 ln = fragNormal;
        if (hasNormalMap > 0.5 && !outside)
        {
            // tangent space (x east, y north, z up) to map space (x east, y up, z south), added onto the mesh slope
            vec3 t = texture(texture3, pix / mapSize).xyz * 2.0 - 1.0;
            ln = normalize(vec3(ln.x + t.x, ln.y, ln.z - t.y));
        }
        float slopeAmt = 1.0 - clamp(ln.y, 0.0, 1.0);
        float rockN = farMix >= 1.0 ? 0.5 : noise(fragPos.xz * 1.9) * 0.6 + noise(fragPos.xz * 5.3) * 0.4;
        float lumT = dot(col, vec3(0.3, 0.59, 0.11));
        // steep ground shows bare rock, darker in the cracks
        float rockAmt = smoothstep(0.22, 0.48, slopeAmt + (rockN - 0.5) * 0.12);
        vec3 rockCol = mix(vec3(0.34, 0.30, 0.26), vec3(0.52, 0.48, 0.43), rockN) * (0.65 + 0.7 * lumT);
        col = mix(col, rockCol, rockAmt * 0.7);
        // snow above a snow line that drops towards the poles, thinner on steep faces
        float lat = mix(latRange.x, latRange.y, fragPos.z / worldSize.y);
        float cold = smoothstep(15.0, 65.0, abs(lat));
        float snowLine = mix(0.64, 0.42, cold);
        float hN = fragPos.y / max(heightScale, 0.001);
        float snowAmt = smoothstep(snowLine, snowLine + 0.09, hN + (rockN - 0.5) * 0.07) * (1.0 - smoothstep(0.28, 0.62, slopeAmt) * 0.75);
        col = mix(col, vec3(0.90, 0.93, 0.98) * (0.88 + 0.12 * rockN), snowAmt * 0.85);
        float rcov = 0.0, bank = 0.0;
        {
            // one filtered lookup in the downsampled river texture (coverage of a 2x2 block); the mip chain widens it as you zoom out
            vec2 ruv = pix / mapSize;
            float rv = texture(texture4, ruv).r;
            if (tpp < 1.0)
            {
                rcov = smoothstep(0.10, 0.38, rv);
                bank = clamp(textureLod(texture4, ruv, 2.0).r * 5.0, 0.0, 1.0);
            }
            else
            {
                // zoomed out: widen the line with the zoom so a river stays a thin, visible thread instead of vanishing
                rcov = clamp(rv * tpp * 1.6, 0.0, 1.0) * (1.0 - smoothstep(6.0, 14.0, tpp));
                bank = rcov * 0.5;
            }
        }
        if (rcov > 0.01 || bank > 0.01)
        {
            // flow runs downhill: the terrain normal leans towards the lower side
            vec2 sl = fragNormal.xz;
            float slope = length(sl);
            vec2 fd = slope > 0.01 ? sl / slope : vec2(0.0, 1.0);
            vec2 pd = vec2(-fd.y, fd.x);
            vec2 wpm = vec2(fragPos.x * cosLat, fragPos.z); // east/south distance in map units
            vec2 q = vec2(dot(wpm, fd), dot(wpm, pd));
            float speed = 0.5 + 3.5 * smoothstep(0.0, 0.4, slope);
            float e = 0.03;
            float k0 = streak(q, time, speed), ks = streak(q + vec2(e, 0.0), time, speed), ka = streak(q + vec2(0.0, e), time, speed);
            float ra = (0.15 + 0.85 * detail) * 0.7;
            vec2 sv = fd * ((k0 - ks) / e) + pd * ((k0 - ka) / e);
            vec3 rn = normalize(vec3(sv.x * ra * 0.4, 1.0, sv.y * ra * 0.4));
            rn = normalize(fragE * rn.x + fragU * rn.y + fragS * rn.z);
            float rndv = max(dot(rn, viewDir), 0.0);
            float rfres = 0.02 + 0.98 * pow(1.0 - rndv, 5.0);
            vec3 rrd = reflect(-viewDir, rn);
            vec3 rsky = mix(vec3(0.66, 0.78, 0.90), vec3(0.22, 0.42, 0.76), sqrt(clamp(dot(rrd, fragU), 0.0, 1.0)));
            // darker and deeper in the middle of the channel, lighter and greener over the shallows at the edge
            float mid = smoothstep(0.55, 1.0, rcov);
            vec3 rcol = mix(riverColor * 1.25 + vec3(0.03, 0.09, 0.07), riverColor * 0.75, mid) * (0.85 + 0.3 * k0);
            rcol = mix(rcol, rsky, clamp(rfres * 0.85, 0.0, 0.7));
            vec3 rh = normalize(sunDir + viewDir);
            rcol += vec3(1.0, 0.94, 0.82) * pow(max(dot(rn, rh), 0.0), 140.0) * 0.9 * (0.4 + 0.6 * detail);
            // white water where the river drops fast
            rcol = mix(rcol, vec3(0.92, 0.96, 0.97), smoothstep(0.22, 0.5, slope) * smoothstep(0.55, 0.8, k0) * 0.7);
            // wet banks: the ground darkens and turns greener around the water
            col = mix(col, col * vec3(0.78, 0.86, 0.72), bank * (1.0 - rcov) * 0.55);
            col = mix(col, rcol, rcov * 0.94);
        }

        // hill shading
        vec3 n = normalize(fragE * ln.x + fragU * ln.y + fragS * ln.z);
        float ndl = max(dot(n, sunDir), 0.0) * sunUp;
        // sky light from above (cool, stronger on open ground) plus a warm sun; low sun rakes the relief
        float skyAmt = 0.5 + 0.5 * dot(n, fragU);
        vec3 amb = mix(vec3(0.31, 0.32, 0.34), vec3(0.43, 0.44, 0.45), skyAmt) * (0.22 + 0.78 * sunUp);
        col *= amb + vec3(1.0, 0.95, 0.86) * (ndl * 0.80);
        // distance haze on the flat map: far terrain fades towards the sky, which gives the tilted view its depth
        if (globe < 0.5)
        {
            float hz = clamp(1.0 - exp(-length(viewPos - fragWorld) * 0.0005), 0.0, 0.2);
            col = mix(col, vec3(0.62, 0.70, 0.78) * (0.30 + 0.70 * sunUp), hz);
        }

        // map mode color: multiplied over the terrain so hills and forests still show (HOI4 style)
        vec4 mc = lut(loc, 0);
        if (mc.a > 0.0)
        {
            vec3 tinted = mix(mc.rgb * (0.55 + 0.45 * ndl) * (0.22 + 0.78 * sunUp), col * mc.rgb * 2.1, 0.45);
            col = mix(col, tinted, clamp(mc.a * tintStrength, 0.0, 1.0));
            // a band of the country color just inside its border
            float band = 1.0 - smoothstep(0.0, 9.0 * borderScale, dCountry);
            col = mix(col, mc.rgb * 0.8, band * 0.35 * mc.a);
        }
        if ((flags & 128) != 0)
        {
            // demilitarized zone: a pale cross-hatch fixed to the map
            float hp = (fragPos.x - fragPos.z) / (stripePeriod * 1.6);
            float hq = (fragPos.x + fragPos.z) / (stripePeriod * 1.6);
            float h = max(smoothstep(0.42, 0.5, abs(fract(hp) - 0.5)), smoothstep(0.42, 0.5, abs(fract(hq) - 0.5)));
            col = mix(col, vec3(0.88, 0.93, 1.0), h * 0.45);
        }
        if ((flags & 2) != 0)
        {
            // occupied: diagonal stripes of the occupier's color, fixed to the map (a pattern that depends on the pixel's
            // own screen size shimmers like z-fighting while the camera moves) and a band of that color along the border
            float ph = (fragPos.x + fragPos.z) / stripePeriod;
            float edge = clamp(fwidth(ph) * 1.5, 0.015, 0.2);
            float s = smoothstep(0.5 - edge, 0.5 + edge, fract(ph));
            vec3 occ = lut(loc, 2).rgb;
            col = mix(col, occ, s * 0.55);
            col = mix(col, occ * 0.85, line(dProv, 1.6) * 0.55);
        }
        col = mix(col, vec3(0.06), line(dProv, 0.6) * 0.30 * showProvinceBorders);
        col = mix(col, vec3(0.08), line(dState, 0.9) * 0.55 * stateBorders);
        col = mix(col, vec3(0.03), line(dCountry, 1.1 + 0.5 * borderScale) * 0.92);
        col = mix(col, vec3(0.14, 0.12, 0.10), line(dCoast, 0.9) * 0.6);
        col = mix(col, vec3(1.0, 0.0, 0.9), line(dBase, 1.3) * 0.95 * geoBase);
    }
    if (water) col = mix(col, waterColor * 0.6, line(dProv, 0.6) * 0.18 * showProvinceBorders);

    // fog of war: grey and darker; the sea only a little
    if ((flags & 32) != 0 && fogStrength > 0.0)
    {
        float lum = dot(col, vec3(0.3, 0.59, 0.11));
        vec3 fogged;
        if (water) fogged = mix(col, vec3(lum), 0.35) * 0.82;
        else
        {
            fogged = mix(col, vec3(lum) * vec3(0.78, 0.80, 0.86), 0.72) * 0.62;
            float h = step(0.85, fract((fragPos.x - fragPos.z) / max(fwidth(fragPos.x) * cosLat * 9.0, 0.05)));
            fogged *= 1.0 - h * 0.10;
        }
        col = mix(col, fogged, fogStrength);
    }

    // highlights: the whole location brightens (land and water alike)
    float pe = line(dProv, 1.4);
    if (outside) { finalColor = vec4(col, 1.0) * colDiffuse; return; }
    if ((flags & 64) != 0) col = mix(mix(col, vec3(1.0, 0.95, 0.8), 0.2), vec3(1.0, 0.82, 0.35), max(line(dState, 1.6), line(dCountry, 1.6)) * 0.85);
    if ((flags & 8) != 0) col = col * 1.18 + vec3(0.05);
    if ((flags & 4) != 0) col = mix(col * 1.25 + vec3(0.06), vec3(1.0, 0.93, 0.7), pe * 0.85);

    finalColor = vec4(col, 1.0) * colDiffuse;
}
";
}
