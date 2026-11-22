"""Cut NULLWAVE's three Sogni texture layers from the recorded source phrase.
Usage: python tools/prepare_sogni_sfx.py <source.wav> [--output Assets/Audio]
Requires ffmpeg. The original is retained; existing derived outputs are replaced.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess
import wave

CUTS = {
    'kill': (4.41, 0.26, 'highpass=f=1100,lowpass=f=10000', False, 0.20),
    'explosion': (1.08, 0.95, 'highpass=f=45,lowpass=f=2200', False, 0.22),
    'dash': (8.55, 0.30, 'highpass=f=1600,lowpass=f=9000', True, 0.17),
}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('--output', type=Path, default=Path('Assets/Audio'))
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    records = []
    for name, (start, duration, filters, reverse, target_rms) in CUTS.items():
        raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-ss', str(start), '-i', str(args.source),
            '-t', str(duration), '-af', filters + (',areverse' if reverse else ''),
            '-ar', '48000', '-ac', '1', '-f', 'f32le', 'pipe:1'])
        data = list(struct.unpack('<' + 'f'*(len(raw)//4), raw))
        if not data or not all(map(math.isfinite, data)):
            raise ValueError('Empty or non-finite audio: ' + name)
        dc = sum(data)/len(data)
        # Fast click-free attack and a shaped decay turn the phrase fragment into a one-shot.
        for i, value in enumerate(data):
            attack = min(1.0, i / 96)
            release = min(1.0, (len(data)-1-i) / (48000 * .06))
            decay = (1-i/len(data)) ** (0.55 if name != 'dash' else 0.15)
            data[i] = (value-dc) * attack * release * decay
        rms = math.sqrt(sum(x*x for x in data)/len(data))
        peak = max(map(abs, data))
        if rms < 0.0001: raise ValueError('Silent slice: ' + name)
        gain = min(target_rms/rms, 0.75/max(peak, 0.0001))
        pcm = [round(x*gain*32767) for x in data]
        path = args.output / (name + '.wav')
        with wave.open(str(path), 'wb') as wav:
            wav.setparams((1, 2, 48000, len(pcm), 'NONE', 'not compressed'))
            wav.writeframes(struct.pack('<'+'h'*len(pcm), *pcm))
        record = dict(effect=name, file=path.name, sourceStartSeconds=start, durationSeconds=duration,
            filters=filters, reversed=reverse, attackMs=2, releaseMs=60, peak=round(peak*gain, 6),
            rms=round(rms*gain, 6), sha256=hashlib.sha256(path.read_bytes()).hexdigest())
        records.append(record)
        print(name, 'peak', record['peak'], 'rms', record['rms'], 'frames', len(pcm))
    manifest = dict(generator='Sogni AI', model='minimax_music3', seed=2940404251,
        projectId='E0E92CC6-646B-434F-9357-8DC832565BB4',
        promptFile='tools/Prompts/nullwave-sound-palette.txt',
        sourceSha256=hashlib.sha256(args.source.read_bytes()).hexdigest(),
        sourceDurationSeconds=19.992381, billing=dict(paidBy='plan', sparkCharged=0),
        format='48000 Hz mono PCM16 WAV', usage='Generated texture layers mixed with procedural effects',
        listeningReview='Owner listening feedback pending', effects=records)
    (args.output/'manifest.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')

if __name__ == '__main__': main()
