using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SimForge.Core;

namespace SimForge;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _simulationTimer;
    private readonly DispatcherTimer _replacementConfirmationTimer;
    private readonly Graph _graph = new();
    private readonly Dictionary<Border, EditorComponent> _addedComponents = new();
    private readonly List<VisualConnection> _visualConnections = new();
    private readonly List<PaletteEntry> _paletteEntries = new();
    private readonly List<CategoryEntry> _categoryEntries = new();

    private double _timeSeconds;
    private bool _isSimulationRunning;
    private bool _hasShortCircuit;
    private bool _isUpdatingInspector;
    private readonly Dictionary<int, DigitalOutputProfile> _sketchOutputProfiles = new();
    private readonly List<ConditionalOutputRule> _conditionalOutputRules = new();
    private readonly Dictionary<int, double> _pinPhaseElapsedSeconds = new();
    private readonly Dictionary<int, bool> _digitalPinStates = new();
    private readonly HashSet<string> _sketchInputPins = new(StringComparer.OrdinalIgnoreCase);
    private ArduinoSketchProgram? _lastSketchAnalysis;
    private Border? _selectedComponent;
    private VisualConnection? _selectedConnection;
    private Border? _connectionStart;
    private Border? _draggedComponent;
    private Point _dragOffset;
    private int _addedComponentCount;
    private WorkspaceReplacementAction _pendingWorkspaceReplacement;
    private Button? _pendingReplacementButton;
    private object? _pendingReplacementButtonContent;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureAccessibility();
        PopulateComponentLibrary();

        _simulationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _simulationTimer.Tick += SimulationTimer_Tick;
        _replacementConfirmationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _replacementConfirmationTimer.Tick += (_, _) => ResetWorkspaceReplacementConfirmation(true);

        ComponentSearchBox.TextChanged += ComponentSearchBox_TextChanged;
        CodeEditorTextBox.TextChanged += CodeEditorTextBox_TextChanged;
        WorkspaceScrollViewer.SizeChanged += WorkspaceScrollViewer_SizeChanged;
        KeyDown += MainWindow_KeyDown;
        SetDefaultCode();
        UpdateWorkspaceUi();
        ShowWorkspaceInspector();
    }

    private void ConfigureAccessibility()
    {
        AutomationProperties.SetName(ComponentSearchBox, "Search components");
        AutomationProperties.SetHelpText(ComponentSearchBox, "Filter the component library by name, type, or capability.");
        AutomationProperties.SetName(CodeEditorTextBox, "Arduino C++ sketch editor");
        AutomationProperties.SetHelpText(CodeEditorTextBox,
            "Edit setup and loop. SimForge supports pin constants, reads, writes, delays, and simple sensor if/else rules.");
        ToolTip.SetTip(CodeEditorTextBox,
            "Simulation subset: pin constants, digitalWrite, analogRead, digitalRead, pulseIn, DHT reads, and simple if/else.");
        AutomationProperties.SetName(LedColorComboBox, "LED emitter color");
        AutomationProperties.SetName(SignalValueSlider, "Component input value");
        AutomationProperties.SetName(GraphCanvas, "Circuit design canvas");
        AutomationProperties.SetHelpText(GraphCanvas, "Contains movable circuit components and their wire connections.");
        AutomationProperties.SetLiveSetting(StatusText, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(HintText, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(CodeStatusText, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(AssistantSummaryText, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(ShortCircuitBadge, AutomationLiveSetting.Assertive);
    }

    private void SetDefaultCode()
    {
        CodeEditorTextBox.Text =
            "void setup() {\n" +
            "  pinMode(13, OUTPUT);\n" +
            "}\n\n" +
            "void loop() {\n" +
            "  digitalWrite(13, HIGH);\n" +
            "  delay(1000);\n" +
            "  digitalWrite(13, LOW);\n" +
            "  delay(1000);\n" +
            "}";
    }

    private void PopulateComponentLibrary()
    {
        ComponentLibraryPanel.Children.Clear();
        _paletteEntries.Clear();
        _categoryEntries.Clear();

        foreach (var category in ComponentCatalog.GroupBy(component => component.Category))
        {
            var stack = new StackPanel { Margin = new Thickness(0, 5, 0, 3) };
            var categoryEntry = new CategoryEntry(category.Key);

            foreach (var info in category)
            {
                var button = CreatePaletteButton(info);
                stack.Children.Add(button);
                var entry = new PaletteEntry(button, info);
                _paletteEntries.Add(entry);
                categoryEntry.PaletteEntries.Add(entry);
            }

            var expander = new Expander
            {
                Header = category.Key.ToUpperInvariant(),
                IsExpanded = true,
                Content = stack,
                Foreground = Brush("#91A5BC"),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(2),
                Margin = new Thickness(0, 0, 0, 4)
            };

            categoryEntry.Expander = expander;
            _categoryEntries.Add(categoryEntry);
            ComponentLibraryPanel.Children.Add(expander);
        }
    }

    private Button CreatePaletteButton(ComponentInfo info)
    {
        var icon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(info.Name == "LED" ? 17 : 9),
            Background = Brush(info.Surface),
            BorderBrush = Brush(WithAlpha(info.Accent, "66")),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = info.Symbol,
                FontSize = info.Symbol.Length > 3 ? 7 : 9,
                FontWeight = FontWeight.Bold,
                Foreground = Brush(info.Accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var labels = new StackPanel
        {
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = info.Name,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush("#D9E5F2")
                },
                new TextBlock
                {
                    Text = info.Summary,
                    FontSize = 9,
                    Foreground = Brush("#6E849C"),
                    Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(40)));
        content.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        content.Children.Add(icon);
        Grid.SetColumn(labels, 1);
        content.Children.Add(labels);

        var button = new Button { Tag = info.Name, Content = content };
        button.Classes.Add("palette");
        button.Click += PaletteButton_Click;
        return button;
    }

    private void ComponentSearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = ComponentSearchBox.Text?.Trim() ?? string.Empty;
        var visibleCount = 0;

        foreach (var entry in _paletteEntries)
        {
            var isMatch = query.Length == 0 ||
                          entry.Info.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          entry.Info.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          entry.Info.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          entry.Info.Summary.Contains(query, StringComparison.OrdinalIgnoreCase);
            entry.Button.IsVisible = isMatch;
            if (isMatch)
                visibleCount++;
        }

        foreach (var category in _categoryEntries)
        {
            var hasVisibleChildren = category.PaletteEntries.Any(entry => entry.Button.IsVisible);
            category.Expander.IsVisible = hasVisibleChildren;
            if (query.Length > 0 && hasVisibleChildren)
                category.Expander.IsExpanded = true;
        }

        LibraryCountText.Text = visibleCount.ToString();
        SearchEmptyState.IsVisible = visibleCount == 0;
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) && e.Key == Key.K)
        {
            ComponentSearchBox.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _connectionStart is not null)
        {
            CancelConnectionMode("Connection cancelled.");
            e.Handled = true;
            return;
        }

        if (e.Source is TextBox)
            return;

        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            RemoveSelectedElement();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            if (_isSimulationRunning)
                StopSimulation();
            else
                StartSimulation();
            e.Handled = true;
        }
    }

    private void RunButton_Click(object? sender, RoutedEventArgs e) => StartSimulation();

    private void StopButton_Click(object? sender, RoutedEventArgs e) => StopSimulation();

    private void StartSimulation()
    {
        if (!AnalyzeSketch())
        {
            StatusText.Text = "Code issue";
            HintText.Text = "Fix the reported sketch structure issue before starting the simulation.";
            FooterText.Text = "SimForge 0.7.0 · Simulation blocked by sketch diagnostics";
            return;
        }

        CheckShortCircuit();
        var circuitReady = EvaluateCircuitState();
        if (_hasShortCircuit)
        {
            StatusText.Text = "Safety lock";
            HintText.Text = "Simulation blocked: add a current-limiting resistor to the unsafe LED path.";
            FooterText.Text = "SimForge 0.7.0 · Simulation blocked by electrical safety";
            return;
        }

        if (!circuitReady)
        {
            StatusText.Text = "Circuit incomplete";
            HintText.Text = "Complete an LED path or wire a powered sensor signal to a matching controller input.";
            FooterText.Text = "SimForge 0.7.0 · Complete the circuit before running";
            return;
        }

        _isSimulationRunning = true;
        _simulationTimer.Start();
        RunButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        SimulationPulse.Fill = Brush("#3DE19F");
        SimulationStateText.Text = "RUNNING";
        SimulationStateText.Foreground = Brush("#61DCAD");
        LedLight.Fill = Brush("#31D79A");
        ArduinoStatus.Text = "Simulation live";
        ArduinoStatus.Foreground = Brush("#79DBB9");
        StatusText.Text = "Running";
        ApplyConditionalOutputs();
        HintText.Text = BuildSimulationHint();
        FooterText.Text = "SimForge 0.7.0 · Live simulation";
        EvaluateCircuitState();
    }

    private void StopSimulation()
    {
        _simulationTimer.Stop();
        _isSimulationRunning = false;
        RunButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        SimulationPulse.Fill = Brush("#E8A84E");
        SimulationStateText.Text = "PAUSED";
        SimulationStateText.Foreground = Brush("#D9A85F");
        LedLight.Fill = Brush("#6C5937");
        ArduinoStatus.Text = "Simulation paused";
        ArduinoStatus.Foreground = Brush("#C2A26D");
        StatusText.Text = "Paused";
        FooterText.Text = "SimForge 0.7.0 · Simulation paused";
        EvaluateCircuitState();
    }

    private void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        ResetSimulationState();
        HintText.Text = "Simulation state reset. Your circuit and sketch were preserved.";
        FooterText.Text = "SimForge 0.7.0 · Simulation reset";
    }

    private void ResetSimulationState()
    {
        _simulationTimer.Stop();
        _isSimulationRunning = false;
        _timeSeconds = 0;
        foreach (var (pinNumber, profile) in _sketchOutputProfiles)
        {
            _digitalPinStates[pinNumber] = profile.InitialState;
            _pinPhaseElapsedSeconds[pinNumber] = 0;
        }
        TimeText.Text = "0.000 s";
        RunButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        SimulationPulse.Fill = Brush("#496173");
        SimulationStateText.Text = "IDLE";
        SimulationStateText.Foreground = Brush("#71899F");
        LedLight.Fill = Brush("#345E50");
        ArduinoStatus.Text = "System ready";
        ArduinoStatus.Foreground = Brush("#95B8AD");
        StatusText.Text = "Ready";
        CheckShortCircuit();
        EvaluateCircuitState();
    }

    private void PaletteButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string component })
            AddComponent(component);
    }

    private Border AddComponent(string componentName, Point? position = null, bool select = true)
    {
        var info = GetComponentInfo(componentName);
        var model = CreateEditorNode(componentName);
        var visualParts = CreateComponentVisual(info, model);
        var node = visualParts.Node;

        var usableWidth = WorkspaceSurface.Width > 500 ? WorkspaceSurface.Width : 760;
        var columns = Math.Max(1, (int)((usableWidth - 60) / 214));
        var column = _addedComponentCount % columns;
        var row = _addedComponentCount / columns;
        var defaultPosition = new Point(46 + (column * 214), 44 + (row * 136));
        var targetPosition = position ?? defaultPosition;
        EnsureWorkspaceSize(targetPosition.X + node.Width + 36, targetPosition.Y + node.Height + 36);

        Canvas.SetLeft(node, Math.Max(10, targetPosition.X));
        Canvas.SetTop(node, Math.Max(10, targetPosition.Y));
        GraphCanvas.Children.Add(node);
        _graph.AddNode(model);

        var editorComponent = new EditorComponent(info, model, visualParts.Indicator, visualParts.StateButton)
        {
            LedColor = "Red"
        };
        _addedComponents.Add(node, editorComponent);
        UpdateSensorReading(editorComponent);
        _addedComponentCount++;

        if (select)
            SelectComponent(node);

        FooterText.Text = $"{componentName} added to the workspace";
        HintText.Text = $"{componentName} is ready. Select Connect to wire it to another component.";
        CheckShortCircuit();
        EvaluateCircuitState();
        UpdateWorkspaceUi();
        return node;
    }

    private NodeVisualParts CreateComponentVisual(ComponentInfo info, EditorNode model)
    {
        Ellipse? indicator = null;
        Button? stateButton = null;

        var node = new Border
        {
            Width = 184,
            Height = 112,
            Padding = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            ClipToBounds = false,
            Focusable = true
        };
        node.Classes.Add("circuitNode");
        AutomationProperties.SetName(node, $"{info.Name} circuit component");
        AutomationProperties.SetHelpText(node, "Press Enter to select or connect. Use the arrow keys to move the component.");
        ToolTip.SetTip(node, $"{info.Name} · {info.Summary}");

        var root = new Grid { ClipToBounds = false };
        root.RowDefinitions.Add(new RowDefinition(new GridLength(31)));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(24)));

        var header = new Border
        {
            Background = Brush(info.Surface),
            BorderBrush = Brush("#24364B"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(11, 11, 0, 0),
            Padding = new Thickness(10, 0)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        headerGrid.Children.Add(new TextBlock
        {
            Text = info.Name,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#E6EEF8"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var categoryLabel = new TextBlock
        {
            Text = info.CategoryLabel,
            FontSize = 8,
            FontWeight = FontWeight.Bold,
            Foreground = Brush(info.Accent),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(categoryLabel, 1);
        headerGrid.Children.Add(categoryLabel);
        header.Child = headerGrid;
        root.Children.Add(header);

        var body = new Grid { Margin = new Thickness(10, 7, 10, 5) };
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(40)));
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        Control primaryVisual;
        if (info.Name == "LED")
        {
            indicator = new Ellipse
            {
                Width = 28,
                Height = 28,
                Fill = Brush("#4A1F2A"),
                Stroke = Brush(info.Accent),
                StrokeThickness = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            primaryVisual = indicator;
        }
        else
        {
            primaryVisual = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(info.Name is "Potentiometer" ? 17 : 9),
                Background = Brush(info.Surface),
                BorderBrush = Brush(WithAlpha(info.Accent, "77")),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = info.Symbol,
                    FontSize = info.Symbol.Length > 3 ? 7 : 9,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brush(info.Accent),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }
        body.Children.Add(primaryVisual);

        if (IsSwitch(info.Name))
        {
            stateButton = new Button
            {
                Content = "OPEN",
                Height = 26,
                MinWidth = 70,
                Padding = new Thickness(9, 0),
                CornerRadius = new CornerRadius(7),
                Background = Brush("#342129"),
                BorderBrush = Brush("#653441"),
                BorderThickness = new Thickness(1),
                Foreground = Brush("#F29AAA"),
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 0, 0)
            };
            stateButton.Click += (_, eventArgs) =>
            {
                if (_addedComponents.TryGetValue(node, out var component))
                {
                    component.State = !component.State;
                    component.Model.SwitchState = component.State;
                    UpdateSwitchVisual(component);
                    CheckShortCircuit();
                    EvaluateCircuitState();
                    if (!_hasShortCircuit)
                        HintText.Text = $"{component.Info.Name} is now {(component.State ? "closed" : "open")}.";
                    eventArgs.Handled = true;
                }
            };
            Grid.SetColumn(stateButton, 1);
            body.Children.Add(stateButton);
        }
        else
        {
            var summary = new TextBlock
            {
                Text = info.Summary,
                FontSize = 9,
                LineHeight = 13,
                Foreground = Brush("#91A4B8"),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 39,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 0, 0)
            };
            Grid.SetColumn(summary, 1);
            body.Children.Add(summary);
        }
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new Border
        {
            Background = Brush("#0D1621"),
            CornerRadius = new CornerRadius(0, 0, 11, 11),
            Padding = new Thickness(10, 0)
        };
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        footerGrid.Children.Add(new TextBlock
        {
            Text = $"{model.Pins.Count} pin{(model.Pins.Count == 1 ? string.Empty : "s")}",
            FontSize = 9,
            Foreground = Brush("#7189A2"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var signalText = new TextBlock
        {
            Text = info.SignalLabel,
            FontSize = 9,
            Foreground = Brush("#7E95AD"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(signalText, 1);
        footerGrid.Children.Add(signalText);
        footer.Child = footerGrid;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        AddPinVisuals(root, model);

        node.Child = root;
        node.PointerPressed += Component_PointerPressed;
        node.PointerMoved += Component_PointerMoved;
        node.PointerReleased += Component_PointerReleased;
        node.KeyDown += Component_KeyDown;
        return new NodeVisualParts(node, indicator, stateButton);
    }

    private static Ellipse CreatePinVisual(NodePin? pin)
    {
        var color = pin is null ? "#6F849A" : pin.SignalType switch
        {
            PinSignalType.Ground => "#8C9AAB",
            PinSignalType.Power => "#FF6C82",
            PinSignalType.Digital => "#5B9BFF",
            PinSignalType.Analog => "#38D0A0",
            PinSignalType.Electrical => "#F1C975",
            _ => "#B593FF"
        };

        return new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = Brush("#08101A"),
            Stroke = Brush(color),
            StrokeThickness = 2,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
    }

    private static void AddPinVisuals(Grid root, EditorNode model)
    {
        var pinLayer = new Canvas
        {
            Width = 184,
            Height = 112,
            ClipToBounds = false,
            IsHitTestVisible = false
        };

        foreach (var pin in model.Pins)
        {
            var appearsOnRight = PinAppearsOnRight(pin);
            var pinCenterY = 56 + GetPinVerticalOffset(pin);
            var marker = CreatePinVisual(pin);
            Canvas.SetLeft(marker, appearsOnRight ? 179 : -5);
            Canvas.SetTop(marker, pinCenterY - 5);
            pinLayer.Children.Add(marker);

            var label = new TextBlock
            {
                Text = pin.Name,
                Width = 46,
                FontSize = 8,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#8AA0B8"),
                TextAlignment = appearsOnRight ? TextAlignment.Right : TextAlignment.Left
            };
            Canvas.SetLeft(label, appearsOnRight ? 130 : 8);
            Canvas.SetTop(label, pinCenterY - 5);
            pinLayer.Children.Add(label);
        }

        Grid.SetRowSpan(pinLayer, 3);
        root.Children.Add(pinLayer);
    }

    private static bool PinAppearsOnRight(NodePin pin)
    {
        if (pin.Direction == PinDirection.Output)
            return true;
        if (pin.Direction == PinDirection.Input)
            return false;

        var pinIndex = pin.Owner.Pins.ToList().IndexOf(pin);
        return pinIndex >= pin.Owner.Pins.Count / 2d;
    }

    private static double GetPinVerticalOffset(NodePin pin)
    {
        var pinIndex = Math.Max(0, pin.Owner.Pins.ToList().IndexOf(pin));
        return (pinIndex - ((pin.Owner.Pins.Count - 1) / 2d)) * 13;
    }

    private void Component_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border node || !_addedComponents.ContainsKey(node))
            return;

        if (_connectionStart is not null && !ReferenceEquals(_connectionStart, node))
        {
            CreateConnection(_connectionStart, node);
            e.Handled = true;
            return;
        }

        node.Focus();
        SelectComponent(node);
        _draggedComponent = node;
        var pointerPosition = e.GetPosition(GraphCanvas);
        _dragOffset = new Point(pointerPosition.X - Canvas.GetLeft(node), pointerPosition.Y - Canvas.GetTop(node));
        e.Pointer.Capture(node);
        e.Handled = true;
    }

    private void Component_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Border node || !ReferenceEquals(node, _draggedComponent) ||
            !e.GetCurrentPoint(node).Properties.IsLeftButtonPressed)
            return;

        var pointerPosition = e.GetPosition(GraphCanvas);
        var nodeWidth = node.Bounds.Width > 0 ? node.Bounds.Width : node.Width;
        var nodeHeight = node.Bounds.Height > 0 ? node.Bounds.Height : node.Height;
        var maxX = Math.Max(10, WorkspaceSurface.Width - nodeWidth - 10);
        var maxY = Math.Max(10, WorkspaceSurface.Height - nodeHeight - 10);
        Canvas.SetLeft(node, Math.Clamp(pointerPosition.X - _dragOffset.X, 10, maxX));
        Canvas.SetTop(node, Math.Clamp(pointerPosition.Y - _dragOffset.Y, 10, maxY));
        UpdateConnectionLines(node);
    }

    private void Component_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Border node || !ReferenceEquals(node, _draggedComponent))
            return;

        e.Pointer.Capture(null);
        _draggedComponent = null;
        FooterText.Text = $"{_addedComponents[node].Info.Name} moved · X {Canvas.GetLeft(node):0} · Y {Canvas.GetTop(node):0}";
    }

    private void Component_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Border node || !_addedComponents.ContainsKey(node))
            return;

        if (e.Key == Key.Enter)
        {
            if (_connectionStart is not null && !ReferenceEquals(_connectionStart, node))
                CreateConnection(_connectionStart, node);
            else
                SelectComponent(node);
            e.Handled = true;
            return;
        }

        var delta = e.Key switch
        {
            Key.Left => new Point(-10, 0),
            Key.Right => new Point(10, 0),
            Key.Up => new Point(0, -10),
            Key.Down => new Point(0, 10),
            _ => default
        };
        if (delta == default)
            return;

        var nodeWidth = node.Bounds.Width > 0 ? node.Bounds.Width : node.Width;
        var nodeHeight = node.Bounds.Height > 0 ? node.Bounds.Height : node.Height;
        var maxX = Math.Max(10, WorkspaceSurface.Width - nodeWidth - 10);
        var maxY = Math.Max(10, WorkspaceSurface.Height - nodeHeight - 10);
        Canvas.SetLeft(node, Math.Clamp(Canvas.GetLeft(node) + delta.X, 10, maxX));
        Canvas.SetTop(node, Math.Clamp(Canvas.GetTop(node) + delta.Y, 10, maxY));
        UpdateConnectionLines(node);
        SelectComponent(node);
        FooterText.Text = $"{_addedComponents[node].Info.Name} moved with keyboard";
        e.Handled = true;
    }

    private void ClearSelection()
    {
        if (_selectedComponent is not null)
        {
            _selectedComponent.Classes.Remove("selected");
            _selectedComponent = null;
        }

        if (_selectedConnection is not null)
        {
            _selectedConnection.Line.Stroke = Brush("#4E8FFF");
            _selectedConnection.Line.StrokeThickness = 4;
            _selectedConnection = null;
        }

        RemoveButton.IsEnabled = false;
        LedColorPanel.IsVisible = false;
        SignalValuePanel.IsVisible = false;
    }

    private void SelectComponent(Border node)
    {
        ClearSelection();
        _selectedComponent = node;
        node.Classes.Add("selected");
        RemoveButton.IsEnabled = true;

        var component = _addedComponents[node];
        var info = component.Info;
        SelectedElementKindText.Text = info.CategoryLabel;
        SelectedElementTitle.Text = info.Name;
        SelectedElementDescription.Text = info.Description;
        SelectedElementSymbol.Text = info.Symbol;
        SelectedElementSymbol.Foreground = Brush(info.Accent);
        InspectorSymbolBorder.Background = Brush(info.Surface);
        InspectorSymbolBorder.BorderBrush = Brush(WithAlpha(info.Accent, "77"));
        GeneralInfoText.Text = info.Summary;
        PinsText.Text = string.Join("  ·  ", component.Model.Pins.Select(pin => $"{pin.Name} [{pin.SignalType}]").ToArray());
        ParametersText.Text = BuildComponentParameterSummary(component);
        TutorialText.Text = info.Tutorial;
        HintText.Text = $"{info.Name} selected. Inspect its pins, parameters, or start a connection.";
        StatusText.Text = "Selected";

        _isUpdatingInspector = true;
        if (info.Name == "LED")
        {
            LedColorPanel.IsVisible = true;
            var selectedIndex = component.LedColor switch
            {
                "Green" => 1,
                "Blue" => 2,
                "Amber" => 3,
                "White" => 4,
                _ => 0
            };
            LedColorComboBox.SelectedIndex = selectedIndex;
        }

        if (info.HasAdjustableValue)
        {
            SignalValuePanel.IsVisible = true;
            SignalValueLabel.Text = info.ValueLabel;
            AutomationProperties.SetName(SignalValueSlider, info.ValueLabel);
            SignalValueSlider.Minimum = info.ValueMin;
            SignalValueSlider.Maximum = info.ValueMax;
            SignalValueSlider.Value = component.Model.ComponentValue;
            UpdateSignalValueText(info, component.Model.ComponentValue);
        }
        _isUpdatingInspector = false;
        UpdateCircuitAssistant();
    }

    private void SelectConnection(VisualConnection visualConnection)
    {
        ClearSelection();
        _selectedConnection = visualConnection;
        visualConnection.Line.Stroke = Brush("#55D7C8");
        visualConnection.Line.StrokeThickness = 6;
        RemoveButton.IsEnabled = true;

        var firstName = _addedComponents[visualConnection.First].Info.Name;
        var secondName = _addedComponents[visualConnection.Second].Info.Name;
        SelectedElementKindText.Text = "WIRE";
        SelectedElementTitle.Text = "Signal connection";
        SelectedElementDescription.Text = $"A validated connection between {firstName} and {secondName}.";
        SelectedElementSymbol.Text = "NET";
        SelectedElementSymbol.Foreground = Brush("#74ADFF");
        InspectorSymbolBorder.Background = Brush("#172B43");
        InspectorSymbolBorder.BorderBrush = Brush("#3C6B9D");
        GeneralInfoText.Text = "Ideal conductor used to carry power or data between compatible terminals.";
        PinsText.Text = $"{firstName}.{visualConnection.Model.From.Name}  ↔  {secondName}.{visualConnection.Model.To.Name}";
        ParametersText.Text = "Resistance: 0 Ω · Propagation: instantaneous";
        TutorialText.Text = "Select the wire and press Delete to remove it. Routing updates automatically when nodes move.";
        HintText.Text = "This wire passed SimForge signal compatibility checks.";
        StatusText.Text = "Wire selected";
        UpdateCircuitAssistant();
    }

    private void ShowWorkspaceInspector()
    {
        SelectedElementKindText.Text = "WORKSPACE";
        SelectedElementTitle.Text = "Circuit workspace";
        SelectedElementDescription.Text = "Add components from the library to start designing and simulating.";
        SelectedElementSymbol.Text = "SF";
        SelectedElementSymbol.Foreground = Brush("#78ADFF");
        InspectorSymbolBorder.Background = Brush("#152942");
        InspectorSymbolBorder.BorderBrush = Brush("#2C527A");
        GeneralInfoText.Text = _addedComponents.Count == 0
            ? "The canvas is ready for a new design. Load the starter circuit for a complete working example."
            : $"{_addedComponents.Count} components and {_visualConnections.Count} wires are currently in the design.";
        PinsText.Text = "No pins selected";
        ParametersText.Text = "Select a component to inspect its electrical values.";
        TutorialText.Text = "Select a node, choose Connect, then click a compatible target. Press Escape to cancel connection mode.";
        LedColorPanel.IsVisible = false;
        SignalValuePanel.IsVisible = false;
        UpdateCircuitAssistant();
    }

    private void RemoveButton_Click(object? sender, RoutedEventArgs e) => RemoveSelectedElement();

    private void RemoveSelectedElement()
    {
        if (_selectedComponent is not null)
        {
            var target = _selectedComponent;
            var component = _addedComponents[target];

            foreach (var connection in _visualConnections
                         .Where(item => ReferenceEquals(item.First, target) || ReferenceEquals(item.Second, target))
                         .ToList())
            {
                GraphCanvas.Children.Remove(connection.Line);
                _visualConnections.Remove(connection);
                _graph.RemoveConnection(connection.Model);
            }

            GraphCanvas.Children.Remove(target);
            _addedComponents.Remove(target);
            _graph.RemoveNode(component.Model);
            if (ReferenceEquals(_connectionStart, target))
                CancelConnectionMode();

            ClearSelection();
            ShowWorkspaceInspector();
            HintText.Text = $"{component.Info.Name} and its attached wires were removed.";
            FooterText.Text = $"{component.Info.Name} removed";
        }
        else if (_selectedConnection is not null)
        {
            var target = _selectedConnection;
            GraphCanvas.Children.Remove(target.Line);
            _visualConnections.Remove(target);
            _graph.RemoveConnection(target.Model);
            ClearSelection();
            ShowWorkspaceInspector();
            HintText.Text = "Wire removed. The remaining circuit was re-evaluated.";
            FooterText.Text = "Wire removed";
        }
        else
        {
            return;
        }

        CheckShortCircuit();
        EvaluateCircuitState();
        UpdateWorkspaceUi();
    }

    private void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_connectionStart is not null)
        {
            CancelConnectionMode("Connection cancelled.");
            return;
        }

        if (_selectedComponent is null)
        {
            HintText.Text = "Select the component where the new connection should begin.";
            StatusText.Text = "Select a node";
            return;
        }

        _connectionStart = _selectedComponent;
        var sourceName = _addedComponents[_connectionStart].Info.Name;
        ConnectButton.Content = "Cancel connection";
        ConnectionModeBadge.IsVisible = true;
        ConnectionModeText.Text = $"From {sourceName} · choose a target";
        StatusText.Text = "Connecting";
        HintText.Text = "Click a second component. SimForge will choose the safest compatible pin pair.";
        _connectionStart.Focus();
    }

    private void CancelConnectionMode(string? message = null, bool preserveStatus = false)
    {
        _connectionStart = null;
        ConnectButton.Content = "⌁  Connect";
        ConnectionModeBadge.IsVisible = false;
        if (!string.IsNullOrWhiteSpace(message))
            HintText.Text = message;
        if (!preserveStatus)
            StatusText.Text = _isSimulationRunning ? "Running" : "Ready";
    }

    private VisualConnection? CreateConnection(Border first, Border second, bool silent = false)
    {
        try
        {
            var firstComponent = _addedComponents[first];
            var secondComponent = _addedComponents[second];
            var (from, to) = FindBestPinPair(firstComponent, secondComponent);
            var connectionModel = _graph.Connect(from, to);
            var line = new Line
            {
                Stroke = Brush("#4E8FFF"),
                StrokeThickness = 4,
                StrokeLineCap = PenLineCap.Round,
                Cursor = new Cursor(StandardCursorType.Hand),
                Focusable = true
            };
            AutomationProperties.SetName(line, $"Wire from {firstComponent.Info.Name} to {secondComponent.Info.Name}");
            AutomationProperties.SetHelpText(line, "Press Delete to remove this wire.");

            var visualConnection = new VisualConnection(first, second, line, connectionModel);
            line.PointerPressed += (_, eventArgs) =>
            {
                SelectConnection(visualConnection);
                line.Focus();
                eventArgs.Handled = true;
            };
            line.GotFocus += (_, _) => SelectConnection(visualConnection);

            GraphCanvas.Children.Insert(0, line);
            _visualConnections.Add(visualConnection);
            UpdateConnectionLine(visualConnection);

            if (!silent)
            {
                SelectConnection(visualConnection);
                FooterText.Text = $"{firstComponent.Info.Name}.{from.Name} connected to {secondComponent.Info.Name}.{to.Name}";
                HintText.Text = "Connection created and validated.";
            }

            CheckShortCircuit();
            EvaluateCircuitState();
            UpdateWorkspaceUi();
            return visualConnection;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            HintText.Text = exception.Message;
            StatusText.Text = "Connection blocked";
            return null;
        }
        finally
        {
            CancelConnectionMode(preserveStatus: true);
        }
    }

    private (NodePin From, NodePin To) FindBestPinPair(EditorComponent first, EditorComponent second)
    {
        var candidates = new List<(NodePin From, NodePin To, int Score)>();
        foreach (var from in first.Model.Pins)
        {
            foreach (var to in second.Model.Pins)
            {
                if (!Connection.AreCompatible(from, to))
                    continue;

                var score = ScorePinPair(first, from, second, to);
                candidates.Add((from, to, score));
            }
        }

        if (candidates.Count == 0)
            throw new ArgumentException($"No compatible pins were found between {first.Info.Name} and {second.Info.Name}.");

        var best = candidates.OrderByDescending(candidate => candidate.Score).First();
        return (best.From, best.To);
    }

    private int ScorePinPair(EditorComponent first, NodePin from, EditorComponent second, NodePin to)
    {
        var score = from.SignalType == to.SignalType ? 20 : 5;
        if (from.Direction == PinDirection.Output && to.Direction == PinDirection.Input)
            score += 12;
        if (from.Direction == PinDirection.Input && to.Direction == PinDirection.Output)
            score += 10;
        if (first.Info.Name == "GND" || second.Info.Name == "GND")
            score += from.SignalType == PinSignalType.Ground && to.SignalType == PinSignalType.Ground ? 100 : -100;
        if ((first.Info.Name == "LED" && from.Name == "Cathode") ||
            (second.Info.Name == "LED" && to.Name == "Cathode"))
            score += first.Info.Name == "GND" || second.Info.Name == "GND" ? 80 : -15;
        if (first.Model.Kind == NodeKind.Microcontroller && from.Name == "D13" &&
            to.SignalType != PinSignalType.Power)
            score += second.Info.Name == "GND" ? -30 : 30;
        if (second.Model.Kind == NodeKind.Microcontroller && to.Name == "D13" &&
            from.SignalType != PinSignalType.Power)
            score += first.Info.Name == "GND" ? -30 : 30;
        if (first.Model.Kind == NodeKind.Microcontroller && second.Model.Kind == NodeKind.Sensor)
            score += ScoreSketchAwareControllerPin(from, to);
        if (second.Model.Kind == NodeKind.Microcontroller && first.Model.Kind == NodeKind.Sensor)
            score += ScoreSketchAwareControllerPin(to, from);
        score -= GetPinUseCount(from) * 45;
        score -= GetPinUseCount(to) * 45;
        return score;
    }

    private int ScoreSketchAwareControllerPin(NodePin controllerPin, NodePin sensorPin)
    {
        var score = 0;
        if (sensorPin.Direction == PinDirection.Output && _sketchInputPins.Contains(controllerPin.Name))
            score += 70;
        if (sensorPin.Direction == PinDirection.Input && TryGetDigitalPinNumber(controllerPin, out var pinNumber) &&
            _sketchOutputProfiles.ContainsKey(pinNumber))
            score += 55;
        return score;
    }

    private int GetPinUseCount(NodePin pin) => _visualConnections.Count(connection =>
        ReferenceEquals(connection.Model.From, pin) || ReferenceEquals(connection.Model.To, pin));

    private void CheckShortCircuit()
    {
        var sourcePins = _addedComponents.Values
            .Where(component => component.Model.Kind == NodeKind.Microcontroller)
            .SelectMany(component => component.Model.Pins)
            .Where(pin => pin.Direction is PinDirection.Output or PinDirection.Bidirectional &&
                          pin.SignalType is PinSignalType.Digital or PinSignalType.Power)
            .ToList();
        var groundPins = GetGroundReferencePins().ToList();
        var leds = _addedComponents.Values.Where(component => component.Info.Name == "LED").ToList();

        _hasShortCircuit = leds.Any(led =>
        {
            var anode = led.Model.Pins.FirstOrDefault(pin => pin.Name == "Anode");
            var cathode = led.Model.Pins.FirstOrDefault(pin => pin.Name == "Cathode");
            if (anode is null || cathode is null)
                return false;

            var hasUnprotectedSourcePath = sourcePins.Any(source =>
                HasPinPath(source, anode, PathLimiterRequirement.Forbidden));
            var hasGroundReturn = groundPins.Any(ground =>
                HasPinPath(cathode, ground, PathLimiterRequirement.Any));
            return hasUnprotectedSourcePath && hasGroundReturn;
        });

        ShortCircuitBadge.IsVisible = _hasShortCircuit;
        if (_hasShortCircuit)
        {
            ShortCircuitStateText.Text = "Unsafe";
            ShortCircuitStateText.Foreground = Brush("#FF718B");
            HintText.Text = "An LED is connected without current limiting. Add a series resistor before running.";
            if (_isSimulationRunning)
                ApplySafetyLock();
        }
        else
        {
            ShortCircuitStateText.Text = "Safe";
            ShortCircuitStateText.Foreground = Brush("#5BD39E");
        }
    }

    private void ApplySafetyLock()
    {
        _simulationTimer.Stop();
        _isSimulationRunning = false;
        RunButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        SimulationPulse.Fill = Brush("#FF607D");
        SimulationStateText.Text = "LOCKED";
        SimulationStateText.Foreground = Brush("#FF8EA4");
        LedLight.Fill = Brush("#5B202D");
        ArduinoStatus.Text = "Output disabled";
        ArduinoStatus.Foreground = Brush("#FF9AAE");
        StatusText.Text = "Safety lock";
        HintText.Text = "Simulation stopped immediately because the circuit became unsafe. Correct the LED path before running again.";
        FooterText.Text = "SimForge 0.7.0 · Emergency safety stop";
    }

    private bool EvaluateCircuitState()
    {
        var circuitReady = false;
        var anyLedOn = false;
        var drivenPins = GetDrivenPins().ToList();
        var groundPins = GetGroundReferencePins().ToList();
        var leds = _addedComponents.Values.Where(component => component.Info.Name == "LED").ToList();
        var onlineSensors = GetOnlineSensors().ToList();
        circuitReady |= onlineSensors.Count > 0;

        foreach (var led in leds)
        {
            var anode = led.Model.Pins.FirstOrDefault(pin => pin.Name == "Anode");
            var cathode = led.Model.Pins.FirstOrDefault(pin => pin.Name == "Cathode");
            if (anode is null || cathode is null)
                continue;

            var candidateSources = drivenPins
                .Where(source => HasPinPath(source.Pin, anode, PathLimiterRequirement.Required))
                .ToList();
            var hasLimitedSourcePath = candidateSources.Count > 0;
            var hasGroundReturn = groundPins.Any(ground =>
                HasPinPath(cathode, ground, PathLimiterRequirement.Any));
            var isPowered = hasLimitedSourcePath && hasGroundReturn && !_hasShortCircuit;
            circuitReady |= isPowered;
            var isOn = isPowered && _isSimulationRunning && candidateSources.Any(source => source.IsHigh);
            anyLedOn |= isOn;
            UpdateLedVisual(led, isOn);
        }

        if (_addedComponents.Count == 0)
        {
            CircuitStateText.Text = "Awaiting components";
            CircuitStateText.Foreground = Brush("#7FA6CF");
        }
        else if (_hasShortCircuit)
        {
            CircuitStateText.Text = "Unsafe topology";
            CircuitStateText.Foreground = Brush("#FF718B");
        }
        else if (circuitReady)
        {
            CircuitStateText.Text = _isSimulationRunning
                ? anyLedOn
                    ? "Live · output high"
                    : onlineSensors.Count > 0
                        ? $"Live · {onlineSensors.Count} sensor{(onlineSensors.Count == 1 ? string.Empty : "s")} online"
                        : "Live · output low"
                : onlineSensors.Count > 0
                    ? $"Ready · {onlineSensors.Count} sensor{(onlineSensors.Count == 1 ? string.Empty : "s")} online"
                    : "Closed · ready";
            CircuitStateText.Foreground = Brush("#5BD39E");
        }
        else if (_visualConnections.Count == 0)
        {
            CircuitStateText.Text = "Unwired";
            CircuitStateText.Foreground = Brush("#D2A25C");
        }
        else
        {
            CircuitStateText.Text = "Incomplete loop";
            CircuitStateText.Foreground = Brush("#D2A25C");
        }
        UpdateCircuitAssistant(circuitReady);
        return circuitReady;
    }

    private void UpdateCircuitAssistant(bool? knownCircuitReady = null)
    {
        var sensors = _addedComponents.Values.Where(component => component.Info.HasAdjustableValue).ToList();
        var unpoweredSensors = sensors
            .Where(sensor => !IsSensorPowered(sensor))
            .Select(sensor => sensor.Info.Name)
            .Distinct()
            .ToList();
        var unwiredSensors = sensors
            .Where(sensor => IsSensorPowered(sensor) && !IsSensorSignalConnected(sensor))
            .Select(sensor => sensor.Info.Name)
            .Distinct()
            .ToList();
        var missingInputPins = _sketchInputPins
            .Where(pin => !IsControllerInputConnected(pin))
            .OrderBy(pin => pin)
            .ToList();
        var missingOutputPins = (_lastSketchAnalysis?.Outputs.Keys ?? Enumerable.Empty<int>())
            .Where(pin => !IsControllerOutputConnected(pin))
            .OrderBy(pin => pin)
            .ToList();
        var leds = _addedComponents.Values.Where(component => component.Info.Name == "LED").ToList();
        var incompleteLedCount = leds.Count(led => !IsLedPathComplete(led));
        var selectedSensorName = _selectedComponent is not null &&
                                 _addedComponents.TryGetValue(_selectedComponent, out var selected) &&
                                 selected.Info.HasAdjustableValue
            ? selected.Info.Name
            : sensors.FirstOrDefault()?.Info.Name;
        var circuitReady = knownCircuitReady ??
                           (sensors.Any(sensor => IsSensorPowered(sensor) && IsSensorSignalConnected(sensor)) ||
                            leds.Any(IsLedPathComplete));

        var report = CircuitAssistant.Analyze(new CircuitAssistantInput(
            _addedComponents.Count,
            _addedComponents.Values.Any(component => component.Model.Kind == NodeKind.Microcontroller),
            leds.Count > 0,
            circuitReady,
            _hasShortCircuit,
            incompleteLedCount,
            unpoweredSensors,
            unwiredSensors,
            missingInputPins,
            missingOutputPins,
            _lastSketchAnalysis?.IsValid ?? false,
            _lastSketchAnalysis?.Diagnostic ?? "No supported Arduino I/O",
            selectedSensorName));
        RenderCircuitAssistant(report);
    }

    private bool IsLedPathComplete(EditorComponent led)
    {
        var anode = led.Model.Pins.FirstOrDefault(pin => pin.Name == "Anode");
        var cathode = led.Model.Pins.FirstOrDefault(pin => pin.Name == "Cathode");
        if (anode is null || cathode is null || _hasShortCircuit)
            return false;

        var hasProtectedSource = GetDrivenPins().Any(source =>
            HasPinPath(source.Pin, anode, PathLimiterRequirement.Required));
        var hasGroundReturn = GetGroundReferencePins().Any(ground =>
            HasPinPath(cathode, ground, PathLimiterRequirement.Any));
        return hasProtectedSource && hasGroundReturn;
    }

    private bool IsControllerInputConnected(string pinName)
    {
        var inputs = _addedComponents.Values
            .Where(component => component.Model.Kind == NodeKind.Microcontroller)
            .SelectMany(component => component.Model.Pins)
            .Where(pin => pin.Name.Equals(pinName, StringComparison.OrdinalIgnoreCase) &&
                          pin.Direction is PinDirection.Input or PinDirection.Bidirectional)
            .ToList();
        return _addedComponents.Values
            .Where(component => component.Info.HasAdjustableValue)
            .SelectMany(component => component.Model.Pins)
            .Where(pin => pin.Direction == PinDirection.Output)
            .Any(output => inputs.Any(input => HasPinPath(output, input, PathLimiterRequirement.Any)));
    }

    private bool IsControllerOutputConnected(int pinNumber)
    {
        var pinName = $"D{pinNumber}";
        var outputs = _addedComponents.Values
            .Where(component => component.Model.Kind == NodeKind.Microcontroller)
            .SelectMany(component => component.Model.Pins)
            .Where(pin => pin.Name.Equals(pinName, StringComparison.OrdinalIgnoreCase) &&
                          pin.Direction is PinDirection.Output or PinDirection.Bidirectional)
            .ToList();
        return outputs.Any(output => _visualConnections.Any(connection => connection.Model.IsConnectedTo(output)));
    }

    private void RenderCircuitAssistant(CircuitAssistantReport report)
    {
        AssistantStatusText.Text = report.Status;
        AssistantSummaryText.Text = report.Summary;
        var (foreground, background) = report.Status switch
        {
            "READY" => ("#6DE0B0", "#17382E"),
            "UNSAFE" or "FIX" => ("#FF9AAE", "#3A1D29"),
            "CHECK" => ("#F0C06A", "#3A2D19"),
            _ => ("#7FB4E8", "#172C42")
        };
        AssistantStatusText.Foreground = Brush(foreground);
        AssistantStatusBadge.Background = Brush(background);

        if (report.Issues.Count == 0)
        {
            AssistantIssuesText.Text = "✓ No blocking circuit issues detected.";
            AssistantIssuesText.Foreground = Brush("#7BCDAC");
        }
        else
        {
            const int visibleIssueLimit = 4;
            var issueLines = report.Issues.Take(visibleIssueLimit)
                .Select((issue, index) => $"{index + 1}. {issue.Title} — {issue.Instruction}")
                .ToList();
            if (report.Issues.Count > visibleIssueLimit)
                issueLines.Add($"+ {report.Issues.Count - visibleIssueLimit} more issue(s)");
            AssistantIssuesText.Text = string.Join(Environment.NewLine, issueLines);
            AssistantIssuesText.Foreground = Brush(report.Issues.Any(issue => issue.Severity == GuidanceSeverity.Error)
                ? "#E7A4B1"
                : "#C8B889");
        }

        AssistantCodePanel.IsVisible = !string.IsNullOrWhiteSpace(report.CodeExample);
        AssistantCodeText.Text = report.CodeExample ?? string.Empty;
    }

    private IEnumerable<DrivenPin> GetDrivenPins()
    {
        foreach (var component in _addedComponents.Values.Where(component =>
                     component.Model.Kind == NodeKind.Microcontroller))
        {
            foreach (var pin in component.Model.Pins.Where(pin =>
                         pin.Direction is PinDirection.Output or PinDirection.Bidirectional))
            {
                if (pin.SignalType == PinSignalType.Power)
                {
                    yield return new DrivenPin(pin, true);
                    continue;
                }

                if (pin.SignalType == PinSignalType.Digital && TryGetDigitalPinNumber(pin, out var pinNumber) &&
                    _digitalPinStates.TryGetValue(pinNumber, out var isHigh))
                    yield return new DrivenPin(pin, isHigh);
            }
        }

    }

    private IEnumerable<EditorComponent> GetOnlineSensors()
    {
        foreach (var sensor in _addedComponents.Values.Where(component => component.Info.HasAdjustableValue))
        {
            UpdateSensorReading(sensor);
            if (IsSensorPowered(sensor) && IsSensorSignalConnected(sensor))
                yield return sensor;
        }
    }

    private bool IsSensorSignalConnected(EditorComponent sensor)
    {
        var controllerInputs = _addedComponents.Values
            .Where(component => component.Model.Kind == NodeKind.Microcontroller)
            .SelectMany(component => component.Model.Pins)
            .Where(pin => pin.Direction is PinDirection.Input or PinDirection.Bidirectional)
            .ToList();
        var hasDataPath = sensor.Model.Pins
            .Where(pin => pin.Direction == PinDirection.Output)
            .Any(output => controllerInputs.Any(input => HasPinPath(output, input, PathLimiterRequirement.Any)));
        if (!hasDataPath)
            return false;

        if (sensor.Info.Name != "HC-SR04 Distance")
            return true;

        var trigger = sensor.Model.Pins.FirstOrDefault(pin => pin.Name == "TRIG");
        if (trigger is null)
            return false;

        return GetDrivenPins().Any(source =>
            source.Pin.SignalType == PinSignalType.Digital &&
            HasPinPath(source.Pin, trigger, PathLimiterRequirement.Any));
    }

    private bool IsSensorPowered(EditorComponent sensor)
    {
        var supplyPin = sensor.Model.Pins.FirstOrDefault(pin => pin.SignalType == PinSignalType.Power);
        var groundPin = sensor.Model.Pins.FirstOrDefault(pin => pin.SignalType == PinSignalType.Ground);
        if (supplyPin is null || groundPin is null)
            return false;

        var powerSources = _addedComponents.Values
            .Where(component => component.Model.Kind == NodeKind.Microcontroller)
            .SelectMany(component => component.Model.Pins)
            .Where(pin => pin.Direction == PinDirection.Output && pin.SignalType == PinSignalType.Power);
        var hasSupply = powerSources.Any(source =>
            HasPinPath(source, supplyPin, PathLimiterRequirement.Any));
        var hasGround = GetGroundReferencePins().Any(reference =>
            HasPinPath(groundPin, reference, PathLimiterRequirement.Any));
        return hasSupply && hasGround;
    }

    private bool ApplyConditionalOutputs()
    {
        var changed = false;
        foreach (var rule in _conditionalOutputRules)
        {
            var next = TryReadSensorInput(rule.Condition, out var inputValue)
                ? rule.Evaluate(inputValue)
                : rule.FalseState;
            var previous = _digitalPinStates.GetValueOrDefault(rule.OutputPin);
            if (previous == next)
                continue;

            _digitalPinStates[rule.OutputPin] = next;
            changed = true;
        }

        return changed;
    }

    private bool TryReadSensorInput(ArduinoInputCondition condition, out double value)
    {
        var controllerPins = _addedComponents.Values
            .Where(component => component.Model.Kind == NodeKind.Microcontroller)
            .SelectMany(component => component.Model.Pins)
            .Where(pin => pin.Name.Equals(condition.Pin, StringComparison.OrdinalIgnoreCase) &&
                          pin.Direction is PinDirection.Input or PinDirection.Bidirectional)
            .ToList();

        foreach (var sensor in _addedComponents.Values.Where(component => component.Info.HasAdjustableValue))
        {
            if (!IsSensorPowered(sensor) || !SensorCanProvide(sensor.Info.Name, condition.Kind))
                continue;

            var hasDataPath = sensor.Model.Pins
                .Where(pin => pin.Direction == PinDirection.Output)
                .Any(output => controllerPins.Any(input => HasPinPath(output, input, PathLimiterRequirement.Any)));
            if (!hasDataPath ||
                (condition.Kind is ArduinoInputKind.PulseDurationMicroseconds or ArduinoInputKind.DistanceCentimeters &&
                 !IsSensorSignalConnected(sensor)))
                continue;

            UpdateSensorReading(sensor);
            value = condition.Kind switch
            {
                ArduinoInputKind.Analog when sensor.Info.Name == "LDR Sensor" =>
                    SensorSimulation.ReadPhotoresistor(sensor.Model.ComponentValue).AdcValue,
                ArduinoInputKind.Analog => SensorSimulation.ReadPotentiometer(sensor.Model.ComponentValue).AdcValue,
                ArduinoInputKind.PulseDurationMicroseconds =>
                    SensorSimulation.ReadHcSr04(sensor.Model.ComponentValue).EchoDurationMicroseconds,
                ArduinoInputKind.DistanceCentimeters => sensor.Model.ComponentValue,
                ArduinoInputKind.TemperatureCelsius =>
                    SensorSimulation.ReadDht11(sensor.Model.ComponentValue).TemperatureCelsius,
                ArduinoInputKind.RelativeHumidity =>
                    SensorSimulation.ReadDht11(sensor.Model.ComponentValue).HumidityPercent,
                ArduinoInputKind.Digital => sensor.Model.Pins
                    .Where(pin => pin.Direction == PinDirection.Output)
                    .Select(pin => pin.Value)
                    .FirstOrDefault() > 0 ? 1 : 0,
                _ => 0
            };
            return true;
        }

        value = 0;
        return false;
    }

    private static bool SensorCanProvide(string sensorName, ArduinoInputKind inputKind) => inputKind switch
    {
        ArduinoInputKind.Analog => sensorName is "LDR Sensor" or "Potentiometer",
        ArduinoInputKind.PulseDurationMicroseconds => sensorName == "HC-SR04 Distance",
        ArduinoInputKind.DistanceCentimeters => sensorName == "HC-SR04 Distance",
        ArduinoInputKind.TemperatureCelsius or ArduinoInputKind.RelativeHumidity => sensorName == "DHT11 Temperature",
        ArduinoInputKind.Digital => sensorName is "HC-SR04 Distance" or "DHT11 Temperature",
        _ => false
    };

    private string? BuildConditionalReactionSummary()
    {
        var reactions = new List<string>();
        foreach (var rule in _conditionalOutputRules)
        {
            if (!TryReadSensorInput(rule.Condition, out var inputValue))
                continue;

            var state = rule.Evaluate(inputValue) ? "HIGH" : "LOW";
            reactions.Add($"{rule.Condition.Pin} {inputValue:0.##} → D{rule.OutputPin} {state}");
        }

        return reactions.Count == 0 ? null : string.Join(" · ", reactions);
    }

    private IEnumerable<NodePin> GetGroundReferencePins() => _addedComponents.Values
        .Where(component => component.Info.Name == "GND" || component.Model.Kind == NodeKind.Microcontroller)
        .SelectMany(component => component.Model.Pins)
        .Where(pin => pin.SignalType == PinSignalType.Ground);

    private static bool TryGetDigitalPinNumber(NodePin pin, out int pinNumber)
    {
        pinNumber = 0;
        return pin.Name.Length > 1 && pin.Name[0] == 'D' && int.TryParse(pin.Name[1..], out pinNumber);
    }

    private bool HasPinPath(NodePin start, NodePin target, PathLimiterRequirement limiterRequirement)
    {
        var queue = new Queue<(NodePin Pin, bool HasLimiter)>();
        var visited = new HashSet<(NodePin, bool)> { (start, false) };
        queue.Enqueue((start, false));

        while (queue.Count > 0)
        {
            var (current, hasLimiter) = queue.Dequeue();
            if (ReferenceEquals(current, target))
            {
                if (limiterRequirement == PathLimiterRequirement.Any ||
                    limiterRequirement == PathLimiterRequirement.Required && hasLimiter ||
                    limiterRequirement == PathLimiterRequirement.Forbidden && !hasLimiter)
                    return true;
            }

            foreach (var edge in GetPinEdges(current))
            {
                var nextHasLimiter = hasLimiter || edge.AddsCurrentLimiter;
                if (!visited.Add((edge.Pin, nextHasLimiter)))
                    continue;
                queue.Enqueue((edge.Pin, nextHasLimiter));
            }
        }
        return false;
    }

    private IEnumerable<PinEdge> GetPinEdges(NodePin pin)
    {
        foreach (var connection in _visualConnections)
        {
            if (ReferenceEquals(connection.Model.From, pin))
                yield return new PinEdge(connection.Model.To, false);
            else if (ReferenceEquals(connection.Model.To, pin))
                yield return new PinEdge(connection.Model.From, false);
        }

        var component = _addedComponents.Values.FirstOrDefault(candidate => ReferenceEquals(candidate.Model, pin.Owner));
        if (component is null)
            yield break;

        var conductsInternally = component.Info.Name == "Resistor" ||
                                 IsSwitch(component.Info.Name) && component.State;
        if (!conductsInternally)
            yield break;

        var addsLimiter = component.Info.Name == "Resistor";
        foreach (var sibling in component.Model.Pins.Where(sibling => !ReferenceEquals(sibling, pin)))
            yield return new PinEdge(sibling, addsLimiter);
    }

    private void LedColorComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingInspector || _selectedComponent is null ||
            !_addedComponents.TryGetValue(_selectedComponent, out var component) || component.Info.Name != "LED")
            return;

        if (LedColorComboBox.SelectedItem is ComboBoxItem { Content: string colorName })
        {
            component.LedColor = colorName;
            EvaluateCircuitState();
            HintText.Text = $"LED emitter color changed to {colorName.ToLowerInvariant()}.";
        }
    }

    private void SignalValueSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingInspector || _selectedComponent is null ||
            !_addedComponents.TryGetValue(_selectedComponent, out var component) || !component.Info.HasAdjustableValue)
            return;

        component.Model.ComponentValue = e.NewValue;
        UpdateSensorReading(component);
        UpdateSignalValueText(component.Info, e.NewValue);
        ParametersText.Text = BuildComponentParameterSummary(component);
        HintText.Text = BuildSensorChangeHint(component.Info, e.NewValue);
        if (_isSimulationRunning)
        {
            ApplyConditionalOutputs();
            var reaction = BuildConditionalReactionSummary();
            if (!string.IsNullOrEmpty(reaction))
                HintText.Text += $" · {reaction}";
        }
        EvaluateCircuitState();
    }

    private void UpdateSignalValueText(ComponentInfo info, double value)
    {
        SignalValueText.Text = $"{value:0.#}{info.ValueUnit}";
    }

    private static string BuildParameterSummary(ComponentInfo info, double value) =>
        info.Name switch
        {
            "LDR Sensor" => BuildPhotoresistorSummary(info, value),
            "Potentiometer" => BuildPotentiometerSummary(info, value),
            "HC-SR04 Distance" => BuildUltrasonicSummary(info, value),
            "DHT11 Temperature" => BuildDht11Summary(info, value),
            _ => info.Parameters
        };

    private string BuildComponentParameterSummary(EditorComponent component)
    {
        var summary = BuildParameterSummary(component.Info, component.Model.ComponentValue);
        if (!component.Info.HasAdjustableValue)
            return summary;

        var circuitState = !IsSensorPowered(component)
            ? "connect VCC and GND"
            : !IsSensorSignalConnected(component)
                ? component.Info.Name == "HC-SR04 Distance"
                    ? "wire TRIG and ECHO to the controller"
                    : "wire the signal to a matching controller input"
                : "powered · signal online";
        return $"{summary}\nCircuit: {circuitState}";
    }

    private static string BuildPhotoresistorSummary(ComponentInfo info, double value)
    {
        var reading = SensorSimulation.ReadPhotoresistor(value);
        return $"{info.Parameters}\nDivider output {reading.Voltage:0.00} V · ADC {reading.AdcValue}/{reading.AdcMaximum}\nLDR ≈ {reading.SourceResistanceOhms / 1_000:0.#} kΩ";
    }

    private static string BuildPotentiometerSummary(ComponentInfo info, double value)
    {
        var reading = SensorSimulation.ReadPotentiometer(value);
        return $"{info.Parameters}\nWiper output {reading.Voltage:0.00} V · ADC {reading.AdcValue}/{reading.AdcMaximum}";
    }

    private static string BuildUltrasonicSummary(ComponentInfo info, double value)
    {
        var reading = SensorSimulation.ReadHcSr04(value);
        return $"{info.Parameters}\nEcho pulse {reading.EchoDurationMicroseconds:0} µs at 20 °C\nRound trip · {reading.SpeedOfSoundMetersPerSecond:0.0} m/s";
    }

    private static string BuildDht11Summary(ComponentInfo info, double value)
    {
        var reading = SensorSimulation.ReadDht11(value);
        return $"{info.Parameters}\nReported {reading.TemperatureCelsius:0} °C · ±{reading.TemperatureAccuracyCelsius:0} °C\nDigital refresh ≥ {reading.MinimumSampleIntervalSeconds:0} s";
    }

    private static string BuildSensorChangeHint(ComponentInfo info, double value) => info.Name switch
    {
        "LDR Sensor" => $"Light updated. Analog input is {SensorSimulation.ReadPhotoresistor(value).AdcValue}/1023.",
        "Potentiometer" => $"Wiper updated. Analog input is {SensorSimulation.ReadPotentiometer(value).Voltage:0.00} V.",
        "HC-SR04 Distance" => $"Target updated. Echo pulse is {SensorSimulation.ReadHcSr04(value).EchoDurationMicroseconds:0} µs.",
        "DHT11 Temperature" => $"Temperature updated. DHT11 reports {SensorSimulation.ReadDht11(value).TemperatureCelsius:0} °C at its next sample.",
        _ => $"{info.ValueLabel} updated to {value:0.#}{info.ValueUnit}."
    };

    private static void UpdateSensorReading(EditorComponent component)
    {
        switch (component.Info.Name)
        {
            case "LDR Sensor":
                component.Model.SetPinSignalValue("OUT", SensorSimulation.ReadPhotoresistor(component.Model.ComponentValue).Voltage);
                break;
            case "Potentiometer":
                component.Model.SetPinSignalValue("WIPER", SensorSimulation.ReadPotentiometer(component.Model.ComponentValue).Voltage);
                break;
            case "HC-SR04 Distance":
                component.Model.SetPinSignalValue("ECHO", SensorSimulation.ReadHcSr04(component.Model.ComponentValue).EchoDurationMicroseconds);
                break;
            case "DHT11 Temperature":
                component.Model.SetPinSignalValue("DATA", SensorSimulation.ReadDht11(component.Model.ComponentValue).TemperatureCelsius);
                break;
        }
    }

    private static void UpdateSwitchVisual(EditorComponent component)
    {
        if (component.StateButton is null)
            return;

        component.StateButton.Content = component.State ? "CLOSED" : "OPEN";
        component.StateButton.Background = Brush(component.State ? "#17382E" : "#342129");
        component.StateButton.BorderBrush = Brush(component.State ? "#2D745B" : "#653441");
        component.StateButton.Foreground = Brush(component.State ? "#6DE0B0" : "#F29AAA");
    }

    private static void UpdateLedVisual(EditorComponent component, bool isOn)
    {
        if (component.Indicator is null)
            return;

        var colorHex = (component.LedColor, isOn) switch
        {
            ("Green", true) => "#38E28E",
            ("Green", false) => "#174F36",
            ("Blue", true) => "#4CA6FF",
            ("Blue", false) => "#183B5B",
            ("Amber", true) => "#FFBE45",
            ("Amber", false) => "#5E481A",
            ("White", true) => "#F4FAFF",
            ("White", false) => "#59636D",
            (_, true) => "#FF5D78",
            _ => "#5B202D"
        };
        component.Indicator.Fill = Brush(colorHex);
    }

    private void UpdateConnectionLines(Border node)
    {
        foreach (var connection in _visualConnections.Where(connection =>
                     ReferenceEquals(connection.First, node) || ReferenceEquals(connection.Second, node)))
            UpdateConnectionLine(connection);
    }

    private static void UpdateConnectionLine(VisualConnection connection)
    {
        connection.Line.StartPoint = GetNodePinEdgePoint(connection.First, connection.Model.From);
        connection.Line.EndPoint = GetNodePinEdgePoint(connection.Second, connection.Model.To);
    }

    private static Point GetNodeCenter(Control node)
    {
        var width = node.Bounds.Width > 0 ? node.Bounds.Width : node.Width;
        var height = node.Bounds.Height > 0 ? node.Bounds.Height : node.Height;
        return new Point(Canvas.GetLeft(node) + (width / 2), Canvas.GetTop(node) + (height / 2));
    }

    private static Point GetNodePinEdgePoint(Control node, NodePin pin)
    {
        var center = GetNodeCenter(node);
        var width = node.Bounds.Width > 0 ? node.Bounds.Width : node.Width;
        var x = PinAppearsOnRight(pin) ? center.X + (width / 2) : center.X - (width / 2);
        return new Point(x, center.Y + GetPinVerticalOffset(pin));
    }

    private static EditorNode CreateEditorNode(string componentName)
    {
        var kind = componentName switch
        {
            "Arduino Uno" or "Arduino Nano" or "ESP32 DevKit" or "Raspberry Pi Pico" or "STM32 Blue Pill" or "ATtiny85" => NodeKind.Microcontroller,
            "LED" => NodeKind.Actuator,
            "LDR Sensor" or "Potentiometer" or "HC-SR04 Distance" or "DHT11 Temperature" => NodeKind.Sensor,
            _ => NodeKind.Electronic
        };
        var node = new EditorNode(componentName, kind)
        {
            ComponentValue = GetComponentInfo(componentName).DefaultValue
        };

        switch (componentName)
        {
            case "Arduino Uno":
            case "Arduino Nano":
            case "ESP32 DevKit":
            case "Raspberry Pi Pico":
            case "STM32 Blue Pill":
            case "ATtiny85":
                node.AddTerminal("D2", PinDirection.Input, PinSignalType.Digital);
                node.AddTerminal("A0", PinDirection.Input, PinSignalType.Analog);
                node.AddTerminal("D7", PinDirection.Bidirectional, PinSignalType.Digital);
                node.AddTerminal("D13", PinDirection.Output, PinSignalType.Digital);
                node.AddTerminal("5V", PinDirection.Output, PinSignalType.Power);
                node.AddTerminal("GND", PinDirection.Passive, PinSignalType.Ground);
                break;
            case "LED":
                node.AddTerminal("Anode", PinDirection.Input, PinSignalType.Digital);
                node.AddTerminal("Cathode", PinDirection.Passive, PinSignalType.Ground);
                break;
            case "GND":
                node.AddTerminal("GND", PinDirection.Passive, PinSignalType.Ground);
                break;
            case "Button":
            case "Toggle Switch":
            case "Slide Switch":
                node.AddTerminal("IN", PinDirection.Bidirectional, PinSignalType.Electrical);
                node.AddTerminal("OUT", PinDirection.Bidirectional, PinSignalType.Electrical);
                break;
            case "LDR Sensor":
                node.AddTerminal("VCC", PinDirection.Input, PinSignalType.Power);
                node.AddTerminal("OUT", PinDirection.Output, PinSignalType.Analog);
                node.AddTerminal("GND", PinDirection.Passive, PinSignalType.Ground);
                break;
            case "Potentiometer":
                node.AddTerminal("VCC", PinDirection.Input, PinSignalType.Power);
                node.AddTerminal("WIPER", PinDirection.Output, PinSignalType.Analog);
                node.AddTerminal("GND", PinDirection.Passive, PinSignalType.Ground);
                break;
            case "HC-SR04 Distance":
                node.AddTerminal("VCC", PinDirection.Input, PinSignalType.Power);
                node.AddTerminal("TRIG", PinDirection.Input, PinSignalType.Digital);
                node.AddTerminal("ECHO", PinDirection.Output, PinSignalType.Digital);
                node.AddTerminal("GND", PinDirection.Passive, PinSignalType.Ground);
                break;
            case "DHT11 Temperature":
                node.AddTerminal("VCC", PinDirection.Input, PinSignalType.Power);
                node.AddTerminal("DATA", PinDirection.Output, PinSignalType.Digital);
                node.AddTerminal("GND", PinDirection.Passive, PinSignalType.Ground);
                break;
            default:
                node.AddTerminal("A", PinDirection.Passive, PinSignalType.Electrical);
                node.AddTerminal("B", PinDirection.Passive, PinSignalType.Electrical);
                break;
        }
        return node;
    }

    private void SimulationTimer_Tick(object? sender, EventArgs e)
    {
        const double deltaSeconds = 0.05;
        _timeSeconds += deltaSeconds;
        TimeText.Text = $"{_timeSeconds:0.000} s";
        var simulationContext = new SimulationContext(_timeSeconds);
        foreach (var component in _addedComponents.Values)
            component.Model.Step(simulationContext, deltaSeconds);

        var changed = false;
        foreach (var (pinNumber, profile) in _sketchOutputProfiles)
        {
            var previous = _digitalPinStates.GetValueOrDefault(pinNumber);
            var next = profile.Mode switch
            {
                DigitalOutputMode.High => true,
                DigitalOutputMode.Low => false,
                _ => previous
            };

            if (profile.Mode == DigitalOutputMode.Blink)
            {
                var elapsed = _pinPhaseElapsedSeconds.GetValueOrDefault(pinNumber) + deltaSeconds;
                var phaseDuration = previous ? profile.HighDurationSeconds : profile.LowDurationSeconds;
                while (elapsed >= phaseDuration)
                {
                    elapsed -= phaseDuration;
                    next = !next;
                    phaseDuration = next ? profile.HighDurationSeconds : profile.LowDurationSeconds;
                }
                _pinPhaseElapsedSeconds[pinNumber] = elapsed;
            }

            if (previous != next)
            {
                _digitalPinStates[pinNumber] = next;
                changed = true;
            }
        }

        if (ApplyConditionalOutputs())
            changed = true;

        if (changed)
            EvaluateCircuitState();
    }

    private bool AnalyzeSketch()
    {
        var code = CodeEditorTextBox.Text ?? string.Empty;
        var lines = Math.Max(1, code.Count(character => character == '\n') + 1);
        CodeMetricsText.Text = $"{lines} line{(lines == 1 ? string.Empty : "s")}";
        CodeLineNumbersText.Text = string.Join(Environment.NewLine, Enumerable.Range(1, lines));
        var editorHeight = Math.Max(188, (lines * 16) + 20);
        CodeEditorSurface.Height = editorHeight;
        CodeEditorTextBox.Height = editorHeight;

        var analysis = ArduinoSketchProgram.Analyze(code);
        _lastSketchAnalysis = analysis;
        _sketchOutputProfiles.Clear();
        _conditionalOutputRules.Clear();
        _pinPhaseElapsedSeconds.Clear();
        _digitalPinStates.Clear();
        _sketchInputPins.Clear();
        foreach (var (pinNumber, profile) in analysis.OutputProfiles)
        {
            _sketchOutputProfiles[pinNumber] = profile;
            _pinPhaseElapsedSeconds[pinNumber] = 0;
            _digitalPinStates[pinNumber] = profile.InitialState;
        }
        foreach (var pin in analysis.InputPins)
            _sketchInputPins.Add(pin);
        _conditionalOutputRules.AddRange(analysis.ConditionalOutputs);

        if (analysis.IsValid)
        {
            CodeStatusDot.Fill = Brush("#46D39A");
            CodeStatusText.Text = analysis.Diagnostic;
            CodeStatusText.Foreground = Brush("#7DDBB8");
        }
        else
        {
            CodeStatusDot.Fill = Brush("#FF718B");
            CodeStatusText.Text = analysis.Diagnostic;
            CodeStatusText.Foreground = Brush("#FF9AAE");
        }
        UpdateCircuitAssistant();
        return analysis.IsValid;
    }

    private string BuildSimulationHint()
    {
        var activities = new List<string>();
        var staticPins = _sketchOutputProfiles
            .Where(pair => pair.Value.Mode is DigitalOutputMode.High or DigitalOutputMode.Low)
            .Select(pair => $"D{pair.Key} {(pair.Value.Mode == DigitalOutputMode.High ? "HIGH" : "LOW")}");
        activities.AddRange(staticPins);
        activities.AddRange(_sketchOutputProfiles
            .Where(pair => pair.Value.Mode == DigitalOutputMode.Blink)
            .Select(pair => $"D{pair.Key} {pair.Value.HighDurationSeconds:0.##}s HIGH / {pair.Value.LowDurationSeconds:0.##}s LOW"));
        activities.AddRange(_conditionalOutputRules.Select(rule =>
            $"D{rule.OutputPin} follows {rule.Condition.ToDisplayString()}"));
        if (_sketchInputPins.Count > 0)
            activities.Add($"reading {string.Join(", ", _sketchInputPins.OrderBy(pin => pin))}");

        return activities.Count == 0
            ? "Simulation is live."
            : $"Sketch live · {string.Join(" · ", activities)}";
    }

    private void CodeEditorTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var isValid = AnalyzeSketch();
        if (!_isSimulationRunning)
            return;

        if (isValid)
        {
            EvaluateCircuitState();
            return;
        }

        _simulationTimer.Stop();
        _isSimulationRunning = false;
        RunButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        SimulationPulse.Fill = Brush("#FF607D");
        SimulationStateText.Text = "CODE ERROR";
        SimulationStateText.Foreground = Brush("#FF8EA4");
        ArduinoStatus.Text = "Sketch paused";
        ArduinoStatus.Foreground = Brush("#FF9AAE");
        StatusText.Text = "Code issue";
        HintText.Text = "Simulation paused because the edited sketch is no longer valid.";
        FooterText.Text = "SimForge 0.7.0 · Fix sketch diagnostics to continue";
        EvaluateCircuitState();
    }

    private void StarterCircuitButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryConfirmWorkspaceReplacement(WorkspaceReplacementAction.LoadDemo, sender as Button))
            return;

        ClearWorkspace(false);
        var workspaceWidth = WorkspaceSurface.Width > 0 ? WorkspaceSurface.Width : 760;
        Point unoPosition;
        Point resistorPosition;
        Point ledPosition;
        Point groundPosition;

        if (workspaceWidth >= 760)
        {
            unoPosition = new Point(44, 58);
            resistorPosition = new Point((workspaceWidth - 184) / 2, 58);
            ledPosition = new Point(workspaceWidth - 228, 58);
            groundPosition = new Point(workspaceWidth - 228, 226);
        }
        else
        {
            var rightColumn = Math.Max(238, workspaceWidth - 218);
            unoPosition = new Point(34, 46);
            resistorPosition = new Point(rightColumn, 46);
            ledPosition = new Point(rightColumn, 214);
            groundPosition = new Point(34, 214);
        }

        var uno = AddComponent("Arduino Uno", unoPosition, false);
        var resistor = AddComponent("Resistor", resistorPosition, false);
        var led = AddComponent("LED", ledPosition, false);
        var ground = AddComponent("GND", groundPosition, false);

        CreateConnection(uno, resistor, true);
        CreateConnection(resistor, led, true);
        CreateConnection(led, ground, true);
        SelectComponent(led);
        CheckShortCircuit();
        EvaluateCircuitState();
        UpdateWorkspaceUi();
        HintText.Text = "Starter circuit ready. Press Run to simulate the blinking LED on pin D13.";
        TutorialText.Text = "The resistor limits LED current and the ground node completes the return path.";
        FooterText.Text = "SimForge 0.7.0 · Starter circuit loaded";
        StatusText.Text = "Demo ready";
    }

    private void ClearWorkspaceButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryConfirmWorkspaceReplacement(WorkspaceReplacementAction.Clear, sender as Button))
            return;

        ClearWorkspace(true);
        StatusText.Text = "Ready";
    }

    private bool TryConfirmWorkspaceReplacement(WorkspaceReplacementAction action, Button? sourceButton)
    {
        if (_addedComponents.Count == 0)
        {
            ResetWorkspaceReplacementConfirmation();
            return true;
        }

        if (_pendingWorkspaceReplacement == action)
        {
            ResetWorkspaceReplacementConfirmation();
            return true;
        }

        ResetWorkspaceReplacementConfirmation();
        _pendingWorkspaceReplacement = action;
        _pendingReplacementButton = sourceButton;
        _pendingReplacementButtonContent = sourceButton?.Content;
        if (sourceButton is not null)
        {
            sourceButton.Content = action == WorkspaceReplacementAction.Clear ? "Confirm clear" : "Confirm load";
            sourceButton.Classes.Add("danger");
        }

        _replacementConfirmationTimer.Start();
        StatusText.Text = "Confirm action";
        HintText.Text = action == WorkspaceReplacementAction.Clear
            ? "This removes every component and wire. Click Confirm clear within four seconds to continue."
            : "Loading the demo replaces the current workspace. Click Confirm load within four seconds to continue.";
        FooterText.Text = "SimForge 0.7.0 · Waiting for confirmation";
        return false;
    }

    private void ResetWorkspaceReplacementConfirmation(bool expired = false)
    {
        var hadPendingAction = _pendingWorkspaceReplacement != WorkspaceReplacementAction.None;
        _replacementConfirmationTimer.Stop();
        if (_pendingReplacementButton is not null)
        {
            _pendingReplacementButton.Content = _pendingReplacementButtonContent;
            _pendingReplacementButton.Classes.Remove("danger");
        }

        _pendingWorkspaceReplacement = WorkspaceReplacementAction.None;
        _pendingReplacementButton = null;
        _pendingReplacementButtonContent = null;

        if (expired && hadPendingAction)
        {
            StatusText.Text = _isSimulationRunning ? "Running" : "Ready";
            HintText.Text = "Confirmation expired. Your workspace was left unchanged.";
            FooterText.Text = "SimForge 0.7.0 · Workspace unchanged";
        }
    }

    private void ClearWorkspace(bool announce)
    {
        ResetSimulationState();
        GraphCanvas.Children.Clear();
        _visualConnections.Clear();
        _addedComponents.Clear();
        _graph.Connections.Clear();
        _graph.Nodes.Clear();
        _selectedComponent = null;
        _selectedConnection = null;
        _connectionStart = null;
        _draggedComponent = null;
        _addedComponentCount = 0;
        ResetWorkspaceSurfaceSize();
        ConnectButton.Content = "⌁  Connect";
        ConnectionModeBadge.IsVisible = false;
        RemoveButton.IsEnabled = false;
        UpdateWorkspaceUi();
        ShowWorkspaceInspector();
        EvaluateCircuitState();

        if (announce)
        {
            HintText.Text = "Workspace cleared. Add a component or load the starter circuit.";
            FooterText.Text = "SimForge 0.7.0 · New empty circuit";
        }
    }

    private void GraphCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_connectionStart is not null)
            CancelConnectionMode("Connection cancelled. Select a node to begin again.");

        var point = e.GetPosition(GraphCanvas);
        ClearSelection();
        ShowWorkspaceInspector();
        CursorPositionText.Text = $"X {point.X:0}  ·  Y {point.Y:0}";
        FooterText.Text = "SimForge 0.7.0 · Workspace selected";
        StatusText.Text = _isSimulationRunning ? "Running" : "Ready";
    }

    private void GraphCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(GraphCanvas);
        CursorPositionText.Text = $"X {point.X:0}  ·  Y {point.Y:0}";
    }

    private void UpdateWorkspaceUi()
    {
        var componentCount = _addedComponents.Count;
        var wireCount = _visualConnections.Count;
        EmptyStatePanel.IsVisible = componentCount == 0;
        ComponentCountText.Text = $"{componentCount} component{(componentCount == 1 ? string.Empty : "s")}";
        ConnectionCountText.Text = $"{wireCount} wire{(wireCount == 1 ? string.Empty : "s")}";
        CanvasStatText.Text = $"{componentCount} node{(componentCount == 1 ? string.Empty : "s")} · {wireCount} wire{(wireCount == 1 ? string.Empty : "s")}";
    }

    private void WorkspaceScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var minimumWidth = Math.Max(420, e.NewSize.Width);
        var minimumHeight = Math.Max(300, e.NewSize.Height);
        if (_addedComponents.Count == 0)
        {
            WorkspaceSurface.Width = minimumWidth;
            WorkspaceSurface.Height = minimumHeight;
        }
        else
        {
            WorkspaceSurface.Width = Math.Max(WorkspaceSurface.Width, minimumWidth);
            WorkspaceSurface.Height = Math.Max(WorkspaceSurface.Height, minimumHeight);
        }
    }

    private void EnsureWorkspaceSize(double requiredWidth, double requiredHeight)
    {
        WorkspaceSurface.Width = Math.Max(WorkspaceSurface.Width, requiredWidth);
        WorkspaceSurface.Height = Math.Max(WorkspaceSurface.Height, requiredHeight);
    }

    private void ResetWorkspaceSurfaceSize()
    {
        WorkspaceSurface.Width = Math.Max(420, WorkspaceScrollViewer.Bounds.Width);
        WorkspaceSurface.Height = Math.Max(300, WorkspaceScrollViewer.Bounds.Height);
    }

    private static bool IsSwitch(string name) => name is "Button" or "Toggle Switch" or "Slide Switch";

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    private static string WithAlpha(string color, string alpha) =>
        color.StartsWith('#') && color.Length == 7 ? $"#{alpha}{color[1..]}" : color;

    private static ComponentInfo GetComponentInfo(string name) =>
        ComponentCatalog.FirstOrDefault(component => component.Name == name) ??
        new ComponentInfo(name, "Other", "PART", "GEN", "General SimForge component.", "General circuit element",
            "No detailed pin map", "No editable parameters", "#AFC1D6", "#202B38",
            "Connect this element to compatible pins.", "PASSIVE");

    private static readonly IReadOnlyList<ComponentInfo> ComponentCatalog =
    [
        new("Arduino Uno", "Microcontrollers", "UNO", "MCU", "ATmega328P development board with a familiar 5 V I/O platform.", "ATmega328P · 5 V", "D2, A0, D7, D13, 5V, GND", "Clock 16 MHz · Logic 5 V", "#78ACFF", "#162F50", "Use D13 as an output, D2/D7 for digital sensors, and A0 for analog sensors.", "DIGITAL"),
        new("Arduino Nano", "Microcontrollers", "NANO", "MCU", "Compact ATmega328P board designed for breadboard projects.", "Compact AVR · 5 V", "D2, A0, D7, D13, 5V, GND", "Clock 16 MHz · Logic 5 V", "#78ACFF", "#162F50", "The Nano exposes digital and analog input paths in this simulation.", "DIGITAL"),
        new("ESP32 DevKit", "Microcontrollers", "ESP32", "MCU", "Dual-core wireless microcontroller with Wi-Fi and Bluetooth.", "Wi-Fi · Bluetooth · 3.3 V", "D2, A0, D7, D13, 5V, GND", "Clock 240 MHz · Logic 3.3 V", "#B09AFF", "#282047", "Respect 3.3 V logic levels when pairing the ESP32 with external devices.", "DIGITAL"),
        new("Raspberry Pi Pico", "Microcontrollers", "PICO", "MCU", "RP2040 microcontroller board with two ARM Cortex-M0+ cores.", "RP2040 · dual core", "D2, A0, D7, D13, 5V, GND", "Clock 133 MHz · Logic 3.3 V", "#62D5C1", "#153A36", "Use A0 for analog sensors and D2/D7 for digital sensor data.", "DIGITAL"),
        new("STM32 Blue Pill", "Microcontrollers", "STM", "MCU", "STM32F103 board for fast 32-bit embedded control.", "Cortex-M3 · 72 MHz", "D2, A0, D7, D13, 5V, GND", "Clock 72 MHz · Logic 3.3 V", "#68C7FF", "#163448", "Verify signal voltage compatibility before wiring 5 V modules.", "DIGITAL"),
        new("ATtiny85", "Microcontrollers", "85", "MCU", "Minimal 8-bit AVR microcontroller for compact projects.", "Minimal 8-bit AVR", "D2, A0, D7, D13, 5V, GND", "Clock 8 MHz · Logic 5 V", "#F3CE84", "#382F1F", "Use the exposed digital and analog paths for compact sensor projects.", "DIGITAL"),
        new("LED", "Basic Electronics", "LED", "OUTPUT", "Light-emitting diode that converts electrical energy into visible light.", "Light-emitting diode", "Anode, Cathode", "Forward voltage 2.0 V · Current 20 mA", "#FF718B", "#3D1E28", "Always place a current-limiting resistor in series with an LED.", "POLARIZED"),
        new("Resistor", "Basic Electronics", "Ω", "PASSIVE", "Passive element that limits current and divides voltage.", "220 Ω · current limiter", "A, B", "Resistance 220 Ω · Power 0.25 W", "#F1C975", "#382F1F", "Use a resistor to protect LEDs and shape analog signals.", "ANALOG"),
        new("Capacitor", "Basic Electronics", "C", "PASSIVE", "Passive component that stores electrical charge.", "10 µF · charge storage", "A, B", "Capacitance 10 µF · Rating 16 V", "#75A9FF", "#1A304D", "Capacitors can smooth supply noise and create timing networks.", "ANALOG"),
        new("GND", "Basic Electronics", "GND", "POWER", "Common zero-volt reference used to complete the circuit return path.", "Common 0 V reference", "GND", "Reference potential 0 V", "#B7C3D0", "#252C35", "Connect return paths to ground, but never short an unprotected power source directly to it.", "GROUND"),
        new("Button", "Switches", "PB", "SWITCH", "Momentary switch that closes a digital path when activated.", "Momentary contact", "IN, OUT", "State open", "#C1CEDC", "#29303A", "Click the control on the node to change its simulated contact state.", "DIGITAL"),
        new("Toggle Switch", "Switches", "TGL", "SWITCH", "Mechanical switch that maintains its open or closed state.", "Latching on / off", "IN, OUT", "State open", "#F2A65A", "#3A291E", "A closed switch conducts; an open switch breaks the simulated path.", "DIGITAL"),
        new("Slide Switch", "Switches", "S1", "SWITCH", "Two-position selector for routing a digital signal.", "Two-position selector", "IN, OUT", "Position open", "#F2D866", "#38331C", "Use slide switches to model persistent user input.", "DIGITAL"),
        new("LDR Sensor", "Sensors & Inputs", "LUX", "SENSOR", "Non-linear photoresistor voltage divider whose output follows ambient light.", "10 kΩ divider · analog ADC", "VCC, OUT, GND", "LDR 1 kΩ–1 MΩ · 10-bit ADC", "#FFBE5C", "#3B2D1A", "Wire OUT to A0; use if (analogRead(A0) > 600) to drive an output.", "ANALOG", true, "Light level", 0, 100, "%", 50),
        new("Potentiometer", "Sensors & Inputs", "POT", "INPUT", "10 kΩ voltage divider with a linear adjustable wiper output.", "0–5 V analog wiper", "VCC, WIPER, GND", "Position 0–100% · 10-bit ADC", "#69C1FF", "#18334A", "Wire WIPER to A0; compare analogRead(A0) in a simple if/else.", "ANALOG", true, "Wiper position", 0, 100, "%", 50),
        new("HC-SR04 Distance", "Sensors & Inputs", "CM", "SENSOR", "Ultrasonic time-of-flight module with temperature-adjusted echo timing.", "40 kHz · 2–400 cm", "VCC, TRIG, ECHO, GND", "10 µs trigger · timed echo pulse", "#5CE0C7", "#173936", "Drive TRIG from D7, connect ECHO to D2, then read pulseIn(D2, HIGH).", "DIGITAL", true, "Target distance", 2, 400, " cm", 100),
        new("DHT11 Temperature", "Sensors & Inputs", "°C", "SENSOR", "Quantized digital temperature and humidity sensor with a limited refresh rate.", "1 °C resolution · 2 s refresh", "VCC, DATA, GND", "0–50 °C · ±2 °C", "#FF7D7D", "#3D2022", "Connect DATA to D2; compare dht.readTemperature() in a simple if/else.", "DIGITAL", true, "Temperature", 0, 50, " °C", 24)
    ];

    private sealed record ComponentInfo(
        string Name,
        string Category,
        string Symbol,
        string CategoryLabel,
        string Description,
        string Summary,
        string Pins,
        string Parameters,
        string Accent,
        string Surface,
        string Tutorial,
        string SignalLabel,
        bool HasAdjustableValue = false,
        string ValueLabel = "Input value",
        double ValueMin = 0,
        double ValueMax = 100,
        string ValueUnit = "",
        double DefaultValue = 50);

    private sealed class EditorComponent
    {
        public EditorComponent(ComponentInfo info, EditorNode model, Ellipse? indicator, Button? stateButton)
        {
            Info = info;
            Model = model;
            Indicator = indicator;
            StateButton = stateButton;
        }

        public ComponentInfo Info { get; }
        public EditorNode Model { get; }
        public Ellipse? Indicator { get; }
        public Button? StateButton { get; }
        public string LedColor { get; set; } = "Red";
        public bool State { get; set; }
    }

    private sealed record NodeVisualParts(Border Node, Ellipse? Indicator, Button? StateButton);
    private sealed record VisualConnection(Border First, Border Second, Line Line, Connection Model);
    private sealed record PaletteEntry(Button Button, ComponentInfo Info);
    private readonly record struct DrivenPin(NodePin Pin, bool IsHigh);
    private readonly record struct PinEdge(NodePin Pin, bool AddsCurrentLimiter);

    private sealed class CategoryEntry(string name)
    {
        public string Name { get; } = name;
        public Expander Expander { get; set; } = null!;
        public List<PaletteEntry> PaletteEntries { get; } = [];
    }

    private sealed class EditorNode : Node
    {
        public EditorNode(string name, NodeKind kind) : base(name, kind)
        {
        }

        public override void Step(SimulationContext context, double deltaTimeSeconds)
        {
        }
    }

    private enum PathLimiterRequirement
    {
        Any,
        Required,
        Forbidden
    }

    private enum WorkspaceReplacementAction
    {
        None,
        Clear,
        LoadDemo
    }
}
