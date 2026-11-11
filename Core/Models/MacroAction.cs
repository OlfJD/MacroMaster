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
    Media,
    // Added later: keep new values at the end, saved files store the number
    WaitForPixel,   // wait until the pixel at MoveX/MoveY has the colour in TextContent (give up after DelayMs)
    RepeatStart,    // repeat everything up to the matching RepeatEnd, RepeatCount times
    RepeatEnd
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
    private int _repeatCount = 2;
    private int _depth;

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

    /// <summary>How many times a <see cref="ActionType.RepeatStart"/> block runs.</summary>
    public int RepeatCount
    {
        get => _repeatCount;
        set { _repeatCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    /// <summary>"#RRGGBB" (as stored in <see cref="TextContent"/>) → colour parts. False if it isn't a colour.</summary>
    public static bool TryParseColor(string? text, out int r, out int g, out int b)
    {
        r = g = b = 0;
        string s = (text ?? string.Empty).Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out int rgb)) return false;
        r = (rgb >> 16) & 0xFF;
        g = (rgb >> 8) & 0xFF;
        b = rgb & 0xFF;
        return true;
    }

    // Editor-only: how many repeat blocks this step sits inside (indents its row)
    [System.Text.Json.Serialization.JsonIgnore]
    public int Depth
    {
        get => _depth;
        set { if (_depth == value) return; _depth = value; OnPropertyChanged(); }
    }

    // Editor-only: this step's key button is waiting for a key press (or its "Pick" button for a click)
    private bool _isCapturingKey;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCapturingKey
    {
        get => _isCapturingKey;
        set { _isCapturingKey = value; OnPropertyChanged(); }
    }

    // Editor-only: this step is being dragged to a new position
    private bool _isDragging;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDragging
    {
        get => _isDragging;
        set { _isDragging = value; OnPropertyChanged(); }
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
                ActionType.WaitForPixel => $"Wait for pixel {MoveX},{MoveY} = {TextContent}",
                ActionType.RepeatStart => $"Repeat {RepeatCount}x",
                ActionType.RepeatEnd => "End of repeat",
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
                ActionType.WaitForPixel => "monitor",
                ActionType.RepeatStart or ActionType.RepeatEnd => "repeat",
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
