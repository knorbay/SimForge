using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SimForge.Core;

namespace SimForge;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _simulationTimer;
    private readonly Graph _graph = new();
    private readonly Dictionary<Border, EditorComponent> _addedComponents = new();
    private readonly List<VisualConnection> _visualConnections = new();
    private double _timeSeconds;
    private bool _ledIsOn;
    private Border? _selectedComponent;
    private VisualConnection? _selectedConnection;
    private Border? _connectionStart;
    private Border? _draggedComponent;
    private Avalonia.Point _dragOffset;
    private int _addedComponentCount;

    public MainWindow()
    {
        InitializeComponent();

        _simulationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _simulationTimer.Tick += SimulationTimer_Tick;

        KeyDown += MainWindow_KeyDown;
        SetDefaultCode();
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
            "}\n";
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            RemoveSelectedElement();
        }
    }

    private void RunButton_Click(object? sender, RoutedEventArgs e)
    {
        _simulationTimer.Start();
        RunButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusText.Text = "Simülasyon çalışıyor";
        ArduinoStatus.Text = "Durum: çalışıyor";
        HintText.Text = "Arduino C++ koda göre D13 pini tetikleniyor.";
        FooterText.Text = "SimForge 0.2 · Simülasyon çalışıyor";
        CheckShortCircuit();
        EvaluateCircuitState();
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        _simulationTimer.Stop();
        RunButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusText.Text = "Duraklatıldı";
        ArduinoStatus.Text = "Durum: duraklatıldı";
        FooterText.Text = "SimForge 0.2 · Simülasyon duraklatıldı";
        EvaluateCircuitState();
    }

    private void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        _simulationTimer.Stop();
        _timeSeconds = 0;
        _ledIsOn = false;
        TimeText.Text = "0.000 s";
        LedLight.Fill = new SolidColorBrush(Color.Parse("#472C2C"));
        RunButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusText.Text = "Hazır";
        ArduinoStatus.Text = "Durum: hazır";
        ShortCircuitBadge.IsVisible = false;
        ShortCircuitStateText.Text = "Yok (Normal)";
        ShortCircuitStateText.Foreground = new SolidColorBrush(Color.Parse("#86D8A9"));
        HintText.Text = "Simülasyonu çalıştırarak davranışı test edin.";
        FooterText.Text = "SimForge 0.2 · Elektronik çalışma alanı";
        EvaluateCircuitState();
    }

    private void PaletteButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string component })
            AddComponent(component);
    }

    private void AddComponent(string componentName)
    {
        var info = GetComponentInfo(componentName);
        var node = CreateComponentVisual(componentName, info);
        var column = _addedComponentCount % 3;
        var row = _addedComponentCount / 3;

        Canvas.SetLeft(node, 50 + (column * 190));
        Canvas.SetTop(node, 50 + (row * 110));
        GraphCanvas.Children.Add(node);
        var model = CreateEditorNode(componentName);
        _graph.AddNode(model);
        _addedComponents.Add(node, new EditorComponent(info, model, "Kırmızı", false));
        _addedComponentCount++;

        SelectComponent(node);
        FooterText.Text = $"{componentName} çalışma alanına eklendi.";
        CheckShortCircuit();
        EvaluateCircuitState();
    }

    private Border CreateComponentVisual(string componentName, ComponentInfo info)
    {
        var node = new Border
        {
            Width = 160,
            Height = 88,
            Background = new SolidColorBrush(Color.Parse("#1D2938")),
            BorderBrush = new SolidColorBrush(Color.Parse("#50627B")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(7),
            Padding = new Avalonia.Thickness(8),
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        var stack = new StackPanel { Spacing = 3 };

        stack.Children.Add(new TextBlock
        {
            Text = componentName.ToUpperInvariant(),
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse(info.Accent))
        });

        if (componentName == "LED")
        {
            var ledGrid = new Grid { Height = 32 };
            var ellipse = new Ellipse
            {
                Name = "LedDiode",
                Width = 26,
                Height = 26,
                Fill = new SolidColorBrush(Color.Parse("#4A1515")),
                Stroke = new SolidColorBrush(Color.Parse("#FF6B6B")),
                StrokeThickness = 2,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            ledGrid.Children.Add(ellipse);
            stack.Children.Add(ledGrid);
        }
        else if (componentName is "Geçiş Şalteri" or "Sürgülü Anahtar" or "Buton")
        {
            var switchBtn = new Button
            {
                Content = "KAPALI",
                Background = new SolidColorBrush(Color.Parse("#7A2E2E")),
                Foreground = Brushes.White,
                FontSize = 10,
                Padding = new Avalonia.Thickness(8, 3),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            switchBtn.Click += (s, e) =>
            {
                if (_addedComponents.TryGetValue(node, out var comp))
                {
                    comp.State = !comp.State;
                    switchBtn.Content = comp.State ? "AÇIK" : "KAPALI";
                    switchBtn.Background = new SolidColorBrush(Color.Parse(comp.State ? "#1E6B37" : "#7A2E2E"));
                    EvaluateCircuitState();
                }
            };
            stack.Children.Add(switchBtn);
        }

        stack.Children.Add(new TextBlock
        {
            Text = info.Summary,
            Foreground = new SolidColorBrush(Color.Parse("#CBD7E6")),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        });

        node.Child = stack;
        node.PointerPressed += Component_PointerPressed;
        node.PointerMoved += Component_PointerMoved;
        node.PointerReleased += Component_PointerReleased;
        return node;
    }

    private void Component_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border node && _addedComponents.ContainsKey(node))
        {
            if (_connectionStart is not null && !ReferenceEquals(_connectionStart, node))
            {
                CreateConnection(_connectionStart, node);
                e.Handled = true;
                return;
            }

            SelectComponent(node);
            _draggedComponent = node;
            var pointerPosition = e.GetPosition(GraphCanvas);
            _dragOffset = new Avalonia.Point(
                pointerPosition.X - Canvas.GetLeft(node),
                pointerPosition.Y - Canvas.GetTop(node));
            e.Pointer.Capture(node);
            e.Handled = true;
        }
    }

    private void Component_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Border node || !ReferenceEquals(node, _draggedComponent) ||
            !e.GetCurrentPoint(node).Properties.IsLeftButtonPressed)
            return;

        var pointerPosition = e.GetPosition(GraphCanvas);
        Canvas.SetLeft(node, Math.Max(0, pointerPosition.X - _dragOffset.X));
        Canvas.SetTop(node, Math.Max(0, pointerPosition.Y - _dragOffset.Y));
        UpdateConnectionLines(node);
    }

    private void Component_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Border node && ReferenceEquals(node, _draggedComponent))
        {
            e.Pointer.Capture(null);
            _draggedComponent = null;
            FooterText.Text = $"{_addedComponents[node].Info.Name} taşındı.";
        }
    }

    private void ClearSelection()
    {
        if (_selectedComponent is not null)
        {
            _selectedComponent.BorderBrush = new SolidColorBrush(Color.Parse("#50627B"));
            _selectedComponent = null;
        }

        if (_selectedConnection is not null)
        {
            _selectedConnection.Line.Stroke = new SolidColorBrush(Color.Parse("#69B7FF"));
            _selectedConnection = null;
        }

        RemoveButton.IsEnabled = false;
        LedColorPanel.IsVisible = false;
    }

    private void SelectComponent(Border node)
    {
        ClearSelection();

        _selectedComponent = node;
        _selectedComponent.BorderBrush = new SolidColorBrush(Color.Parse("#79A8FF"));
        RemoveButton.IsEnabled = true;

        var comp = _addedComponents[node];
        var info = comp.Info;

        SelectedElementTitle.Text = info.Name;
        SelectedElementDescription.Text = info.Description;
        TutorialText.Text = info.Tutorial;
        GeneralInfoText.Text = info.Summary;
        PinsText.Text = info.Pins;
        ParametersText.Text = info.Parameters;
        HintText.Text = $"{info.Name} seçildi. Ayarlarını sağ panelden yönetebilirsiniz.";
        StatusText.Text = $"Seçili: {info.Name}";

        if (info.Name == "LED")
        {
            LedColorPanel.IsVisible = true;
        }
    }

    private void SelectConnection(VisualConnection visualConnection)
    {
        ClearSelection();

        _selectedConnection = visualConnection;
        _selectedConnection.Line.Stroke = new SolidColorBrush(Color.Parse("#FF7979"));
        RemoveButton.IsEnabled = true;

        var firstName = _addedComponents[visualConnection.First].Info.Name;
        var secondName = _addedComponents[visualConnection.Second].Info.Name;

        SelectedElementTitle.Text = "Kablo Bağlantısı";
        SelectedElementDescription.Text = $"{firstName} ile {secondName} arasındaki bağlantı hattı.";
        TutorialText.Text = "Kablolar elektrik sinyalini ve gerilimi iletir. Dirençsiz bağlantılarda aşırı akım oluşabilir.";
        GeneralInfoText.Text = "İletken Kablo";
        PinsText.Text = $"{firstName} ↔ {secondName}";
        ParametersText.Text = "Direnç: 0 Ω (ideal iletken)";
        HintText.Text = "Seçili kabloyu silmek için Delete tuşuna basın.";
        StatusText.Text = "Seçili bağlantı";
    }

    private void RemoveButton_Click(object? sender, RoutedEventArgs e)
    {
        RemoveSelectedElement();
    }

    private void RemoveSelectedElement()
    {
        if (_selectedComponent is not null)
        {
            var target = _selectedComponent;
            var component = _addedComponents[target];
            var name = component.Info.Name;

            foreach (var visualConnection in _visualConnections
                         .Where(connection => ReferenceEquals(connection.First, target) || ReferenceEquals(connection.Second, target))
                         .ToList())
            {
                GraphCanvas.Children.Remove(visualConnection.Line);
                _visualConnections.Remove(visualConnection);
                _graph.RemoveConnection(visualConnection.Model);
            }

            GraphCanvas.Children.Remove(target);
            _addedComponents.Remove(target);
            _graph.RemoveNode(component.Model);

            if (ReferenceEquals(_connectionStart, target))
            {
                _connectionStart = null;
                ConnectButton.Content = "⌁  Bağlantı kur";
            }

            ClearSelection();
            SelectedElementTitle.Text = "Eleman seçilmedi";
            SelectedElementDescription.Text = "Sol kütüphaneden bir bileşen seçip ekleyin.";
            TutorialText.Text = "Devrenizi oluşturmak için elemanları ekleyin ve pinlerini bağlayın.";
            GeneralInfoText.Text = "Seçili bileşen yok.";
            PinsText.Text = "—";
            ParametersText.Text = "—";
            HintText.Text = $"{name} kaldırıldı.";
            StatusText.Text = "Hazır";
            FooterText.Text = $"{name} kaldırıldı.";
            CheckShortCircuit();
            EvaluateCircuitState();
        }
        else if (_selectedConnection is not null)
        {
            var targetConn = _selectedConnection;
            GraphCanvas.Children.Remove(targetConn.Line);
            _visualConnections.Remove(targetConn);
            _graph.RemoveConnection(targetConn.Model);

            ClearSelection();
            SelectedElementTitle.Text = "Eleman seçilmedi";
            SelectedElementDescription.Text = "Sol kütüphaneden bir bileşen seçip ekleyin.";
            TutorialText.Text = "Devrenizi oluşturmak için elemanları ekleyin ve pinlerini bağlayın.";
            GeneralInfoText.Text = "Seçili bileşen yok.";
            PinsText.Text = "—";
            ParametersText.Text = "—";
            HintText.Text = "Kablo kaldırıldı.";
            StatusText.Text = "Hazır";
            FooterText.Text = "Kablo kaldırıldı.";
            CircuitStateText.Text = $"{_graph.Connections.Count} bağlantı";
            CheckShortCircuit();
            EvaluateCircuitState();
        }
    }

    private void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedComponent is null)
        {
            HintText.Text = "Önce bağlantının başlayacağı bileşeni seçin.";
            return;
        }

        _connectionStart = _selectedComponent;
        ConnectButton.Content = "Hedef elemanı seçin";
        StatusText.Text = $"Bağlantı başlangıcı: {_addedComponents[_connectionStart].Info.Name}";
        HintText.Text = "Bağlantıyı kurmak için ikinci bileşene tıklayın.";
    }

    private void CreateConnection(Border first, Border second)
    {
        try
        {
            var firstComponent = _addedComponents[first];
            var secondComponent = _addedComponents[second];
            var connection = _graph.Connect(firstComponent.Model.Pins.First(), secondComponent.Model.Pins.First());
            var line = new Line
            {
                Stroke = new SolidColorBrush(Color.Parse("#69B7FF")),
                StrokeThickness = 3,
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            var visualConnection = new VisualConnection(first, second, line, connection);

            line.PointerPressed += (s, e) =>
            {
                SelectConnection(visualConnection);
                e.Handled = true;
            };

            GraphCanvas.Children.Insert(0, line);
            _visualConnections.Add(visualConnection);
            UpdateConnectionLine(visualConnection);
            FooterText.Text = $"{firstComponent.Info.Name} ↔ {secondComponent.Info.Name} bağlandı.";
            HintText.Text = "Kablo bağlantısı sağlandı.";
            CircuitStateText.Text = $"{_graph.Connections.Count} bağlantı";
            CheckShortCircuit();
            EvaluateCircuitState();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            HintText.Text = exception.Message;
            StatusText.Text = "Bağlantı başarısız";
        }
        finally
        {
            _connectionStart = null;
            ConnectButton.Content = "⌁  Bağlantı kur";
        }
    }

    private void CheckShortCircuit()
    {
        var hasMicrocontroller = _addedComponents.Values.Any(c => c.Model.Kind == NodeKind.Microcontroller);
        var hasGnd = _addedComponents.Values.Any(c => c.Info.Name == "GND");
        var directConnection = _visualConnections.Any(vc =>
        {
            var name1 = _addedComponents[vc.First].Info.Name;
            var name2 = _addedComponents[vc.Second].Info.Name;
            var isMc1 = _addedComponents[vc.First].Model.Kind == NodeKind.Microcontroller;
            var isMc2 = _addedComponents[vc.Second].Model.Kind == NodeKind.Microcontroller;
            return (isMc1 && name2 == "GND") || (isMc2 && name1 == "GND");
        });

        if (hasMicrocontroller && hasGnd && directConnection)
        {
            ShortCircuitBadge.IsVisible = true;
            ShortCircuitStateText.Text = "TEHLİKE (Aşırı Akım!)";
            ShortCircuitStateText.Foreground = new SolidColorBrush(Color.Parse("#FF5555"));
            HintText.Text = "⚠️ Kısa devre algılandı! Güç ile GND arasında direnç kullanmalısınız.";
        }
        else
        {
            ShortCircuitBadge.IsVisible = false;
            ShortCircuitStateText.Text = "Yok (Normal)";
            ShortCircuitStateText.Foreground = new SolidColorBrush(Color.Parse("#86D8A9"));
        }
    }

    private void EvaluateCircuitState()
    {
        var poweredBorders = new HashSet<Border>();

        if (_ledIsOn)
        {
            var sources = _addedComponents
                .Where(kvp => kvp.Value.Model.Kind == NodeKind.Microcontroller)
                .Select(kvp => kvp.Key)
                .ToList();

            var queue = new Queue<Border>(sources);
            var visited = new HashSet<Border>(sources);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                poweredBorders.Add(current);

                var neighbors = _visualConnections
                    .Where(vc => ReferenceEquals(vc.First, current) || ReferenceEquals(vc.Second, current))
                    .Select(vc => ReferenceEquals(vc.First, current) ? vc.Second : vc.First);

                foreach (var neighbor in neighbors)
                {
                    if (visited.Contains(neighbor))
                        continue;

                    if (_addedComponents.TryGetValue(neighbor, out var comp))
                    {
                        if (comp.Info.Name is "Geçiş Şalteri" or "Sürgülü Anahtar" or "Buton")
                        {
                            if (!comp.State)
                                continue;
                        }
                    }

                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }
        }

        foreach (var (border, editorComp) in _addedComponents)
        {
            if (editorComp.Info.Name == "LED")
            {
                var isPowered = poweredBorders.Contains(border);
                UpdateLedVisual(border, editorComp, isPowered);
            }
        }
    }

    private void LedColorComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_selectedComponent is null || !_addedComponents.TryGetValue(_selectedComponent, out var comp) || comp.Info.Name != "LED")
            return;

        if (LedColorComboBox.SelectedItem is ComboBoxItem item && item.Content is string colorName)
        {
            comp.LedColor = colorName;
            EvaluateCircuitState();
        }
    }

    private static void UpdateLedVisual(Border border, EditorComponent comp, bool isOn)
    {
        var stack = border.Child as StackPanel;
        if (stack is null) return;

        foreach (var child in stack.Children)
        {
            if (child is Grid grid)
            {
                foreach (var gridChild in grid.Children)
                {
                    if (gridChild is Ellipse ellipse)
                    {
                        var colorHex = (comp.LedColor, isOn) switch
                        {
                            ("Yeşil", true) => "#2ECC71",
                            ("Yeşil", false) => "#145A32",
                            ("Mavi", true) => "#3498DB",
                            ("Mavi", false) => "#1B4F72",
                            ("Sarı", true) => "#F1C40F",
                            ("Sarı", false) => "#7D6608",
                            ("Beyaz", true) => "#FDFEFE",
                            ("Beyaz", false) => "#7B7D7D",
                            (_, true) => "#E74C3C",
                            (_, false) => "#641E16"
                        };
                        ellipse.Fill = new SolidColorBrush(Color.Parse(colorHex));
                    }
                }
            }
        }
    }

    private void UpdateConnectionLines(Border node)
    {
        foreach (var connection in _visualConnections.Where(connection => ReferenceEquals(connection.First, node) || ReferenceEquals(connection.Second, node)))
            UpdateConnectionLine(connection);
    }

    private static void UpdateConnectionLine(VisualConnection connection)
    {
        connection.Line.StartPoint = GetNodeCenter(connection.First);
        connection.Line.EndPoint = GetNodeCenter(connection.Second);
    }

    private static Avalonia.Point GetNodeCenter(Control node) => new(
        Canvas.GetLeft(node) + (node.Bounds.Width / 2),
        Canvas.GetTop(node) + (node.Bounds.Height / 2));

    private static EditorNode CreateEditorNode(string componentName)
    {
        var isMicrocontroller = componentName is "Arduino Uno" or "Arduino Nano" or "ESP32 DevKit" or "Raspberry Pi Pico" or "STM32 Blue Pill" or "ATtiny85";
        var node = new EditorNode(componentName, isMicrocontroller ? NodeKind.Microcontroller : NodeKind.Electronic);

        if (isMicrocontroller)
        {
            node.AddTerminal("D13", PinDirection.Output, PinSignalType.Digital);
            node.AddTerminal("GND", PinDirection.Passive, PinSignalType.Ground);
        }
        else if (componentName == "GND")
        {
            node.AddTerminal("GND", PinDirection.Passive, PinSignalType.Ground);
        }
        else
        {
            node.AddTerminal("A", PinDirection.Passive, PinSignalType.Analog);
            node.AddTerminal("B", PinDirection.Passive, PinSignalType.Analog);
        }

        return node;
    }

    private static ComponentInfo GetComponentInfo(string name) => name switch
    {
        "Arduino Uno" => new(name, "ATmega328P tabanlı mikrodenetleyici geliştirme kartı.", "5 V · 14 dijital / 6 analog pini", "D0–D13, A0–A5, 5V, 3V3, GND", "Saat: 16 MHz · Mantık: 5 V", "#8DB8FF", "Arduino Uno, sensör verilerini okumak ve çıktılara (LED, motor vb.) komut göndermek için kullanılan standart mikrodenetleyicidir."),
        "Arduino Nano" => new(name, "Kompakt ATmega328P geliştirme kartı.", "5 V · Breadboard dostu", "D0–D13, A0–A7, 5V, GND", "Saat: 16 MHz · Mantık: 5 V", "#8DB8FF", "Nano, Uno ile aynı mimariye sahip olup daha küçük boyutu sayesinde devre tahtası üzerinde az yer kaplar."),
        "ESP32 DevKit" => new(name, "Wi-Fi ve Bluetooth destekli çift çekirdekli geliştirme kartı.", "3.3 V · IoT destekli", "GPIO, ADC, DAC, 3V3, GND", "Saat: 240 MHz · Mantık: 3.3 V", "#A98BFF", "IoT ve kablosuz haberleşme projeleri için yüksek işlem gücü sağlayan karttır."),
        "Raspberry Pi Pico" => new(name, "RP2040 işlemcili mikrodenetleyici kartı.", "3.3 V · Çift çekirdek ARM", "GP0–GP28, VSYS, 3V3, GND", "Saat: 133 MHz · Mantık: 3.3 V", "#70D6C5", "Raspberry Pi tarafından üretilen C/C++ ve MicroPython destekli güçlü mikrodenetleyicidir."),
        "STM32 Blue Pill" => new(name, "STM32F103 tabanlı 32 bit ARM geliştirme kartı.", "3.3 V · ARM Cortex-M3", "PA, PB, PC, 3V3, GND", "Saat: 72 MHz · Mantık: 3.3 V", "#5CB8FF", "Endüstriyel seviyede yüksek hızlı kontrol sağlayan 32 bit ARM mimarisidir."),
        "ATtiny85" => new(name, "Ultra küçük 8 bit AVR mikrodenetleyici.", "5 V · 6 I/O pini", "PB0–PB5, VCC, GND", "Saat: 8 MHz · Mantık: 5 V", "#F2CC8F", "Minimal boyut gerektiren basit projeler için kullanılan küçük entegre devredir."),
        "LED" => new(name, "Işık Yayan Diyot (Light Emitting Diode).", "Yuvarlak diyot göstergesi", "Anot (+), Katot (-)", "Gerilim: 2.0 V · Akım: 20 mA", "#F08A8A", "LED, elektrik enerjisini ışığa dönüştüren yarı iletken elemandır. Anot (+) artıya, katot (-) eksiye bağlanır."),
        "Direnç" => new(name, "Elektrik akımına karşı direnç gösteren eleman.", "Varsayılan: 220 Ω", "Uç 1, Uç 2", "Direnç: 220 Ω · Güç: 0.25 W", "#F2CC8F", "Direnç, hassas bileşenlerin (LED vb.) yüksek akımdan zarar görmesini engellemek için akımı sınırlar."),
        "Kondansatör" => new(name, "Elektrik yükü depolayan pasif eleman.", "Varsayılan: 10 µF", "Pozitif, Negatif", "Kapasite: 10 µF · Gerilim: 16 V", "#6BA4FF", "Voltaj dalgalanmalarını filtrelemek ve yük depolamak için kullanılır."),
        "GND" => new(name, "Devrenin ortak 0V referans noktası.", "0 V Toprak hattı", "GND", "Gerilim: 0 V", "#AAB7C6", "Tüm devre elemanlarının gerilim referansını tamamlamak için ortak GND hattına bağlanması gerekir."),
        "Buton" => new(name, "Basıldığında devreyi tamamlayan anahtar.", "Anlık basmalı buton", "Uç 1, Uç 2", "Durum: Bırakılmış", "#9DB1CA", "Basıldığında iki ucu birleştirerek akım geçişine izin verir."),
        "Geçiş Şalteri" => new(name, "Kalıcı durum değiştiren mekanik şalter.", "Açık/Kapalı şalter", "Giriş, Çıkış", "Durum: Açık", "#E67E22", "Şalter bir kez tıklandığında konumunu değiştirir ve açık veya kapalı kalır."),
        "Sürgülü Anahtar" => new(name, "İki konumlu sürgülü anahtar.", "Kutup seçici", "Pin 1, Pin 2", "Konum: 1", "#F1C40F", "Sürgülü anahtar mekanik olarak akım yolunu değiştirmek için kullanılır."),
        "LDR Sensör" => new(name, "Işıkla direnci değişen foto-direnç.", "Ortam ışık sensörü", "VCC, OUT, GND", "Direnç: 1 kΩ - 100 kΩ", "#F39C12", "Üzerine düşen ışık miktarı arttıkça direnci düşen sensördür."),
        "Potansiyometre" => new(name, "Ayarlanabilir değişken direnç.", "0 - 10 kΩ pot", "VCC, WIPER, GND", "Direnç: 0-10 kΩ", "#3498DB", "Döner başlığı ile gerilim bölücü veya değişken akım ayarlayıcı olarak kullanılır."),
        "HC-SR04 Mesafe" => new(name, "Ultrasonik ses dalgası ile mesafe ölçer.", "2 cm - 400 cm mesafe", "VCC, TRIG, ECHO, GND", "Frekans: 40 kHz", "#1ABC9C", "Ses dalgası gönderip yankı süresinden engelin mesafesini hesaplar."),
        "DHT11 Sıcaklık" => new(name, "Dijital sıcaklık ve nem sensörü.", "Sıcaklık/Nem ölçer", "VCC, DATA, GND", "Aralık: 0-50 °C · 20-90% Nem", "#E74C3C", "Ortamın sıcaklığını ve bağıl nem oranını dijital veri olarak gönderir."),
        _ => new(name, "SimForge Bileşeni.", "Genel elektronik elemanı.", "—", "—", "#AFC1D6", "Genel devre elemanı.")
    };

    private void SimulationTimer_Tick(object? sender, EventArgs e)
    {
        _timeSeconds += 0.1;
        TimeText.Text = $"{_timeSeconds:0.000} s";

        var shouldBeOn = ((int)_timeSeconds % 2) == 0;
        if (shouldBeOn == _ledIsOn)
            return;

        _ledIsOn = shouldBeOn;
        LedLight.Fill = new SolidColorBrush(Color.Parse(_ledIsOn ? "#FF4D4D" : "#472C2C"));
        CircuitStateText.Text = _ledIsOn ? "LED açık" : "LED kapalı";

        EvaluateCircuitState();
    }

    private void GraphCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(GraphCanvas);
        FooterText.Text = $"Çalışma alanı · X: {point.X:0}, Y: {point.Y:0}";
        ClearSelection();
    }

    private sealed record ComponentInfo(
        string Name,
        string Description,
        string Summary,
        string Pins,
        string Parameters,
        string Accent,
        string Tutorial);

    private sealed class EditorComponent
    {
        public EditorComponent(ComponentInfo info, EditorNode model, string ledColor, bool state)
        {
            Info = info;
            Model = model;
            LedColor = ledColor;
            State = state;
        }

        public ComponentInfo Info { get; }
        public EditorNode Model { get; }
        public string LedColor { get; set; }
        public bool State { get; set; }
    }

    private sealed record VisualConnection(Border First, Border Second, Line Line, Connection Model);

    private sealed class EditorNode : Node
    {
        public EditorNode(string name, NodeKind kind) : base(name, kind)
        {
        }

        public NodePin AddTerminal(string name, PinDirection direction, PinSignalType signalType) =>
            AddPin(name, direction, signalType);

        public override void Step(SimulationContext context, double deltaTimeSeconds)
        {
        }
    }
}
