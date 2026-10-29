using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace MacUI
{
    // Colors follow the Windows light/dark setting, like macOS does.
    internal static class Theme
    {
        public static readonly bool Dark = ReadDark();
        public static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public static readonly Color MenuBarBg = C(Dark ? "#C81C1C1E" : "#D6F5F5F7");
        public static readonly Color Text = C(Dark ? "#F2FFFFFF" : "#E0000000");
        public static readonly Color DockBg = C(Dark ? "#B82C2C2E" : "#BFF5F5F7");
        public static readonly Color DockBorder = C(Dark ? "#33FFFFFF" : "#26000000");
        public static readonly Color PanelBg = C(Dark ? "#F52A2A2C" : "#F7F6F6F6");
        public static readonly Color PanelBorder = C(Dark ? "#33FFFFFF" : "#1F000000");
        public static readonly Color Hover = C(Dark ? "#26FFFFFF" : "#17000000");
        public static readonly Color Separator = C(Dark ? "#2EFFFFFF" : "#21000000");
        public static readonly Color Accent = C("#FF2F6FEB");

        public static Brush B(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        static Color C(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        static bool ReadDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("SystemUsesLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static ResourceDictionary CreateResources()
        {
            string xaml = Xaml
                .Replace("$PanelBg$", PanelBg.ToString())
                .Replace("$PanelBorder$", PanelBorder.ToString())
                .Replace("$Text$", Text.ToString())
                .Replace("$Hover$", Hover.ToString())
                .Replace("$Sep$", Separator.ToString())
                .Replace("$Accent$", Accent.ToString());
            return (ResourceDictionary)XamlReader.Parse(xaml);
        }

        const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='PanelBg' Color='$PanelBg$'/>
  <SolidColorBrush x:Key='PanelBorder' Color='$PanelBorder$'/>
  <SolidColorBrush x:Key='Text' Color='$Text$'/>
  <SolidColorBrush x:Key='Hover' Color='$Hover$'/>
  <SolidColorBrush x:Key='Sep' Color='$Sep$'/>
  <SolidColorBrush x:Key='Accent' Color='$Accent$'/>

  <Style TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Margin='10' Padding='5' CornerRadius='9' MinWidth='210'
                  Background='{StaticResource PanelBg}' BorderBrush='{StaticResource PanelBorder}' BorderThickness='1'>
            <Border.Effect>
              <DropShadowEffect BlurRadius='16' ShadowDepth='4' Direction='270' Opacity='0.3'/>
            </Border.Effect>
            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Border x:Name='Bd' CornerRadius='5' Padding='6,3,12,4' Background='Transparent'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='18'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto'/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name='Check' Text='✓' Visibility='Hidden' VerticalAlignment='Center'/>
              <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='False' VerticalAlignment='Center'/>
              <TextBlock Grid.Column='2' Text='{TemplateBinding InputGestureText}' Margin='28,0,0,0' Opacity='0.5' VerticalAlignment='Center'/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Check' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{StaticResource Accent}'/>
              <Setter Property='Foreground' Value='White'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.4'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'>
          <Border Height='1' Margin='8,5' Background='{StaticResource Sep}'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='SpotItem' TargetType='ListBoxItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBoxItem'>
          <Border x:Name='Bd' CornerRadius='8' Padding='10,6' Background='Transparent'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{StaticResource Hover}'/>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{StaticResource Accent}'/>
              <Setter Property='Foreground' Value='White'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <DataTemplate x:Key='SpotTemplate'>
    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='46'/>
        <ColumnDefinition Width='*'/>
      </Grid.ColumnDefinitions>
      <Image Source='{Binding Icon}' Width='32' Height='32' HorizontalAlignment='Left' RenderOptions.BitmapScalingMode='HighQuality'/>
      <TextBlock Text='{Binding Glyph}' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='22' Width='32'
                 TextAlignment='Center' HorizontalAlignment='Left' VerticalAlignment='Center'/>
      <StackPanel Grid.Column='1' VerticalAlignment='Center'>
        <TextBlock Text='{Binding Title}' FontSize='15' TextTrimming='CharacterEllipsis'/>
        <TextBlock Text='{Binding Subtitle}' FontSize='12' Opacity='0.65' TextTrimming='CharacterEllipsis'/>
      </StackPanel>
    </Grid>
  </DataTemplate>
</ResourceDictionary>";
    }

    internal static class Menus
    {
        public static MenuItem Add(ContextMenu menu, string header, Action action, string gesture = null)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
            item.Click += (s, e) =>
            {
                try { action(); }
                catch (Exception ex) { App.Log(ex); }
            };
            menu.Items.Add(item);
            return item;
        }
    }

    internal static class Shell
    {
        public static void Open(string target, string args = null)
        {
            try
            {
                if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || target.StartsWith("::"))
                {
                    Process.Start("explorer.exe", "\"" + target + "\"");
                    return;
                }
                var psi = new ProcessStartInfo(target) { UseShellExecute = true };
                if (!string.IsNullOrEmpty(args)) psi.Arguments = args;
                if (File.Exists(target)) psi.WorkingDirectory = Path.GetDirectoryName(target);
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
        }

        public static void QuickSettings() { Native.SendKeys(Native.VK_LWIN, (byte)'A'); }
        public static void NotificationCenter() { Native.SendKeys(Native.VK_LWIN, (byte)'N'); }
        public static void StartMenu() { Native.SendKeys(Native.VK_LWIN); }
    }

    internal static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool Enabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue("MacUI") != null;
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) key.SetValue("MacUI", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
                    else key.DeleteValue("MacUI", false);
                }
            }
        }
    }
}
