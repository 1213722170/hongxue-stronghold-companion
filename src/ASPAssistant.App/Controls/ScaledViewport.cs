using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ASPAssistant.App.Controls;

// A finite inner viewport preserves list virtualization even when the outer frame needs scrolling.
internal sealed class ScaledViewport : ScrollViewer
{
    private readonly FrameworkElement _child;
    private readonly double _originalWidth;
    private readonly double _originalHeight;
    private readonly Transform _originalTransform;
    private readonly double _minimumWidth;
    private readonly double _minimumHeight;
    private readonly ScaleTransform _transform = new();

    public ScaledViewport(FrameworkElement child, double minimumWidth, double minimumHeight)
    {
        _child = child;
        _originalWidth = child.Width;
        _originalHeight = child.Height;
        _originalTransform = child.LayoutTransform;
        _minimumWidth = minimumWidth;
        _minimumHeight = minimumHeight;
        child.SetCurrentValue(WidthProperty, Math.Max(minimumWidth, child.ActualWidth));
        child.SetCurrentValue(HeightProperty, Math.Max(minimumHeight, child.ActualHeight));
        child.LayoutTransform = _transform;
        Content = child;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Focusable = false;
        BorderThickness = new Thickness(0);
        Resources[typeof(ScrollBar)] = Application.Current.FindResource("ViewportScrollBar");
        SizeChanged += (_, _) => UpdateBounds();
    }

    public double ContentScale
    {
        get => _transform.ScaleX;
        set { _transform.ScaleX = _transform.ScaleY = value; UpdateBounds(); }
    }

    private void UpdateBounds()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return; // Before the first arrange there is no viewport yet.
        var width = ActualWidth;
        var height = ActualHeight;
        var dpi = VisualTreeHelper.GetDpi(_child);
        var verticalBar = (ScrollBar)Template.FindName("PART_VerticalScrollBar", this);
        var horizontalBar = (ScrollBar)Template.FindName("PART_HorizontalScrollBar", this);
        var vertical = false;
        var horizontal = false;
        // Auto can keep both bars alive after the first zoomed arrange. Use the finite content minimum instead.
        for (int i = 0; i < 3; i++)
        {
            vertical = _minimumHeight * ContentScale > height + 1 / dpi.DpiScaleY;
            horizontal = _minimumWidth * ContentScale > width + 1 / dpi.DpiScaleX;
            width = Math.Max(0, ActualWidth - (vertical ? verticalBar.Width : 0));
            height = Math.Max(0, ActualHeight - (horizontal ? horizontalBar.Height : 0));
        }
        HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Visible : ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Visible : ScrollBarVisibility.Disabled;
        // Leave one device pixel for layout rounding, otherwise fractional zoom can add both scrollbars.
        _child.SetCurrentValue(WidthProperty, Math.Max(_minimumWidth, Math.Floor((width - 1 / dpi.DpiScaleX) / ContentScale * dpi.DpiScaleX) / dpi.DpiScaleX));
        _child.SetCurrentValue(HeightProperty, Math.Max(_minimumHeight, Math.Floor((height - 1 / dpi.DpiScaleY) / ContentScale * dpi.DpiScaleY) / dpi.DpiScaleY));
    }

    public FrameworkElement Detach()
    {
        Content = null;
        _child.LayoutTransform = _originalTransform;
        _child.SetCurrentValue(WidthProperty, _originalWidth);
        _child.SetCurrentValue(HeightProperty, _originalHeight);
        return _child;
    }
}
