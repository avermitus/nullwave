namespace Nullwave.Render;

public static class Shaders
{
    public const string FullscreenVert = """
#version 410 core
out vec2 vUv;
void main() {
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    vUv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}
""";

    public const string ColorVert = """
#version 410 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec4 aColor;
uniform vec2 uCam;
uniform vec2 uView;
out vec4 vColor;
void main() {
    vec2 p = (aPos - uCam) / uView;
    gl_Position = vec4(p.x, -p.y, 0.0, 1.0);
    vColor = aColor;
}
""";

    public const string ColorFrag = """
#version 410 core
in vec4 vColor;
out vec4 fragColor;
void main() {
    fragColor = vColor;
}
""";

    public const string UiVert = """
#version 410 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec4 aColor;
uniform vec2 uSize;
out vec4 vColor;
void main() {
    vec2 p = vec2(aPos.x / uSize.x * 2.0 - 1.0, 1.0 - aPos.y / uSize.y * 2.0);
    gl_Position = vec4(p, 0.0, 1.0);
    vColor = aColor;
}
""";

    // Stationary 3D-only reactive backdrop: 5 genuine 3D raymarched/perspective scenes locked to screenPos.
    public const string BackgroundFrag = """
#version 410 core
in vec2 vUv;
out vec4 fragColor;
uniform vec2 uResolution;
uniform float uTime;
uniform float uFlow;
uniform float uIntensity;
uniform float uDrop;
uniform float uBuild;
uniform float uKick;
uniform float uSnare;
uniform float uHat;
uniform float uOverdrive;
uniform float uMotionScale;
uniform float uAnticipation;
uniform float uBass;
uniform float uLowMid;
uniform float uMid;
uniform float uHigh;
uniform float uEnergy;
uniform float uBeat;
uniform float uDown;
uniform float uSeed;
uniform vec3 uColA;
uniform vec3 uColB;
uniform vec3 uColC;
uniform vec3 uPrim;
uniform vec3 uSec;
// transition blend factor between genome A and genome B
uniform float uMix;
// abyssal theme switch (0 = Standard, 1 = Abyssal) + live combat light pools
uniform float uDarkTheme;
uniform int uLightCount;
uniform vec4 uLights[12];
uniform vec3 uLightCols[12];

vec3 PO;     // palette offset
vec3 PF;     // palette frequency

// ---------------------------------------------------------------- utils
float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}
mat2 rot2(float a) { float c = cos(a), s = sin(a); return mat2(c, -s, s, c); }
// Deep, cool architectural cosine palette — stays subdued so bright ships & bullets pop
vec3 pal(float t) {
    vec3 c = 0.42 + 0.36 * cos(6.28318 * (PF * (t + uDrop * 0.08 + uOverdrive * 0.10) + PO));
    return mix(vec3(0.06, 0.09, 0.20), c, 0.78);
}
float smin(float a, float b, float k) {
    float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
    return mix(b, a, h) - k * h * (1.0 - h);
}

// ---------------------------------------------------------------- 0: LIQUID 3D (stationary raymarched obsidian metaball sculpture)
float liquidMap3D(vec3 p, vec4 a) {
    float k = 0.52 + a.y * 0.35;
    float d = length(p) - (0.72 + a.z * 0.18);
    // 5 fixed sculptural lobes seeded by genome parameters (100% stationary in 3D space)
    for (int i = 0; i < 5; i++) {
        float fi = float(i);
        float ang = fi * 1.2566 + a.w * 6.2832;
        float elev = sin(fi * 2.13 + a.x * 6.2832) * 0.92;
        float rad = 1.15 + 0.48 * cos(fi * 1.7 + a.y * 5.0);
        vec3 bp = vec3(cos(ang) * rad, elev, sin(ang) * rad * 0.72);
        float sr = 0.34 + 0.14 * fract(a.z * 3.7 + fi * 0.37);
        d = smin(d, length(p - bp) - sr, k);
    }
    return d;
}
vec3 genLiquid3D(vec2 uv, vec4 a) {
    vec3 ro = vec3(0.0, 0.0, -5.8);
    vec3 rd = normalize(vec3(uv, 1.5));
    vec3 fogBg = vec3(0.014, 0.020, 0.044) * (1.0 - 0.35 * length(uv));
    float b = dot(ro, rd);
    float disc = b * b - (dot(ro, ro) - 3.15 * 3.15);
    vec3 col = fogBg;
    if (disc > 0.0) {
        float t = max(0.0, -b - sqrt(disc));
        float tEnd = -b + sqrt(disc);
        float minD = 10.0;
        bool hit = false;
        for (int i = 0; i < 40; i++) {
            float d = liquidMap3D(ro + rd * t, a);
            minD = min(minD, d);
            if (d < 0.002 * t) { hit = true; break; }
            t += d * 0.85;
            if (t > tEnd) break;
        }
        float rimHalo = exp(-max(minD, 0.0) * 5.2);
        if (hit) {
            vec3 p = ro + rd * t;
            const vec2 e = vec2(0.0025, -0.0025);
            vec3 n = normalize(
                e.xyy * liquidMap3D(p + e.xyy, a) +
                e.yyx * liquidMap3D(p + e.yyx, a) +
                e.yxy * liquidMap3D(p + e.yxy, a) +
                e.xxx * liquidMap3D(p + e.xxx, a));
            vec3 keyDir = normalize(vec3(-0.55, 0.65, -0.52));
            vec3 fillDir = normalize(vec3(0.65, -0.40, -0.60));
            float diff = max(dot(n, keyDir), 0.0);
            float fill = max(dot(n, fillDir), 0.0);
            float fres = pow(1.0 - clamp(dot(n, -rd), 0.0, 1.0), 3.0);
            float spec = pow(max(dot(reflect(rd, n), keyDir), 0.0), 36.0);
            // Stationary sculptural contour lighting band illuminated in place on snare/drop
            float contour = exp(-abs(fract(p.y * 2.4 + p.x * 1.1) - 0.5) * 22.0);
            vec3 baseTint = pal(dot(n, keyDir) * 0.45 + p.y * 0.18 + a.x);
            vec3 rimTint = pal(0.45 + p.x * 0.22 + a.z);
            float pulse = 0.55 + uKick * 0.55 + uSnare * 0.40 + uDrop * 0.70 + uOverdrive * 0.45;
            vec3 surf = vec3(0.015, 0.022, 0.048)
                      + baseTint * (0.08 + diff * 0.20 + fill * 0.08)
                      + rimTint * fres * 0.52 * pulse
                      + rimTint * contour * (0.04 + uSnare * 0.24 + uDrop * 0.18)
                      + mix(vec3(0.65, 0.80, 1.0), rimTint, 0.45) * spec * (0.28 + uHat * 0.72 + uSnare * 0.28);
            float depthFog = exp(-max(t - 2.6, 0.0) * 0.18);
            col = mix(fogBg, surf, depthFog);
        }
        col += pal(0.25 + a.w) * rimHalo * (0.04 + uKick * 0.08 + uDrop * 0.14 + uOverdrive * 0.08);
    }
    return col;
}

// ---------------------------------------------------------------- 1: CYBER TUNNEL 3D (stationary 3D perspective polygonal chamber)
vec3 genCyberTunnel3D(vec2 uv, vec4 a) {
    vec3 rd = normalize(vec3(uv, 1.45));
    float sides = floor(6.0 + a.y * 4.0); // 6..9 sided polygonal chamber
    float seg = 6.28318 / sides;
    float r = max(length(rd.xy), 0.005);
    float ang = atan(rd.y, rd.x) + a.w * 1.5708; // fixed orientation — zero rotation
    float localA = mod(ang, seg) - seg * 0.5;
    float wallR = r * cos(localA) / cos(seg * 0.5);
    // True 3D perspective depth along Z (completely stationary — zero forward scroll)
    float radius = 2.4 + a.x * 0.5;
    float z = radius / max(wallR, 0.018);
    float faceCenterA = ang - localA;
    vec3 wallN = normalize(vec3(-cos(faceCenterA), -sin(faceCenterA), 0.0));
    float grazing = pow(1.0 - clamp(abs(dot(wallN, -rd)), 0.0, 1.0), 2.2);

    // Stationary structural ribs and longitudinal trusses
    float ribSpacing = 1.65 + a.z * 0.85;
    float ribPhase = abs(fract(z / ribSpacing) - 0.5);
    float rib = exp(-ribPhase * 24.0);
    float truss = exp(-abs(localA) * 28.0);
    float panelId = hash21(vec2(floor(z / ribSpacing), floor(ang / seg)));
    float panelEdge = exp(-abs(ribPhase - 0.22) * 32.0);

    // Illumination-only harmonic pulse lighting stationary ribs in place
    float lightWave = exp(-abs(fract(z * 0.14 - uFlow * 0.22 * uMotionScale) - 0.5) * 14.0);
    float fog = exp(-z * 0.072);
    vec3 baseWall = vec3(0.016, 0.022, 0.048) + pal(floor(ang / seg) * 0.12 + a.x) * (0.045 + 0.04 * grazing);
    vec3 ribCol = pal(z * 0.035 + a.z);
    vec3 trussCol = pal(0.45 + a.w);
    float beatBoost = 0.52 + uKick * 0.55 + uSnare * 0.42 + uDrop * 0.72 + uOverdrive * 0.48;
    vec3 lit = baseWall
             + ribCol * rib * (0.22 * beatBoost + lightWave * (0.28 + uKick * 0.45 + uDrop * 0.42))
             + trussCol * truss * (0.14 + uHat * 0.34 + uSnare * 0.18)
             + ribCol * panelEdge * step(0.58, panelId) * (0.06 + uHat * 0.32);

    vec3 fogCol = vec3(0.010, 0.014, 0.034) + pal(0.5 + a.y) * (0.025 + uBass * 0.04 + uDrop * 0.06);
    vec3 col = mix(fogCol, lit, fog);
    // Deep vanishing-point architectural core glow (subdued)
    col += pal(0.6 + a.z) * exp(-length(uv) * 7.5) * (0.06 + uBass * 0.10 + uDrop * 0.14 + uOverdrive * 0.10);
    return col;
}

// ---------------------------------------------------------------- 2: VOID HORIZON 3D (stationary 3D perspective floor + ceiling grid)
vec3 genVoidHorizon3D(vec2 uv, vec4 a) {
    vec3 rd = normalize(vec3(uv, 1.45));
    float planeH = 1.65 + a.x * 0.35;
    float ay = max(abs(rd.y), 0.012);
    float t = planeH / ay;
    vec2 wxz = rd.xz * t; // 100% stationary world coordinates on the 3D planes

    float scale = 0.85 + a.y * 0.45;
    vec2 g = wxz * scale;
    vec2 f = abs(fract(g) - 0.5);
    // Distance-damped crisp perspective grid lines (zero distant moire shimmer)
    float aa = 1.0 / (1.0 + t * 0.13);
    float minorX = exp(-f.x * (28.0 * aa + 5.0));
    float minorZ = exp(-f.y * (28.0 * aa + 5.0));
    float grid = max(minorX, minorZ);

    vec2 maj = abs(fract(g * 0.25) - 0.5);
    float majorLine = max(exp(-maj.x * (36.0 * aa + 6.0)), exp(-maj.y * (36.0 * aa + 6.0)));
    float node = exp(-length(f) * (20.0 * aa + 4.0));

    // Illumination-only wave pulsing outward along stationary 3D depth t
    float depthPulse = exp(-abs(fract(t * 0.12 - uFlow * 0.24 * uMotionScale) - 0.5) * 14.0);
    float fog = exp(-t * 0.088);
    float sideTint = rd.y > 0.0 ? 0.38 : 0.0;
    vec3 gridCol = pal(sideTint + a.z + t * 0.018);
    vec3 accentCol = pal(0.52 + sideTint + a.w);
    float beatBoost = 0.48 + uKick * 0.52 + uSnare * 0.38 + uDrop * 0.68 + uOverdrive * 0.45;

    vec3 planeSurf = vec3(0.012, 0.017, 0.040)
                   + gridCol * grid * (0.15 + depthPulse * (0.22 + uSnare * 0.35 + uDrop * 0.35))
                   + accentCol * majorLine * 0.24 * beatBoost
                   + accentCol * node * (0.06 + uHat * 0.42 + uDrop * 0.18);

    // Far 3D architectural horizon glow + subtle distant monolith skyline silhouette
    float horizonMask = exp(-abs(uv.y) * 9.5);
    float colIdx = floor(uv.x * (10.0 + a.w * 8.0));
    float towerH = 0.04 + 0.10 * hash21(vec2(colIdx, floor(a.x * 30.0)));
    float skyline = step(abs(uv.y), towerH) * exp(-abs(uv.y) * 14.0) * (0.35 + 0.65 * step(0.42, hash21(vec2(colIdx, 7.0))));
    vec3 horizonCol = vec3(0.014, 0.020, 0.048)
                    + pal(0.28 + a.z) * horizonMask * (0.12 + uBass * 0.12 + uDrop * 0.18 + uOverdrive * 0.12)
                    - vec3(0.018) * skyline;

    return mix(horizonCol, planeSurf, fog);
}

// ---------------------------------------------------------------- 3: CATHEDRAL 3D (stationary 3D raymarched fluted monolith colonnade)
float cathedralMap3D(vec3 p, vec4 a, out float matId) {
    // Open central nave (|x| < 1.85) frames combat cleanly; fluted columns repeat in stationary Z
    float zPeriod = 3.4 + a.x * 0.8;
    vec3 q = p;
    q.z = mod(p.z + zPeriod * 0.5, zPeriod) - zPeriod * 0.5;
    float colX = 2.45 + a.y * 0.45;
    vec2 cp = vec2(abs(q.x) - colX, q.z);
    // Fluted octagonal monolith column
    float colR = 0.48 + 0.04 * cos(atan(cp.y, cp.x) * 8.0);
    float dPillar = length(cp) - colR;
    // Overhead gothic-sci-fi vault beams & floor/ceiling slabs
    float dVault = max(abs(q.z) - 0.26, -(length(vec2(q.x, q.y - 0.55)) - (colX + 0.15)));
    float dSlab = (2.15 + a.z * 0.25) - abs(p.y);
    float dArch = min(dPillar, max(dVault, -p.y + 0.55));
    if (dArch < dSlab) { matId = 1.0; return dArch; }
    matId = 0.0;
    return dSlab;
}
vec3 genCathedral3D(vec2 uv, vec4 a) {
    vec3 ro = vec3(0.0, 0.0, 0.0);
    vec3 rd = normalize(vec3(uv, 1.45));
    vec3 fogCol = vec3(0.012, 0.017, 0.042) + pal(0.3 + a.w) * exp(-length(uv) * 5.0) * (0.07 + uBass * 0.10 + uDrop * 0.14);
    float t = 0.4;
    float matId = 0.0;
    bool hit = false;
    for (int i = 0; i < 42; i++) {
        float d = cathedralMap3D(ro + rd * t, a, matId);
        if (d < 0.003 * t) { hit = true; break; }
        t += d * 0.85;
        if (t > 28.0) break;
    }
    if (!hit) return fogCol;

    vec3 p = ro + rd * t;
    float dummy = 0.0;
    const vec2 e = vec2(0.004, -0.004);
    vec3 n = normalize(
        e.xyy * cathedralMap3D(p + e.xyy, a, dummy) +
        e.yyx * cathedralMap3D(p + e.yyx, a, dummy) +
        e.yxy * cathedralMap3D(p + e.yxy, a, dummy) +
        e.xxx * cathedralMap3D(p + e.xxx, a, dummy));

    vec3 keyDir = normalize(vec3(-0.35, 0.72, -0.60));
    float diff = max(dot(n, keyDir), 0.0);
    float fres = pow(1.0 - clamp(dot(n, -rd), 0.0, 1.0), 2.8);
    float spec = pow(max(dot(reflect(rd, n), keyDir), 0.0), 28.0);
    // Vertical architectural light strips on pillars & perspective seam lines on floor/ceiling
    float fluteLight = matId > 0.5
        ? exp(-abs(fract(p.y * 0.85) - 0.5) * 18.0)
        : exp(-abs(fract(p.x * 0.65) - 0.5) * 20.0) * 0.55;
    float lightWave = exp(-abs(fract(p.z * 0.10 - uFlow * 0.20 * uMotionScale) - 0.5) * 14.0);

    vec3 stoneTint = pal(matId * 0.35 + p.z * 0.025 + a.z);
    vec3 rimTint = pal(0.55 + a.w);
    float beatBoost = 0.50 + uKick * 0.55 + uSnare * 0.42 + uDrop * 0.70 + uOverdrive * 0.48;
    vec3 surf = vec3(0.014, 0.019, 0.044)
              + stoneTint * (0.06 + diff * 0.16)
              + rimTint * fres * 0.34 * beatBoost
              + rimTint * fluteLight * (0.14 * beatBoost + lightWave * (0.24 + uKick * 0.38 + uDrop * 0.35))
              + vec3(0.65, 0.82, 1.0) * spec * (0.18 + uHat * 0.52);

    float fog = exp(-t * 0.092);
    return mix(fogCol, surf, fog);
}

// ---------------------------------------------------------------- 4: ORBITAL GYROID 3D (stationary 3D concentric torus rings + gyroid core)
float orbitalMap3D(vec3 p, vec4 a, out float partId) {
    // 3 stationary tilted concentric architectural torus rings (fixed seeded angles — zero spin)
    vec3 p1 = p;
    p1.xy *= rot2(0.45 + a.x * 1.4);
    p1.yz *= rot2(0.55 + a.y * 1.2);
    float r1 = length(vec2(length(p1.xz) - 2.15, p1.y)) - 0.085;

    vec3 p2 = p;
    p2.xz *= rot2(1.15 + a.y * 1.6);
    p2.xy *= rot2(-0.65 + a.z * 1.2);
    float r2 = length(vec2(length(p2.xz) - 1.68, p2.y)) - 0.075;

    vec3 p3 = p;
    p3.yz *= rot2(1.20 + a.z * 1.4);
    p3.xz *= rot2(-0.55 + a.w * 1.5);
    float r3 = length(vec2(length(p3.xz) - 1.26, p3.y)) - 0.065;

    float rings = min(r1, min(r2, r3));

    // Carved stationary 3D gyroid core suspended inside the rings
    float gScale = 3.2 + a.w * 1.4;
    vec3 gp = p * gScale;
    float gy = (dot(sin(gp), cos(gp.yzx))) / gScale;
    float core = max(length(p) - 0.96, abs(gy) - 0.095);

    if (rings < core) { partId = 1.0; return rings; }
    partId = 0.0;
    return core;
}
vec3 genOrbitalGyroid3D(vec2 uv, vec4 a) {
    vec3 ro = vec3(0.0, 0.0, -5.8);
    vec3 rd = normalize(vec3(uv, 1.5));
    vec3 fogBg = vec3(0.013, 0.019, 0.044) * (1.0 - 0.35 * length(uv));
    float b = dot(ro, rd);
    float disc = b * b - (dot(ro, ro) - 2.85 * 2.85);
    vec3 col = fogBg;
    if (disc > 0.0) {
        float t = max(0.0, -b - sqrt(disc));
        float tEnd = -b + sqrt(disc);
        float minD = 10.0;
        float partId = 0.0;
        bool hit = false;
        for (int i = 0; i < 42; i++) {
            float d = orbitalMap3D(ro + rd * t, a, partId);
            minD = min(minD, d);
            if (d < 0.002 * t) { hit = true; break; }
            t += d * 0.80;
            if (t > tEnd) break;
        }
        float halo = exp(-max(minD, 0.0) * 6.0);
        if (hit) {
            vec3 p = ro + rd * t;
            float dummy = 0.0;
            const vec2 e = vec2(0.0025, -0.0025);
            vec3 n = normalize(
                e.xyy * orbitalMap3D(p + e.xyy, a, dummy) +
                e.yyx * orbitalMap3D(p + e.yyx, a, dummy) +
                e.yxy * orbitalMap3D(p + e.yxy, a, dummy) +
                e.xxx * orbitalMap3D(p + e.xxx, a, dummy));
            vec3 keyDir = normalize(vec3(-0.52, 0.68, -0.52));
            float diff = max(dot(n, keyDir), 0.0);
            float fres = pow(1.0 - clamp(dot(n, -rd), 0.0, 1.0), 2.8);
            float spec = pow(max(dot(reflect(rd, n), keyDir), 0.0), 42.0);
            vec3 baseTint = pal(partId * 0.42 + p.y * 0.20 + a.x);
            vec3 rimTint = pal(0.55 + partId * 0.25 + a.z);
            float pulse = 0.52 + uKick * 0.55 + uSnare * 0.42 + uDrop * 0.72 + uOverdrive * 0.48;
            // Core veins pulse on snare; outer rings glint sharply on hi-hats
            float vein = (1.0 - partId) * (0.14 + uSnare * 0.36 + uDrop * 0.26);
            vec3 surf = vec3(0.015, 0.022, 0.048)
                      + baseTint * (0.08 + diff * 0.18 + vein)
                      + rimTint * fres * 0.48 * pulse
                      + mix(vec3(0.70, 0.85, 1.0), rimTint, 0.4) * spec * (0.28 + uHat * 0.82 * (0.6 + 0.4 * partId));
            float depthFog = exp(-max(t - 2.8, 0.0) * 0.20);
            col = mix(fogBg, surf, depthFog);
        }
        col += pal(0.35 + a.w) * halo * (0.04 + uKick * 0.09 + uDrop * 0.14 + uOverdrive * 0.08);
    }
    return col;
}

// ================================================================ stationary 3D genome dispatch
struct Layer {
    int op0; int op1; int op2; int op3;
    vec4 p0; vec4 p1; vec4 p2; vec4 p3;
    int gen; vec4 ga; vec4 gb;
    int blend; float gain;
    vec3 po; vec3 pf;
};
uniform Layer uLA[3];
uniform Layer uLB[3];
uniform int uCountA;
uniform int uCountB;
uniform vec4 uGlobA;
uniform vec4 uGlobB;

vec3 runGen3D(int g, vec4 a, vec2 uv) {
    if (g == 0) return genLiquid3D(uv, a);
    if (g == 1) return genCyberTunnel3D(uv, a);
    if (g == 2) return genVoidHorizon3D(uv, a);
    if (g == 3) return genCathedral3D(uv, a);
    return genOrbitalGyroid3D(uv, a);
}

vec3 renderGenomeA(vec2 uv) {
    PO = uLA[0].po; PF = uLA[0].pf;
    return runGen3D(uLA[0].gen, uLA[0].ga, uv);
}

vec3 renderGenomeB(vec2 uv) {
    PO = uLB[0].po; PF = uLB[0].pf;
    return runGen3D(uLB[0].gen, uLB[0].ga, uv);
}

void main() {
    float aspect = uResolution.x / max(uResolution.y, 1.0);
    vec2 screenPos = (vUv - 0.5) * vec2(aspect, 1.0);
    // 100% stationary screen-centered coordinates — zero camera, aim, velocity, or twist warp
    vec2 uv = screenPos;
    vec3 rawCol = renderGenomeA(uv);
    if (uMix > 0.001) {
        vec3 next = renderGenomeB(uv);
        float wipe = smoothstep(uMix * 1.6 - 0.25, uMix * 1.6, length(uv) * 0.9);
        rawCol = mix(next, rawCol, wipe);
        PO = uLA[0].po; PF = uLA[0].pf;
    }

    float r = length(uv);
    // Subtle illumination-only sonar & pre-drop anticipation cues (stationary 3D geometry)
    float waveR = fract(uFlow * 0.34 + uDown * 0.22);
    float sonar = exp(-abs(r - waveR * 1.40) * 18.0) * clamp(uKick * 0.35 + uSnare * 0.30 + uDrop * 0.48 + uOverdrive * 0.32, 0.0, 1.05);
    float pullR = (1.0 - uAnticipation * 0.78) * 1.15;
    float tensionRing = exp(-abs(r - pullR) * 20.0) * uAnticipation;
    vec3 tensionCol = vec3(0.45, 0.34, 0.85) * (0.28 + 0.55 * uAnticipation);

    // Standard theme: deep dark-navy atmospheric 3D scene with center combat readability well
    vec3 col = vec3(0.015, 0.020, 0.045) + rawCol;
    col += rawCol * sonar * 0.55;
    col += (rawCol * 0.45 + tensionCol * 0.08) * tensionRing;
    float vig = smoothstep(1.42, 0.18, r);
    col *= 0.72 + 0.28 * vig;
    // Subtle center combat-readability well so ship, enemies, lasers & bullets pop immediately
    col *= 0.72 + 0.28 * smoothstep(0.15, 0.95, r);
    col += 0.002 * hash21(vUv * uResolution + uTime);
    float reactiveGain = 1.0 + min(uKick, 1.5) * 0.18 + min(uSnare, 1.5) * 0.16 + min(uHat, 1.5) * 0.10 + min(uDrop, 1.8) * 0.24 + uOverdrive * 0.20;
    col = pow(max(col, 0.0), vec3(1.12)) * reactiveGain;
    // Strict bloom-safe highlight ceiling: asymptote 0.70 / 1.55 = 0.451 (well below 0.62 Standard bloom threshold)
    col = (col * 0.70) / (1.0 + col * 1.55);

    if (uDarkTheme > 0.001) {
        vec3 localLight = vec3(0.0);
        for (int i = 0; i < 12; i++) {
            if (i >= uLightCount) break;
            vec4 lt = uLights[i];
            float d = length(screenPos - lt.xy) / max(lt.z, 0.02);
            float falloff = exp(-d * d * 2.2) * lt.w;
            localLight += uLightCols[i] * falloff;
        }
        float pulseGate = clamp(0.28 + uKick * 0.36 + uSnare * 0.30 + uHat * 0.16 + uDrop * 0.42 + uOverdrive * 0.34, 0.0, 1.05);
        vec3 voidBase = vec3(0.004, 0.005, 0.012);
        vec3 ridgeCol = pow(clamp(max(rawCol - vec3(0.025), 0.0), 0.0, 2.0), vec3(1.20));
        vec3 sharpPat = ridgeCol * pulseGate * 1.05;
        vec3 sonarPat = ridgeCol * sonar * 0.65 + (ridgeCol * 0.55 + tensionCol * 0.08) * tensionRing;
        vec3 litPat = rawCol * localLight * 1.55 + ridgeCol * localLight * 0.95 + localLight * 0.016;
        vec3 abyssal = voidBase + sharpPat + sonarPat + litPat;
        abyssal *= 0.68 + 0.32 * vig;
        abyssal *= 0.78 + 0.22 * smoothstep(0.15, 0.95, r);
        abyssal += 0.0015 * hash21(vUv * uResolution + uTime);
        // Strict Abyssal bloom-safe ceiling: asymptote 0.62 / 1.60 = 0.388 (well below 0.72 Abyssal bloom threshold)
        abyssal = (abyssal * 0.62) / (1.0 + abyssal * 1.60);
        col = mix(col, abyssal, uDarkTheme);
    }
    fragColor = vec4(col * min(uIntensity, 1.05), 1.0);
}
""";

    public const string BrightFrag = """
#version 410 core
in vec2 vUv;
out vec4 fragColor;
uniform sampler2D uScene;
uniform float uThreshold;
uniform float uKnee;
void main() {
    vec3 c = texture(uScene, vUv).rgb;
    float br = max(c.r, max(c.g, c.b));
    float soft = clamp(br - uThreshold + uKnee, 0.0, 2.0 * uKnee);
    soft = soft * soft / (4.0 * uKnee + 0.0001);
    float contrib = max(soft, br - uThreshold) / max(br, 0.0001);
    fragColor = vec4(c * contrib, 1.0);
}
""";

    public const string BlurFrag = """
#version 410 core
in vec2 vUv;
out vec4 fragColor;
uniform sampler2D uTex;
uniform vec2 uTexel;
uniform vec2 uDir;
void main() {
    vec2 o = uTexel * uDir;
    vec3 c = texture(uTex, vUv).rgb * 0.227027;
    c += texture(uTex, vUv + o * 1.384615).rgb * 0.316216;
    c += texture(uTex, vUv - o * 1.384615).rgb * 0.316216;
    c += texture(uTex, vUv + o * 3.230769).rgb * 0.070270;
    c += texture(uTex, vUv - o * 3.230769).rgb * 0.070270;
    fragColor = vec4(c, 1.0);
}
""";

    public const string DownFrag = """
#version 410 core
in vec2 vUv;
out vec4 fragColor;
uniform sampler2D uTex;
uniform vec2 uTexel;
void main() {
    vec3 c = texture(uTex, vUv).rgb * 0.25;
    c += texture(uTex, vUv + vec2(uTexel.x, 0.0)).rgb * 0.25;
    c += texture(uTex, vUv + vec2(0.0, uTexel.y)).rgb * 0.25;
    c += texture(uTex, vUv + uTexel).rgb * 0.25;
    fragColor = vec4(c, 1.0);
}
""";

    public const string CompositeFrag = """
#version 410 core
in vec2 vUv;
out vec4 fragColor;
uniform sampler2D uScene;
uniform sampler2D uBloom0;
uniform sampler2D uBloom1;
uniform sampler2D uBloom2;
uniform vec2 uResolution;
uniform float uTime;
uniform float uExposure;
uniform float uBloomStrength;
uniform float uAberration;
uniform float uFlash;
uniform float uBeat;
uniform int uDamage;
uniform float uHue;
uniform float uSat;
uniform float uContrast;
uniform float uThump;
uniform float uGlitch;
uniform float uVignette;
uniform vec3 uTint;
uniform float uSpeedLines;
uniform vec2 uSpeedDir;
uniform float uDarkTheme;
uniform int uShockwaveCount;
uniform vec4 uShockwaves[4];

vec3 aces(vec3 x) {
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
}
float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}
vec3 hueRotate(vec3 c, float a) {
    // rotate chroma in YIQ space, keeping luma
    float y = dot(c, vec3(0.299, 0.587, 0.114));
    float i = dot(c, vec3(0.596, -0.274, -0.322));
    float q = dot(c, vec3(0.211, -0.523, 0.312));
    float ca = cos(a), sa = sin(a);
    float i2 = i * ca - q * sa;
    float q2 = i * sa + q * ca;
    return vec3(y + 0.956 * i2 + 0.621 * q2, y - 0.272 * i2 - 0.647 * q2, y - 1.106 * i2 + 1.703 * q2);
}
void main() {
    vec2 uv = vUv;
    // glitch: on sharp hi-hat spikes and drops, a few horizontal slices slip sideways
    if (uGlitch > 0.01) {
        float band = floor(uv.y * 46.0);
        float h = hash21(vec2(band, floor(uTime * 24.0)));
        if (h < uGlitch * 0.22) uv.x += (hash21(vec2(band, uTime)) - 0.5) * 0.045 * uGlitch;
    }
    float swChroma = 0.0;
    if (uShockwaveCount > 0) {
        float aspect = uResolution.x / max(uResolution.y, 1.0);
        vec2 swDisp = vec2(0.0);
        for (int i = 0; i < 4; i++) {
            if (i >= uShockwaveCount) break;
            vec4 sw = uShockwaves[i];
            vec2 rel = (uv - sw.xy) * vec2(aspect, 1.0);
            float dist = length(rel);
            float diff = dist - sw.z;
            float band = 0.055 + sw.z * 0.06;
            float ring = exp(-(diff * diff) / max(0.0004, band * band));
            float amp = sw.w * 0.018 * ring;
            vec2 nrm = dist > 0.0005 ? rel / dist : vec2(0.0);
            swDisp += vec2(nrm.x / aspect, nrm.y) * amp;
            swChroma += sw.w * ring;
        }
        uv = clamp(uv + swDisp, vec2(0.001), vec2(0.999));
    }
    vec2 dir = uv - 0.5;
    float ca = uAberration * 0.0045 * (0.35 + uBeat) + swChroma * 0.0055;
    vec3 scene;
    scene.r = texture(uScene, uv + dir * ca).r;
    scene.g = texture(uScene, uv).g;
    scene.b = texture(uScene, uv - dir * ca).b;
    // kick thump: a short radial zoom blur toward the centre on every beat
    if (uThump > 0.0005) {
        vec3 acc = scene;
        for (int k = 1; k <= 6; k++) acc += texture(uScene, uv - dir * uThump * float(k) / 6.0).rgb;
        scene = acc / 7.0;
    }
    vec3 bloom = texture(uBloom0, uv).rgb * 0.56
               + texture(uBloom1, uv).rgb * 0.29
               + texture(uBloom2, uv).rgb * 0.18;
    vec3 col = scene + bloom * uBloomStrength;
    col *= uExposure;
    col = aces(col);
    // speed lines: streaks rushing outward from a point just ahead of the ship's heading
    if (uSpeedLines > 0.01) {
        vec2 c = (uv - 0.5 - uSpeedDir * 0.12) * vec2(uResolution.x / uResolution.y, 1.0);
        float ang = atan(c.y, c.x);
        float r = length(c);
        float lane = floor(ang * 95.0);
        float h = hash21(vec2(lane, 3.1));
        float pos = fract(r * 1.6 - uTime * (2.0 + h * 3.5) + h * 7.0);
        float streak = step(0.82, h) * smoothstep(0.0, 0.15, pos) * smoothstep(0.55, 0.15, pos);
        // edges only: the centre of the screen, where you fight, stays clean
        streak *= smoothstep(0.55, 0.95, r) * smoothstep(1.0, 0.0, abs(fract(ang * 95.0) - 0.5) * 2.0);
        col += vec3(0.75, 0.95, 1.0) * streak * uSpeedLines * 0.4;
    }
    // music grading: mood hue, saturation and contrast follow the song's sections
    col = hueRotate(col, uHue);
    float luma = dot(col, vec3(0.299, 0.587, 0.114));
    col = mix(vec3(luma), col, uSat);
    float themeContrast = mix(1.0, 1.07, uDarkTheme);
    col = clamp((col - 0.5) * (uContrast * themeContrast) + 0.5, 0.0, 1.0);
    col *= uTint;
    if (uDamage == 1) {
        col = mix(col, vec3(1.0, 0.10, 0.18), 0.16);
    }
    col = mix(col, vec3(1.0), clamp(uFlash, 0.0, 0.45));
    float vig = smoothstep(1.12, 0.35, length(dir * vec2(1.0, 1.1)));
    float vigFloor = mix(0.36, 0.22, uDarkTheme);
    float effVig = mix(uVignette, max(uVignette, 0.85), uDarkTheme);
    col *= mix(1.0, vigFloor + (1.0 - vigFloor) * vig, effVig);
    float grain = hash21(uv * uResolution + fract(uTime) * 113.0) - 0.5;
    col += grain * mix(0.018, 0.012, uDarkTheme);
    col *= 0.985 + 0.015 * sin(uv.y * uResolution.y * 3.14159);
    col = clamp(col, 0.0, 1.0);
    fragColor = vec4(col, 1.0);
}
""";
    public const string MeshVert = """
#version 410 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec4 aColor;
layout(location = 3) in float aGlow;
uniform mat4 uViewProj;
out vec3 vNormal;
out vec4 vColor;
out float vGlow;
out vec3 vWorld;
void main() {
    gl_Position = uViewProj * vec4(aPos, 1.0);
    vNormal = aNormal;
    vColor = aColor;
    vGlow = aGlow;
    vWorld = aPos;
}
""";

    public const string MeshFrag = """
#version 410 core
in vec3 vNormal;
in vec4 vColor;
in float vGlow;
in vec3 vWorld;
out vec4 fragColor;
uniform vec3 uLightDir;
uniform vec3 uViewDir;
uniform vec3 uEye;
uniform int uPerspective;
uniform vec3 uRimColor;
uniform vec3 uFill;
uniform float uRim;
uniform float uBrightness;
uniform float uGlowScale;
uniform vec3 uFogColor;
uniform float uFogNear;
uniform float uFogFar;
uniform float uDarkTheme;
void main() {
    vec3 n = normalize(vNormal);
    vec3 V = uPerspective == 1 ? normalize(uEye - vWorld) : normalize(uViewDir);
    vec3 L = normalize(uLightDir);
    float diff = max(dot(n, L), 0.0);
    float back = max(dot(n, -L), 0.0);
    float hemi = mix(0.16, 0.42, clamp(n.z * 0.5 + 0.5, 0.0, 1.0));
    vec3 h = normalize(L + V);
    float spec = pow(max(dot(n, h), 0.0), 36.0);
    float fres = pow(1.0 - clamp(dot(n, V), 0.0, 1.0), 3.0);
    vec3 base = vColor.rgb;
    float darkHemi = mix(hemi, hemi * 0.32, uDarkTheme);
    float darkDiff = mix(diff, diff * 0.55, uDarkTheme);
    float fillScale = mix(0.35, 0.14, uDarkTheme);
    float rimBoost = mix(1.0, 2.15, uDarkTheme);
    float glowBoost = mix(1.0, 1.25, uDarkTheme);
    vec3 col = base * (darkHemi + darkDiff * 0.95) + uFill * back * fillScale * base;
    col += vec3(spec) * 0.55 * (0.3 + diff);
    col += uRimColor * fres * uRim * rimBoost;
    col *= uBrightness;
    col += base * vGlow * uGlowScale * glowBoost;
    float alpha = vColor.a;
    if (uPerspective == 1) {
        float d = length(uEye - vWorld);
        float f = smoothstep(uFogNear, uFogFar, d);
        col = mix(col, uFogColor, f);
        alpha *= 1.0 - f * 0.85;
    }
    fragColor = vec4(col, alpha);
}
""";
}
