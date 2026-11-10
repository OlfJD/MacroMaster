using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MacroMaster.Core.Models;
using Binding = System.Windows.Data.Binding;

namespace MacroMaster.UI.Editor;

/// <summary>A value/label pair for the small dropdowns inside each step row.</summary>
public sealed record Choice(object Value, string Label)
{
    // The dropdown's closed state renders the item itself, so it must read as its label
    public override string ToString() => Label;
}

/// <summary>Dropdown options for the inline step editors, worded for people rather than Win32.</summary>
public static class StepChoices
{
    public static List<Choice> KeyTypes { get; } = new()
    {
        new(ActionType.KeyTap, "Press"),
        new(ActionType.KeyDown, "Hold down"),
        new(ActionType.KeyUp, "Let go of"),
    };

    public static List<Choice> MouseTypes { get; } = new()
    {
        new(ActionType.MouseClick, "Click"),
        new(ActionType.MouseDown, "Hold down"),
        new(ActionType.MouseUp, "Let go of"),
        new(ActionType.MouseScroll, "Scroll"),
    };

    public static List<Choice> MouseButtons { get; } = new()
    {
        new(MouseButtonType.Left, "left button"),
        new(MouseButtonType.Right, "right button"),
        new(MouseButtonType.Middle, "middle button"),
        new(MouseButtonType.XButton1, "side button 4"),
        new(MouseButtonType.XButton2, "side button 5"),
    };

    /// <summary>Values are the command names <c>InputSimulator.SendMediaCommand</c> understands.</summary>
    public static List<Choice> MediaCommands { get; } = new()
    {
        new("PlayPause", "Play / pause"),
        new("Next", "Next track"),
        new("Prev", "Previous track"),
        new("Stop", "Stop"),
        new("VolumeUp", "Volume up"),
        new("VolumeDown", "Volume down"),
        new("Mute", "Mute"),
    };
}

/// <summary>Picks the inline editor for a step: key, mouse, wait, move, text or media key.</summary>
public sealed class StepTemplateSelector : DataTemplateSelector
{
    public DataTemplate? KeyTemplate { get; set; }
    public DataTemplate? MouseTemplate { get; set; }
    public DataTemplate? WaitTemplate { get; set; }
    public DataTemplate? MoveTemplate { get; set; }
    public DataTemplate? TextTemplate { get; set; }
    public DataTemplate? MediaTemplate { get; set; }
    public DataTemplate? OtherTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is MacroAction a
        ? a.Type switch
        {
            ActionType.KeyTap or ActionType.KeyDown or ActionType.KeyUp => KeyTemplate,
            ActionType.MouseClick or ActionType.MouseDown or ActionType.MouseUp or ActionType.MouseScroll => MouseTemplate,
            ActionType.Delay => WaitTemplate,
            ActionType.MouseMoveRelative or ActionType.MouseMoveAbsolute => MoveTemplate,
            ActionType.Text => TextTemplate,
            ActionType.Media => MediaTemplate,
            _ => OtherTemplate
        }
        : null;
}

/// <summary>Shows a key name as it appears on a keycap ("j" → "J"; empty → "Set key").</summary>
public sealed class KeyLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = (value as string)?.Trim();
        if (string.IsNullOrEmpty(key)) return "Set key";
        return key.Length == 1 ? key.ToUpperInvariant() : key;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
