"""Turn the fal (ElevenLabs Sound Effects v3) mp3s in sfx/ into the game's sample layers.

For each effect: decode to 48 kHz mono, cut the leading silence, trim to the voice's in-game life
with a smooth fade, match the peak of the procedural voice it replaces (measured with
`Nullwave --sfxdump`), and, for pitched voices, measure the root so the game can retune the sample
to the song's key. Writes Assets/Audio/<effect>.wav and updates Assets/Audio/manifest.json.
Usage: python prepare_sfx.py <sfxdump_dir>   (requires ffmpeg and numpy)
"""
import hashlib, json, os, subprocess, sys, wave
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
AUDIO = os.path.normpath(os.path.join(HERE, "..", "..", "Audio"))
RATE = 48000

# effect: (in-game life in seconds, SoundBank.LevelOf, pitched, prompt)
EFFECTS = {
    "shot": (0.12, 0.95, True, "one single tiny crisp liquid-chrome plink, a short glassy energy bolt shot with a faint metallic ring, very short and dry, game weapon sound"),
    "hit": (0.12, 0.82, False, "one small sharp impact of a glass pellet striking a marble statue, short ceramic chip tick, dry, single hit"),
    "graze": (0.14, 1.0, False, "a very quick airy silver shimmer, a bullet brushing past a feathered wing, soft glassy whisper, very short"),
    "pickup": (0.55, 0.78, False, "a bright quick rising three-note celestial chime on crystal bells, sparkling holy reward, short"),
    "laserwarn": (0.6, 0.86, False, "an ominous rising charge of a holy choir and electric hum gathering energy, building tension right before a light beam fires"),
    "laserfire": (0.6, 0.85, False, "a searing divine light beam blast, a bright burning ray of holy plasma with a short angelic shriek"),
    "burst": (1.6, 1.3, False, "a huge heavenly impact with a soaring angelic choir chord, cathedral reverb and shimmering liquid chrome debris"),
    "hurt": (0.5, 0.87, False, "a harsh painful glitchy impact, cracking stone and a dissonant low pipe organ stab, an alarming damage warning"),
    "boss": (1.8, 0.7, False, "a terrifying colossal angelic roar, many choir voices in dissonance with deep brass and grinding stone, an ancient being awakening"),
    "menu": (0.06, 0.6, False, "one tiny soft chrome click, a delicate glass UI select tick, extremely short"),
    "unlock": (1.0, 0.63, False, "a triumphant shimmering choir ah swell with golden bells, a short heavenly reward fanfare"),
    "wave": (0.7, 1.1, False, "a solemn deep cathedral bell toll with a chrome shimmer, a single strike announcing a new wave"),
    "note": (0.9, 1.0, True, "a single pure crystal glass bell note, one clear sustained tone ringing out, clean and steady pitch"),
    "drumkick": (0.25, 1.0, False, "one deep punchy electronic kick drum hit with a heavy stone thud, single dry hit"),
    "drumsnare": (0.18, 1.0, False, "one crisp electronic snare hit made of a cracking marble slab with a chrome rattle, single dry hit"),
    "drumhat": (0.07, 1.0, False, "one very short closed hi-hat tick like a tiny chrome bell shaker, single dry hit"),
    "bassshot": (0.42, 1.0, True, "one short deep growling distorted synth bass pluck with a metallic chrome edge, a single sustained low note, steady pitch"),
    "leadshot": (0.15, 1.0, True, "one short bright shimmering glass harp pluck with chrome sparkle, a single clear note, steady pitch"),
}
SAMPLE_WEIGHT = 1.1  # SoundBank.Render mixes the sample at this weight over the procedural voice
# Peak matching leaves sparse sounds quieter than the dense synth they replace. These boosts close the
# RMS gap measured with `Nullwave --sfxdump` (new vs old render); the sample peak is still capped below.
LOUDNESS_TRIM_DB = {"hit": 3.0, "pickup": 5.8, "hurt": 3.7, "boss": 4.2, "menu": 6.5, "unlock": 5.6,
                    "note": 7.3, "drumsnare": 5.1, "drumhat": 3.6, "leadshot": 3.7}


def decode(path):
    raw = subprocess.check_output(["ffmpeg", "-v", "error", "-i", path, "-ar", str(RATE), "-ac", "1", "-f", "f32le", "pipe:1"])
    return np.frombuffer(raw, np.float32).astype(np.float64)


def wav_peak(path):
    # SfxProbe writes IEEE float stereo, which the wave module cannot read
    raw = subprocess.check_output(["ffmpeg", "-v", "error", "-i", path, "-f", "f32le", "pipe:1"])
    return float(np.abs(np.frombuffer(raw, np.float32)).max())


def root_hz(x):
    """Fundamental of a pitched one-shot via a harmonic product spectrum (60 Hz - 2 kHz), with confidence."""
    seg = x[int(0.01 * RATE):int(0.01 * RATE) + 8192]
    if len(seg) < 2048:
        seg = np.pad(seg, (0, 2048 - len(seg)))
    n = 1 << 16
    spec = np.abs(np.fft.rfft(seg * np.hanning(len(seg)), n))
    hps = spec.copy()
    for h in (2, 3):
        dec = spec[::h]
        hps[:len(dec)] *= dec
    freqs = np.fft.rfftfreq(n, 1 / RATE)
    band = (freqs >= 60) & (freqs <= 2000)
    floor = np.median(spec[band]) + 1e-12
    i = np.argmax(np.where(band, hps, 0))
    top = np.argmax(np.where(band, spec, 0))
    rel = float(spec[i] / (spec[top] + 1e-12))
    if rel < 0.3:
        # inharmonic partials (bells): the strongest partial is the heard pitch
        i, rel = top, 1.0
    return float(freqs[i]), float(spec[i] / floor), rel


def main():
    ref_dir = sys.argv[1]
    manifest_path = os.path.join(AUDIO, "manifest.json")
    manifest = json.load(open(manifest_path, encoding="utf-8"))
    keep = [e for e in manifest["effects"] if e["effect"] not in EFFECTS]
    records = []
    for name, (life, level, pitched, prompt) in EFFECTS.items():
        x = decode(os.path.join(HERE, "sfx", name + ".mp3"))
        x -= x.mean()
        threshold = np.abs(x).max() * 0.03
        start = max(0, int(np.argmax(np.abs(x) > threshold)) - 48)
        x = x[start:start + int(life * RATE)]
        n = len(x)
        attack = np.minimum(1.0, np.arange(n) / 48.0)            # 1 ms click-free attack
        fade_len = max(int(n * 0.25), int(0.01 * RATE))
        release = np.ones(n)
        release[-fade_len:] = np.cos(np.linspace(0, np.pi / 2, fade_len)) ** 2
        x = x * attack * release
        ref = wav_peak(os.path.join(ref_dir, name + ".wav"))
        boost = 10 ** (LOUDNESS_TRIM_DB.get(name, 0.0) / 20)
        target = min(0.95, 0.9 * ref / (SAMPLE_WEIGHT * level) * boost)
        x *= target / max(np.abs(x).max(), 1e-6)
        pcm = np.clip(np.round(x * 32767), -32768, 32767).astype("<i2")
        out = os.path.join(AUDIO, name + ".wav")
        with wave.open(out, "wb") as w:
            w.setparams((1, 2, RATE, len(pcm), "NONE", "not compressed"))
            w.writeframes(pcm.tobytes())
        record = dict(effect=name, file=name + ".wav", prompt=prompt, durationSeconds=round(n / RATE, 3),
                      peak=round(float(np.abs(x).max()), 4), matchedProceduralPeak=round(ref, 4))
        if pitched:
            hz, conf, rel = root_hz(x)
            # only trust a clear tonal peak; otherwise the sample plays at its own pitch
            if conf > 40 and rel > 0.3:
                record["rootHz"] = round(hz, 2)
            print(f"  {name:10s} root {hz:7.1f} Hz conf {conf:6.1f} rel {rel:.2f} -> {'tuned' if 'rootHz' in record else 'untuned'}")
        record["sha256"] = hashlib.sha256(open(out, "rb").read()).hexdigest()
        records.append(record)
        print(f"{name:10s} {n / RATE:.3f}s peak {record['peak']:.3f} (procedural {ref:.3f})")
    manifest["usage"] = "Sample layers that lead every effect; the procedural voice stays underneath for punch"
    manifest["source"] = "Assets/Source/fal/sfx/*.mp3 (prepare_sfx.py: trimmed to voice life, faded, peak-matched)"
    manifest["effects"] = keep + records
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")
    print("wrote", len(records), "effects to", AUDIO)


if __name__ == "__main__":
    main()
