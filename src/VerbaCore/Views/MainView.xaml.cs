using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Markdig;
using Markdig.Wpf;
using VerbaCore.ViewModels;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using SystemColors = System.Windows.SystemColors;

namespace VerbaCore.Views;

public partial class MainView : UserControl
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseSupportedExtensions()
        .Build();

    private static readonly FontFamily AppFont = (FontFamily)Application.Current.FindResource("AppContentFont");
    private const double BaseFontSize = 14.0;
    private const int RenderThrottleMs = 200;

    private MainViewModel? _vm;
    private DateTime _lastRenderTime = DateTime.MinValue;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public void FocusInput()
    {
        InputTextBox.Focus();
        InputTextBox.SelectAll();
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var vm = DataContext as MainViewModel;
            vm?.LookupCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as MainViewModel;
        if (_vm != null)
            _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm == null) return;

        if (e.PropertyName == nameof(MainViewModel.ResultText))
        {
            if (_vm.IsLoading)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastRenderTime).TotalMilliseconds >= RenderThrottleMs)
                {
                    _lastRenderTime = now;
                    RenderPlainText(_vm.ResultText);
                }
            }
            else if (!string.IsNullOrEmpty(_vm.ResultText))
            {
                RenderPlainText(_vm.ResultText);
            }
            else
            {
                ResultViewer.Document = new FlowDocument();
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsLoading) && !_vm.IsLoading)
        {
            if (!string.IsNullOrEmpty(_vm.ResultText))
                RenderMarkdown(_vm.ResultText);
        }
    }

    private void RenderMarkdown(string markdown)
    {
        try
        {
            var doc = Markdig.Wpf.Markdown.ToFlowDocument(markdown, MarkdownPipeline);
            ApplyThemeToDocument(doc);
            ResultViewer.Document = doc;
        }
        catch
        {
            RenderPlainText(markdown);
        }
    }

    private void RenderPlainText(string text)
    {
        var textBrush = TryFindResource("TextFillColorPrimaryBrush") as Brush ?? SystemColors.WindowTextBrush;
        var doc = new FlowDocument(new Paragraph(new Run(text)))
        {
            Foreground = textBrush,
            FontSize = BaseFontSize,
            FontFamily = AppFont,
            PagePadding = new Thickness(0)
        };
        ResultViewer.Document = doc;
    }

    #region Theme-aware FlowDocument styling

    private record struct ThemeColors(
        SolidColorBrush Heading, SolidColorBrush HeadingSub,
        SolidColorBrush Bold, SolidColorBrush Italic,
        SolidColorBrush CodeFg, SolidColorBrush CodeBg,
        SolidColorBrush BlockquoteBg, SolidColorBrush BlockquoteBorder,
        SolidColorBrush HrColor);

    private static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static ThemeColors DarkTheme() => new(
        Heading:         Frozen(0xFF, 0x60, 0xCD, 0xFF),
        HeadingSub:      Frozen(0xFF, 0x90, 0xD0, 0xF0),
        Bold:            Frozen(0xFF, 0xFF, 0xFF, 0xFF),
        Italic:          Frozen(0xFF, 0xCC, 0xDD, 0xFF),
        CodeFg:          Frozen(0xFF, 0xE0, 0x90, 0xFF),
        CodeBg:          Frozen(0x30, 0xFF, 0xFF, 0xFF),
        BlockquoteBg:    Frozen(0x18, 0x80, 0xC0, 0xFF),
        BlockquoteBorder:Frozen(0x60, 0x60, 0xCD, 0xFF),
        HrColor:         Frozen(0x30, 0xFF, 0xFF, 0xFF));

    private static ThemeColors LightTheme() => new(
        Heading:         Frozen(0xFF, 0x00, 0x5A, 0x9E),
        HeadingSub:      Frozen(0xFF, 0x1A, 0x6C, 0xB0),
        Bold:            Frozen(0xFF, 0x1A, 0x1A, 0x2E),
        Italic:          Frozen(0xFF, 0x55, 0x55, 0x77),
        CodeFg:          Frozen(0xFF, 0x9B, 0x28, 0xB9),
        CodeBg:          Frozen(0x12, 0x00, 0x00, 0x00),
        BlockquoteBg:    Frozen(0x0C, 0x00, 0x60, 0xC0),
        BlockquoteBorder:Frozen(0x60, 0x00, 0x5A, 0x9E),
        HrColor:         Frozen(0x20, 0x00, 0x00, 0x00));

    private void ApplyThemeToDocument(FlowDocument doc)
    {
        var textBrush = TryFindResource("TextFillColorPrimaryBrush") as Brush ?? SystemColors.WindowTextBrush;

        bool isDark = textBrush is SolidColorBrush scb
            && (scb.Color.R + scb.Color.G + scb.Color.B) > 384;

        var c = isDark ? DarkTheme() : LightTheme();

        doc.Foreground = textBrush;
        doc.FontSize = BaseFontSize;
        doc.FontFamily = AppFont;
        doc.PagePadding = new Thickness(0);

        foreach (var block in doc.Blocks)
            ApplyThemeToBlock(block, c);
    }

    private static void ApplyThemeToBlock(Block block, in ThemeColors c)
    {
        if (block is Paragraph p)
        {
            var pFontSize = p.FontSize;
            bool isHeading = !double.IsNaN(pFontSize) && Math.Abs(pFontSize - BaseFontSize) > 0.5;

            if (isHeading)
            {
                double ratio = pFontSize / 12.0; // Markdig default base ~12
                p.FontSize = BaseFontSize * Math.Max(ratio, 1.15);
                p.FontWeight = FontWeights.Bold;
                p.Margin = new Thickness(0, 14, 0, 6);
                p.Foreground = ratio > 1.3 ? c.Heading : c.HeadingSub;
            }

            // Detect horizontal rule: empty paragraph with only whitespace or "———"
            if (p.Inlines.Count == 0)
            {
                p.BorderBrush = c.HrColor;
                p.BorderThickness = new Thickness(0, 0, 0, 1);
                p.Margin = new Thickness(0, 12, 0, 12);
            }

            foreach (var inline in p.Inlines)
                ApplyThemeToInline(inline, c);
        }
        else if (block is Section section)
        {
            // Blockquotes — left accent border + subtle background
            section.Background = c.BlockquoteBg;
            section.BorderBrush = c.BlockquoteBorder;
            section.BorderThickness = new Thickness(3, 0, 0, 0);
            section.Padding = new Thickness(14, 10, 14, 10);
            section.Margin = new Thickness(0, 8, 0, 8);

            foreach (var sBlock in section.Blocks)
                ApplyThemeToBlock(sBlock, c);
        }
        else if (block is System.Windows.Documents.List list)
        {
            list.Margin = new Thickness(16, 4, 0, 4);
            foreach (var item in list.ListItems)
                foreach (var itemBlock in item.Blocks)
                    ApplyThemeToBlock(itemBlock, c);
        }
        else if (block is System.Windows.Documents.Table table)
        {
            table.Foreground = c.Bold;
        }
    }

    private static void ApplyThemeToInline(Inline inline, in ThemeColors c)
    {
        if (inline is Bold bold)
        {
            bold.Foreground = c.Bold;
            foreach (var child in bold.Inlines)
                ApplyThemeToInline(child, c);
        }
        else if (inline is Italic italic)
        {
            italic.Foreground = c.Italic;
            foreach (var child in italic.Inlines)
                ApplyThemeToInline(child, c);
        }
        else if (inline is Run run && run.Background != null)
        {
            // Inline code
            run.Foreground = c.CodeFg;
            run.Background = c.CodeBg;
            run.FontFamily = (FontFamily)Application.Current.FindResource("AppCodeFont");
        }
        else if (inline is Span span)
        {
            foreach (var child in span.Inlines)
                ApplyThemeToInline(child, c);
        }
    }

    #endregion
}
