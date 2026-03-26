using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Linq;

namespace RGZ_TIMP;

public sealed class GraphNodeControl : UserControl
{
    private readonly Grid _root;
    private readonly Ellipse _handle;
    private readonly MenuItem _deleteMenuItem;
    private readonly double _radius;

    public GraphNodeControl(int nodeNumber)
    {
        NodeNumber = nodeNumber;
        Width = 60;
        Height = 60;
        _radius = Width / 2;

        _root = new Grid();

        var body = new Ellipse
        {
            Fill = Brushes.White,
            Stroke = Brushes.Black,
            StrokeThickness = 2
        };

        var title = new TextBlock
        {
            Text = nodeNumber.ToString(),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _handle = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = Brushes.Black,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand
        };

        _root.Children.Add(body);
        _root.Children.Add(title);
        _root.Children.Add(_handle);

        Content = _root;

        _deleteMenuItem = new MenuItem { Header = "Удалить" };
        ContextMenu = new ContextMenu();
        ContextMenu.Items.Add(_deleteMenuItem);

        MouseEnter += (_, _) => UpdateHandleVisibility();
        MouseLeave += (_, _) =>
        {
            if (!_isDragFromHandle)
            {
                _handle.Visibility = Visibility.Collapsed;
            }
        };

        MouseMove += GraphNodeControl_MouseMove;
        PreviewKeyDown += (_, _) => UpdateHandleVisibility();
        PreviewKeyUp += (_, _) => UpdateHandleVisibility();

        _handle.MouseLeftButtonDown += (s, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                return;
            }

            _isDragFromHandle = true;
            _handle.Visibility = Visibility.Visible;
            HandleDragStarted?.Invoke(this, e);
            e.Handled = true;
        };

        _handle.MouseLeftButtonUp += (s, e) =>
        {
            _isDragFromHandle = false;
            HandleDragCompleted?.Invoke(this, e);
            e.Handled = true;
        };

        MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                NodeDoubleClicked?.Invoke(this, e);
                e.Handled = true;
            }
        };

        UpdateHandleBoundary(new Point(Width, Height / 2));
    }

    public int NodeNumber { get; }

    public string NodeCode { get; set; } = "rnd(1..n)";

    public event MouseButtonEventHandler? HandleDragStarted;

    public event MouseButtonEventHandler? HandleDragCompleted;

    public event MouseButtonEventHandler? NodeDoubleClicked;

    private bool _isDragFromHandle;

    private void GraphNodeControl_MouseMove(object sender, MouseEventArgs e)
    {
        UpdateHandleBoundary(e.GetPosition(this));
        UpdateHandleVisibility();
    }

    private void UpdateHandleVisibility()
    {
        if (_isDragFromHandle)
        {
            _handle.Visibility = Visibility.Visible;
            return;
        }

        _handle.Visibility = IsMouseOver && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void WireDeleteAction(RoutedEventHandler handler)
    {
        _deleteMenuItem.Click += handler;
    }

    public void RefreshHandleVisibility()
    {
        UpdateHandleVisibility();
    }

    public void UpdateNodeToolTip(Canvas canvas, IEnumerable<GraphEdgeControl> outgoingEdges)
    {
        var center = GetCenterOn(canvas);
        var outgoingIds = outgoingEdges.Select(x => x.EdgeId).ToList();
        var outgoingText = outgoingIds.Count == 0 ? "-" : string.Join(", ", outgoingIds);

        ToolTip = $"Узел #{NodeNumber}\n" +
                  $"Центр: ({center.X:F0}; {center.Y:F0})\n" +
                  $"Радиус: {_radius:F0}\n" +
                  $"Исходящие дуги: {outgoingText}\n" +
                  $"Код: {NodeCode}";
    }

    public void EndHandleDrag()
    {
        _isDragFromHandle = false;
        UpdateHandleVisibility();
    }

    public void UpdateHandleBoundary(Point pointInNode)
    {
        var center = new Point(Width / 2, Height / 2);
        var vector = pointInNode - center;
        var len = vector.Length;
        if (len < 0.001)
        {
            vector = new Vector(1, 0);
            len = 1;
        }

        vector.Normalize();
        var radius = Width / 2;
        var boundary = center + (vector * radius);

        _handle.RenderTransform = new TranslateTransform(boundary.X - (_handle.Width / 2), boundary.Y - (_handle.Height / 2));
    }

    public Point GetCenterOn(Canvas canvas)
    {
        return TranslatePoint(new Point(Width / 2, Height / 2), canvas);
    }

    public Point GetHandleCenterOn(Canvas canvas)
    {
        return _handle.TranslatePoint(new Point(_handle.Width / 2, _handle.Height / 2), canvas);
    }
}
