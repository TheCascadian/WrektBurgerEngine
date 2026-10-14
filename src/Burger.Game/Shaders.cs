namespace Burger.Runtime;

/// <summary>GLSL 330 shaders for the map.</summary>
internal static class Shaders
{
    public const string TerrainVertex = @"#version 330
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;
uniform mat4 mvp;
uniform mat4 matModel;
uniform mat4 matNormal;
uniform float globe;        // 1: bend the flat map onto a sphere (the fragment shader still reads the flat map position)
uniform float globeRadius;
uniform vec2 worldSize;
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
        gl_Position = mvp * vec4(vertexPosition, 1.0);
    }
}
";

    public const string TerrainFragment = @"#version 330
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
uniform float hasNormalMap;
uniform float heightScale;  // world height of the highest point (heightmap value 255)
uniform vec2 latRange;      // latitude (degrees) of the top and bottom edge of the map
uniform vec4 colDiffuse;
uniform vec2 mapSize;
uniform vec2 worldSize;
uniform vec2 lutSize;
uniform float blockRows;
uniform float atlasGrid;
uniform float tileScale;
uniform vec2 atlasTile;   // texture size in pixels, wrapped border in pixels
uniform vec3 waterColor;
uniform vec3 riverColor;
uniform float time;
uniform float borderScale;
uniform float stateBorders; // 1 up close, fades to 0 when zoomed out
uniform float showProvinceBorders;
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
// river flag of a map pixel: land texels carry 255 in alpha (water texels carry the coast distance, at most 240)
float riv(ivec2 p) { return texelFetch(texture0, clampPix(p), 0).a > 0.98 ? 1.0 : 0.0; }

// ripples stretched along the flow: x runs downstream, y across the channel
float streak(vec2 q, float t, float speed)
{
    return noise(vec2(q.x * 1.3 - t * speed, q.y * 4.5)) * 0.6 + noise(vec2(q.x * 3.2 - t * speed * 1.7, q.y * 10.0)) * 0.4;
}

float swell(vec2 p, float detail)
{
    float t = time;
    return noise(p * 0.35 + vec2(t * 0.04, t * 0.02)) * 0.50
         + noise(p * 0.90 + vec2(-t * 0.06, t * 0.05)) * 0.30
         + noise(p * 2.10 + vec2(t * 0.10, -t * 0.07)) * 0.15 * detail
         + noise(p * 4.70 + vec2(-t * 0.15, t * 0.12)) * 0.05 * detail;
}
float waves(vec2 p)
{
    return noise(p * 0.9 + vec2(time * 0.05, time * 0.03)) * 0.6 + noise(p * 2.3 - vec2(time * 0.07, -time * 0.04)) * 0.4;
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
    int L[4]; int O[4]; int ST[4]; int WA[4];
    for (int k = 0; k < 4; k++)
    {
        L[k] = locAt(b + o4[k]);
        O[k] = ownerOf(L[k]);
        ST[k] = stateOf(L[k]);
        WA[k] = flagsOf(L[k]) & 1;
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
    int flags = flagsOf(loc);
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
        float shallow = outside ? 0.0 : clamp(data.a * 255.0 / 240.0, 0.0, 1.0);
        shallow = shallow * shallow;
        vec3 deep = waterColor * 0.72;
        vec3 shore = mix(waterColor, vec3(0.30, 0.62, 0.66), 0.55) * 1.15;
        col = mix(deep, shore, shallow);
        // layered, drifting swell: four noise octaves; the fine ones fade out with distance so far water stays calm and does not shimmer
        vec2 wp = fragPos.xz;
        if (globe > 0.5)
        {
            vec3 an = abs(normalize(fragWorld)); // surface coordinates instead of map coordinates (those stretch into rays at the poles)
            wp = (an.y >= an.x && an.y >= an.z) ? fragWorld.xz : (an.x >= an.z ? fragWorld.yz : fragWorld.xy);
        }
        float e = 0.05;
        float h0 = swell(wp, detail), hx = swell(wp + vec2(e, 0.0), detail), hz = swell(wp + vec2(0.0, e), detail);
        float amp = 0.10 + 0.55 * detail;
        vec3 wn = normalize(vec3((h0 - hx) / e * amp * 0.5, 1.0, (h0 - hz) / e * amp * 0.5));
        wn = normalize(fragE * wn.x + fragU * wn.y + fragS * wn.z);
        // water takes on the sky: Schlick fresnel, so it is dark and clear looking down and mirror-like at a grazing angle
        // daylight on this water: the sky reflection, glitter and foam are lit by the sun, so they go dark with it at night
        float day = smoothstep(-0.12, 0.3, globe > 0.5 ? dot(fragU, sunDir) : sunDir.y);
        float ndv = max(dot(wn, viewDir), 0.0);
        float fres = 0.02 + 0.98 * pow(1.0 - ndv, 5.0);
        vec3 rd = reflect(-viewDir, wn);
        float skyUp = clamp(dot(rd, fragU), 0.0, 1.0);
        vec3 sky = mix(vec3(0.66, 0.78, 0.90), vec3(0.22, 0.42, 0.76), sqrt(skyUp));
        col *= 0.88 + 0.24 * h0;
        // light scattering through thin crests
        col += vec3(0.04, 0.13, 0.12) * smoothstep(0.55, 0.85, h0) * (0.4 + 0.6 * detail);
        col = mix(col, sky * (0.06 + 0.94 * day), clamp(fres * 0.9, 0.0, 0.85));
        // sun glitter: a tight highlight plus a soft sheen
        vec3 hv = normalize(sunDir + viewDir);
        float nh = max(dot(wn, hv), 0.0);
        col += vec3(1.0, 0.94, 0.82) * (pow(nh, 260.0) * 1.6 * (0.3 + 0.7 * detail) + pow(nh, 28.0) * 0.10) * day;
        // foam along the coast, broken up by noise so it laps in and out
        float lap = noise(wp * 0.9 + vec2(time * 0.2, -time * 0.15));
        col = mix(col, vec3(0.88, 0.93, 0.92) * (0.10 + 0.90 * day), line(dCoast + (lap - 0.5) * 3.0, 1.5) * 0.55);
        if ((flags & 16) != 0) col = mix(col, shore, 0.4);
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
        vec2 wq = pp + (vec2(noise(pix * 0.8 + 3.1), noise(pix * 0.8 + 17.7)) - 0.5) * 2.6
                     + (vec2(noise(pix * 2.1 + 5.0), noise(pix * 2.1 + 9.0)) - 0.5) * 1.1;
        ivec2 qb = ivec2(floor(wq));
        float tId[6]; float tW[6]; int tN = 0;
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
                if (k == tN) { if (tN >= 6) continue; tId[tN] = id; tW[tN] = 0.0; tN++; }
                tW[k] += kw;
            }
        float tot = 0.0;
        for (int k = 0; k < tN; k++) tot += tW[k];
        vec3 tC[6]; float tH[6]; float tMax = 0.0;
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
        // large scale variation hides the texture repeating
        // (on the globe this noise is taken in 3D: in map coordinates it stretches into rays at the poles)
        if (globe > 0.5) col *= 0.90 + 0.20 * vnoise3(fragWorld * 0.11) * (0.5 + 0.5 * vnoise3(fragWorld * 0.37));
        else col *= 0.90 + 0.20 * noise(fragPos.xz * 0.11) * (0.5 + 0.5 * noise(fragPos.xz * 0.37));
        vec3 ln = fragNormal;
        if (hasNormalMap > 0.5 && !outside)
        {
            // tangent space (x east, y north, z up) to map space (x east, y up, z south), added onto the mesh slope
            vec3 t = texture(texture3, pix / mapSize).xyz * 2.0 - 1.0;
            ln = normalize(vec3(ln.x + t.x, ln.y, ln.z - t.y));
        }
        float slopeAmt = 1.0 - clamp(ln.y, 0.0, 1.0);
        float rockN = noise(fragPos.xz * 1.9) * 0.6 + noise(fragPos.xz * 5.3) * 0.4;
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
        if (tpp < 1.0)
        {
            float r00 = riv(b), r10 = riv(b + ivec2(1, 0)), r01 = riv(b + ivec2(0, 1)), r11 = riv(b + ivec2(1, 1));
            float rv = mix(mix(r00, r10, f.x), mix(r01, r11, f.x), f.y);
            rcov = smoothstep(0.18, 0.55, rv);
            float sb = 0.0;
            for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++) sb += riv(ivec2(floor(pix)) + ivec2(i, j));
            bank = clamp(sb * 0.35, 0.0, 1.0);
        }
        else
        {
            // zoomed out: widen the line with the zoom so a river stays a thin, visible thread instead of vanishing
            float sw = 0.0, spacing = tpp * 0.9;
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) sw += riv(ivec2(floor(pix + vec2(i, j) * spacing)));
            rcov = clamp(sw * 0.4, 0.0, 1.0) * (1.0 - smoothstep(6.0, 14.0, tpp));
            bank = rcov * 0.5;
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
