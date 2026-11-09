using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MacroMaster.Core.Models;
using Binding = System.Windows.Data.Binding;

namespace MacroMaster.UI.Converters;

/// <summary>"Ctrl + Shift + F6" → ["Ctrl", "Shift", "F6"] so each part renders as a keycap.</summary>
public sealed class TriggerKeysConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text) || text == "None") return new[] { "—" };
        return text.Split(" + ", StringSplitOptions.RemoveEmptyEntries);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ModeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not MacroDefinition m) return string.Empty;
        return m.Mode switch
        {
            ExecutionMode.Once => "Run once",
            ExecutionMode.Toggle => "Toggle loop",
            ExecutionMode.HoldWhileDown => "While held",
            ExecutionMode.RepeatCount => $"Repeat ×{Math.Max(1, m.RepeatCount)}",
            _ => m.Mode.ToString()
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ModeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ExecutionMode mode
            ? mode switch
            {
                ExecutionMode.Toggle => "repeat",
                ExecutionMode.RepeatCount => "repeat",
                ExecutionMode.HoldWhileDown => "mouse",
                _ => "play"
            }
            : "play";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ProcessLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string;
        return string.IsNullOrWhiteSpace(s) || s.Trim() == "*" ? "All apps" : s.Trim();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Collapsed when the process filter is global ("*"), visible otherwise.</summary>
public sealed class IsGlobalProcessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string;
        return string.IsNullOrWhiteSpace(s) || s.Trim() == "*" ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class IndexPlusOneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i ? (i + 1).ToString(culture) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
