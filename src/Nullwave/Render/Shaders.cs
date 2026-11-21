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

    // Map-anchored 3D backdrop: an organic raymarched world lying underneath the arena floor, seen
    // through the same camera as the fight (it scrolls and parallaxes with the map, never with
    // the ship). Its shape is driven directly by the music's spectrum, beat clock and onsets.
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
// world camera (game units): the backdrop is fixed to the map through these
uniform vec2 uCam;
uniform vec2 uView;
uniform float uOblique;
// continuous beat clock (integer part = beat index) and how much to trust it
uniform float uBeatPos;
uniform float uBeatLock;
// shockwaves rolling through the 3D world: xy = origin (scene units), z = age (s), w = strength
uniform int uWaveCount;
uniform vec4 uWaves[6];
uniform int uAtmoA;
uniform int uAtmoB;
// the music's spectrum, 16 log bands low -> high, each normalised against its own recent level
uniform float uSpec[16];
// readability: the backdrop darkens under the ship and enemies (screenPos xy, radius, strength)
uniform int uFocusCount;
uniform vec4 uFocus[16];
// musical understanding: drums vs. melody per register (low, mid, high), chord colour, brightness
uniform vec3 uPerc;
uniform vec3 uHarm;
uniform float uChordShift;
uniform float uBright;
// EDM show (Renderer.EdmFxFor): kaleidoscope through build-ups, the camera lifting away through a
// build and slamming down on the drop, beat-stepped hue, a beat-locked pump, strobe
uniform float uKaleido;
uniform float uVertigo;
uniform float uDive;
uniform float uCamKick;
uniform float uHueStep;
uniform float uPump;
uniform float uStrobe;

vec3 PO;     // palette offset
vec3 PF;     // palette frequency
vec4 GA;     // scene params A
vec4 GB;     // scene params B
vec4 GG;     // global params (fog, key light, phase)
float BEAT_I, BEAT_F, SNAP, HOP, MOT, TENSION;
float SB, SM, SH;   // spectrum: lows, mids, highs
vec3 PERC, HARM;    // drums / melody per register
float CHORD;        // palette phase shift from the current chord
float MID, MID2;    // material scratch written by the scene maps

// ---------------------------------------------------------------- utils
float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}
vec2 hash22(vec2 p) { float n = hash21(p); return vec2(n, hash21(p + n * 17.17 + 3.1)); }
float hash31(vec3 p) { return hash21(p.xy + p.z * 37.17); }
mat2 rot2(float a) { float c = cos(a), s = sin(a); return mat2(c, -s, s, c); }
vec3 pal(float t) { return 0.5 + 0.5 * cos(6.28318 * (PF * t + PO + CHORD)); }
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i), b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0)), d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}
float vnoise3(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = mix(hash31(i), hash31(i + vec3(1, 0, 0)), f.x);
    float b = mix(hash31(i + vec3(0, 1, 0)), hash31(i + vec3(1, 1, 0)), f.x);
    float c = mix(hash31(i + vec3(0, 0, 1)), hash31(i + vec3(1, 0, 1)), f.x);
    float d = mix(hash31(i + vec3(0, 1, 1)), hash31(i + vec3(1, 1, 1)), f.x);
    return mix(mix(a, b, f.y), mix(c, d, f.y), f.z);
}
float fbm2(vec2 p) {
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 3; i++) { s += a * vnoise(p); p = rot2(0.7) * p * 2.03 + 1.7; a *= 0.5; }
    return s / 0.875;
}
// spectrum lookup, x = 0 (sub bass) .. 1 (air)
float spec(float x) {
    float f = clamp(x, 0.0, 1.0) * 15.0;
    int i = int(floor(f));
    int j = min(i + 1, 15);
    return mix(uSpec[i], uSpec[j], f - float(i));
}

// Shockwaves from beats, drops and big hits roll outward through the world from where they began.
float waveField(vec2 q) {
    float s = 0.0;
    for (int i = 0; i < 6; i++) {
        if (i >= uWaveCount) break;
        vec4 w = uWaves[i];
        float d = length(q - w.xy) - w.z * 8.0;
        s += w.w * exp(-d * d * 0.9) * exp(-w.z * 0.9);
    }
    return s * MOT;
}

// ================================================================ 0: SPECTRAL SEA
// A glossy sea built from eight swell trains, one per slice of the spectrum: the bass lifts the
// long swells, the mids and highs chop the surface. Directions follow the golden angle (no grid).
float seaH(vec2 q) {
    float t = uFlow * (0.5 + GA.y * 0.4);
    float h = 0.0;
    float ang = GA.x * 6.2832;
    for (int i = 0; i < 8; i++) {
        float fi = float(i);
        float k = 0.42 * pow(1.52, fi) * (0.8 + GB.x * 0.5);
        float a = ang + fi * 2.39996 + sin(fi * 1.7 + GA.z * 6.0) * 0.5;
        vec2 d = vec2(cos(a), sin(a));
        float reg = i < 3 ? 0.5 + HARM.x * 0.9 : i < 6 ? 0.5 + HARM.y * 0.9 : 0.45 + PERC.z * 1.1;
        float amp = (0.08 + spec(fi / 7.0) * 1.0) * reg / (1.0 + fi * 0.6);
        h += sin(dot(q, d) * k + t * sqrt(k) * 1.7 + fi * 1.3) * amp;
    }
    return h * (0.6 + MOT * 0.5) + waveField(q) * 1.2;
}
float mapSea(vec3 p) {
    float base = -2.8 - GB.y * 1.2 - TENSION * 0.6 * MOT;
    float hh = seaH(p.xy);
    MID = hh;
    return (p.z - (base + hh)) * 0.35;
}

// ================================================================ 1: MOLTEN CRUST
// Domain-warped crust plates split by glowing fissures. Each fissure listens to its own slice of
// the spectrum; the bass widens and lifts the magma, kicks heave the plates.
float crust(vec2 q, out float vein) {
    vec2 w = q * (0.16 + GA.x * 0.10) + GA.zw * 30.0;
    vec2 warp = vec2(vnoise(w * 0.7 + uFlow * 0.03), vnoise(w * 0.7 + 5.2 - uFlow * 0.025));
    float n = fbm2(w + warp * (1.6 + SM * 0.8));
    vein = 1.0 - smoothstep(0.0, 0.02 + SB * 0.035, abs(n - 0.5));
    return n;
}
float mapCrust(vec3 p) {
    float vein;
    float n = crust(p.xy, vein);
    float surf = -3.2 - GB.y * 1.0 + (n - 0.5) * (2.4 + GB.x * 1.6) - vein * (0.3 + SB * 0.6)
               + waveField(p.xy) * 1.0 + uKick * 0.12 * MOT - TENSION * 0.7 * MOT;
    MID = n; MID2 = vein;
    return (p.z - surf) * 0.4;
}

// ================================================================ 2: SOUND DUNES
// Ridged hills whose octaves are the spectrum: bass shapes the big ridges, mids the folds, highs
// the grit, so the land re-forms with the song. Contour lines climb a step on every beat.
float dunesH(vec2 q) {
    float h = 0.0, amp = 1.0;
    vec2 x = q * (0.13 + GA.x * 0.10) + GA.zw * 40.0;
    mat2 m = rot2(0.85 + GA.y * 1.5) * 2.05;
    for (int i = 0; i < 4; i++) {
        float n = 1.0 - abs(vnoise(x + uFlow * 0.02 * float(i)) * 2.0 - 1.0);
        h += n * n * amp * (0.45 + spec(0.08 + float(i) * 0.27) * 1.3);
        x = m * x + 3.7;
        amp *= 0.5;
    }
    return h;
}
float mapDunes(vec3 p) {
    float h = dunesH(p.xy);
    float surf = -5.6 - GB.y * 1.0 + h * (1.6 + GB.x * 1.4) * (0.8 + HARM.x * 0.4) + waveField(p.xy) * 1.4 - TENSION * 0.8 * MOT;
    surf = min(surf, -1.0);
    MID = h;
    return (p.z - surf) * 0.38;
}

// ================================================================ 3: GYROID CAVERN
// An endless gyroid lattice flowing up through itself; walls swell with the bass, the lattice
// stretches with the mids, veins spark with the highs.
float mapGyroid(vec3 p) {
    float s = (0.75 + GA.x * 0.7) * (1.0 + (SM - 0.4) * 0.08 * MOT);
    vec3 q = p * s;
    // warp the lattice with low-frequency noise so it never reads as a repeating pattern
    vec2 wq = p.xy * (0.11 + GA.z * 0.08) + GA.zw * 17.0;
    q.xy += (vec2(vnoise(wq), vnoise(wq.yx + 5.3)) - 0.5) * (5.0 + GB.z * 3.0);
    q.z += (vnoise(wq * 1.7 + 9.1) - 0.5) * 3.0 + uFlow * (0.2 + GA.y * 0.25) * MOT;
    float g = dot(sin(q), cos(q.yzx));
    float th = 0.06 + GB.x * 0.12 + (HARM.x * 0.16 + SB * 0.08 + PERC.x * 0.12 + uDrop * 0.18) * MOT + waveField(p.xy) * 0.4;
    float d = (abs(g) - th) / s * 0.6;
    MID = g;
    return max(d, p.z + 1.2 + GB.y * 1.2);
}

// ================================================================ dispatch, shading
float mapScene(int g, vec3 p) {
    if (g == 0) return mapSea(p);
    if (g == 1) return mapCrust(p);
    if (g == 2) return mapDunes(p);
    return mapGyroid(p);
}

vec3 normalScene(int g, vec3 p, float t) {
    float h = 0.0012 * t + 0.002;
    const vec2 k = vec2(1.0, -1.0);
    return normalize(
        k.xyy * mapScene(g, p + k.xyy * h) +
        k.yyx * mapScene(g, p + k.yyx * h) +
        k.yxy * mapScene(g, p + k.yxy * h) +
        k.xxx * mapScene(g, p + k.xxx * h));
}

vec3 shadeScene(int g, vec3 p, vec3 n, vec3 rd) {
    vec3 L = normalize(vec3(GG.y - 0.5, GG.z - 0.5, 0.9));
    float dif = max(dot(n, L), 0.0);
    float fres = pow(1.0 - clamp(dot(n, -rd), 0.0, 1.0), 3.0);
    float spc = pow(max(dot(reflect(rd, n), L), 0.0), 40.0);
    float hs = GB.w;
    vec3 col;
    if (g == 0) {
        vec3 refl = reflect(rd, n);
        float sp = pow(max(dot(refl, L), 0.0), 70.0);
        vec3 sky = mix(pal(0.15 + hs), pal(0.55 + hs), clamp(refl.x * 0.6 + refl.y * 0.6 + 0.5, 0.0, 1.0));
        float crest = smoothstep(0.25, 0.85, MID);
        float trough = 1.0 - smoothstep(-0.6, 0.1, MID);
        vec3 water = mix(vec3(0.05, 0.12, 0.26), pal(0.62 + MID * 0.18 + hs), 0.40);
        col = water * (0.15 + 0.35 * dif) + sky * (0.035 + fres * 0.25);
        col += mix(vec3(0.36, 0.55, 0.78), sky, 0.42) * sp * (0.18 + SH * 0.44);
        // Treble catches the crests while bass lights the water from below.
        col += pal(0.3 + MID * 0.4 + hs) * crest * (0.08 + SH * 0.45 + uSnare * 0.27);
        col += water * trough * (0.09 + SB * 0.30 + uKick * 0.12);
    } else if (g == 1) {
        float n0 = MID, vein = MID2;
        // each fissure listens to its own band, so the cracks flicker in the shape of the song
        float band = fract(n0 * 9.7 + hash21(floor(p.xy * 0.35)) * 0.5);
        float heat = 0.15 + spec(band) * 1.1 + PERC.x * 0.7 + HARM.x * 0.25 + uDrop * 0.4;
        vec3 rock = pal(0.55 + n0 * 0.3 + hs) * 0.6 + vec3(0.05);
        vec3 magma = mix(pal(0.05 + hs), vec3(1.0, 0.55, 0.2), 0.35);
        col = rock * (0.10 + dif * 0.32 + fres * 0.14) + rock * spc * 0.12;
        col += magma * vein * heat * (0.38 + fres * 0.18);
        col += magma * (1.0 - smoothstep(0.0, 0.08, abs(n0 - 0.5))) * (0.05 + SB * 0.17);
    } else if (g == 2) {
        float h = MID;
        vec3 tone = pal(h * 0.35 + hs);
        vec3 tone2 = pal(h * 0.35 + 0.5 + hs);
        float cz = p.z * (0.7 + GB.z * 0.8) - SNAP * 0.25;
        float contour = floor(cz + 0.5);
        float major = 1.0 - step(0.5, mod(contour, 3.0));
        float line = 1.0 - smoothstep(0.0, mix(0.025, 0.04, major), abs(cz - contour));
        // Every contour still listens to its own band. Key light and elevation set its prominence.
        float lineBand = spec(fract(contour * 0.173 + GB.w));
        float peak = smoothstep(0.9, 1.5, h);
        vec3 sand = mix(vec3(0.07, 0.14, 0.28), tone, 0.58);
        col = sand * (0.12 + dif * 0.36) + tone2 * fres * 0.10;
        col += mix(sand, tone2, 0.55) * line * mix(0.35, 0.82, major)
             * (0.22 + 0.60 * dif + 0.18 * peak) * (0.05 + lineBand * 0.46 + PERC.y * 0.35);
        col += tone2 * peak * (0.035 + SH * 0.35);
    } else {
        float s = 0.75 + GA.x * 0.7;
        vec3 q2 = p * s * 2.1 + vec3(vnoise(p.xy * 0.3) * 4.0, 0.0, uFlow * 0.4);
        float g2 = dot(sin(q2.yzx), cos(q2.zxy));
        float vein = exp(-abs(g2) * 5.0);
        float climb = exp(-abs(fract(-p.z * 0.22 + SNAP * 0.5 + GG.w) - 0.5) * 12.0);
        float side = step(0.0, MID);
        vec3 tone = pal(0.15 + side * 0.3 + hs);
        vec3 tone2 = pal(0.6 + side * 0.2 + hs);
        vec3 stone = mix(vec3(0.06, 0.12, 0.25), tone, 0.55);
        col = stone * (0.12 + dif * 0.38) + tone2 * fres * 0.16 + stone * spc * 0.18;
        col += tone2 * vein * (0.36 + 0.38 * dif + 0.26 * fres)
             * (0.045 + SH * 0.30 + PERC.z * 0.48 + climb * (0.13 + HARM.x * 0.60));
    }
    col += pal(0.5 + hs) * waveField(p.xy) * 0.5;
    return col;
}

// ================================================================ 4: STORM NEBULA (volumetric)
// Drifting cloud banks. Depth maps to frequency (shallow = treble, deep = bass), so each layer
// glows with its band; the bass thickens the cloud and snares fire lightning inside it.
vec3 renderNebula(vec3 ro, vec3 rd, vec3 fogC) {
    float t0 = (-0.8 - ro.z) / rd.z;
    float t1 = (-9.0 - ro.z) / rd.z;
    const int STEPS = 30;
    float dt = (t1 - t0) / float(STEPS);
    float t = t0 + dt * hash21(gl_FragCoord.xy + fract(uTime) * 61.0);
    float sc = 0.30 + GA.x * 0.18;
    vec3 acc = vec3(0.0);
    float trans = 1.0;
    // lightning: a random spot near the view per beat, lit on the snare
    vec2 boltC = floor(ro.xy / 7.0) * 7.0 + hash22(vec2(BEAT_I, 3.7)) * 7.0;
    for (int i = 0; i < STEPS; i++) {
        vec3 p = ro + rd * t;
        vec3 q = p * sc + vec3(GA.zw * 20.0, uFlow * 0.12);
        float n = vnoise3(q) * 0.65 + vnoise3(q * 2.3 + 3.1) * 0.35;
        float th = 0.60 - HARM.x * 0.10 - SB * 0.05 - PERC.x * 0.05;
        float dens = smoothstep(th, th + 0.12, n);
        if (dens > 0.001) {
            float band = clamp((-p.z - 0.8) / 8.2, 0.0, 1.0);
            float e = spec(1.0 - band);
            // bright rims where the cloud thins, dark cores: reads as sculpted cloud, not blur
            float rim = 1.0 - smoothstep(th + 0.02, th + 0.16, n);
            vec3 c = mix(fogC, pal(band * 0.6 + n * 0.3 + GB.w) * (0.09 + e * 0.37), 0.30 + rim * 0.70);
            float bd = length(p.xy - boltC);
            c += vec3(0.75, 0.85, 1.0) * max(uSnare, PERC.y) * exp(-bd * bd * 0.12) * 0.9;
            float a = 1.0 - exp(-dens * dt * 2.2);
            acc += trans * a * c;
            trans *= 1.0 - a;
            if (trans < 0.03) break;
        }
        t += dt;
    }
    return acc + fogC * trans;
}

// ================================================================ 5: AURORA ABYSS
// Curtains of light hanging at seven depths, drawn along drifting noise contours (no rows).
// Each depth carries one band of the spectrum: the song is laid out from treble (near) to bass (deep).
vec3 renderAurora(vec3 ro, vec3 rd, vec3 fogC) {
    vec3 acc = vec3(0.0);
    for (int k = 0; k < 7; k++) {
        float fk = float(k);
        float z = -0.9 - fk * (1.0 + GA.y * 0.5);
        float t = (z - ro.z) / rd.z;
        vec2 q = (ro + rd * t).xy;
        vec2 w = q * (0.09 + GA.x * 0.06) + vec2(fk * 7.3, GA.z * 20.0);
        float v = vnoise(w + vec2(uFlow * 0.03, 0.0)) + vnoise(w * 2.1 + 4.1) * 0.35;
        float s = fract(v * (2.5 + GB.x * 2.0) + fk * 0.37);
        // A broad curtain body grades into a narrow, softer backlit edge.
        float sheet = exp(-s * 9.0) * 0.72 + exp(-(1.0 - s) * 40.0) * 0.29;
        float x = fk / 6.0;
        float e = spec(x);
        float melody = x < 0.34 ? HARM.z : x < 0.67 ? HARM.y : HARM.x;
        vec3 c = pal(x * 0.7 + v * 0.2 + GB.w);
        float depthFade = exp(-fk * 0.19);
        acc += c * sheet * (0.02 + e * 0.35 + melody * 0.35 + PERC.x * 0.08) * depthFade;
        acc += c * waveField(q) * 0.06;
    }
    return fogC + acc;
}

// ================================================================ atmosphere
// Between the floor and the world, also fixed to the map (jittered, never in rows).
vec3 atmosphere(int kind, vec3 ro, vec3 rd, float tHit) {
    vec3 acc = vec3(0.0);
    if (kind == 1) {
        // MOTES: three depth planes of drifting sparks that twinkle with the highs
        for (int k = 0; k < 3; k++) {
            float z = -0.7 - float(k) * (0.9 + GA.w * 0.6);
            float t = (z - ro.z) / rd.z;
            if (t > tHit) break;
            vec2 q = (ro + rd * t).xy;
            float cell = 0.55 + float(k) * 0.25;
            vec2 id = floor(q / cell);
            vec2 j = hash22(id + float(k) * 31.0);
            vec2 c = (id + 0.1 + 0.8 * j) * cell;
            c += vec2(sin(uFlow * 0.7 + j.x * 6.28), cos(uFlow * 0.6 + j.y * 6.28)) * cell * 0.2 * MOT;
            float d = length(q - c) / cell;
            float gl = exp(-d * d * 260.0) * 1.2 + exp(-d * d * 30.0) * 0.08;
            acc += pal(j.y * 0.5 + 0.2 + GB.w) * gl * (0.25 + spec(0.6 + j.x * 0.4) * 1.2) * step(0.55, j.x);
        }
    } else if (kind == 2) {
        // FOG BANKS: drifting cloud layers lit by the kick and the bass
        for (int k = 0; k < 3; k++) {
            float z = -1.0 - float(k) * 1.1;
            float t = (z - ro.z) / rd.z;
            if (t > tHit) break;
            vec2 q = (ro + rd * t).xy * (0.18 + GA.w * 0.12) + vec2(uFlow * 0.05, -uFlow * 0.03) * (1.0 + float(k));
            float nz = vnoise(q) * 0.6 + vnoise(q * 2.3 + 7.1) * 0.4;
            acc += pal(0.3 + float(k) * 0.12 + GB.w) * smoothstep(0.45, 0.85, nz) * (0.03 + SB * 0.10 + uKick * 0.06 + uDrop * 0.12);
        }
    } else if (kind == 3) {
        // EMBERS: sparse sparks rising out of the depths on the beat
        for (int k = 0; k < 2; k++) {
            float z = -1.2 - float(k) * 1.6;
            float t = (z - ro.z) / rd.z;
            if (t > tHit) break;
            vec2 q = (ro + rd * t).xy;
            float cell = 1.4 + float(k) * 0.6;
            vec2 id = floor(q / cell);
            vec2 j = hash22(id + 9.0 + float(k) * 13.0);
            vec2 c = (id + 0.1 + 0.8 * j) * cell;
            float d = length(q - c) / cell;
            float on = step(0.7, hash21(id + vec2(BEAT_I * 0.61, float(k))));
            acc += pal(0.05 + j.x * 0.3 + GB.w) * exp(-d * d * 120.0) * on * (0.2 + HOP * 1.2) * (0.4 + SB);
        }
    }
    return acc;
}

vec3 renderWorld(int g, int atmo, vec4 a, vec4 b, vec3 po, vec3 pf, vec4 glob, vec3 ro, vec3 rd) {
    PO = po; PF = pf; GA = a; GB = b; GG = glob;
    vec3 fogC = vec3(0.018, 0.033, 0.067) + pal(0.7 + GB.w) * (0.022 + SB * 0.042 + uDrop * 0.05);
    float tMax = (-12.0 - ro.z) / rd.z;
    vec3 col;
    float t = tMax;
    if (g == 4) col = renderNebula(ro, rd, fogC);
    else if (g == 5) col = renderAurora(ro, rd, fogC);
    else {
        // start each march just above the highest point that world can reach
        float zTop = g == 0 ? -0.6 : g == 1 ? -0.8 : g == 2 ? -1.0 : -1.2;
        t = (zTop - ro.z) / rd.z;
        bool hit = false;
        for (int i = 0; i < 90; i++) {
            float d = mapScene(g, ro + rd * t);
            if (d < 0.004 * t) { hit = true; break; }
            t += d;
            if (t > tMax) break;
        }
        col = fogC;
        if (hit) {
            vec3 p = ro + rd * t;
            vec3 n = normalScene(g, p, t);
            mapScene(g, p);
            col = mix(fogC, shadeScene(g, p, n, rd), exp(-max(-p.z - 0.6, 0.0) * (0.10 + GG.x * 0.10)));
        } else {
            t = tMax;
        }
    }
    col += atmosphere(atmo, ro, rd, t);
    return col;
}

// ================================================================ genome uniforms
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

void main() {
    MOT = clamp(uMotionScale, 0.0, 1.5);
    BEAT_I = floor(uBeatPos);
    BEAT_F = fract(uBeatPos);
    SNAP = BEAT_I + smoothstep(0.0, 0.28, BEAT_F);
    float hopLocked = (1.0 - exp(-BEAT_F * 30.0)) * pow(1.0 - BEAT_F, 1.6) * 1.35;
    HOP = mix(clamp(uKick, 0.0, 1.2), hopLocked * (0.75 + min(uKick, 1.0) * 0.45), clamp(uBeatLock, 0.0, 1.0));
    TENSION = clamp(uAnticipation, 0.0, 1.0);
    PERC = clamp(uPerc, 0.0, 1.5);
    HARM = clamp(uHarm, 0.0, 1.5);
    CHORD = uChordShift + uHueStep;
    SB = (uSpec[0] + uSpec[1] + uSpec[2] + uSpec[3]) * 0.25;
    SM = (uSpec[5] + uSpec[6] + uSpec[7] + uSpec[8] + uSpec[9]) * 0.2;
    SH = (uSpec[11] + uSpec[12] + uSpec[13] + uSpec[14]) * 0.25;

    // Camera ray through the arena plane: z = 0 maps exactly onto the 2D world camera, so the
    // world below scrolls and parallaxes with the map. Scene units are game units / 100.
    vec2 ndc = vUv * 2.0 - 1.0;
    // build-ups fold the world into a turning kaleidoscope (the readability pools below still use
    // the real screen positions, so the fight stays legible)
    if (uKaleido > 0.01) {
        float aspectK = uResolution.x / max(uResolution.y, 1.0);
        vec2 kn = ndc * vec2(aspectK, 1.0);
        float seg = 6.28318 / 6.0;
        float ka = atan(kn.y, kn.x) + SNAP * 0.2618 * MOT;
        ka = abs(mod(ka + seg * 0.5, seg) - seg * 0.5);
        vec2 kf = vec2(cos(ka), sin(ka)) * length(kn) / vec2(aspectK, 1.0);
        ndc = mix(ndc, kf, clamp(uKaleido, 0.0, 1.0));
    }
    // EDM camera: the eye lifts away through a build (the world flattens and recedes), slams down
    // into the world on the drop and eases back, and dips on every kick
    float eyeH = max(uView.y, 1.0) * 0.02
               * (1.0 + clamp(uVertigo, 0.0, 1.0) * 0.6)
               * (1.0 - clamp(uDive, 0.0, 0.6))
               * (1.0 - min(uKick, 1.0) * uCamKick * MOT);
    vec3 ro = vec3(uCam * 0.01 + vec2(0.0, uOblique * eyeH), eyeH);
    vec3 onFloor = vec3((uCam + vec2(ndc.x, -ndc.y) * uView) * 0.01, 0.0);
    vec3 rd = normalize(onFloor - ro);

    vec3 col = renderWorld(uLA[0].gen, uAtmoA, uLA[0].ga, uLA[0].gb, uLA[0].po, uLA[0].pf, uGlobA, ro, rd);
    float aspect = uResolution.x / max(uResolution.y, 1.0);
    vec2 screenPos = (vUv - 0.5) * vec2(aspect, 1.0);
    float r = length(screenPos);
    if (uMix > 0.001) {
        vec3 next = renderWorld(uLB[0].gen, uAtmoB, uLB[0].ga, uLB[0].gb, uLB[0].po, uLB[0].pf, uGlobB, ro, rd);
        float front = uMix * 1.6;
        float wipe = smoothstep(front - 0.25, front, r * 0.9);
        col = mix(next, col, wipe) + pal(0.5) * exp(-abs(r * 0.9 - front + 0.12) * 18.0) * (1.0 - uMix) * 0.4;
    }

    // music punch: exposure follows the loudness and pumps on hits
    col *= 0.72 + uEnergy * 0.35 + (HARM.x + HARM.y) * 0.08 + max(min(uKick, 1.5), PERC.x) * 0.18 + PERC.y * 0.08
         + min(uDrop, 2.0) * 0.28 + uOverdrive * 0.2;
    // bright, airy music sharpens the highlights; dark, bassy music softens them
    col *= 0.9 + clamp(uBright, 0.0, 1.0) * 0.2;
    // drops pump with the beat clock: a hit of light on every beat that drains before the next
    col *= mix(1.0, 0.8 + 0.45 * exp(-BEAT_F * 4.0), clamp(uPump, 0.0, 1.0));
    // pre-drop: colour drains and the world darkens as tension builds, so the drop hits harder
    float lum = dot(col, vec3(0.299, 0.587, 0.114));
    col = mix(col, vec3(lum), 0.18 + TENSION * 0.45) * (1.0 - TENSION * 0.35);
    col *= 1.0 - 0.32 * smoothstep(0.65, 1.15, r);
    // readability: dark pools under the ship and every enemy
    float focus = 1.0;
    for (int i = 0; i < 16; i++) {
        if (i >= uFocusCount) break;
        vec4 f = uFocus[i];
        float d = length(screenPos - f.xy) / max(f.z, 0.01);
        focus *= 1.0 - f.w * exp(-d * d * 1.6);
    }
    col *= mix(0.22, 1.0, focus);
    // Use a shared shoulder so bright terrain keeps its palette colour.
    float peak = max(col.r, max(col.g, col.b));
    col *= 0.95 / (1.0 + peak * 1.35);
    // the drop's first bar pulses on the beat (tinted, and kept dim under the ship and enemies)
    col += (pal(0.15) * 0.6 + 0.25) * clamp(uStrobe, 0.0, 0.5) * mix(0.12, 1.0, focus);
    col += 0.003 * hash21(vUv * uResolution + uTime);

    if (uDarkTheme > 0.001) {
        vec3 localLight = vec3(0.0);
        for (int i = 0; i < 12; i++) {
            if (i >= uLightCount) break;
            vec4 lt = uLights[i];
            float d = length(screenPos - lt.xy) / max(lt.z, 0.02);
            localLight += uLightCols[i] * exp(-d * d * 2.2) * lt.w;
        }
        float pulseGate = clamp(0.45 + uKick * 0.36 + uSnare * 0.30 + uHat * 0.16 + uDrop * 0.42 + uOverdrive * 0.34, 0.0, 1.3);
        vec3 abyssal = col * (0.34 + pulseGate * (0.28 + peak * 0.12))
                    + localLight * (col * 1.20 + 0.010);
        col = mix(col, abyssal, uDarkTheme);
    }
    vec3 ambient = mix(vec3(0.021, 0.034, 0.067), vec3(0.014, 0.025, 0.052), uDarkTheme);
    col += ambient * mix(0.42, 1.0, focus);
    fragColor = vec4(col * min(uIntensity, 1.1), 1.0);
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
// EDM camera (Renderer.EdmFxFor): a roll that wobbles in on drops, letterbox bars through builds
uniform float uRoll;
uniform float uLetterbox;

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
    // camera roll: turn the frame about its centre, scaled up just enough that no edge shows
    // (Renderer.UnrollScreen maps the mouse the same way, so aiming stays true)
    if (abs(uRoll) > 0.0001) {
        float aspectR = uResolution.x / max(uResolution.y, 1.0);
        vec2 c = (uv - 0.5) * vec2(aspectR, 1.0);
        float cr = cos(uRoll), sr = sin(uRoll);
        c = mat2(cr, -sr, sr, cr) * c / (cos(abs(uRoll)) + sin(abs(uRoll)) * max(aspectR, 1.0 / aspectR));
        uv = c / vec2(aspectR, 1.0) + 0.5;
    }
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
    // Fade the contrast adjustment toward black and white to retain coloured shadows.
    col = clamp(col, 0.0, 1.0);
    float contrast = uContrast * mix(1.0, 1.05, uDarkTheme) - 1.0;
    col += (col - 0.5) * contrast * (4.0 * col * (1.0 - col));
    col *= uTint;
    if (uDamage == 1) {
        col = mix(col, vec3(1.0, 0.10, 0.18), 0.16);
    }
    col = mix(col, vec3(1.0), clamp(uFlash, 0.0, 0.45));
    float vig = 1.0 - smoothstep(0.35, 1.12, length(dir * vec2(1.0, 1.1)));
    float vigFloor = mix(0.36, 0.30, uDarkTheme);
    float effVig = mix(uVignette, max(uVignette, 0.85), uDarkTheme);
    col *= mix(1.0, vigFloor + (1.0 - vigFloor) * vig, effVig);
    float grain = hash21(uv * uResolution + fract(uTime) * 113.0) - 0.5;
    col += grain * mix(0.018, 0.012, uDarkTheme);
    col *= 0.985 + 0.015 * sin(uv.y * uResolution.y * 3.14159);
    // letterbox bars close in through a build-up and snap open on the drop; they are translucent,
    // so anything under them still shows
    if (uLetterbox > 0.0005) {
        float edge = min(vUv.y, 1.0 - vUv.y);
        col *= edge < uLetterbox ? 0.32 : 1.0;
        col += vec3(0.45, 0.9, 1.0) * exp(-abs(edge - uLetterbox) * 700.0) * 0.35;
    }
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
layout(location = 4) in vec2 aUV;
out vec2 vUV;
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
    vUV = aUV;
    vWorld = aPos;
}
""";

    public const string MeshFrag = """
#version 410 core
in vec3 vNormal;
in vec4 vColor;
in float vGlow;
in vec2 vUV;
uniform sampler2D uAlbedoMap, uNormalMap, uRoughnessMap, uEmissiveMap;
uniform float uEmissiveIntensity;
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
    bool mapped = vUV.x >= 0.0;
    vec3 n = normalize(vNormal);
    float roughness = 0.5;
    vec3 emission = vec3(0);
    vec3 base = vColor.rgb;
    if (mapped) {
        vec3 dx = dFdx(vWorld), dy = dFdy(vWorld);
        vec2 ux = dFdx(vUV), uy = dFdy(vUV);
        float det = ux.x * uy.y - ux.y * uy.x;
        if (abs(det) > 1e-12) {
            vec3 tangent = (dx * uy.y - dy * ux.y) / det;
            tangent = normalize(tangent - n * dot(n, tangent));
            // Assimp flips V for top-down image rows; normal maps retain +Y in OBJ space.
            vec3 bitangent = normalize(cross(n, tangent));
            vec3 dv = (dy * ux.x - dx * uy.x) / det;
            bitangent *= -sign(dot(bitangent, dv));
            vec3 detail = texture(uNormalMap, vUV).xyz * 2.0 - 1.0;
            n = normalize(mat3(tangent, bitangent, n) * detail);
        }
        base = mix(texture(uAlbedoMap, vUV).rgb, vec3(1), clamp(vGlow, 0.0, 1.0));
        roughness = texture(uRoughnessMap, vUV).r;
        emission = texture(uEmissiveMap, vUV).rgb * uEmissiveIntensity;
    }
    vec3 V = uPerspective == 1 ? normalize(uEye - vWorld) : normalize(uViewDir);
    vec3 L = normalize(uLightDir);
    float diff = max(dot(n, L), 0.0);
    float back = max(dot(n, -L), 0.0);
    vec3 hemi = mix(vec3(0.13, 0.17, 0.26), vec3(0.28, 0.36, 0.46), n.z * 0.5 + 0.5);
    vec3 h = normalize(L + V);
    float spec = pow(max(dot(n, h), 0.0), (mapped ? mix(84.0, 5.0, roughness) : 36.0));
    float fres = pow(1.0 - clamp(dot(n, V), 0.0, 1.0), 3.0);
    // Cool ambient and a directional key shape the hull. Reserve the bright response for emitters.
    vec3 key = vec3(0.80, 0.88, 0.94) * diff * mix(0.78, 0.56, uDarkTheme);
    vec3 fill = uFill * back * mix(0.40, 0.30, uDarkTheme);
    vec3 col = base * (hemi * mix(1.0, 0.72, uDarkTheme) + key + fill);
    vec3 specColor = mix(vec3(0.40, 0.57, 0.78), base, 0.40);
    // Atlas-mapped surfaces scale the highlight by their roughness map.
    float specScale = mapped ? mix(1.55, 0.15, roughness) : 1.0;
    col += specColor * spec * specScale * (0.06 + 0.20 * diff);
    col += uRimColor * fres * uRim * mix(0.73, 1.38, uDarkTheme);
    col *= uBrightness;
    float glowGain = uGlowScale * mix(1.0, 1.14, uDarkTheme);
    float glow = vGlow * glowGain;
    col += base * (glow / (1.0 + 0.16 * glow)) + emission * glowGain;
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
