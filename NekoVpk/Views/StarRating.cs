using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace NekoVpk.Views;

public class StarRating : StackPanel
{
    private const string FilledStarData =
        "M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.63L12,2L9.19,8.63L2,9.24L7.46,13.97L5.82,21L12,17.27Z";
    private const string OutlineStarData =
        "M12,15.39L8.24,17.66L9.23,13.38L5.91,10.5L10.29,10.13L12,6.09L13.71,10.13L18.09,10.5L14.77,13.38L15.76,17.66M22,9.24L14.81,8.63L12,2L9.19,8.63L2,9.24L7.46,13.97L5.82,21L12,17.27L18.18,21L16.54,13.97L22,9.24Z";

    public static readonly StyledProperty<string?> StarsProperty =
        AvaloniaProperty.Register<StarRating, string?>(nameof(Stars));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<StarRating, double>(nameof(IconSize), 14.0);

    static StarRating()
    {
        StarsProperty.Changed.AddClassHandler<StarRating>((s, _) => s.Rebuild());
        IconSizeProperty.Changed.AddClassHandler<StarRating>((s, _) => s.Rebuild());
    }

    public StarRating()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 1;
        Rebuild();
    }

    public string? Stars
    {
        get => GetValue(StarsProperty);
        set => SetValue(StarsProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();

        string stars = Stars ?? string.Empty;
        bool rated = stars.Length > 0;
        int filled = stars.Count(c => c == '★');

        IBrush brush = rated ? Brushes.Gold : Brushes.Gray;
        Geometry filledStar = Geometry.Parse(FilledStarData);
        Geometry outlineStar = Geometry.Parse(OutlineStarData);

        for (int i = 0; i < 5; i++)
        {
            Children.Add(new PathIcon
            {
                Width = IconSize,
                Height = IconSize,
                Data = i < filled ? filledStar : outlineStar,
                Foreground = brush
            });
        }
    }
}
