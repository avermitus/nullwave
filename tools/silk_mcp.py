"""MCP server for the NULLWAVE debug bridge (Debug builds only).

Exposes the game's local playtest bridge on http://localhost:5055/ to Claude Code:
  get_state           JSON snapshot: screen, player, world, audio, fps, recent exceptions
  capture_screenshot  PNG of the current frame
  send_input          hold keys / move the mouse / click for N frames (works unfocused)

Run the game as a Debug build first (NULLWAVE_PLAYTEST=1 keeps it off your Spotify routing and
real save), e.g.:  set NULLWAVE_PLAYTEST=1 && .dotnet\\dotnet.exe run --project src\\Nullwave
Register:  claude mcp add silknet-game -- python tools/silk_mcp.py
"""

import json

import httpx

try:  # mcp 1.x
    from mcp.server.fastmcp import FastMCP, Image
except ModuleNotFoundError:  # mcp 2.x renamed FastMCP to MCPServer (same tool/Image/run API)
    from mcp.server.mcpserver import Image
    from mcp.server.mcpserver import MCPServer as FastMCP

BASE = "http://localhost:5055"
mcp = FastMCP("silknet-game")


def _get(path: str, timeout: float = 5.0) -> httpx.Response:
    try:
        return httpx.get(BASE + path, timeout=timeout)
    except httpx.HTTPError as exc:
        raise RuntimeError(
            f"NULLWAVE debug bridge not reachable at {BASE} ({exc}). "
            "Start a Debug build of the game first."
        ) from exc


@mcp.tool()
def get_state() -> str:
    """Current game state as JSON: screen (Title/Playing/Paused/...), player position, velocity and
    health, wave/score/enemy and bullet counts, camera, audio analysis, FPS and recent exceptions."""
    r = _get("/state")
    r.raise_for_status()
    return r.text


@mcp.tool()
def capture_screenshot() -> Image:
    """PNG screenshot of the game's backbuffer, read on the render thread before SwapBuffers."""
    r = _get("/screenshot", timeout=10.0)
    r.raise_for_status()
    return Image(data=r.content, format="png")


@mcp.tool()
def send_input(
    key: str | None = None,
    keys: list[str] | None = None,
    frames: int = 1,
    mouse_x: float | None = None,
    mouse_y: float | None = None,
    click: bool = False,
    release: bool = False,
) -> str:
    """Inject input into the game for `frames` rendered frames (the game runs at up to 120 fps).

    key / keys: Silk.NET key names, e.g. "W", "A", "S", "D" (move), "Space" (dash), "E" (burst),
        "Enter" (confirm), "Escape" (back), "P" (pause), "Up"/"Down" (menus), "Shift" (focus).
    mouse_x / mouse_y: window pixel coordinates (1600x900 window) for aiming / menus; persists
        until `release`.
    click: hold the left mouse button (fire) for `frames` frames.
    release: clear all held virtual keys, clicks and the virtual mouse position first.
    """
    body: dict = {"frames": frames}
    if key:
        body["key"] = key
    if keys:
        body["keys"] = keys
    if mouse_x is not None:
        body["mouseX"] = mouse_x
    if mouse_y is not None:
        body["mouseY"] = mouse_y
    if click:
        body["click"] = "left"
    if release:
        body["release"] = True
    try:
        r = httpx.post(BASE + "/input", content=json.dumps(body), timeout=5.0)
    except httpx.HTTPError as exc:
        raise RuntimeError(f"NULLWAVE debug bridge not reachable at {BASE} ({exc})") from exc
    return r.text


if __name__ == "__main__":
    mcp.run()
