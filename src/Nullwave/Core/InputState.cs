using System.Numerics;
using Nullwave.World;
using Silk.NET.Input;
#if DEBUG
using System.Collections.Concurrent;
#endif

namespace Nullwave.Core;

internal struct PlayerActionBuffer
{
    private bool _dash;
    private bool _burst;

    public void Sample(InputFrame frame)
    {
        _dash |= frame.Dash;
        _burst |= frame.Burst;
    }

    public InputFrame ForTick(InputFrame frame, bool updatesPlayer)
    {
        var input = new InputFrame(frame.Move, frame.Aim, frame.Fire,
            updatesPlayer && _dash, frame.Focus, updatesPlayer && _burst);
        if (updatesPlayer) this = default;
        return input;
    }
}

public sealed class InputState
{
    private readonly IInputContext _input;
    private IKeyboard? _keyboard;
    private IMouse? _mouse;
    private IGamepad? _gamepad;
    private readonly HashSet<Key> _down = new();
    private readonly HashSet<Key> _prev = new();
    private bool _mouseLeft, _prevMouseLeft;
    private bool _padA, _prevPadA;
    private bool _padB, _prevPadB;
    private bool _padX, _prevPadX;
    private bool _padStart, _prevPadStart;
    private bool _padUp, _prevPadUp;
    private bool _padDown, _prevPadDown;
    private bool _padLeft, _prevPadLeft;
    private bool _padRight, _prevPadRight;
    private bool _padLb, _prevPadLb;

    private static readonly Key[] PollKeys =
    [
        Key.W, Key.A, Key.S, Key.D, Key.Space, Key.ShiftLeft, Key.ShiftRight,
        Key.E, Key.P, Key.F1, Key.F2, Key.F3, Key.Tab, Key.Escape, Key.Enter, Key.Backspace,
        Key.Up, Key.Down, Key.Left, Key.Right,
        Key.Number0, Key.Number1, Key.Number2, Key.Number3, Key.Number4,
        Key.Number5, Key.Number6, Key.Number7, Key.Number8, Key.Number9,
        Key.Keypad0, Key.Keypad1, Key.Keypad2, Key.Keypad3, Key.Keypad4,
        Key.Keypad5, Key.Keypad6, Key.Keypad7, Key.Keypad8, Key.Keypad9,
        Key.B, Key.C, Key.F, Key.G, Key.H, Key.I, Key.J, Key.K, Key.L, Key.M,
        Key.N, Key.O, Key.Q, Key.R, Key.T, Key.U, Key.V, Key.X, Key.Y, Key.Z,
    ];

    private static readonly (Key Key, char Char)[] SeedCharKeys =
    [
        (Key.Number0, '0'), (Key.Number1, '1'), (Key.Number2, '2'), (Key.Number3, '3'), (Key.Number4, '4'),
        (Key.Number5, '5'), (Key.Number6, '6'), (Key.Number7, '7'), (Key.Number8, '8'), (Key.Number9, '9'),
        (Key.Keypad0, '0'), (Key.Keypad1, '1'), (Key.Keypad2, '2'), (Key.Keypad3, '3'), (Key.Keypad4, '4'),
        (Key.Keypad5, '5'), (Key.Keypad6, '6'), (Key.Keypad7, '7'), (Key.Keypad8, '8'), (Key.Keypad9, '9'),
        (Key.A, 'A'), (Key.B, 'B'), (Key.C, 'C'), (Key.D, 'D'), (Key.E, 'E'), (Key.F, 'F'),
        (Key.G, 'G'), (Key.H, 'H'), (Key.I, 'I'), (Key.J, 'J'), (Key.K, 'K'), (Key.L, 'L'),
        (Key.M, 'M'), (Key.N, 'N'), (Key.O, 'O'), (Key.P, 'P'), (Key.Q, 'Q'), (Key.R, 'R'),
        (Key.S, 'S'), (Key.T, 'T'), (Key.U, 'U'), (Key.V, 'V'), (Key.W, 'W'), (Key.X, 'X'),
        (Key.Y, 'Y'), (Key.Z, 'Z'),
    ];

    public InputState(IInputContext input)
    {
        _input = input;
        if (_input.Keyboards.Count > 0) _keyboard = _input.Keyboards[0];
        if (_input.Mice.Count > 0)
        {
            _mouse = _input.Mice[0];
            _mouse.Cursor.CursorMode = CursorMode.Hidden;
        }
        if (_input.Gamepads.Count > 0) _gamepad = _input.Gamepads[0];
    }

    public Vector2 MousePosition { get; private set; }
    public bool GamepadPresent => _gamepad != null;
    public Vector2 Move { get; private set; }
    public Vector2 AimStick { get; private set; }
    public bool Fire { get; private set; }
    public bool Focus { get; private set; }
    public bool DashPressed { get; private set; }
    public bool BurstPressed { get; private set; }
    public bool PausePressed { get; private set; }
    public bool ConfirmPressed { get; private set; }
    public bool BackPressed { get; private set; }
    public bool UpPressed { get; private set; }
    public bool DownPressed { get; private set; }
    public bool LeftPressed { get; private set; }
    public bool RightPressed { get; private set; }
    public bool SpacePressed { get; private set; }
    public bool TabPressed { get; private set; }
    public bool DebugPressed { get; private set; }
    public bool FullscreenPressed { get; private set; }
    public bool AudioDevicePressed { get; private set; }
    public bool MouseLeftPressed { get; private set; }
    public bool MouseLeftDown { get; private set; }
    public bool RPressed { get; private set; }
    public bool NPressed { get; private set; }
    public bool CPressed { get; private set; }
    public bool LPressed { get; private set; }
    public bool BackspacePressed { get; private set; }
    public bool EPressed { get; private set; }
    public bool MPressed { get; private set; }
    public bool OnePressed { get; private set; }
    public bool TwoPressed { get; private set; }
    public bool ThreePressed { get; private set; }

    public bool WasPressed(Key key) => _down.Contains(key) && !_prev.Contains(key);

#if DEBUG
    /// <summary>Debug bridge input: keys held for N frames, a mouse position and left-button clicks.</summary>
    public sealed record VirtualCommand(Key[] Keys, int Frames, float? MouseX, float? MouseY, bool Click, bool Release);
    public static readonly ConcurrentQueue<VirtualCommand> Virtual = new();
    private readonly List<(Key Key, int Frames)> _virtualKeys = new();
    private int _virtualClick;
    private Vector2? _virtualMouse;

    /// <summary>Merges queued virtual input into this frame (works while the window is unfocused).</summary>
    private void ApplyVirtual()
    {
        while (Virtual.TryDequeue(out var c))
        {
            if (c.Release) { _virtualKeys.Clear(); _virtualClick = 0; _virtualMouse = null; }
            foreach (var k in c.Keys) _virtualKeys.Add((k, c.Frames));
            if (c.MouseX != null || c.MouseY != null)
                _virtualMouse = new Vector2(c.MouseX ?? _virtualMouse?.X ?? MousePosition.X, c.MouseY ?? _virtualMouse?.Y ?? MousePosition.Y);
            if (c.Click) _virtualClick = Math.Max(_virtualClick, c.Frames);
        }
        for (int i = _virtualKeys.Count - 1; i >= 0; i--)
        {
            var (key, frames) = _virtualKeys[i];
            _down.Add(key);
            if (frames <= 1) _virtualKeys.RemoveAt(i);
            else _virtualKeys[i] = (key, frames - 1);
        }
        if (_virtualMouse != null) MousePosition = _virtualMouse.Value;
        if (_virtualClick > 0) { _mouseLeft = true; _virtualClick--; }
    }
#endif

    /// <summary>Writes newly pressed alphanumeric seed characters (normalized via <see cref="SeedCode.NormalizeChar"/>) into <paramref name="destination"/>.</summary>
    public int ReadTypedSeedChars(Span<char> destination)
    {
        int count = 0;
        for (int i = 0; i < SeedCharKeys.Length && count < destination.Length; i++)
        {
            var (key, ch) = SeedCharKeys[i];
            if (!WasPressed(key)) continue;
            char norm = SeedCode.NormalizeChar(ch);
            if (norm != '\0') destination[count++] = norm;
        }
        return count;
    }

    public void BeginFrame()
    {
        _prev.Clear();
        foreach (var k in _down) _prev.Add(k);
        _down.Clear();
        _prevMouseLeft = _mouseLeft;
        _prevPadA = _padA;
        _prevPadB = _padB;
        _prevPadX = _padX;
        _prevPadStart = _padStart;
        _prevPadUp = _padUp;
        _prevPadDown = _padDown;
        _prevPadLeft = _padLeft;
        _prevPadRight = _padRight;
        _prevPadLb = _padLb;

        if (_keyboard != null)
        {
            foreach (var key in PollKeys)
                if (_keyboard.IsKeyPressed(key)) _down.Add(key);
        }

        if (_mouse != null)
        {
            MousePosition = _mouse.Position;
            _mouseLeft = _mouse.IsButtonPressed(MouseButton.Left);
        }
#if DEBUG
        ApplyVirtual();
#endif

        if (_input.Gamepads.Count > 0)
        {
            _gamepad = _input.Gamepads[0];
            _padA = PadButton(ButtonName.A);
            _padB = PadButton(ButtonName.B);
            _padX = PadButton(ButtonName.X);
            _padStart = PadButton(ButtonName.Start);
            _padUp = PadButton(ButtonName.DPadUp);
            _padDown = PadButton(ButtonName.DPadDown);
            _padLeft = PadButton(ButtonName.DPadLeft);
            _padRight = PadButton(ButtonName.DPadRight);
            _padLb = PadButton(ButtonName.LeftBumper);
        }
        else
        {
            _gamepad = null;
            _padA = _padB = _padX = _padStart = _padUp = _padDown = _padLeft = _padRight = _padLb = false;
        }

        Vector2 keyboardMove = Vector2.Zero;
        if (IsDown(Key.W)) keyboardMove.Y -= 1;
        if (IsDown(Key.S)) keyboardMove.Y += 1;
        if (IsDown(Key.A)) keyboardMove.X -= 1;
        if (IsDown(Key.D)) keyboardMove.X += 1;
        if (keyboardMove.LengthSquared() > 0) keyboardMove = Vector2.Normalize(keyboardMove);

        Vector2 stick = Vector2.Zero;
        Vector2 aimStick = Vector2.Zero;
        if (_gamepad != null)
        {
            if (_gamepad.Thumbsticks.Count > 0)
            {
                var left = _gamepad.Thumbsticks[0];
                stick = new Vector2(left.X, left.Y);
                if (stick.Length() < 0.22f) stick = Vector2.Zero;
                else stick = Vector2.Normalize(stick) * MathF.Min(1f, stick.Length());
            }
            if (_gamepad.Thumbsticks.Count > 1)
            {
                var right = _gamepad.Thumbsticks[1];
                aimStick = new Vector2(right.X, right.Y);
                if (aimStick.Length() < 0.25f) aimStick = Vector2.Zero;
            }
        }

        Move = keyboardMove.LengthSquared() > 0 ? keyboardMove : stick;
        AimStick = aimStick;

        bool triggerFire = false;
        if (_gamepad != null && _gamepad.Triggers.Count > 1)
            triggerFire = _gamepad.Triggers[1].Position > 0.18f;
        Fire = _mouseLeft || triggerFire;
        Focus = IsDown(Key.ShiftLeft) || IsDown(Key.ShiftRight) || _padLb;
        DashPressed = WasPressed(Key.Space) || (_padA && !_prevPadA);
        BurstPressed = WasPressed(Key.E) || (_padX && !_prevPadX);
        PausePressed = WasPressed(Key.P) || (_padStart && !_prevPadStart);
        ConfirmPressed = WasPressed(Key.Enter) || (_padA && !_prevPadA);
        BackPressed = WasPressed(Key.Escape) || (_padB && !_prevPadB);
        UpPressed = WasPressed(Key.W) || WasPressed(Key.Up) || (_padUp && !_prevPadUp);
        DownPressed = WasPressed(Key.S) || WasPressed(Key.Down) || (_padDown && !_prevPadDown);
        LeftPressed = WasPressed(Key.A) || WasPressed(Key.Left) || (_padLeft && !_prevPadLeft);
        RightPressed = WasPressed(Key.D) || WasPressed(Key.Right) || (_padRight && !_prevPadRight);
        SpacePressed = WasPressed(Key.Space);
        TabPressed = WasPressed(Key.Tab);
        DebugPressed = WasPressed(Key.F1);
        FullscreenPressed = WasPressed(Key.F2);
        AudioDevicePressed = WasPressed(Key.F3);
        RPressed = WasPressed(Key.R);
        NPressed = WasPressed(Key.N);
        CPressed = WasPressed(Key.C);
        LPressed = WasPressed(Key.L);
        BackspacePressed = WasPressed(Key.Backspace);
        EPressed = WasPressed(Key.E);
        MPressed = WasPressed(Key.M);
        OnePressed = WasPressed(Key.Number1);
        TwoPressed = WasPressed(Key.Number2);
        ThreePressed = WasPressed(Key.Number3);
        MouseLeftDown = _mouseLeft;
        MouseLeftPressed = _mouseLeft && !_prevMouseLeft;
#if DEBUG
        DebugLast = $"fire={Fire} move=({Move.X:0.##},{Move.Y:0.##}) mouse=({MousePosition.X:0},{MousePosition.Y:0}) down=[{string.Join(",", _down)}] vKeys={_virtualKeys.Count} vClick={_virtualClick}";
#endif
    }
#if DEBUG
    /// <summary>What the game saw from the input layer last frame (debug bridge state).</summary>
    public static volatile string DebugLast = "";
#endif

    private bool IsDown(Key key) => _down.Contains(key);

    private float _rumbleLow = -1f, _rumbleHigh = -1f;

    /// <summary>Drives the gamepad's two motors (0..1): low = heavy motor, high = light motor.</summary>
    public void SetRumble(float low, float high)
    {
        if (_gamepad == null) return;
        low = Math.Clamp(low, 0f, 1f);
        high = Math.Clamp(high, 0f, 1f);
        if (MathF.Abs(low - _rumbleLow) < 0.02f && MathF.Abs(high - _rumbleHigh) < 0.02f) return;
        _rumbleLow = low;
        _rumbleHigh = high;
        try
        {
            var motors = _gamepad.VibrationMotors;
            if (motors.Count > 0) motors[0].Speed = low;
            if (motors.Count > 1) motors[1].Speed = high;
        }
        catch
        {
            // pads without motors, or a disconnect mid-frame
        }
    }

    private bool PadButton(ButtonName name)
    {
        if (_gamepad == null) return false;
        foreach (var button in _gamepad.Buttons)
            if (button.Name == name && button.Pressed) return true;
        return false;
    }
}
