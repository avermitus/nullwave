using System.Numerics;
using Silk.NET.Input;

namespace Nullwave.Core;

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
        Key.E, Key.P, Key.F1, Key.F2, Key.F3, Key.Tab, Key.Escape, Key.Enter,
        Key.Up, Key.Down, Key.Left, Key.Right, Key.Number1, Key.Number2, Key.Number3,
        Key.R, Key.N, Key.Q, Key.M,
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
    public bool EPressed { get; private set; }
    public bool MPressed { get; private set; }
    public bool OnePressed { get; private set; }
    public bool TwoPressed { get; private set; }
    public bool ThreePressed { get; private set; }

    public bool WasPressed(Key key) => _down.Contains(key) && !_prev.Contains(key);

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
        EPressed = WasPressed(Key.E);
        MPressed = WasPressed(Key.M);
        OnePressed = WasPressed(Key.Number1);
        TwoPressed = WasPressed(Key.Number2);
        ThreePressed = WasPressed(Key.Number3);
        MouseLeftDown = _mouseLeft;
        MouseLeftPressed = _mouseLeft && !_prevMouseLeft;
    }

    private bool IsDown(Key key) => _down.Contains(key);

    private bool PadButton(ButtonName name)
    {
        if (_gamepad == null) return false;
        foreach (var button in _gamepad.Buttons)
            if (button.Name == name && button.Pressed) return true;
        return false;
    }
}
