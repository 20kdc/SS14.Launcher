using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace SS14.Launcher.Views;

public sealed partial class DungSpinner : UserControl
{
    public static readonly StyledProperty<double> AnimationProgressProperty =
        AvaloniaProperty.Register<DungSpinner, double>(nameof(AnimationProgress));

    public static readonly StyledProperty<IBrush> FillProperty =
        AvaloniaProperty.Register<DungSpinner, IBrush>(nameof(Fill));

    private readonly IPen _pen = new Pen();

    static DungSpinner()
    {
        AffectsRenderVisibleOnly<DungSpinner>(AnimationProgressProperty, FillProperty);
    }

    public DungSpinner()
    {
        InitializeComponent();
    }

    public double AnimationProgress
    {
        get => GetValue(AnimationProgressProperty);
        set => SetValue(AnimationProgressProperty, value);
    }

    public IBrush Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var centerX = Bounds.Width / 2;
        var centerY = Bounds.Height / 2;

        // Offset so that 0,0 is the center of the control.
        var offset = Matrix.CreateTranslation(centerX, centerY);

        using var translateState = context.PushPreTransform(offset);

        var brush = Fill;
        var progress = AnimationProgress * Math.PI * 2;

        void DrawElectron(double angle, double xScale, double yScale, double radius, double animationOffset,
            double mul = 1)
        {
            var rotation = Matrix.CreateRotation(angle);
            using var _ = context.PushPreTransform(rotation);

            var p = (progress + animationOffset) * mul;
            var x = Math.Sin(p) * xScale;
            var y = Math.Cos(p) * yScale;

            var ellipseGeometry = new EllipseGeometry(new Rect(x - radius, y - radius, radius * 2, radius * 2));

            context.DrawGeometry(brush, _pen, ellipseGeometry);
        }

        const double sizeElectron = 1.5;
        const double sizeNucleus = 3;
        const double pathX = 4;
        const double pathY = 10;

        DrawElectron(Math.PI * 2d / 3d, pathX, pathY, sizeElectron, 0.5);
        DrawElectron(Math.PI / 3d, pathX, pathY, sizeElectron, 0.33, -1);
        DrawElectron(0, pathX, pathY, sizeElectron, 0.0);
        DrawElectron(0, 0, 0, sizeNucleus, 0);
    }

    /// <summary>
    /// Customized version of Avalonia's AffectsRender() that also checks if the control is displayed before forcing
    /// it to be re-rendered.  This is a workaround for the bug on linux (and maybe other platforms) where avalonia
    /// is constantly re-rendering the animation, even if the animation is hidden.
    /// </summary>
    protected static void AffectsRenderVisibleOnly<T>(params AvaloniaProperty[] properties) where T : Visual
    {
        foreach (AvaloniaProperty avaloniaProperty in properties)
        {
            avaloniaProperty.Changed.Subscribe(delegate (AvaloniaPropertyChangedEventArgs e)
            {
                Invalidate(e);
            });
        }

        static void Invalidate(AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Sender is T val)
            {
                if (val.IsEffectivelyVisible)
                    val.InvalidateVisual();
            }
        }
    }
}
