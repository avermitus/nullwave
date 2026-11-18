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

    // Fluid abstract backdrop: domain-warped flowing nebula + raymarched liquid metaballs.
    public const string BackgroundFrag = """
#version 410 core
in vec2 vUv;
out vec4 fragColor;
uniform vec2 uResolution;
uniform vec2 uCam;
uniform float uTime;
uniform float uFlow;
uniform float uIntensity;
uniform float uDrop;
uniform float uBuild;
uniform float uKick;
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
// player motion: how fast (0..1) and which way the ship is moving in screen space
uniform float uSpeed;
uniform vec2 uVel;
uniform float uTravel;
// two backdrop genomes, blended by uMix during a transition
uniform int uStyleA;
uniform int uStyleB;
uniform float uMix;
uniform vec4 uParamA;
uniform vec4 uParamB;
uniform vec4 uMutA;
uniform vec4 uMutB;
uniform vec3 uPalOffA;
uniform vec3 uPalOffB;
uniform vec3 uPalFreqA;
uniform vec3 uPalFreqB;

vec4 P;      // current style params
vec4 MUT;    // current mutators: x mirror, y swirl, z zoom pulse, w roll speed
vec3 PO;     // palette offset
vec3 PF;     // palette frequency

// ---------------------------------------------------------------- utils
float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}
vec2 hash22(vec2 p) {
    float n = hash21(p);
    return vec2(n, hash21(p + n + 17.17));
}
float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i), b = hash21(i + vec2(1, 0)), c = hash21(i + vec2(0, 1)), d = hash21(i + vec2(1, 1));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}
float fbm(vec2 p) {
    float v = 0.0, a = 0.5;
    mat2 r = mat2(0.8, 0.6, -0.6, 0.8);
    for (int i = 0; i < 5; i++) { v += a * noise(p); p = r * p * 2.02 + 3.1; a *= 0.5; }
    return v;
}
mat2 rot(float a) { float c = cos(a), s = sin(a); return mat2(c, -s, s, c); }
// random cosine palette per genome: every backdrop gets its own colourway
vec3 pal(float t) { return 0.5 + 0.5 * cos(6.28318 * (PF * t + PO)); }

float band(int i) {
    if (i == 0) return uBass;
    if (i == 1) return uLowMid;
    if (i == 2) return uMid;
    if (i == 3) return uHigh;
    if (i == 4) return uBass * 0.6 + uMid * 0.4;
    return uEnergy;
}

vec3 nebula(vec2 p) {
    vec2 q = vec2(fbm(p + vec2(0.0, uFlow * 0.11)), fbm(p + vec2(5.2, 1.3) - uFlow * 0.07));
    vec2 r = vec2(fbm(p + 3.6 * q + vec2(1.7, 9.2) + uFlow * 0.13), fbm(p + 3.6 * q + vec2(8.3, 2.8) - uFlow * 0.09));
    float f = fbm(p + (3.2 + uBass * 1.4) * r);
    vec3 col = mix(pal(f * 0.6), pal(0.5 + length(q) * 0.4), clamp(length(r) * 0.6, 0.0, 1.0));
    return col * (f * f * 0.9 + 0.03);
}

// ---------------------------------------------------------------- 0: liquid metaballs
float smin(float a, float b, float k) {
    float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
    return mix(b, a, h) - k * h * (1.0 - h);
}
float liquidMap(vec3 p) {
    float k = 0.4 + P.y * 0.5 + uBass * 0.35 + uDrop * 0.4;
    float d = length(p) - (0.45 + uBass * 0.35 + uKick * 0.16 + uDrop * 0.45);
    int n = 3 + int(P.x * 4.0);
    for (int i = 0; i < 7; i++) {
        if (i >= n) break;
        float fi = float(i);
        float t = uFlow * (0.21 + fi * 0.037) + fi * 1.91 + P.w * 20.0;
        vec3 bp = vec3(sin(t * 1.13 + fi) * 1.55, cos(t * 0.87 + fi * 2.1) * 0.95, sin(t * 0.71 + fi * 0.6) * 0.9);
        float r = 0.24 + band(i % 6) * 0.32 + uKick * 0.06 + uDrop * 0.1;
        d = smin(d, length(p - bp * (1.0 + uDrop * 0.8)) - r, k);
    }
    float freq = (1.0 + P.z * 2.0) * (1.0 + uBuild);
    float ripple = sin(p.x * 6.0 * freq + uFlow * 2.3) * sin(p.y * 5.0 * freq - uFlow * 1.7) * sin(p.z * 7.0 * freq + uFlow * 1.1);
    return d + ripple * (0.025 + uHigh * 0.06 + uBuild * 0.05 + uDrop * 0.08);
}
vec3 styleLiquid(vec2 uv) {
    vec3 col = nebula(uv * 1.45 + P.w * 9.0) * (0.55 + uEnergy * 0.45);
    vec3 ro = vec3(uv.x * 0.0, 0.0, -6.6 + uBass * 0.3);
    vec3 rd = normalize(vec3(uv, 1.55));
    mat2 r2 = rot(uFlow * 0.05 + uTravel * 0.2);
    ro.xz = r2 * ro.xz; rd.xz = r2 * rd.xz;
    float b = dot(ro, rd);
    float disc = b * b - (dot(ro, ro) - 3.4 * 3.4);
    float glow = 0.0;
    if (disc > 0.0) {
        float t = max(0.0, -b - sqrt(disc));
        float tEnd = -b + sqrt(disc);
        float minD = 10.0;
        bool hit = false;
        for (int i = 0; i < 56; i++) {
            float d = liquidMap(ro + rd * t);
            minD = min(minD, d);
            if (d < 0.0015 * t) { hit = true; break; }
            t += d * 0.85;
            if (t > tEnd) break;
        }
        glow = exp(-max(minD, 0.0) * 5.5);
        if (hit) {
            vec3 p = ro + rd * t;
            const vec2 e = vec2(0.002, -0.002);
            vec3 n = normalize(e.xyy * liquidMap(p + e.xyy) + e.yyx * liquidMap(p + e.yyx) + e.yxy * liquidMap(p + e.yxy) + e.xxx * liquidMap(p + e.xxx));
            float fres = pow(1.0 - clamp(dot(n, -rd), 0.0, 1.0), 3.0);
            float spec = pow(max(dot(reflect(rd, n), normalize(vec3(-0.5, 0.7, -0.6))), 0.0), 40.0);
            vec3 tint = pal(dot(n, rd) * 0.8 + p.y * 0.25 + uFlow * 0.04);
            vec3 surf = tint * 0.05 + nebula(reflect(rd, n).xy * 1.2) * 0.6 + tint * fres * (0.7 + uKick * 1.3 + uDrop * 2.5) + vec3(spec) * 0.3;
            col = mix(col, surf, 0.85);
        }
    }
    return col + pal(uFlow * 0.05) * glow * (0.06 + uKick * 0.14 + uDrop * 0.4);
}

// ---------------------------------------------------------------- 1: hyperspace tunnel
vec3 styleTunnel(vec2 uv) {
    uv -= uVel * 0.12 * uSpeed;                  // the vanishing point leans away from the turn
    uv *= rot(uFlow * 0.08 * (P.w - 0.5) * 4.0);
    float r = length(uv);
    float a = atan(uv.y, uv.x);
    float sides = floor(3.0 + P.y * 6.0);
    float seg = 6.28318 / sides;
    float aa = mod(a + uFlow * 0.1 * P.z, seg) - seg * 0.5;
    float pr = r * cos(aa) / cos(seg * 0.5);   // polygonal tunnel cross-section
    float depth = 0.32 / max(pr, 0.015) + uTravel * (1.2 + P.x * 2.0) + uFlow * 0.8;
    float rings = pow(0.5 + 0.5 * sin(depth * 3.14159 * (1.0 + P.z)), 18.0 - uKick * 10.0);
    float stripes = pow(0.5 + 0.5 * sin(a * sides * (1.0 + floor(P.x * 3.0)) + depth * 0.7), 24.0);
    float panel = step(0.82, hash21(vec2(floor(depth * 2.0), floor(a / seg * 3.0 + depth * 0.1))));
    vec3 col = pal(depth * 0.03 + a * 0.05) * (rings * (0.7 + uKick * 1.2 + uDrop * 2.0) + stripes * (0.35 + uHigh) + panel * 0.25 * (0.4 + uBass));
    col *= smoothstep(0.0, 0.5, pr);           // far end fades into the vanishing point
    col += pal(0.5 + uFlow * 0.02) * exp(-r * 7.0) * (0.35 + uBass * 0.8 + uDrop * 2.0);
    return col;
}

// ---------------------------------------------------------------- 2: kaleidoscope fractal
vec3 styleKaleido(vec2 uv) {
    uv *= 1.1 + 0.25 * sin(uFlow * 0.13) - uKick * 0.08;
    uv *= rot(uFlow * 0.04 * (P.x - 0.5) * 3.0 + uTravel * 0.05);
    float n = floor(5.0 + P.y * 9.0);
    float a = atan(uv.y, uv.x), r = length(uv);
    a = mod(a, 6.28318 / n);
    a = abs(a - 3.14159 / n);
    uv = r * vec2(cos(a), sin(a));
    // Kali-set fold: abs(p)/dot(p,p) - c, with c drifting, gives endlessly morphing lace
    vec2 p = uv * (1.3 + P.x * 0.8);
    vec2 c = vec2(0.72 + 0.18 * sin(uFlow * 0.11 + P.z * 6.0), 0.52 + 0.2 * cos(uFlow * 0.09 + P.w * 6.0)) * (1.0 + uDrop * 0.08);
    vec3 col = vec3(0.0);
    for (int i = 0; i < 10; i++) {
        p = abs(p) / max(dot(p, p), 0.04) - c;
        float l = length(p);
        col += pal(float(i) * 0.07 + l * 0.08 + uFlow * 0.02) * exp(-abs(l - 1.0) * (3.5 - uKick * 1.5)) * (0.22 + band(i % 6) * 0.15);
    }
    return col * (0.9 + uKick * 0.5 + uDrop * 1.5);
}

// ---------------------------------------------------------------- 3: flythrough of an endless field
float fieldMap(vec3 p, out float id) {
    p.xy *= rot(p.z * 0.04 * (P.y - 0.5) * 3.0);
    vec3 cell = floor((p + 2.0) / 4.0);
    id = hash21(cell.xy + cell.z * 7.31);
    vec3 q = mod(p + 2.0, 4.0) - 2.0;
    q.xy *= rot(uFlow * 0.4 * (id - 0.5) + id * 6.0);
    float s = 0.45 + P.x * 0.35 + uKick * 0.12 * step(0.5, id);
    float torus = length(vec2(length(q.xz) - s * 1.4, q.y)) - s * 0.18;
    vec3 bq = abs(q) - vec3(s);
    float box = length(max(bq, 0.0)) + min(max(bq.x, max(bq.y, bq.z)), 0.0) - 0.05;
    float oct = (abs(q.x) + abs(q.y) + abs(q.z) - s * 1.6) * 0.577;
    float d = P.z < 0.33 ? torus : P.z < 0.66 ? box : oct;
    // keep a flight corridor open down the middle
    float corridor = 1.6 - length(p.xy);
    return max(d, corridor);
}
vec3 styleField(vec2 uv) {
    vec3 ro = vec3(sin(uFlow * 0.07) * 0.6, cos(uFlow * 0.05) * 0.4, uTravel * (3.0 + P.x * 3.0) + uFlow * 1.5);
    vec3 rd = normalize(vec3(uv - uVel * 0.1 * uSpeed, 1.3 - uSpeed * 0.35));
    rd.xy *= rot(uFlow * 0.03 * (P.w - 0.5) * 6.0 + uVel.x * 0.25 * uSpeed);
    float t = 0.0, id = 0.0, glow = 0.0;
    vec3 col = vec3(0.0);
    for (int i = 0; i < 60; i++) {
        vec3 p = ro + rd * t;
        float d = fieldMap(p, id);
        glow += exp(-max(d, 0.0) * 5.0) * 0.05;
        if (d < 0.003) {
            float fog = exp(-t * 0.06);
            col = pal(id + p.z * 0.02) * (0.45 + 0.9 * step(0.7, id) * (0.6 + uKick + uDrop)) * fog;
            break;
        }
        t += d * 0.9;
        if (t > 45.0) break;
    }
    return col + pal(uFlow * 0.03 + 0.3) * glow * (0.35 + uKick * 0.6 + uDrop);
}

// ---------------------------------------------------------------- 4: aurora ribbons
vec3 styleAurora(vec2 uv) {
    uv *= rot((P.w - 0.5) * 1.2);
    vec3 col = nebula(uv * 0.8 + P.z * 5.0) * 0.4;
    int layers = 4 + int(P.x * 4.0);
    for (int i = 0; i < 8; i++) {
        if (i >= layers) break;
        float fi = float(i);
        float speed = 0.3 + fi * 0.13 + uTravel * 0.4;
        float curve = sin(uv.x * (1.5 + fi * 0.7 * P.y) + uFlow * speed + fi * 1.7) * (0.12 + band(i % 6) * 0.18)
                    + (fbm(vec2(uv.x * 1.3 + uFlow * 0.1, fi)) - 0.5) * 0.4;
        float y = uv.y - curve + 0.45 - fi * 0.13;
        float core = exp(-abs(y) * (55.0 - uKick * 20.0));
        float veil = exp(-max(y, 0.0) * 6.0) * step(0.0, y) * 0.25;
        col += pal(fi * 0.11 + uv.x * 0.08 + uFlow * 0.02) * (core * (0.8 + uDrop * 2.0) + veil * (0.5 + uHigh));
    }
    return col;
}

// ---------------------------------------------------------------- 5: shattering cells
vec3 styleCells(vec2 uv) {
    float scale = 3.0 + P.x * 6.0 - uKick * 0.6;
    uv *= rot(uFlow * 0.03 * (P.y - 0.5) * 4.0);
    vec2 p = uv * scale + vec2(uTravel * 2.0 * (P.z - 0.5), uTravel * 2.5 + uFlow * 0.2);
    vec2 ip = floor(p), fp = fract(p);
    float f1 = 8.0, f2 = 8.0;
    float cid = 0.0;
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++) {
            vec2 g = vec2(x, y);
            vec2 o = hash22(ip + g);
            o = 0.5 + 0.45 * sin(uFlow * 0.6 + 6.28318 * o);
            float d = length(g + o - fp);
            if (d < f1) { f2 = f1; f1 = d; cid = hash21(ip + g); }
            else if (d < f2) f2 = d;
        }
    float edge = f2 - f1;
    float lit = step(1.0 - (0.08 + uKick * 0.25 + uDrop * 0.6), fract(cid * 7.0 + floor(uFlow * 0.5) * 0.37));
    vec3 col = pal(cid + uFlow * 0.02) * (0.08 + 0.1 * f1 + lit * (0.5 + uKick));
    col += pal(cid * 0.3 + 0.5) * exp(-edge * (12.0 - uBass * 5.0)) * (0.6 + uHigh * 0.6 + uDrop);
    return col;
}

vec3 runStyle(int style, vec2 uv) {
    if (style == 0) return styleLiquid(uv);
    if (style == 1) return styleTunnel(uv);
    if (style == 2) return styleKaleido(uv);
    if (style == 3) return styleField(uv);
    if (style == 4) return styleAurora(uv);
    return styleCells(uv);
}

// mutators shared by every style: mirror, swirl, zoom pulse
vec3 renderGenome(int style, vec4 params, vec4 mut, vec3 po, vec3 pf, vec2 uv) {
    P = params; MUT = mut; PO = po; PF = pf;
    if (MUT.x > 0.6) uv.x = abs(uv.x);
    float r = length(uv);
    uv *= rot(MUT.y * 2.5 * exp(-r * 1.5) * sin(uFlow * 0.2));
    uv *= 1.0 - MUT.z * 0.12 * uKick;
    return runStyle(style, uv);
}

void main() {
    float aspect = uResolution.x / max(uResolution.y, 1.0);
    vec2 uv = (vUv - 0.5) * vec2(aspect, 1.0);
    uv += uCam * 0.00004;
    vec3 col = renderGenome(uStyleA, uParamA, uMutA, uPalOffA, uPalFreqA, uv);
    if (uMix > 0.001) {
        // transition: the new genome blooms out from the centre over the old one
        vec3 next = renderGenome(uStyleB, uParamB, uMutB, uPalOffB, uPalFreqB, uv);
        float wipe = smoothstep(uMix * 1.6 - 0.25, uMix * 1.6, length(uv) * 0.9);
        col = mix(next, col, wipe);
        col += pal(uMix) * exp(-abs(length(uv) * 0.9 - uMix * 1.6 + 0.12) * 30.0) * 0.6 * (1.0 - uMix);
    }
    float r = length(uv);
    // ambient colour wash from the genome's palette: dark styles never collapse to black, so
    // dark enemy bodies always have a lit backdrop to read against
    vec3 ambient = mix(pal(0.15 + uv.y * 0.35 + uFlow * 0.01), pal(0.65 + uv.x * 0.25 - uFlow * 0.01), 0.5 + 0.5 * sin(uFlow * 0.07 + uv.x));
    float lum = dot(col, vec3(0.299, 0.587, 0.114));
    col += ambient * (0.11 + uKick * 0.04) * clamp(1.0 - lum * 3.0, 0.0, 1.0);
    col += uColC * exp(-r * 2.2) * uBeat * 0.06;
    float vig = smoothstep(1.35, 0.2, r);
    col *= 0.68 + 0.32 * vig;
    col += 0.003 * hash21(vUv * uResolution + uTime);
    // soft-clip the highlights: busy styles stay a backdrop, dark ones keep their ambient colour
    col = col * 1.05 / (1.0 + col * 1.8);
    fragColor = vec4(col * 0.85 * uIntensity, 1.0);
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
    vec2 dir = uv - 0.5;
    float ca = uAberration * 0.0045 * (0.35 + uBeat);
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
    col = clamp((col - 0.5) * uContrast + 0.5, 0.0, 1.0);
    col *= uTint;
    if (uDamage == 1) {
        col = mix(col, vec3(1.0, 0.10, 0.18), 0.16);
    }
    col = mix(col, vec3(1.0), clamp(uFlash, 0.0, 0.45));
    float vig = smoothstep(1.12, 0.35, length(dir * vec2(1.0, 1.1)));
    col *= mix(1.0, 0.36 + 0.64 * vig, uVignette);
    float grain = hash21(uv * uResolution + fract(uTime) * 113.0) - 0.5;
    col += grain * 0.018;
    col *= 0.985 + 0.015 * sin(uv.y * uResolution.y * 3.14159);
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
    vec3 col = base * (hemi + diff * 0.95) + uFill * back * 0.35 * base;
    col += vec3(spec) * 0.55 * (0.3 + diff);
    col += uRimColor * fres * uRim;
    col *= uBrightness;
    col += base * vGlow * uGlowScale;
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
