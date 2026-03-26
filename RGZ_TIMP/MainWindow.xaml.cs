using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Linq;
using Microsoft.Win32;

namespace RGZ_TIMP;

/// <summary>
/// Макетный интерфейс без реализации бизнес-логики.
/// </summary>
public partial class MainWindow : Window
{
    private readonly List<GraphNodeControl> _nodes = new();
    private readonly List<GraphEdgeControl> _edges = new();
    private int _nextEdgeId = 1;

    private GraphNodeControl? _movingNode;
    private Vector _moveOffset;

    private GraphNodeControl? _lineSourceNode;

    public MainWindow()
    {
        InitializeComponent();

        GraphCanvas.MouseMove += GraphCanvas_MouseMove;
        GraphCanvas.MouseLeftButtonUp += GraphCanvas_MouseLeftButtonUp;
        Loaded += MainWindow_Loaded;
        PreviewKeyDown += MainWindow_PreviewKeyChanged;
        PreviewKeyUp += MainWindow_PreviewKeyChanged;

        BuildMockGraph();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateAllEdges();
    }

    private void MainWindow_PreviewKeyChanged(object sender, KeyEventArgs e)
    {
        foreach (var node in _nodes)
        {
            node.RefreshHandleVisibility();
        }
    }

    private void BuildMockGraph()
    {
        var node1 = AddNode(1, 170, 150);
        var node2 = AddNode(2, 360, 130);
        var node3 = AddNode(3, 300, 300);

        AddEdge(node1, node2, -10);
        AddEdge(node2, node1, -10);
        AddEdge(node2, node3);
        AddEdge(node1, node3);

        UpdateNodeToolTips();
    }

    private GraphNodeControl AddNode(int number, double centerX, double centerY)
    {
        var node = new GraphNodeControl(number);

        node.NodeDoubleClicked += Node_MouseDoubleClick;
        node.HandleDragStarted += Node_HandleDragStarted;
        node.HandleDragCompleted += Node_HandleDragCompleted;
        node.MouseLeftButtonDown += Node_MouseLeftButtonDown;
        node.MouseLeftButtonUp += Node_MouseLeftButtonUp;
        node.MouseMove += Node_MouseMove;
        node.WireDeleteAction(Node_DeleteMenuItem_Click);

        _nodes.Add(node);
        GraphCanvas.Children.Add(node);
        Canvas.SetLeft(node, centerX - (node.Width / 2));
        Canvas.SetTop(node, centerY - (node.Height / 2));
        Panel.SetZIndex(node, 20);

        return node;
    }

    private void AddEdge(GraphNodeControl from, GraphNodeControl to, double parallelOffset = 0)
    {
        var localOrder = _edges.Count(x => ReferenceEquals(x.From, from)) + 1;
        var edge = new GraphEdgeControl(_nextEdgeId++, localOrder, from, to, parallelOffset);
        edge.EdgeDoubleClicked += Edge_MouseDoubleClick;

        _edges.Add(edge);
        GraphCanvas.Children.Add(edge.Line);
        GraphCanvas.Children.Add(edge.Arrow);

        Panel.SetZIndex(edge.Line, 5);
        Panel.SetZIndex(edge.Arrow, 6);

        edge.UpdateGeometry(GraphCanvas);
        UpdateNodeToolTips();
    }

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not GraphNodeControl node || e.ClickCount > 1)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            return;
        }

        _movingNode = node;
        var pointer = e.GetPosition(GraphCanvas);
        var center = node.GetCenterOn(GraphCanvas);
        _moveOffset = pointer - center;
        node.CaptureMouse();
    }

    private void Node_MouseMove(object sender, MouseEventArgs e)
    {
        if (_movingNode is not GraphNodeControl node || !ReferenceEquals(node, sender) || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pointer = e.GetPosition(GraphCanvas);
        var center = pointer - _moveOffset;

        Canvas.SetLeft(node, center.X - (node.Width / 2));
        Canvas.SetTop(node, center.Y - (node.Height / 2));

        UpdateAllEdges();
        UpdateNodeToolTips();
    }

    private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not GraphNodeControl node || _movingNode is null)
        {
            return;
        }

        if (ReferenceEquals(node, _movingNode))
        {
            _movingNode = null;
            node.ReleaseMouseCapture();
        }
    }

    private void Node_HandleDragStarted(object sender, MouseButtonEventArgs e)
    {
        if (sender is not GraphNodeControl node || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        _lineSourceNode = node;

        var start = node.GetHandleCenterOn(GraphCanvas);
        PreviewLine.X1 = start.X;
        PreviewLine.Y1 = start.Y;
        PreviewLine.X2 = start.X;
        PreviewLine.Y2 = start.Y;
        PreviewLine.Visibility = Visibility.Visible;

        GraphCanvas.CaptureMouse();
    }

    private void Node_HandleDragCompleted(object sender, MouseButtonEventArgs e)
    {
        StopPreviewLine();
    }

    private void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_lineSourceNode is null)
        {
            return;
        }

        var current = e.GetPosition(GraphCanvas);
        var pointInNode = e.GetPosition(_lineSourceNode);
        _lineSourceNode.UpdateHandleBoundary(pointInNode);

        var start = _lineSourceNode.GetHandleCenterOn(GraphCanvas);
        PreviewLine.X1 = start.X;
        PreviewLine.Y1 = start.Y;
        PreviewLine.X2 = current.X;
        PreviewLine.Y2 = current.Y;
    }

    private void GraphCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        StopPreviewLine();
    }

    private void StopPreviewLine()
    {
        if (_lineSourceNode is not null)
        {
            _lineSourceNode.EndHandleDrag();
        }

        _lineSourceNode = null;
        PreviewLine.Visibility = Visibility.Collapsed;
        GraphCanvas.ReleaseMouseCapture();
    }

    private void Node_DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
    }

    private void UpdateAllEdges()
    {
        foreach (var edge in _edges)
        {
            edge.UpdateGeometry(GraphCanvas);
        }
    }

    private void UpdateNodeToolTips()
    {
        foreach (var node in _nodes)
        {
            var outgoing = _edges.Where(x => ReferenceEquals(x.From, node));
            node.UpdateNodeToolTip(GraphCanvas, outgoing);
        }
    }

    private void CreateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
            FileName = "graph.xml"
        };

        dialog.ShowDialog(this);
    }

    private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*"
        };

        dialog.ShowDialog(this);
    }

    private void SaveMenuItem_Click(object sender, RoutedEventArgs e)
    {
        
    }

    private void AnimationSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowAnimationSettingsDialog();
    }

    private void RunMenuItem_Click(object sender, RoutedEventArgs e)
    {
    }

    private void StopMenuItem_Click(object sender, RoutedEventArgs e)
    {
    }

    private void HelpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this, "Демонстрационный макет интерфейса без реализации логики.", "Справка", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AddNodeMenuItem_Click(object sender, RoutedEventArgs e)
    {
    }

    private void DeleteSelectionMenuItem_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Node_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (sender is GraphNodeControl node)
        {
            ShowNodeCodeDialog(node);
            UpdateNodeToolTips();
            return;
        }

        ShowTextInputDialog("Код узла", "Введите код узла:", true);
    }

    private void Edge_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (sender is GraphEdgeControl edge)
        {
            ShowEdgeSettingsDialog(edge);
            return;
        }

        ShowTextInputDialog("Предикат дуги", "Введите предикат:", false);
    }

    private void ShowNodeCodeDialog(GraphNodeControl node)
    {
        var dialog = new Window
        {
            Title = "Код узла",
            Width = 500,
            Height = 420,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var textBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 12),
            Text = node.NodeCode,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 230,
            MinLines = 12
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 90,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        okButton.Click += (_, _) =>
        {
            node.NodeCode = textBox.Text;
            dialog.DialogResult = true;
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Введите код узла:", Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(textBox);
        panel.Children.Add(okButton);

        dialog.Content = panel;
        dialog.ShowDialog();
    }

    private void ShowEdgeSettingsDialog(GraphEdgeControl edge)
    {
        var dialog = new Window
        {
            Title = "Параметры дуги",
            Width = 360,
            Height = 230,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var predicateBox = new TextBox
        {
            Text = edge.Predicate.ToString(),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var delayBox = new TextBox
        {
            Text = edge.DelaySeconds.ToString(),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 90,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        okButton.Click += (_, _) =>
        {
            if (int.TryParse(predicateBox.Text, out var predicate))
            {
                edge.Predicate = predicate;
            }

            if (int.TryParse(delayBox.Text, out var delay))
            {
                edge.DelaySeconds = delay;
            }

            edge.UpdateGeometry(GraphCanvas);
            dialog.DialogResult = true;
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Предикат:", Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(predicateBox);
        panel.Children.Add(new TextBlock { Text = "Задержка (сек):", Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(delayBox);
        panel.Children.Add(okButton);

        dialog.Content = panel;
        dialog.ShowDialog();
    }

    private void ShowAnimationSettingsDialog()
    {
        var dialog = new Window
        {
            Title = "Настройки анимации",
            Width = 360,
            Height = 220,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var durationBox = new TextBox
        {
            Text = "20",
            Margin = new Thickness(0, 0, 0, 10)
        };

        var delayBox = new TextBox
        {
            Text = "1000",
            Margin = new Thickness(0, 0, 0, 12)
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 90,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        okButton.Click += (_, _) => dialog.DialogResult = true;

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Длительность (сек):", Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(durationBox);
        panel.Children.Add(new TextBlock { Text = "Задержка между переходами (мс):", Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(delayBox);
        panel.Children.Add(okButton);

        dialog.Content = panel;
        dialog.ShowDialog();
    }

    private void ShowTextInputDialog(string title, string label, bool isMultiLine)
    {
        var dialog = new Window
        {
            Title = title,
            Width = isMultiLine ? 500 : 360,
            Height = isMultiLine ? 420 : 180,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var textBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 12),
            AcceptsReturn = isMultiLine,
            TextWrapping = isMultiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = isMultiLine ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            Height = isMultiLine ? 230 : double.NaN,
            MinLines = isMultiLine ? 12 : 1
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 90,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        okButton.Click += (_, _) => dialog.DialogResult = true;

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(textBox);
        panel.Children.Add(okButton);

        dialog.Content = panel;
        dialog.ShowDialog();
    }
}
