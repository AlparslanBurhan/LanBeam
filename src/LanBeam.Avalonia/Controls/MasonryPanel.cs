using System;
using Avalonia;
using Avalonia.Controls;

namespace LanBeam.Ui.Controls;

/// <summary>
/// Sütun tabanlı masonry düzeni: öğeleri sabit genişlikli sütunlara yerleştirip her sütunu
/// bağımsız yığar. Farklı yükseklikteki kartların arasında boşluk kalmaz; genişledikçe sütun artar.
/// </summary>
public sealed class MasonryPanel : Panel
{
    public static readonly StyledProperty<double> ColumnWidthProperty =
        AvaloniaProperty.Register<MasonryPanel, double>(nameof(ColumnWidth), 388.0);

    public double ColumnWidth
    {
        get => GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    static MasonryPanel()
    {
        AffectsMeasure<MasonryPanel>(ColumnWidthProperty);
        AffectsArrange<MasonryPanel>(ColumnWidthProperty);
    }

    private int ColumnCount(double availableWidth)
    {
        if (double.IsInfinity(availableWidth) || availableWidth <= 0 || ColumnWidth <= 0) return 1;
        return Math.Max(1, (int)(availableWidth / ColumnWidth));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int columns = ColumnCount(availableSize.Width);
        var heights = new double[columns];
        foreach (Control child in Children)
        {
            child.Measure(new Size(ColumnWidth, double.PositiveInfinity));
            int col = Shortest(heights);
            heights[col] += child.DesiredSize.Height;
        }
        double width = double.IsInfinity(availableSize.Width) ? columns * ColumnWidth : availableSize.Width;
        double height = 0;
        foreach (double h in heights) height = Math.Max(height, h);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = ColumnCount(finalSize.Width);
        var heights = new double[columns];
        foreach (Control child in Children)
        {
            int col = Shortest(heights);
            child.Arrange(new Rect(col * ColumnWidth, heights[col], ColumnWidth, child.DesiredSize.Height));
            heights[col] += child.DesiredSize.Height;
        }
        return finalSize;
    }

    private static int Shortest(double[] heights)
    {
        int best = 0;
        for (int i = 1; i < heights.Length; i++)
            if (heights[i] < heights[best]) best = i;
        return best;
    }
}
