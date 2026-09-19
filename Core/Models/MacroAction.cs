using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MacroMaster.Core.Models;

public enum ActionType
{
    KeyDown,
    KeyUp,
    KeyTap,
    MouseDown,
    MouseUp,
    MouseClick,
    MouseScroll,
    MouseMoveRelative,
    MouseMoveAbsolute,
    Delay,
    Text,
    Media
}

public enum MouseButtonType
{
    Left,
    Right,
    Middle,
    XButton1,
    XButton2
}

public class MacroAction : INotifyPropertyChanged
{
    private ActionType _type = ActionType.Delay;
    private string _key = string.Empty;
    private int _virtualKeyCode;
    private int _scanCode;
    private MouseButtonType _mouseButton = MouseButtonType.Left;
    private int _mouseDelta;
    private int _moveX;
    private int _moveY;
    private int _delayMs = 50;
    private int _randomJitterMs = 0;
    private string _textContent = string.Empty;
    private string _mediaCommand = string.Empty;

    public ActionType Type
    {
        get => _type;
        set { _type = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); OnPropertyChanged(nameof(IconKey)); }
    }

    // Key Actions
    public string Key
    {
        get => _key;
        set { _key = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    public int VirtualKeyCode
    {
        get => _virtualKeyCode;
        set { _virtualKeyCode = value; OnPropertyChanged(); }
    }

    public int ScanCode
    {
        get => _scanCode;
        set { _scanCode = value; OnPropertyChanged(); }
    }

    // Mouse Actions
    public MouseButtonType MouseButton
    {
        get => _mouseButton;
        set { _mouseButton = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    public int MouseDelta
    {
        get => _mouseDelta;
        set { _mouseDelta = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    // Mouse Move Actions
    public int MoveX
    {
        get => _moveX;
        set { _moveX = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    public int MoveY
    {
        get => _moveY;
        set { _moveY = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    // Delays / Hold Durations
    public int DelayMs
    {
        get => _delayMs;
        set { _delayMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    public int RandomJitterMs
    {
        get => _randomJitterMs;
        set { _randomJitterMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    // Text / Media
    public string TextContent
    {
        get => _textContent;
        set { _textContent = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    public string MediaCommand
    {
        get => _mediaCommand;
        set { _mediaCommand = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    public string DisplayText
    {
        get
        {
            return Type switch
            {
                ActionType.KeyDown => $"Key Down: {Key.ToUpperInvariant()}",
                ActionType.KeyUp => $"Key Up: {Key.ToUpperInvariant()}",
                ActionType.KeyTap => $"Key Tap: {Key.ToUpperInvariant()} (Hold {DelayMs}ms)",
                ActionType.MouseDown => $"Mouse Down: {MouseButton}",
                ActionType.MouseUp => $"Mouse Up: {MouseButton}",
                ActionType.MouseClick => $"Mouse Click: {MouseButton} (Hold {DelayMs}ms)",
                ActionType.MouseScroll => $"Mouse Scroll: {MouseDelta}",
                ActionType.MouseMoveRelative => $"Mouse Move (Rel): X={MoveX:+0;-0;0}, Y={MoveY:+0;-0;0}",
                ActionType.MouseMoveAbsolute => $"Mouse Move (Abs): X={MoveX}, Y={MoveY}",
                ActionType.Delay => $"Wait: {DelayMs} ms" + (RandomJitterMs > 0 ? $" (±{RandomJitterMs}ms)" : ""),
                ActionType.Text => $"Type: \"{TextContent}\"",
                ActionType.Media => $"Media: {MediaCommand}",
                _ => Type.ToString()
            };
        }
    }

    public string IconKey
    {
        get
        {
            return Type switch
            {
                ActionType.KeyDown or ActionType.KeyUp or ActionType.KeyTap => "keyboard",
                ActionType.MouseDown or ActionType.MouseUp or ActionType.MouseClick => "mouse",
                ActionType.MouseMoveRelative or ActionType.MouseMoveAbsolute => "crosshair",
                ActionType.MouseScroll => "mouse",
                ActionType.Delay => "clock",
                ActionType.Text => "type",
                ActionType.Media => "play",
                _ => "zap"
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
