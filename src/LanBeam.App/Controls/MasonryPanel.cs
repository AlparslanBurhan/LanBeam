using System.Windows;
using System.Windows.Controls;

namespace LanBeam.App.Controls;

/// <summary>
/// Sütun tabanlı "masonry" düzeni: öğeleri sabit genişlikli sütunlara yerleştirir ve her sütunu
/// bağımsız olarak yukarıdan aşağıya yığar. WrapPanel'in aksine satır hizalaması yapmadığından,
/// farklı yükseklikteki kartların arasında boşluk kalmaz. Sütun sayısı mevcut genişliğe göre artar.
/// </summary>
public sealed class MasonryPanel : Panel
{
    public static readonly DependencyProperty ColumnWidthProperty = DependencyProperty.Register(
        nameof(ColumnWidth), typeof(double), typeof(MasonryPanel),
        new FrameworkPropertyMetadata(388.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Bir sütunun genişliği (kart genişliği + kartlar arası yatay boşluk).</summary>
    public double ColumnWidth
    {
        get => (double)GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    private int ColumnCount(double availableWidth)
    {
        if (double.IsInfinity(availableWidth) || availableWidth <= 0 || ColumnWidth <= 0)
            return 1;
        return Math.Max(1, (int)(availableWidth / ColumnWidth));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int columns = ColumnCount(availableSize.Width);
        var heights = new double[columns];

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(ColumnWidth, double.PositiveInfinity));
            int col = ShortestColumn(heights);
            heights[col] += child.DesiredSize.Height;
        }

        double width = double.IsInfinity(availableSize.Width) ? columns * ColumnWidth : availableSize.Width;
        double height = heights.Length > 0 ? heights.Max() : 0;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = ColumnCount(finalSize.Width);
        var heights = new double[columns];

        foreach (UIElement child in InternalChildren)
        {
            int col = ShortestColumn(heights);
            double x = col * ColumnWidth;
            double y = heights[col];
            child.Arrange(new Rect(x, y, ColumnWidth, child.DesiredSize.Height));
            heights[col] += child.DesiredSize.Height;
        }

        return finalSize;
    }

    private static int ShortestColumn(double[] heights)
    {
        int best = 0;
        for (int i = 1; i < heights.Length; i++)
            if (heights[i] < heights[best])
                best = i;
        return best;
    }
}
