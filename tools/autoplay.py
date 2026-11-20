"""Bridge-driven autoplay for a NULLWAVE Debug build (see tools/silk_mcp.py).

Starts a run from the title, then plays: aims at the nearest enemy, holds fire, circle-strafes
away from threats, dashes now and then, and picks an attunement when offered. Logs a state line
every second and saves screenshots to capture/autoplay/. Usage: python tools/autoplay.py [seconds]
"""
import json
import math
import os
import sys
import time

import httpx

BASE = "http://localhost:5055"
OUT = os.path.join("capture", "autoplay")
os.makedirs(OUT, exist_ok=True)
seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 60.0


def state():
    return httpx.get(BASE + "/state", timeout=5).json()


def send(**body):
    httpx.post(BASE + "/input", content=json.dumps(body), timeout=5)


def shot(name):
    r = httpx.get(BASE + "/screenshot", timeout=10)
    with open(os.path.join(OUT, name), "wb") as f:
        f.write(r.content)


s = state()
print("start screen:", s["screen"])
if s["screen"] == "Title":
    send(release=True)
    send(key="Enter", frames=2)
    time.sleep(1.5)

t0 = time.time()
next_log = 0.0
next_shot = 0.0
angle = 0.0
events = []
last_health = None
last_screen = None
max_bullets = 0
min_fps = 999.0
while time.time() - t0 < seconds:
    s = state()
    el = time.time() - t0
    screen = s["screen"]
    w = s.get("world") or {}
    p = s.get("player") or {}
    win = s["window"]
    if screen != last_screen:
        events.append(f"{el:5.1f}s screen -> {screen}")
        shot(f"screen_{screen}_{int(el)}.png")
        last_screen = screen
    if screen == "Attuning":
        send(key="1", frames=2)
        time.sleep(0.4)
        continue
    if screen == "GameOver":
        events.append(f"{el:5.1f}s GAME OVER wave={w.get('wave')} score={w.get('score')}")
        runs = globals().get("runs", 0) + 1
        globals()["runs"] = runs
        if runs >= 3:
            break
        shot(f"gameover_{runs}.png")
        send(release=True)
        send(key="N", frames=2)   # straight into a new run
        last_health = None
        time.sleep(1.5)
        continue
    if screen != "Playing":
        time.sleep(0.3)
        continue
    if last_health is not None and p.get("health", 0) < last_health:
        events.append(f"{el:5.1f}s hit: health {last_health}->{p['health']}")
    last_health = p.get("health")
    if w.get("phase") != "Playing":
        pass
    fps = s.get("fps", 0)
    if el > 3:
        min_fps = min(min_fps, fps)
    max_bullets = max(max_bullets, w.get("enemyBullets", 0))
    # only enemies that are already in play and on screen are worth shooting at
    enemies = [e for e in (w.get("nearestEnemies") or [])
               if not e["spawning"] and 0 <= e["mouseX"] <= win["mouseWidth"] and 0 <= e["mouseY"] <= win["mouseHeight"]]
    threats = w.get("nearestEnemies") or []
    # aim at the nearest enemy (or straight ahead) and keep the trigger held
    if enemies:
        e = enemies[0]
        mx, my = e["mouseX"], e["mouseY"]
    else:
        mx, my = win["mouseWidth"] * 0.75, win["mouseHeight"] * 0.5
    # circle-strafe: move perpendicular to the nearest threat, drifting away when it is close
    keys = []
    if threats:
        dx, dy = threats[0]["dx"], threats[0]["dy"]
        d = math.hypot(dx, dy) or 1
        px, py = -dy / d, dx / d
        away = 0.8 if d < 280 else -0.2 if d > 650 else 0.0
        mvx, mvy = px - dx / d * away, py - dy / d * away
    else:
        angle += 0.15
        mvx, mvy = math.cos(angle), math.sin(angle)
    if mvx > 0.35: keys.append("D")
    if mvx < -0.35: keys.append("A")
    if mvy > 0.35: keys.append("S")
    if mvy < -0.35: keys.append("W")
    send(keys=keys or None, frames=14, mouseX=mx, mouseY=my, click=True)
    if threats and math.hypot(threats[0]["dx"], threats[0]["dy"]) < 160 and int(el * 10) % 7 == 0:
        send(key="Space", frames=2)
    if el >= next_log:
        next_log += 1.0
        print(f"{el:5.1f}s {screen:8} wave={w.get('wave')} phase={w.get('phase')} score={w.get('score')} hp={p.get('health')} "
              f"pos=({p.get('x')},{p.get('y')}) spd={p.get('speedFrac')} enemies={w.get('enemies')} eb={w.get('enemyBullets')} "
              f"wells={w.get('gravityWells')} rails={w.get('melodyRails')} fps={fps} exc={len(s.get('recentExceptions', []))}")
    if el >= next_shot:
        next_shot += 8.0
        shot(f"play_{int(el):03d}.png")
    time.sleep(0.1)

send(release=True)
s = state()
print("events:")
for e in events:
    print("  ", e)
print(f"minFps(after 3s)={min_fps} maxEnemyBullets={max_bullets}")
print("exceptions:", json.dumps(s.get("recentExceptions", []), indent=1))
