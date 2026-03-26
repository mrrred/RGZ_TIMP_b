using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RGZ_TIMP;

public sealed class GraphEdgeControl
{
    private const double ArrowSize = 10;
    private readonly double _parallelOffset;

    public GraphEdgeControl(int edgeId, int localOrder, GraphNodeControl from, GraphNodeControl to, double parallelOffset = 0)
    {
        EdgeId = edgeId;
        LocalOrder = localOrder;
        From = from;
        To = to;
        _parallelOffset = parallelOffset;
        TargetNodeNumber = to.NodeNumber;

        Line = new Line
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

    public int LocalOrder { get; }

    public GraphNodeControl From { get; }

    public GraphNodeControl To { get; }

    public int TargetNodeNumber { get; }

    public int Predicate { get; set; } = 1;

    public int DelaySeconds { get; set; } = 2;

    public Line Line { get; }

    public Polygon Arrow { get; }

    public event MouseButtonEventHandler? EdgeDoubleClicked;

    public void UpdateGeometry(Canvas canvas)
    {
        var from = From.GetCenterOn(canvas);
        var to = To.GetCenterOn(canvas);

        var direction = to - from;
        if (direction.Length < 0.001)
        {
            return;
        }

        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var offset = normal * _parallelOffset;

        var start = from + (direction * 30) + offset;
        var end = to - (direction * 30) + offset;

        Line.X1 = start.X;
        Line.Y1 = start.Y;
        Line.X2 = end.X;
        Line.Y2 = end.Y;

        var tip = end;
        var p1 = tip - (direction * ArrowSize) + (normal * (ArrowSize * 0.6));
        var p2 = tip - (direction * ArrowSize) - (normal * (ArrowSize * 0.6));
        Arrow.Points = new PointCollection { tip, p1, p2 };

        UpdateToolTip();
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
