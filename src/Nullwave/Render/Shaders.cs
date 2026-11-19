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
uniform float uSnare;
uniform float uHat;
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
uniform vec2 uAim;
uniform float uTravel;
// transition blend factor between genome A and genome B
uniform float uMix;

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
            vec3 surf = tint * 0.05 + nebula(reflect(rd, n).xy * 1.2) * 0.6 + tint * fres * (0.7 + uKick * 1.3 + uSnare * 0.75 + uDrop * 2.5) + vec3(spec) * (0.3 + uHat * 0.85 + uSnare * 0.35);
            col = mix(col, surf, 0.85);
        }
    }
    return col + pal(uFlow * 0.05) * glow * (0.06 + uKick * 0.14 + uSnare * 0.08 + uDrop * 0.4);
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
    float rings = pow(0.5 + 0.5 * sin(depth * 3.14159 * (1.0 + P.z)), max(5.0, 18.0 - uKick * 10.0 - uSnare * 3.0));
    float stripes = pow(0.5 + 0.5 * sin(a * sides * (1.0 + floor(P.x * 3.0)) + depth * 0.7), 24.0);
    float panel = step(0.82, hash21(vec2(floor(depth * 2.0), floor(a / seg * 3.0 + depth * 0.1))));
    vec3 col = pal(depth * 0.03 + a * 0.05) * (rings * (0.7 + uKick * 1.2 + uSnare * 0.85 + uDrop * 2.0) + stripes * (0.35 + uHigh + uHat * 0.35) + panel * (0.25 * (0.4 + uBass) + uHat * 0.45));
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
        float lace = exp(-abs(l - 1.0) * max(1.3, 3.5 - uKick * 1.3 - uSnare * 0.5));
        float glint = exp(-abs(l - 1.0) * 18.0) * uHat * 0.45;
        col += pal(float(i) * 0.07 + l * 0.08 + uFlow * 0.02) * (lace * (0.22 + band(i % 6) * 0.15) + glint);
    }
    return col * (0.9 + uKick * 0.5 + uSnare * 0.35 + uDrop * 1.5);
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
            col = pal(id + p.z * 0.02) * (0.45 + 0.9 * step(0.7, id) * (0.6 + uKick + uSnare * 0.65 + uDrop) + uHat * 0.25 * step(0.45, id)) * fog;
            break;
        }
        t += d * 0.9;
        if (t > 45.0) break;
    }
    return col + pal(uFlow * 0.03 + 0.3) * glow * (0.35 + uKick * 0.6 + uSnare * 0.35 + uHat * 0.25 + uDrop);
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
        float core = exp(-abs(y) * max(18.0, 55.0 - uKick * 20.0 - uSnare * 10.0));
        float veil = exp(-max(y, 0.0) * 6.0) * step(0.0, y) * (0.25 + uHat * 0.18 * (0.5 + 0.5 * sin(uv.x * 24.0 + fi * 3.1 + uFlow * 6.0)));
        col += pal(fi * 0.11 + uv.x * 0.08 + uFlow * 0.02) * (core * (0.8 + uSnare * 0.65 + uDrop * 2.0) + veil * (0.5 + uHigh + uHat * 0.45));
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
    float lit = step(1.0 - clamp(0.08 + uKick * 0.25 + uSnare * 0.22 + uDrop * 0.6, 0.0, 0.92), fract(cid * 7.0 + floor(uFlow * 0.5) * 0.37));
    vec3 col = pal(cid + uFlow * 0.02) * (0.08 + 0.1 * f1 + lit * (0.5 + uKick + uSnare * 0.55));
    col += pal(cid * 0.3 + 0.5) * (exp(-edge * max(4.0, 12.0 - uBass * 5.0)) * (0.6 + uHigh * 0.6 + uDrop) + exp(-edge * 42.0) * uHat * 0.85);
    return col;
}

// ================================================================ generative backdrop
// A backdrop is 1-3 randomly assembled layers. Each layer runs its own chain of up to four
// motion transforms (space itself moves: tunnels, spiral zooms, folds, flowing warps, ripples,
// scrolling, tiled cells) into one of 13 pattern generators, then blends onto the layers below.
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
uniform vec4 uGlobA;   // x spin speed, y zoom, z mirror, w drift angle
uniform vec4 uGlobB;

float fbm3(vec2 p) {
    float v = 0.0, a = 0.5;
    mat2 r = mat2(0.8, 0.6, -0.6, 0.8);
    for (int i = 0; i < 3; i++) { v += a * noise(p); p = r * p * 2.03 + 1.7; a *= 0.5; }
    return v;
}

vec2 applyOp(int op, vec4 q, vec2 p, float t) {
    if (op == 1) {            // rotate: steady spin
        p *= rot(t * (q.x - 0.5) * 0.7 + q.y * 6.2832);
    } else if (op == 2) {     // tunnel: fly down an endless tube
        float r = length(p), a = atan(p.y, p.x);
        // abs(angle): mirrored around the tube so there is no seam where atan wraps at +-pi
        p = vec2(abs(a) / 3.14159 * (1.0 + floor(q.x * 4.0)), (0.22 + q.y * 0.3) / max(r, 0.02) + uTravel * (1.0 + q.z * 2.0) + t * 0.25 * q.w);
    } else if (op == 3) {     // kaleidoscope fold, slowly turning
        float n = 3.0 + floor(q.x * 9.0);
        float r = length(p), a = atan(p.y, p.x) + t * 0.06 * (q.y - 0.5);
        a = mod(a, 6.2832 / n);
        a = abs(a - 3.14159 / n);
        p = r * vec2(cos(a), sin(a));
    } else if (op == 4) {     // flowing domain warp
        float f = 0.7 + q.x * 2.0;
        vec2 w = vec2(fbm3(p * f + vec2(t * 0.21 * (0.3 + q.y), 0.0)), fbm3(p * f + vec2(5.2, 1.3) - t * 0.17 * (0.3 + q.y)));
        p += (w - 0.5) * (0.35 + q.z * 1.3) * (1.0 + uBass * 0.4 + uSnare * 0.22 + uDrop * 0.45);
    } else if (op == 5) {     // swirl: a vortex that winds and unwinds
        float r = length(p);
        p *= rot((1.0 + q.x * 2.5) * exp(-r * (0.8 + q.y * 2.0)) * sin(t * (0.15 + q.z * 0.3) + q.w * 6.0) + (uKick * 0.25 + uSnare * 0.18 + uDrop * 0.35) * (0.35 + q.w));
    } else if (op == 6) {     // mirror symmetry
        p.x = abs(p.x) - q.x * 0.35;
        if (q.y > 0.5) p.y = abs(p.y) - q.z * 0.25;
    } else if (op == 7) {     // log-spiral: an infinite zoom that never ends
        float r = max(length(p), 1e-3), a = atan(p.y, p.x);
        float lr = log(r);
        p = vec2(lr * (0.8 + q.x * 1.8) - t * (0.12 + q.y * 0.35) - uTravel * 0.25, abs(a) / 3.14159 * (1.0 + floor(q.z * 3.0)) + lr * (q.w - 0.5) * 2.0);
    } else if (op == 8) {     // scroll: the pattern streams past in one direction
        vec2 d = vec2(cos(q.x * 6.2832), sin(q.x * 6.2832));
        p += d * (t * (0.04 + q.y * 0.3) + uTravel * q.z * 0.6);
    } else if (op == 9) {     // ripple: rings of displacement radiating outward
        float r = max(length(p), 1e-3);
        p += p / r * sin(r * (8.0 + q.x * 20.0) - t * (1.0 + q.y * 3.0)) * (0.02 + q.z * 0.06) * (1.0 + uKick + uSnare * 0.55 + uDrop * 0.9);
    } else if (op == 10) {    // tiled cells, each spinning on its own
        float s = 1.5 + q.x * 4.5;
        vec2 id = floor(p * s);
        vec2 f = fract(p * s) - 0.5;
        float h = hash21(id + floor(q.w * 50.0));
        f *= rot(t * (h - 0.5) * 2.0 * (0.3 + q.y) + h * 6.2832);
        p = f * (1.0 + q.z * 2.0) + vec2(h * 7.0, h * 3.0) * q.w;
    } else if (op == 11) {    // breathing zoom on the beat
        p *= max(0.45, 1.0 - (uKick * 0.14 + uDrop * 0.12) * (0.3 + q.x * 0.7) + sin(t * 0.35 + q.z * 6.0) * 0.12 * q.y);
    }
    return p;
}

// ---------------------------------------------------------------- new generators
vec3 genInk(vec2 p, vec4 a, float t) {
    p *= 0.8 + a.x * 1.6;
    vec2 q = vec2(fbm(p + vec2(0.0, t * 0.11)), fbm(p + vec2(5.2, 1.3) - t * 0.07));
    float foldGain = 3.6 + uKick * 0.45 + uDrop * 0.8;
    vec2 r = vec2(fbm(p + foldGain * q + vec2(1.7, 9.2) + t * 0.13), fbm(p + foldGain * q + vec2(8.3, 2.8) - t * 0.09));
    float f = fbm(p + (2.5 + a.y * 2.0 + uBass + uDrop * 0.9) * r);
    float ridge = exp(-abs(fract(f * 3.5 + length(r) * 0.4) - 0.5) * 18.0);
    float sheen = exp(-abs(fract(f * 8.0 - t * 0.35) - 0.5) * 26.0) * (0.14 + uHat * 0.78);
    vec3 base = mix(pal(f * 0.7 + a.z), pal(0.5 + length(q) * 0.5), clamp(length(r) * 0.7, 0.0, 1.0));
    return base * (f * f * (1.45 + uDrop * 0.45) + 0.03)
         + pal(0.25 + f + a.z) * (ridge * (0.16 + uSnare * 0.85 + uKick * 0.25) + sheen);
}

vec3 genTruchet(vec2 p, vec4 a, float t) {
    // quarter-circle tiles; light dashes race along the joined arcs like a circuit
    float s = 2.5 + a.x * 6.0;
    vec2 g = p * s;
    vec2 id = floor(g), f = fract(g) - 0.5;
    float h = hash21(id + floor(a.w * 40.0));
    if (h > 0.5) f.x = -f.x;
    vec2 c = f.x + f.y > 0.0 ? vec2(0.5) : vec2(-0.5);
    float d = abs(length(f - c) - 0.5);
    float ang = atan(f.y - c.y, f.x - c.x);
    float dir = (h > 0.5 ? 1.0 : -1.0) * (mod(id.x + id.y, 2.0) * 2.0 - 1.0);
    float phase = ang * (1.0 + floor(a.y * 3.0)) / 1.5708 + dir * t * (0.6 + a.z * 2.0);
    float dash = smoothstep(0.35, 0.5, fract(phase));
    float sparkle = smoothstep(0.84, 0.98, fract(phase * 3.0 + h * 7.0)) * uHat;
    float surge = step(0.55, fract(h * 11.3 + floor(t * 2.0) * 0.37)) * uSnare;
    float line = exp(-d * max(16.0, 40.0 - uKick * 15.0 - uDrop * 10.0));
    float core = exp(-d * 70.0);
    return pal(h * 0.4 + t * 0.03) * (line * (0.24 + dash * (1.05 + uHigh + uDrop * 0.75) + surge * 0.95)
         + core * (sparkle * 1.4 + uDrop * 0.55));
}

vec3 genInterference(vec2 p, vec4 a, float t) {
    // drifting wave sources whose crests interfere into shifting moire
    float v = 0.0;
    float overtone = 0.0;
    float f = 10.0 + a.x * 30.0;
    for (int i = 0; i < 4; i++) {
        float fi = float(i);
        vec2 c = 0.7 * vec2(sin(t * (0.21 + a.y * 0.3) + fi * 2.1), cos(t * (0.17 + a.z * 0.3) + fi * 1.3));
        float ph = length(p - c) * f - t * (1.5 + a.w * 3.0) - uSnare * 0.85;
        v += sin(ph);
        overtone += sin(ph * 2.0 + fi);
    }
    v *= 0.25;
    overtone *= 0.25;
    float crest = pow(0.5 + 0.5 * v, max(2.2, 6.0 - uBass * 2.0 - uDrop * 1.5));
    float sharp = pow(0.5 + 0.5 * overtone, 10.0) * uHat;
    return pal(v * 0.3 + t * 0.02) * (0.06 + crest * (0.85 + uKick + uSnare * 0.75 + uDrop * 1.15))
         + pal(v * 0.3 + 0.42 + t * 0.02) * sharp * 0.95;
}

vec3 genStreams(vec2 p, vec4 a, float t) {
    // particle streams: three depth layers of particles scrolling past with motion streaks
    vec2 dir = vec2(cos(a.x * 6.2832), sin(a.x * 6.2832));
    vec3 col = vec3(0.0);
    for (int k = 0; k < 3; k++) {
        float fk = float(k);
        float s = 5.0 + fk * 4.0 + a.y * 6.0;
        float speed = (0.15 + a.z * 0.6) * (1.0 + fk * 0.6) * (1.0 + uSpeed * 1.5 + uDrop * 0.85);
        vec2 q = p * s + dir * (t * speed + uTravel * (1.0 + fk)) * s;
        vec2 id = floor(q), f = fract(q) - 0.5;
        float h = hash21(id + fk * 13.0);
        if (h > clamp(0.50 + uHigh * 0.15 + uHat * 0.18, 0.25, 0.88)) continue;
        vec2 o = (hash22(id + 3.0) - 0.5) * 0.6;
        vec2 rel = f - o;
        float along = dot(rel, dir);
        float tailLen = 0.45 * (0.3 + uSpeed + uDrop * 0.55 + uSnare * 0.25);
        float side = length(rel - dir * clamp(along, -tailLen, 0.0));
        float streak = exp(-side * (56.0 + fk * 20.0)) * (0.5 + 0.5 * sin(t * 4.0 + h * 40.0));
        float headGlint = exp(-length(rel) * 32.0) * (0.2 + uHat * 1.15);
        float flash = 1.0 + uKick * 0.55 + uSnare * 0.85 * step(0.45, fract(h * 17.0 + fk * 0.31)) + uDrop * 0.65;
        col += pal(h + fk * 0.2) * (streak * (0.62 - fk * 0.12) * flash + headGlint * 0.45);
    }
    return col;
}

vec3 genGridPulse(vec2 p, vec4 a, float t) {
    // neon lattice with light pulses running along its lines
    float s = 2.0 + a.x * 5.0;
    vec2 g = p * s;
    vec2 f = abs(fract(g) - 0.5);
    float lines = exp(-min(f.x, f.y) * max(18.0, 35.0 - uDrop * 12.0));
    float node = exp(-length(f) * 28.0);
    float rowH = hash21(vec2(floor(g.y), 3.0));
    float colH = hash21(vec2(floor(g.x), 9.0));
    float pulseX = exp(-fract(g.x * 0.25 - t * (0.5 + a.y * 1.5) * (rowH > 0.5 ? 1.0 : -1.0) + rowH) * 9.0) * step(f.y, 0.06);
    float pulseY = exp(-fract(g.y * 0.25 - t * (0.5 + a.z * 1.5) * (colH > 0.5 ? 1.0 : -1.0) + colH) * 9.0) * step(f.x, 0.06);
    float snareWave = exp(-abs(fract(length(floor(g)) * 0.18 - t * 1.2) - 0.5) * 14.0) * lines * uSnare;
    return pal(rowH * 0.3 + colH * 0.3 + t * 0.02) * (lines * (0.18 + uDrop * 0.35) + (pulseX + pulseY) * (1.15 + uKick + uDrop * 0.85) + snareWave * 1.1)
         + pal(0.5 + rowH * 0.2 + colH * 0.2 + t * 0.02) * node * (0.2 + uHat * 1.35 + uDrop * 0.4);
}

vec3 genPlasma(vec2 p, vec4 a, float t) {
    // crisp harmonic contour bands over a deep silk under-glow
    float k = 2.0 + a.x * 4.5;
    float snareKick = uSnare * 0.75;
    float v = sin(p.x * k + t * (0.5 + a.y) + snareKick)
            + sin(p.y * k * 1.3 - t * 0.7 - snareKick * 0.6)
            + sin((p.x + p.y) * k * 0.75 + t * (0.35 + a.z))
            + sin(length(p * k * 0.9) - t * 1.1 + uBass * 2.0);
    v *= 0.25;
    float bands = 2.5 + a.w * 3.5;
    float phase = v * bands + t * 0.12 + uSnare * 0.22;
    float d1 = abs(fract(phase) - 0.5);
    float d2 = abs(fract(phase * 2.0 + 0.25) - 0.5);
    float ridge = exp(-d1 * max(11.0, 22.0 - uKick * 7.0 - uDrop * 6.0));
    float core = exp(-d1 * 48.0);
    float shimmer = exp(-d2 * 36.0) * (0.22 + uHat * 0.95 + uHigh * 0.35);
    float silk = (0.06 + 0.14 * pow(0.5 + 0.5 * v, 2.0)) * (1.0 + uBass * 0.35);
    vec3 base = pal(v * 0.65 + t * 0.03 + a.w);
    vec3 accent = pal(v * 0.65 + 0.38 + t * 0.03 + a.w);
    return base * (silk + ridge * (0.72 + uKick * 0.7 + uDrop * 1.1))
         + accent * (core * (0.45 + uSnare * 0.95 + uDrop * 0.7) + shimmer * 0.55);
}

vec3 genHex(vec2 p, vec4 a, float t) {
    // hexagon lattice; waves of light travel outward through the cells
    float s = 2.0 + a.x * 5.0;
    vec2 g = p * s;
    const vec2 r = vec2(1.0, 1.7320508);
    vec2 h = r * 0.5;
    vec2 ga = mod(g, r) - h;
    vec2 gb = mod(g - h, r) - h;
    vec2 gv = dot(ga, ga) < dot(gb, gb) ? ga : gb;
    vec2 id = g - gv;
    vec2 av = abs(gv);
    float edge = 0.5 - max(dot(av, normalize(vec2(1.0, 1.7320508))), av.x);
    float cid = hash21(id);
    float dist = length(id) * (0.3 + a.y * 0.5);
    float wave = pow(0.5 + 0.5 * sin(dist * 3.0 - t * (1.0 + a.z * 2.5) + cid * a.w * 4.0), 8.0);
    float snareRing = exp(-abs(fract(dist * 0.45 - t * 0.9) - 0.5) * 16.0) * uSnare;
    float facet = exp(-abs(edge - 0.16) * 32.0) * (0.15 + uHat * 1.1) * step(0.35, fract(cid * 9.7 + floor(t * 2.5) * 0.23));
    return pal(cid * 0.3 + dist * 0.05 + t * 0.02) * (exp(-edge * max(16.0, 30.0 - uDrop * 10.0)) * (0.24 + (wave + snareRing) * (1.2 + uKick + uDrop * 0.9)) + (wave + snareRing * 0.7) * 0.14)
         + pal(cid * 0.3 + 0.45 + t * 0.02) * facet * 0.65;
}

vec3 runGen(int g, vec4 a, vec2 p, float t) {
    P = a;
    if (g == 0) return genInk(p, a, t);
    if (g == 1) return styleCells(p);
    if (g == 2) return styleKaleido(p);
    if (g == 3) return genTruchet(p, a, t);
    if (g == 4) return genInterference(p, a, t);
    if (g == 5) return genStreams(p, a, t);
    if (g == 6) return genGridPulse(p, a, t);
    if (g == 7) return styleLiquid(p);
    if (g == 8) return styleField(p);
    if (g == 9) return genPlasma(p, a, t);
    if (g == 10) return styleAurora(p);
    if (g == 11) return genHex(p, a, t);
    return styleTunnel(p);
}

vec3 renderLayer(Layer L, vec2 uv) {
    PO = L.po; PF = L.pf;
    float t = uFlow;
    vec2 p = uv;
    p = applyOp(L.op0, L.p0, p, t);
    p = applyOp(L.op1, L.p1, p, t);
    p = applyOp(L.op2, L.p2, p, t);
    p = applyOp(L.op3, L.p3, p, t);
    MUT = L.gb;
    return runGen(L.gen, L.ga, p, t) * L.gain;
}

vec3 blendLayer(int mode, vec3 base, vec3 l) {
    if (mode == 1) return 1.0 - (1.0 - clamp(base, 0.0, 1.0)) * (1.0 - clamp(l, 0.0, 1.0));
    if (mode == 2) return max(base, l);
    if (mode == 3) return base * (0.35 + l * 1.8) + l * 0.12;
    if (mode == 4) return abs(base - l);
    return base + l;
}

vec3 renderGenomeA(vec2 uv) {
    uv *= rot(uFlow * (uGlobA.x - 0.5) * 0.12) * (0.85 + uGlobA.y * 0.4);
    if (uGlobA.z > 0.75) uv.x = abs(uv.x);
    vec3 col = renderLayer(uLA[0], uv);
    if (uCountA > 1) col = blendLayer(uLA[1].blend, col, renderLayer(uLA[1], uv));
    if (uCountA > 2) col = blendLayer(uLA[2].blend, col, renderLayer(uLA[2], uv));
    PO = uLA[0].po; PF = uLA[0].pf;
    return col;
}

vec3 renderGenomeB(vec2 uv) {
    uv *= rot(uFlow * (uGlobB.x - 0.5) * 0.12) * (0.85 + uGlobB.y * 0.4);
    if (uGlobB.z > 0.75) uv.x = abs(uv.x);
    vec3 col = renderLayer(uLB[0], uv);
    if (uCountB > 1) col = blendLayer(uLB[1].blend, col, renderLayer(uLB[1], uv));
    if (uCountB > 2) col = blendLayer(uLB[2].blend, col, renderLayer(uLB[2], uv));
    return col;
}

void main() {
    float aspect = uResolution.x / max(uResolution.y, 1.0);
    vec2 uv = (vUv - 0.5) * vec2(aspect, 1.0);
    uv += uCam * 0.00004 + uAim * 0.038;
    vec3 col = renderGenomeA(uv);
    if (uMix > 0.001) {
        // transition: the new backdrop blooms out from the centre over the old one
        vec3 next = renderGenomeB(uv);
        float wipe = smoothstep(uMix * 1.6 - 0.25, uMix * 1.6, length(uv) * 0.9);
        col = mix(next, col, wipe);
        col += pal(uMix) * exp(-abs(length(uv) * 0.9 - uMix * 1.6 + 0.12) * 30.0) * 0.6 * (1.0 - uMix);
        PO = uLA[0].po; PF = uLA[0].pf;
    }
    float r = length(uv);
    // ambient colour wash: dark generators never collapse to black, while dense stacks stay moody
    // drifting coloured fog under everything: sparse line generators float in atmosphere
    float fog = fbm3(uv * 1.3 + vec2(uFlow * 0.05, -uFlow * 0.04));
    vec3 ambient = mix(pal(0.15 + fog * 0.5 + uv.y * 0.2), pal(0.65 + fog * 0.4 - uv.x * 0.1), fog);
    float lum = dot(col, vec3(0.299, 0.587, 0.114));
    col += ambient * (0.10 + fog * 0.10 + uKick * 0.03 + uSnare * 0.025) * clamp(1.0 - lum * 3.0, 0.0, 1.0);
    float aimWash = 0.5 + 0.5 * dot(normalize(uv + vec2(0.0001)), normalize(vec2(uAim.x, -uAim.y) + vec2(0.0001)));
    col += mix(uColB, uPrim * 0.28, 0.35) * length(uAim) * aimWash * 0.05 * (0.4 + uEnergy * 0.6);
    col += uColC * exp(-r * 2.2) * uBeat * 0.06;
    float vig = smoothstep(1.35, 0.2, r);
    col *= 0.68 + 0.32 * vig;
    col += 0.003 * hash21(vUv * uResolution + uTime);
    // soft-clip the highlights: busy backdrops stay a backdrop
    col = col * 1.04 / (1.0 + col * 1.85);
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
