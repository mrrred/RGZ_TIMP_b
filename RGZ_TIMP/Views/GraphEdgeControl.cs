using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RGZ_TIMP.Views;

public sealed class GraphEdgeControl
{
    private const double ArrowSize = 10;
    private double _parallelOffset;

    public GraphEdgeControl(int edgeId, int localOrder, GraphNodeControl from, GraphNodeControl to, double parallelOffset = 0)
    {
        EdgeId = edgeId;
        LocalOrder = localOrder;
        From = from;
        To = to;
        _parallelOffset = parallelOffset;
        TargetNodeNumber = to.NodeNumber;

        Line = new Path
        {
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Cursor = Cursors.Hand
        };

        Arrow = new Polygon
        {
            Fill = Brushes.Black,
            Cursor = Cursors.Hand
        };

        Line.MouseLeftButtonDown += Edge_MouseLeftButtonDown;
        Arrow.MouseLeftButtonDown += Edge_MouseLeftButtonDown;

        UpdateToolTip();
    }

    public int EdgeId { get; }

    public int LocalOrder { get; private set; }

    public GraphNodeControl From { get; }

    public GraphNodeControl To { get; }

    public double ParallelOffset => _parallelOffset;

    public int TargetNodeNumber { get; }

    public int Predicate { get; private set; } = 1;

    public int DelaySeconds { get; set; } = 2;

    public Path Line { get; }

    public Polygon Arrow { get; }

    public event MouseButtonEventHandler? EdgeDoubleClicked;

    public void UpdateGeometry(Canvas canvas)
    {
        var from = From.GetCenterOn(canvas);
        var to = To.GetCenterOn(canvas);

        var direction = to - from;
        if (direction.Length < 0.001 || ReferenceEquals(From, To))
        {
            UpdateSelfLoopGeometry(from);
            UpdateToolTip();
            return;
        }

        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var offset = normal * _parallelOffset;
        var radius = 30d;
        var radial = Math.Sqrt(Math.Max(0, (radius * radius) - (_parallelOffset * _parallelOffset)));
        if (radial < 0.001)
        {
            radial = radius;
        }

        var start = from + (direction * radial) + offset;
        var end = to - (direction * radial) + offset;

        Line.Data = new LineGeometry(start, end);

        var tip = end;
        var p1 = tip - (direction * ArrowSize) + (normal * (ArrowSize * 0.6));
        var p2 = tip - (direction * ArrowSize) - (normal * (ArrowSize * 0.6));
        Arrow.Points = new PointCollection { tip, p1, p2 };

        UpdateToolTip();
    }

    public void UpdateLocalOrder(int localOrder)
    {
        LocalOrder = localOrder;
        UpdateToolTip();
    }

    public void UpdatePredicate(int predicate)
    {
        Predicate = predicate;
        UpdateToolTip();
    }

    public void UpdateParallelOffset(double offset)
    {
        _parallelOffset = offset;
    }

    private void UpdateSelfLoopGeometry(Point center)
    {
        var loopRadius = 18d;
        var loopCenter = new Point(center.X + 24, center.Y - 24);

        var start = new Point(loopCenter.X + loopRadius, loopCenter.Y);
        var mid = new Point(loopCenter.X - loopRadius, loopCenter.Y);

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = true,
            Segments =
            {
                new ArcSegment(mid, new Size(loopRadius, loopRadius), 0, false, SweepDirection.Clockwise, true),
                new ArcSegment(start, new Size(loopRadius, loopRadius), 0, false, SweepDirection.Clockwise, true)
            }
        };

        Line.Data = new PathGeometry(new[] { figure });

        var angle = -Math.PI / 4;
        var tip = new Point(
            loopCenter.X + loopRadius * Math.Cos(angle),
            loopCenter.Y + loopRadius * Math.Sin(angle));
        var direction = new Vector(-Math.Sin(angle), Math.Cos(angle));
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var p1 = tip - (direction * ArrowSize) + (normal * (ArrowSize * 0.6));
        var p2 = tip - (direction * ArrowSize) - (normal * (ArrowSize * 0.6));
        Arrow.Points = new PointCollection { tip, p1, p2 };
    }

    private void UpdateToolTip()
    {
        var text = $"Дуга #{EdgeId}\n" +
                   $"Номер на узле: {LocalOrder}\n" +
                   $"В узел: {TargetNodeNumber}\n" +
                   $"Предикат: {Predicate}\n" +
                   $"Задержка: {DelaySeconds} сек";

        Line.ToolTip = text;
        Arrow.ToolTip = text;
    }

    private void Edge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            EdgeDoubleClicked?.Invoke(this, e);
            e.Handled = true;
        }
    }
}
