using System.Runtime.InteropServices;

namespace AuroraPad.Core.Mapping;

/// <summary>Thin wrapper over Win32 SendInput for synthetic keyboard and mouse events.</summary>
public static class InputSimulator
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const uint KeyEventFExtendedKey = 0x0001;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFScanCode = 0x0008;

    private const uint MouseEventFMove = 0x0001;
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const uint MouseEventFRightDown = 0x0008;
    private const uint MouseEventFRightUp = 0x0010;
    private const uint MouseEventFMiddleDown = 0x0020;
    private const uint MouseEventFMiddleUp = 0x0040;
    private const uint MouseEventFXDown = 0x0080;
    private const uint MouseEventFXUp = 0x0100;
    private const uint MouseEventFWheel = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeybdInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    // Keys whose scan codes need the E0 "extended" prefix.
    private static readonly HashSet<ushort> ExtendedKeys =
    [
        0x21, 0x22, 0x23, 0x24, // PageUp, PageDown, End, Home
        0x25, 0x26, 0x27, 0x28, // arrows
        0x2D, 0x2E,             // Insert, Delete
        0xA3, 0xA5,             // right Ctrl, right Alt
        0x5B, 0x5C,             // Windows keys
        0x6F, 0x90,             // numpad divide, NumLock
    ];

    private static void Send(Input input) => SendInput(1, [input], Marshal.SizeOf<Input>());

    public static void Key(ushort vk, bool down)
    {
        // Scan codes are used because many games read raw scan codes rather than virtual keys.
        ushort scan = (ushort)MapVirtualKey(vk, 0);
        uint flags = KeyEventFScanCode | (down ? 0 : KeyEventFKeyUp);
        if (ExtendedKeys.Contains(vk)) flags |= KeyEventFExtendedKey;
        Send(new Input { Type = InputKeyboard, U = new InputUnion { Keyboard = new KeybdInput { Vk = 0, Scan = scan, Flags = flags } } });
    }

    public static void MouseMove(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        Send(new Input { Type = InputMouse, U = new InputUnion { Mouse = new MouseInput { Dx = dx, Dy = dy, Flags = MouseEventFMove } } });
    }

    public static void MouseButton(MappingTarget button, bool down)
    {
        (uint flags, uint data) = button switch
        {
            MappingTarget.MouseLeft => (down ? MouseEventFLeftDown : MouseEventFLeftUp, 0u),
            MappingTarget.MouseRight => (down ? MouseEventFRightDown : MouseEventFRightUp, 0u),
            MappingTarget.MouseMiddle => (down ? MouseEventFMiddleDown : MouseEventFMiddleUp, 0u),
            MappingTarget.Mouse4 => (down ? MouseEventFXDown : MouseEventFXUp, 1u),
            MappingTarget.Mouse5 => (down ? MouseEventFXDown : MouseEventFXUp, 2u),
            _ => (0u, 0u),
        };
        if (flags == 0) return;
        Send(new Input { Type = InputMouse, U = new InputUnion { Mouse = new MouseInput { Flags = flags, MouseData = data } } });
    }

    public static void Wheel(int clicks)
    {
        Send(new Input { Type = InputMouse, U = new InputUnion { Mouse = new MouseInput { Flags = MouseEventFWheel, MouseData = unchecked((uint)(clicks * 120)) } } });
    }
}
