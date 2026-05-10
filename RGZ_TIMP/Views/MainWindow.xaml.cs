using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Linq;
using Microsoft.Win32;

namespace RGZ_TIMP.Views
{
    /// <summary>
    /// Макетный интерфейс без реализации бизнес-логики.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly List<GraphNodeControl> _nodes = new();
        private readonly List<GraphEdgeControl> _edges = new();
        private int _nextEdgeId = 1;
        private int _nextNodeNumber = 1;
        private Point _lastRightClickPosition;
        private readonly Random _random = new();
        private DispatcherTimer? _animationTimer;
        private bool _isAnimating;
        private DateTime _animationEndTime;
        private GraphNodeControl? _currentAnimationNode;
        private GraphNodeControl? _pendingAnimationNode;
        private int _animationDurationSeconds = 20;
        private int _defaultEdgeDelaySeconds = 2;
        private string? _currentProjectPath;
        private bool _isProjectLoaded;
        private bool _isStartHighlighting;
        private bool _isWaitingForTransition;
        private TimeSpan _pendingDelay;

        private GraphNodeControl? _movingNode;
        private Vector _moveOffset;

        private GraphNodeControl? _lineSourceNode;

        public MainWindow()
        {
            InitializeComponent();

            GraphCanvas.MouseMove += GraphCanvas_MouseMove;
            GraphCanvas.MouseLeftButtonUp += GraphCanvas_MouseLeftButtonUp;
            GraphCanvas.MouseRightButtonDown += GraphCanvas_MouseRightButtonDown;
            Loaded += MainWindow_Loaded;
            PreviewKeyDown += MainWindow_PreviewKeyChanged;
            PreviewKeyUp += MainWindow_PreviewKeyChanged;

            _nextNodeNumber = _nodes.Count == 0 ? 1 : _nodes.Max(node => node.NodeNumber) + 1;
            SetProjectState(false);
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

        private void UpdateOppositeOffsets(GraphNodeControl from, GraphNodeControl to)
        {
            if (ReferenceEquals(from, to))
            {
                return;
            }

            var forward = _edges.FirstOrDefault(edge => ReferenceEquals(edge.From, from) && ReferenceEquals(edge.To, to));
            var backward = _edges.FirstOrDefault(edge => ReferenceEquals(edge.From, to) && ReferenceEquals(edge.To, from));

            if (forward is null && backward is null)
            {
                return;
            }

            if (forward is not null && backward is not null)
            {
                forward.UpdateParallelOffset(14);
                backward.UpdateParallelOffset(14);
            }
            else if (forward is not null)
            {
                forward.UpdateParallelOffset(0);
            }
            else if (backward is not null)
            {
                backward.UpdateParallelOffset(0);
            }

            forward?.UpdateGeometry(GraphCanvas);
            backward?.UpdateGeometry(GraphCanvas);
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
            if (_edges.Any(edge => ReferenceEquals(edge.From, from) && ReferenceEquals(edge.To, to)))
            {
                return;
            }

            var localOrder = _edges.Count(x => ReferenceEquals(x.From, from)) + 1;
            var edge = new GraphEdgeControl(_nextEdgeId++, localOrder, from, to, parallelOffset);
            edge.EdgeDoubleClicked += Edge_MouseDoubleClick;
            edge.DelaySeconds = _defaultEdgeDelaySeconds;

            _edges.Add(edge);
            GraphCanvas.Children.Add(edge.Line);
            GraphCanvas.Children.Add(edge.Arrow);

            Panel.SetZIndex(edge.Line, 5);
            Panel.SetZIndex(edge.Arrow, 6);

            edge.UpdateGeometry(GraphCanvas);
            UpdateNodeToolTips();
            UpdateOutgoingPredicates(from);
            UpdateOppositeOffsets(from, to);
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
            TryAddEdgeFromSource(e.GetPosition(GraphCanvas));
            StopPreviewLine();
        }

        private void GraphCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _lastRightClickPosition = e.GetPosition(GraphCanvas);
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
            TryAddEdgeFromSource(e.GetPosition(GraphCanvas));
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

        private void TryAddEdgeFromSource(Point position)
        {
            if (_lineSourceNode is null)
            {
                return;
            }

            var targetNode = FindNodeAt(position);
            if (targetNode is null)
            {
                return;
            }

            var parallelOffset = GetParallelOffset(_lineSourceNode, targetNode);
            AddEdge(_lineSourceNode, targetNode, parallelOffset);
        }

        private GraphNodeControl? FindNodeAt(Point position)
        {
            for (var index = _nodes.Count - 1; index >= 0; index--)
            {
                var node = _nodes[index];
                var left = Canvas.GetLeft(node);
                var top = Canvas.GetTop(node);
                var bounds = new Rect(left, top, node.Width, node.Height);
                if (bounds.Contains(position))
                {
                    return node;
                }
            }

            return null;
        }

        private double GetParallelOffset(GraphNodeControl from, GraphNodeControl to)
        {
            if (ReferenceEquals(from, to))
            {
                return 0;
            }

            var hasOpposite = _edges.Any(edge => ReferenceEquals(edge.From, to) && ReferenceEquals(edge.To, from));
            if (hasOpposite)
            {
                return 8;
            }

            var existing = _edges.Count(edge => ReferenceEquals(edge.From, from) && ReferenceEquals(edge.To, to));
            if (existing == 0)
            {
                return 0;
            }

            var direction = existing % 2 == 1 ? 1 : -1;
            var magnitude = 12 * ((existing + 1) / 2);
            return direction * magnitude;
        }

        private void Node_DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem)
            {
                return;
            }

            if (menuItem.Parent is not ContextMenu contextMenu)
            {
                return;
            }

            if (contextMenu.PlacementTarget is not GraphNodeControl node)
            {
                return;
            }

            var edgesToRemove = _edges.Where(edge => ReferenceEquals(edge.From, node) || ReferenceEquals(edge.To, node)).ToList();
            foreach (var edge in edgesToRemove)
            {
                _edges.Remove(edge);
                GraphCanvas.Children.Remove(edge.Line);
                GraphCanvas.Children.Remove(edge.Arrow);
                UpdateOutgoingPredicates(edge.From);
                UpdateOppositeOffsets(edge.From, edge.To);
            }

            _nodes.Remove(node);
            GraphCanvas.Children.Remove(node);
            UpdateNodeToolTips();
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

        private void UpdateOutgoingPredicates(GraphNodeControl node)
        {
            var outgoing = _edges.Where(edge => ReferenceEquals(edge.From, node)).OrderBy(edge => edge.EdgeId).ToList();
            for (var index = 0; index < outgoing.Count; index++)
            {
                var localOrder = index + 1;
                var edge = outgoing[index];
                edge.UpdateLocalOrder(localOrder);
                edge.UpdatePredicate(localOrder);
            }

            UpdateNodeToolTips();
        }

        private void CreateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                FileName = "graph.xml"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            _currentProjectPath = dialog.FileName;
            CreateNewProject();
            SaveProject(_currentProjectPath);
        }

        private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            _currentProjectPath = dialog.FileName;
            LoadProject(_currentProjectPath);
        }

        private void SaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isProjectLoaded)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentProjectPath))
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                    FileName = "graph.xml"
                };

                if (dialog.ShowDialog(this) != true)
                {
                    return;
                }

                _currentProjectPath = dialog.FileName;
            }

            SaveProject(_currentProjectPath);
        }

        private void AnimationSettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowAnimationSettingsDialog();
        }

        private void RunMenuItem_Click(object sender, RoutedEventArgs e)
        {
            StartAnimation();
        }

        private void StopMenuItem_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
        }

        private void HelpMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, "Графопостроитель ориентированного графа для конечного автомата.", "Справка", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AddNodeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isProjectLoaded)
            {
                return;
            }

            var node = AddNode(_nextNodeNumber++, _lastRightClickPosition.X, _lastRightClickPosition.Y);
            UpdateNodeToolTips();
        }

        private void DeleteSelectionMenuItem_Click(object sender, RoutedEventArgs e)
        {
        }

        private void CreateNewProject()
        {
            StopAnimation();
            ClearGraph();
            _nextNodeNumber = 1;
            _nextEdgeId = 1;
            SetProjectState(true);
        }

        private void ClearGraph()
        {
            foreach (var edge in _edges.ToList())
            {
                GraphCanvas.Children.Remove(edge.Line);
                GraphCanvas.Children.Remove(edge.Arrow);
            }

            foreach (var node in _nodes.ToList())
            {
                GraphCanvas.Children.Remove(node);
            }

            _edges.Clear();
            _nodes.Clear();
        }

        private void SaveProject(string path)
        {
            var project = CreateProjectSnapshot();
            var serializer = new XmlSerializer(typeof(GraphProjectData));
            using var stream = File.Create(path);
            serializer.Serialize(stream, project);
        }

        private void LoadProject(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            var serializer = new XmlSerializer(typeof(GraphProjectData));
            using var stream = File.OpenRead(path);
            if (serializer.Deserialize(stream) is not GraphProjectData project)
            {
                return;
            }

            StopAnimation();
            ClearGraph();

            foreach (var nodeData in project.Nodes)
            {
                var node = AddNode(nodeData.Number, nodeData.CenterX, nodeData.CenterY);
                node.NodeCode = nodeData.NodeCode ?? node.NodeCode;
            }

            _nextNodeNumber = _nodes.Count == 0 ? 1 : _nodes.Max(node => node.NodeNumber) + 1;
            var maxEdgeId = project.Edges.Count == 0 ? 0 : project.Edges.Max(edge => edge.EdgeId);
            _nextEdgeId = Math.Max(project.NextEdgeId, maxEdgeId + 1);
            _animationDurationSeconds = project.AnimationDurationSeconds > 0 ? project.AnimationDurationSeconds : _animationDurationSeconds;
            _defaultEdgeDelaySeconds = project.DefaultEdgeDelaySeconds > 0 ? project.DefaultEdgeDelaySeconds : _defaultEdgeDelaySeconds;

            foreach (var edgeData in project.Edges)
            {
                var from = _nodes.FirstOrDefault(node => node.NodeNumber == edgeData.FromNodeNumber);
                var to = _nodes.FirstOrDefault(node => node.NodeNumber == edgeData.ToNodeNumber);
                if (from is null || to is null)
                {
                    continue;
                }

                var edge = new GraphEdgeControl(edgeData.EdgeId, edgeData.LocalOrder, from, to, edgeData.ParallelOffset);
                edge.UpdatePredicate(edgeData.Predicate);
                edge.DelaySeconds = edgeData.DelaySeconds;
                edge.EdgeDoubleClicked += Edge_MouseDoubleClick;
                _edges.Add(edge);
                GraphCanvas.Children.Add(edge.Line);
                GraphCanvas.Children.Add(edge.Arrow);
                Panel.SetZIndex(edge.Line, 5);
                Panel.SetZIndex(edge.Arrow, 6);
            }

            GraphCanvas.UpdateLayout();
            UpdateNodeToolTips();
            foreach (var node in _nodes)
            {
                UpdateOutgoingPredicates(node);
            }

            foreach (var edge in _edges)
            {
                UpdateOppositeOffsets(edge.From, edge.To);
            }

            UpdateAllEdges();
            SetProjectState(true);
        }

        private GraphProjectData CreateProjectSnapshot()
        {
            var project = new GraphProjectData
            {
                AnimationDurationSeconds = _animationDurationSeconds,
                DefaultEdgeDelaySeconds = _defaultEdgeDelaySeconds,
                NextEdgeId = _nextEdgeId,
                Nodes = _nodes
                    .Select(node => new GraphNodeData
                    {
                        Number = node.NodeNumber,
                        CenterX = node.GetCenterOn(GraphCanvas).X,
                        CenterY = node.GetCenterOn(GraphCanvas).Y,
                        NodeCode = node.NodeCode
                    })
                    .ToList(),
                Edges = _edges
                    .Select(edge => new GraphEdgeData
                    {
                        EdgeId = edge.EdgeId,
                        LocalOrder = edge.LocalOrder,
                        FromNodeNumber = edge.From.NodeNumber,
                        ToNodeNumber = edge.To.NodeNumber,
                        Predicate = edge.Predicate,
                        DelaySeconds = edge.DelaySeconds,
                        ParallelOffset = edge.ParallelOffset
                    })
                    .ToList()
            };

            return project;
        }

        private void SetProjectState(bool isLoaded)
        {
            _isProjectLoaded = isLoaded;
            GraphCanvas.IsEnabled = isLoaded;
            SaveMenuItem.IsEnabled = isLoaded;
            AnimationSettingsMenuItem.IsEnabled = isLoaded;
            RunMenuItem.IsEnabled = isLoaded;
            StopMenuItem.IsEnabled = isLoaded;
            ModeTextBlock.Text = isLoaded ? "Редактирование" : "Проект не создан";
        }

        [Serializable]
        public sealed class GraphProjectData
        {
            public int AnimationDurationSeconds { get; set; }
            public int DefaultEdgeDelaySeconds { get; set; }
            public int NextEdgeId { get; set; }
            public List<GraphNodeData> Nodes { get; set; } = new();
            public List<GraphEdgeData> Edges { get; set; } = new();
        }

        [Serializable]
        public sealed class GraphNodeData
        {
            public int Number { get; set; }
            public double CenterX { get; set; }
            public double CenterY { get; set; }
            public string? NodeCode { get; set; }
        }

        [Serializable]
        public sealed class GraphEdgeData
        {
            public int EdgeId { get; set; }
            public int LocalOrder { get; set; }
            public int FromNodeNumber { get; set; }
            public int ToNodeNumber { get; set; }
            public int Predicate { get; set; }
            public int DelaySeconds { get; set; }
            public double ParallelOffset { get; set; }
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
                    edge.UpdatePredicate(predicate);
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
                Text = _animationDurationSeconds.ToString(),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var delayBox = new TextBox
            {
                Text = (_defaultEdgeDelaySeconds * 1000).ToString(),
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
                if (int.TryParse(durationBox.Text, out var duration) && duration > 0)
                {
                    _animationDurationSeconds = duration;
                }

                if (int.TryParse(delayBox.Text, out var delayMs) && delayMs >= 0)
                {
                    _defaultEdgeDelaySeconds = Math.Max(0, (int)Math.Ceiling(delayMs / 1000d));
                }

                dialog.DialogResult = true;
            };

            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock { Text = "Длительность (сек):", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(durationBox);
            panel.Children.Add(new TextBlock { Text = "Задержка между переходами (мс):", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(delayBox);
            panel.Children.Add(okButton);

            dialog.Content = panel;
            dialog.ShowDialog();
        }

        private void StartAnimation()
        {
            if (_isAnimating)
            {
                return;
            }

            var startNode = _nodes.FirstOrDefault(node => node.NodeNumber == 1);
            if (startNode is null)
            {
                return;
            }

            _isAnimating = true;
            ModeTextBlock.Text = "Анимация";
            _animationEndTime = DateTime.Now.AddSeconds(_animationDurationSeconds);
            _currentAnimationNode = startNode;
            _pendingAnimationNode = null;
            _isStartHighlighting = false;
            _isWaitingForTransition = false;

            EnsureAnimationTimer();
            RunAnimationStep();
        }

        private void StopAnimation()
        {
            if (!_isAnimating)
            {
                return;
            }

            _isAnimating = false;
            _animationTimer?.Stop();
            _currentAnimationNode?.SetHighlighted(false);
            _currentAnimationNode?.SetStartHighlighted(false);
            _currentAnimationNode = null;
            _pendingAnimationNode = null;
            _isStartHighlighting = false;
            _isWaitingForTransition = false;
            ModeTextBlock.Text = "Редактирование";
        }

        private void EnsureAnimationTimer()
        {
            if (_animationTimer is not null)
            {
                return;
            }

            _animationTimer = new DispatcherTimer();
            _animationTimer.Tick += AnimationTimer_Tick;
        }

        private void RunAnimationStep()
        {
            if (!_isAnimating || _currentAnimationNode is null)
            {
                return;
            }

            if (DateTime.Now >= _animationEndTime)
            {
                StopAnimation();
                return;
            }

            _currentAnimationNode.SetStartHighlighted(true);
            _isStartHighlighting = true;

            var outgoing = _edges
                .Where(edge => ReferenceEquals(edge.From, _currentAnimationNode))
                .OrderBy(edge => edge.Predicate)
                .ToList();

            if (outgoing.Count == 0)
            {
                StopAnimation();
                return;
            }

            var predicate = EvaluateNodeCode(_currentAnimationNode.NodeCode, outgoing.Count);
            var selected = outgoing.FirstOrDefault(edge => edge.Predicate == predicate) ?? outgoing[0];

            _pendingAnimationNode = selected.To;
            _pendingDelay = TimeSpan.FromSeconds(Math.Max(0, selected.DelaySeconds));
            _animationTimer!.Interval = TimeSpan.FromMilliseconds(250);
            _animationTimer.Start();
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            _animationTimer?.Stop();

            if (!_isAnimating || _currentAnimationNode is null)
            {
                return;
            }

            if (_isStartHighlighting)
            {
                _currentAnimationNode.SetStartHighlighted(false);
                _isStartHighlighting = false;
                _currentAnimationNode.SetHighlighted(true);
                _isWaitingForTransition = true;
                _animationTimer!.Interval = _pendingDelay;
                _animationTimer.Start();
                return;
            }

            if (_isWaitingForTransition)
            {
                _currentAnimationNode.SetHighlighted(false);
                _currentAnimationNode = _pendingAnimationNode ?? _currentAnimationNode;
                _pendingAnimationNode = null;
                _isWaitingForTransition = false;
                RunAnimationStep();
            }
        }

        private int EvaluateNodeCode(string code, int outgoingCount)
        {
            _ = code;
            return _random.Next(1, outgoingCount + 1);
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
}