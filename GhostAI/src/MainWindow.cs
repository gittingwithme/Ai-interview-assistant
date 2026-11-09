using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace GhostAI {
    public class MainWindow : Window {
        private const int HOTKEY_ID_TOGGLE = 9001;
        private const int HOTKEY_ID_PANIC = 9002;

        private AppSettings _settings;
        private readonly GeminiService _geminiService;
        private readonly AudioLoopbackService _audioService;
        private readonly List<ChatMessage> _chatHistory;

        // UI Controls
        private Border _mainCard;
        private Ellipse _statusDot;
        private ScrollViewer _chatScrollViewer;
        private StackPanel _messagesPanel;
        private TextBox _inputBox;
        private Button _sendBtn;
        private Button _micBtn;
        private Border _micPulseRing;
        private TextBlock _audioStatusText;
        private Border _audioStatusBar;
        private TextBlock _modelBadge;
        private Grid _settingsModal;
        private PasswordBox _apiKeyBox;
        private ComboBox _modelCombo;
        private TextBox _systemPromptBox;
        private Slider _opacitySlider;
        private TextBlock _opacityLabel;
        private CheckBox _alwaysOnTopCheck;

        private bool _isProcessing;
        private IntPtr _hwnd;

        public MainWindow() {
            _settings = AppSettings.Load();
            _geminiService = new GeminiService();
            _audioService = new AudioLoopbackService();
            _chatHistory = new List<ChatMessage>();

            InitializeWindowProperties();
            BuildInterface();
            AttachEvents();

            this.Loaded += OnWindowLoaded;
            this.SourceInitialized += OnSourceInitialized;
            this.Closed += OnWindowClosed;
        }

        private void InitializeWindowProperties() {
            this.Title = "ServiceHost"; // Generic title for stealth
            this.Width = _settings.WindowWidth;
            this.Height = _settings.WindowHeight;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.Topmost = _settings.AlwaysOnTop;
            this.ShowInTaskbar = false;
            this.Opacity = _settings.Opacity;
            this.ResizeMode = ResizeMode.CanResizeWithGrip;

            // Position on right side of screen
            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;
            this.Left = screenW - this.Width - 24;
            this.Top = Math.Max(20, (screenH - this.Height) / 2);
        }

        private void OnSourceInitialized(object sender, EventArgs e) {
            _hwnd = new WindowInteropHelper(this).Handle;

            // 1. Core Stealth: Exclude from screen sharing & recording!
            bool stealthOk = NativeMethods.ApplyStealthMode(_hwnd);

            // 2. Register Global Hotkeys
            // Ctrl + Shift + G : Toggle overlay
            NativeMethods.RegisterHotKey(_hwnd, HOTKEY_ID_TOGGLE, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, 0x47); // 'G'
            // Ctrl + Shift + Escape : Emergency Panic Hide
            NativeMethods.RegisterHotKey(_hwnd, HOTKEY_ID_PANIC, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, 0x1B); // VK_ESCAPE

            // Listen to Win32 messages
            HwndSource source = HwndSource.FromHwnd(_hwnd);
            if (source != null) {
                source.AddHook(HwndHook);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
            if (msg == NativeMethods.WM_HOTKEY) {
                int id = wParam.ToInt32();
                if (id == HOTKEY_ID_TOGGLE) {
                    ToggleVisibility();
                    handled = true;
                } else if (id == HOTKEY_ID_PANIC) {
                    PanicHide();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e) {
            UpdateStatusIndicator();
            if (string.IsNullOrWhiteSpace(_settings.ApiKey)) {
                ShowSettings();
            }
            _inputBox.Focus();
        }

        private void OnWindowClosed(object sender, EventArgs e) {
            if (_hwnd != IntPtr.Zero) {
                NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_ID_TOGGLE);
                NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_ID_PANIC);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  UI Construction (Pure C# WPF with Rich Dark Glassmorphism)
        // ══════════════════════════════════════════════════════════════════

        private void BuildInterface() {
            var rootGrid = new Grid();

            // Outer Glass Panel
            _mainCard = new Border {
                Background = new SolidColorBrush(Color.FromArgb(238, 14, 14, 22)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Margin = new Thickness(10),
                Effect = new DropShadowEffect {
                    BlurRadius = 28,
                    ShadowDepth = 6,
                    Opacity = 0.7,
                    Color = Colors.Black
                }
            };

            var mainLayout = new Grid();
            mainLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) }); // Title Bar
            mainLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // Audio Status Bar
            mainLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // Quick Actions
            mainLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Chat
            mainLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // Input Bar

            // 1. Title Bar
            var titleBar = CreateTitleBar();
            Grid.SetRow(titleBar, 0);
            mainLayout.Children.Add(titleBar);

            // 2. Audio Status Bar
            _audioStatusBar = CreateAudioStatusBar();
            Grid.SetRow(_audioStatusBar, 1);
            mainLayout.Children.Add(_audioStatusBar);

            // 3. Quick Actions
            var quickActions = CreateQuickActionsBar();
            Grid.SetRow(quickActions, 2);
            mainLayout.Children.Add(quickActions);

            // 4. Chat Messages Area
            var chatArea = CreateChatArea();
            Grid.SetRow(chatArea, 3);
            mainLayout.Children.Add(chatArea);

            // 5. Input Area
            var inputArea = CreateInputArea();
            Grid.SetRow(inputArea, 4);
            mainLayout.Children.Add(inputArea);

            _mainCard.Child = mainLayout;
            rootGrid.Children.Add(_mainCard);

            // 6. Settings Modal Overlay (hidden by default)
            _settingsModal = CreateSettingsModal();
            rootGrid.Children.Add(_settingsModal);

            this.Content = rootGrid;
        }

        private UIElement CreateTitleBar() {
            var bar = new Border {
                Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                CornerRadius = new CornerRadius(16, 16, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 0, 10, 0)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: Status Dot + Logo
            var leftStack = new StackPanel {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _statusDot = new Ellipse {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(52, 211, 153)), // Green
                Margin = new Thickness(0, 0, 8, 0),
                Effect = new DropShadowEffect {
                    Color = Color.FromRgb(52, 211, 153),
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.8
                }
            };

            var titleText = new TextBlock {
                Text = "GHOST AI",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 210)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var stealthBadge = new Border {
                Background = new SolidColorBrush(Color.FromArgb(40, 124, 108, 240)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var stealthText = new TextBlock {
                Text = "INVISIBLE",
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(165, 180, 252))
            };
            stealthBadge.Child = stealthText;

            leftStack.Children.Add(_statusDot);
            leftStack.Children.Add(titleText);
            leftStack.Children.Add(stealthBadge);

            // Allow dragging from title bar
            leftStack.MouseLeftButtonDown += (s, e) => {
                if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
            };
            bar.MouseLeftButtonDown += (s, e) => {
                if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
            };

            // Right: Controls (Settings, Hide, Close)
            var rightStack = new StackPanel {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var settingsBtn = CreateIconButton("⚙", "Settings", (s, e) => ShowSettings());
            var hideBtn = CreateIconButton("—", "Hide (Ctrl+Shift+G)", (s, e) => ToggleVisibility());
            var closeBtn = CreateIconButton("✕", "Quit Application", (s, e) => this.Close());
            closeBtn.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));

            rightStack.Children.Add(settingsBtn);
            rightStack.Children.Add(hideBtn);
            rightStack.Children.Add(closeBtn);

            Grid.SetColumn(leftStack, 0);
            Grid.SetColumn(rightStack, 1);

            grid.Children.Add(leftStack);
            grid.Children.Add(rightStack);
            bar.Child = grid;
            return bar;
        }

        private Border CreateAudioStatusBar() {
            var border = new Border {
                Background = new SolidColorBrush(Color.FromArgb(40, 248, 113, 113)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 248, 113, 113)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 6, 12, 6),
                Visibility = Visibility.Collapsed
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            var pulseDot = new Ellipse {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            _audioStatusText = new TextBlock {
                Text = "Listening to computer speakers...",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };

            stack.Children.Add(pulseDot);
            stack.Children.Add(_audioStatusText);
            border.Child = stack;
            return border;
        }

        private UIElement CreateQuickActionsBar() {
            var scroll = new ScrollViewer {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(10, 8, 10, 8)
            };

            var stack = new StackPanel {
                Orientation = Orientation.Horizontal
            };

            stack.Children.Add(CreateQuickActionButton("⚡ Explain", "Explain this concept simply:"));
            stack.Children.Add(CreateQuickActionButton("📝 Summarize", "Summarize the key points of this:"));
            stack.Children.Add(CreateQuickActionButton("💬 Suggest Reply", "Suggest a polite and professional answer to:"));
            stack.Children.Add(CreateQuickActionButton("🌐 Translate", "Translate the following to English:"));
            stack.Children.Add(CreateQuickActionButton("🎯 Key Points", "What are the action items and key takeaways from:"));

            scroll.Content = stack;
            return scroll;
        }

        private Button CreateQuickActionButton(string label, string promptPrefix) {
            var btn = new Button {
                Content = label,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 205)),
                Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(10, 5, 10, 5),
                Cursor = Cursors.Hand
            };

            // Custom template with rounded corners
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentPresenter);
            template.VisualTree = borderFactory;
            btn.Template = template;

            btn.Click += (s, e) => {
                string current = _inputBox.Text.Trim();
                if (!string.IsNullOrEmpty(current)) {
                    _inputBox.Text = promptPrefix + "\n\n" + current;
                } else {
                    _inputBox.Text = promptPrefix + " ";
                }
                _inputBox.Focus();
                _inputBox.CaretIndex = _inputBox.Text.Length;
            };

            return btn;
        }

        private UIElement CreateChatArea() {
            _chatScrollViewer = new ScrollViewer {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(12, 10, 12, 10)
            };

            _messagesPanel = new StackPanel();

            // Welcome Message Card
            var welcomeCard = CreateWelcomeCard();
            _messagesPanel.Children.Add(welcomeCard);

            _chatScrollViewer.Content = _messagesPanel;
            return _chatScrollViewer;
        }

        private Border CreateWelcomeCard() {
            var card = new Border {
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var stack = new StackPanel();

            var icon = new TextBlock {
                Text = "🛡️",
                FontSize = 26,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var heading = new TextBlock {
                Text = "GhostAI Conference Co-Pilot",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var subtext = new TextBlock {
                Text = "Completely undetectable during screen shares. Zoom, Teams, Meet & OBS cannot see this overlay.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 180)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var tagsWrap = new WrapPanel {
                HorizontalAlignment = HorizontalAlignment.Center
            };
            tagsWrap.Children.Add(CreateHintTag("Ctrl+Shift+G : Toggle"));
            tagsWrap.Children.Add(CreateHintTag("Ctrl+Shift+Esc : Panic Hide"));
            tagsWrap.Children.Add(CreateHintTag("Mic : Hear Speakers"));

            stack.Children.Add(icon);
            stack.Children.Add(heading);
            stack.Children.Add(subtext);
            stack.Children.Add(tagsWrap);

            card.Child = stack;
            return card;
        }

        private Border CreateHintTag(string text) {
            var border = new Border {
                Background = new SolidColorBrush(Color.FromArgb(40, 124, 108, 240)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(3)
            };
            var tb = new TextBlock {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(199, 210, 254))
            };
            border.Child = tb;
            return border;
        }

        private UIElement CreateInputArea() {
            var container = new Border {
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                CornerRadius = new CornerRadius(0, 0, 16, 16),
                Padding = new Thickness(12, 10, 12, 10)
            };

            var vStack = new StackPanel();

            // Rounded Input Box with Buttons
            var inputWrapper = new Border {
                Background = new SolidColorBrush(Color.FromArgb(40, 20, 20, 32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 124, 108, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 6, 8, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _inputBox = new TextBox {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                FontSize = 13,
                FontFamily = new FontFamily("Segoe UI, Inter"),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 100,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CaretBrush = new SolidColorBrush(Color.FromRgb(165, 180, 252))
            };

            var actionsStack = new StackPanel {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(6, 0, 0, 2)
            };

            // Mic Button (Audio perception)
            var micContainer = new Grid();
            _micPulseRing = new Border {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                BorderBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                BorderThickness = new Thickness(2),
                Visibility = Visibility.Collapsed
            };
            _micBtn = CreateActionButton("🎙️", "Capture Speaker Audio / Mic", (s, e) => ToggleAudioCapture());
            _micBtn.Width = 32;
            _micBtn.Height = 32;
            micContainer.Children.Add(_micPulseRing);
            micContainer.Children.Add(_micBtn);

            // Send Button
            _sendBtn = CreateActionButton("➔", "Send Message (Enter)", (s, e) => SendMessage());
            _sendBtn.Width = 32;
            _sendBtn.Height = 32;
            _sendBtn.Background = new SolidColorBrush(Color.FromRgb(124, 108, 240));
            _sendBtn.Foreground = Brushes.White;
            _sendBtn.Margin = new Thickness(4, 0, 0, 0);

            actionsStack.Children.Add(micContainer);
            actionsStack.Children.Add(_sendBtn);

            Grid.SetColumn(_inputBox, 0);
            Grid.SetColumn(actionsStack, 1);

            grid.Children.Add(_inputBox);
            grid.Children.Add(actionsStack);
            inputWrapper.Child = grid;

            // Footer metadata
            var footer = new Grid {
                Margin = new Thickness(4, 6, 4, 0)
            };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _modelBadge = new TextBlock {
                Text = _settings.Model,
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(124, 108, 240))
            };

            var hints = new TextBlock {
                Text = "Enter to send • Shift+Enter newline",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 140)),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Grid.SetColumn(_modelBadge, 0);
            Grid.SetColumn(hints, 1);
            footer.Children.Add(_modelBadge);
            footer.Children.Add(hints);

            vStack.Children.Add(inputWrapper);
            vStack.Children.Add(footer);
            container.Child = vStack;
            return container;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Settings Modal Overlay
        // ══════════════════════════════════════════════════════════════════

        private Grid CreateSettingsModal() {
            var overlay = new Grid {
                Background = new SolidColorBrush(Color.FromArgb(200, 8, 8, 14)),
                Visibility = Visibility.Collapsed
            };

            var modalBorder = new Border {
                Background = new SolidColorBrush(Color.FromArgb(250, 18, 18, 28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 124, 108, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18),
                Margin = new Thickness(20),
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 30, Opacity = 0.8, Color = Colors.Black }
            };

            var layout = new StackPanel();

            // Modal Header
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock {
                Text = "Configuration",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            var closeBtn = CreateIconButton("✕", "Close", (s, e) => HideSettings());

            Grid.SetColumn(title, 0);
            Grid.SetColumn(closeBtn, 1);
            headerGrid.Children.Add(title);
            headerGrid.Children.Add(closeBtn);
            layout.Children.Add(headerGrid);

            // API Key
            layout.Children.Add(CreateSettingLabel("Google Gemini API Key"));
            _apiKeyBox = new PasswordBox {
                Password = _settings.ApiKey,
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4)
            };
            layout.Children.Add(_apiKeyBox);

            var keyHint = new TextBlock {
                Text = "Get your free API key at ai.google.dev",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(165, 180, 252)),
                Margin = new Thickness(0, 0, 0, 14)
            };
            layout.Children.Add(keyHint);

            // Model Selection
            layout.Children.Add(CreateSettingLabel("Model"));
            _modelCombo = new ComboBox {
                Margin = new Thickness(0, 0, 0, 14),
                Padding = new Thickness(6, 4, 6, 4)
            };
            _modelCombo.Items.Add("gemini-3.5-flash");
            _modelCombo.Items.Add("gemini-3.6-flash");
            _modelCombo.Items.Add("gemini-3.5-flash-lite");
            _modelCombo.Items.Add("gemini-3.7-flash");
            _modelCombo.Items.Add("gemini-3.8-flash");
            _modelCombo.Items.Add("gemini-flash-latest");
            _modelCombo.Items.Add("gemini-4-argon");
            _modelCombo.Items.Add("gemini-3.1-pro-preview");
            _modelCombo.Items.Add("gemini-pro-latest");
            _modelCombo.SelectedItem = _settings.Model;
            if (_modelCombo.SelectedIndex == -1) {
                _modelCombo.SelectedIndex = 0;
            }
            layout.Children.Add(_modelCombo);

            // System Prompt
            layout.Children.Add(CreateSettingLabel("System Instructions / Prompt"));
            _systemPromptBox = new TextBox {
                Text = _settings.SystemPrompt,
                Height = 60,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 0, 14)
            };
            layout.Children.Add(_systemPromptBox);

            // Opacity Slider
            var opacityRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var opacityTitle = CreateSettingLabel("Window Opacity");
            _opacityLabel = new TextBlock {
                Text = ((int)(_settings.Opacity * 100)) + "%",
                FontSize = 11,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(opacityTitle, 0);
            Grid.SetColumn(_opacityLabel, 1);
            opacityRow.Children.Add(opacityTitle);
            opacityRow.Children.Add(_opacityLabel);
            layout.Children.Add(opacityRow);

            _opacitySlider = new Slider {
                Minimum = 0.2,
                Maximum = 1.0,
                Value = _settings.Opacity,
                TickFrequency = 0.05,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 0, 0, 14)
            };
            _opacitySlider.ValueChanged += (s, e) => {
                _opacityLabel.Text = ((int)(_opacitySlider.Value * 100)) + "%";
                this.Opacity = _opacitySlider.Value;
            };
            layout.Children.Add(_opacitySlider);

            // Always On Top
            _alwaysOnTopCheck = new CheckBox {
                Content = "Always on Top (Floats over all apps)",
                IsChecked = _settings.AlwaysOnTop,
                Foreground = Brushes.White,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 16)
            };
            layout.Children.Add(_alwaysOnTopCheck);

            // Save Button
            var saveBtn = new Button {
                Content = "Save & Apply Settings",
                Height = 36,
                Background = new SolidColorBrush(Color.FromRgb(124, 108, 240)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            var saveTemplate = new ControlTemplate(typeof(Button));
            var saveBorder = new FrameworkElementFactory(typeof(Border));
            saveBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            saveBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            var savePresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            savePresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            savePresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            saveBorder.AppendChild(savePresenter);
            saveTemplate.VisualTree = saveBorder;
            saveBtn.Template = saveTemplate;

            saveBtn.Click += (s, e) => SaveSettings();
            layout.Children.Add(saveBtn);

            modalBorder.Child = layout;
            overlay.Children.Add(modalBorder);
            return overlay;
        }

        private TextBlock CreateSettingLabel(string text) {
            return new TextBlock {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 200)),
                Margin = new Thickness(0, 0, 0, 4)
            };
        }

        private Button CreateIconButton(string icon, string tooltip, RoutedEventHandler onClick) {
            var btn = new Button {
                Content = icon,
                Width = 28,
                Height = 28,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 200)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = tooltip,
                Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 2, 0)
            };
            btn.Click += onClick;
            return btn;
        }

        private Button CreateActionButton(string content, string tooltip, RoutedEventHandler onClick) {
            var btn = new Button {
                Content = content,
                FontSize = 14,
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 220)),
                BorderThickness = new Thickness(0),
                ToolTip = tooltip,
                Cursor = Cursors.Hand
            };
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            btn.Template = template;
            btn.Click += onClick;
            return btn;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Event Handlers & Core Features
        // ══════════════════════════════════════════════════════════════════

        private void AttachEvents() {
            _inputBox.KeyDown += (s, e) => {
                if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) {
                    e.Handled = true;
                    SendMessage();
                }
            };

            this.PreviewKeyDown += (s, e) => {
                if (e.Key == Key.Escape) {
                    if (_settingsModal.Visibility == Visibility.Visible) {
                        HideSettings();
                        e.Handled = true;
                    } else {
                        PanicHide();
                        e.Handled = true;
                    }
                }
            };

            _audioService.OnSpeechRecognized += (recognizedText) => {
                Dispatcher.Invoke(() => {
                    if (!string.IsNullOrWhiteSpace(recognizedText)) {
                        string existing = _inputBox.Text.Trim();
                        if (string.IsNullOrEmpty(existing)) {
                            _inputBox.Text = recognizedText;
                        } else {
                            _inputBox.Text = existing + " " + recognizedText;
                        }
                        _inputBox.CaretIndex = _inputBox.Text.Length;
                    }
                });
            };

            _audioService.OnStatusChanged += (status) => {
                Dispatcher.Invoke(() => {
                    _audioStatusText.Text = status;
                });
            };
        }

        private async void SendMessage() {
            string text = _inputBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(text) || _isProcessing) return;

            _isProcessing = true;
            _sendBtn.IsEnabled = false;

            // Add user message to UI
            AddMessageBubble("user", text);
            _inputBox.Text = "";

            // Add placeholder AI bubble
            var aiBubble = AddMessageBubble("model", "Thinking...");

            try {
                string reply = await _geminiService.SendMessageAsync(
                    _settings.ApiKey,
                    _settings.Model,
                    _settings.SystemPrompt,
                    text,
                    _chatHistory
                );

                aiBubble.Text = reply;
                _chatHistory.Add(new ChatMessage("user", text));
                _chatHistory.Add(new ChatMessage("model", reply));
            } catch (Exception ex) {
                aiBubble.Text = "⚠ " + ex.Message;
                aiBubble.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            } finally {
                _isProcessing = false;
                _sendBtn.IsEnabled = true;
                _chatScrollViewer.ScrollToEnd();
                _inputBox.Focus();
            }
        }

        private async void ToggleAudioCapture() {
            if (_audioService.IsCapturing) {
                // Stop capturing
                byte[] wavData = _audioService.StopCapturing();
                _audioStatusBar.Visibility = Visibility.Collapsed;
                _micPulseRing.Visibility = Visibility.Collapsed;

                if (wavData != null && wavData.Length > 44 && !string.IsNullOrWhiteSpace(_settings.ApiKey)) {
                    _audioStatusText.Text = "Transcribing with Gemini...";
                    _audioStatusBar.Visibility = Visibility.Visible;
                    try {
                        string transcript = await _geminiService.TranscribeAudioAsync(_settings.ApiKey, _settings.Model, wavData);
                        if (!string.IsNullOrWhiteSpace(transcript)) {
                            string existing = _inputBox.Text.Trim();
                            if (string.IsNullOrEmpty(existing)) {
                                _inputBox.Text = transcript;
                            } else {
                                _inputBox.Text = existing + " " + transcript;
                            }
                            _inputBox.CaretIndex = _inputBox.Text.Length;
                        }
                    } catch { }
                    finally {
                        _audioStatusBar.Visibility = Visibility.Collapsed;
                    }
                }
            } else {
                // Start capturing
                _audioStatusBar.Visibility = Visibility.Visible;
                _micPulseRing.Visibility = Visibility.Visible;
                _audioService.StartCapturing();
            }
        }

        private TextBlock AddMessageBubble(string role, string content) {
            var container = new Border {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(10)
            };

            var stack = new StackPanel();

            var label = new TextBlock {
                Text = role == "user" ? "YOU" : "GHOST AI",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var textBlock = new TextBlock {
                Text = content,
                FontSize = 12,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Segoe UI, Inter")
            };

            if (role == "user") {
                container.Background = new SolidColorBrush(Color.FromArgb(45, 124, 108, 240));
                container.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 124, 108, 240));
                container.BorderThickness = new Thickness(1);
                label.Foreground = new SolidColorBrush(Color.FromRgb(199, 210, 254));
                textBlock.Foreground = Brushes.White;
            } else {
                container.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                container.BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
                container.BorderThickness = new Thickness(1);
                label.Foreground = new SolidColorBrush(Color.FromRgb(165, 180, 252));
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(230, 230, 240));
            }

            stack.Children.Add(label);
            stack.Children.Add(textBlock);
            container.Child = stack;

            _messagesPanel.Children.Add(container);
            _chatScrollViewer.ScrollToEnd();
            return textBlock;
        }

        private void ToggleVisibility() {
            if (this.Visibility == Visibility.Visible) {
                this.Hide();
            } else {
                this.Show();
                if (_hwnd != IntPtr.Zero) {
                    NativeMethods.ApplyStealthMode(_hwnd);
                }
                this.Activate();
                _inputBox.Focus();
            }
        }

        private void PanicHide() {
            this.Hide();
        }

        private void ShowSettings() {
            _apiKeyBox.Password = _settings.ApiKey;
            _modelCombo.SelectedItem = _settings.Model;
            _systemPromptBox.Text = _settings.SystemPrompt;
            _opacitySlider.Value = _settings.Opacity;
            _alwaysOnTopCheck.IsChecked = _settings.AlwaysOnTop;
            _settingsModal.Visibility = Visibility.Visible;
        }

        private void HideSettings() {
            _settingsModal.Visibility = Visibility.Collapsed;
        }

        private void SaveSettings() {
            _settings.ApiKey = _apiKeyBox.Password.Trim();
            _settings.Model = (_modelCombo.SelectedItem != null) ? _modelCombo.SelectedItem.ToString() : "gemini-3.5-flash";
            _settings.SystemPrompt = _systemPromptBox.Text.Trim();
            _settings.Opacity = _opacitySlider.Value;
            _settings.AlwaysOnTop = _alwaysOnTopCheck.IsChecked == true;

            _settings.Save();

            this.Opacity = _settings.Opacity;
            this.Topmost = _settings.AlwaysOnTop;
            _modelBadge.Text = _settings.Model;

            UpdateStatusIndicator();
            HideSettings();
        }

        private void UpdateStatusIndicator() {
            if (!string.IsNullOrWhiteSpace(_settings.ApiKey)) {
                _statusDot.Fill = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Emerald Green
                _statusDot.ToolTip = "Gemini Connected & Stealth Active";
            } else {
                _statusDot.Fill = new SolidColorBrush(Color.FromRgb(251, 191, 36)); // Amber
                _statusDot.ToolTip = "No API Key (Click Settings ⚙)";
            }
        }
    }
}
