using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SimForge.Controls;

public sealed class WorkspaceGrid : Control
{
    private static readonly Pen MinorPen = new(new SolidColorBrush(Color.Parse("#142131")), 1);
    private static readonly Pen MajorPen = new(new SolidColorBrush(Color.Parse("#1B2C40")), 1);

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        const double minorStep = 20;
        const double majorStep = 100;

        for (var x = 0d; x <= Bounds.Width; x += minorStep)
        {
            var pen = Math.Abs(x % majorStep) < 0.1 ? MajorPen : MinorPen;
            context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
        }

        for (var y = 0d; y <= Bounds.Height; y += minorStep)
        {
            var pen = Math.Abs(y % majorStep) < 0.1 ? MajorPen : MinorPen;
            context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
        }
    }
}
