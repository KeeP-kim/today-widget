using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace DeskWidget
{
    // Local templates: keep native themes from changing row insets and disclosure icons.
    internal static class DollarAnalysisStyles
    {
        private static readonly ResourceDictionary Resources = (ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <ControlTemplate x:Key='PredictionSmall' TargetType='{x:Type Button}'>
  <Border x:Name='Frame' Background='#16FFFFFF' BorderBrush='#28FFFFFF' BorderThickness='0.7' CornerRadius='3.5' Padding='{TemplateBinding Padding}'>
   <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
  </Border>
  <ControlTemplate.Triggers>
   <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Background' Value='#334DA3FF'/></Trigger>
   <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#4DA3FF'/></Trigger>
   <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='0.4'/></Trigger>
  </ControlTemplate.Triggers>
 </ControlTemplate>
 <ControlTemplate x:Key='PredictionDots' TargetType='{x:Type Button}'>
  <Border x:Name='Frame' Background='Transparent' BorderBrush='Transparent' BorderThickness='0.7' CornerRadius='3'>
   <StackPanel HorizontalAlignment='Center' VerticalAlignment='Center' IsHitTestVisible='False'>
    <Ellipse Width='1.5' Height='1.5' Margin='0,1' Fill='{TemplateBinding Foreground}'/>
    <Ellipse Width='1.5' Height='1.5' Margin='0,1' Fill='{TemplateBinding Foreground}'/>
    <Ellipse Width='1.5' Height='1.5' Margin='0,1' Fill='{TemplateBinding Foreground}'/>
   </StackPanel>
  </Border>
  <ControlTemplate.Triggers>
   <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Background' Value='#334DA3FF'/></Trigger>
   <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#4DA3FF'/></Trigger>
   <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='0.4'/></Trigger>
  </ControlTemplate.Triggers>
 </ControlTemplate>
 <ControlTemplate x:Key='SmallButton' TargetType='{x:Type Button}'>
  <Border x:Name='Frame' Background='#16FFFFFF' BorderBrush='#28FFFFFF' BorderThickness='1' CornerRadius='5' Padding='{TemplateBinding Padding}'>
   <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
  </Border>
  <ControlTemplate.Triggers>
   <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Background' Value='#334DA3FF'/></Trigger>
   <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#4DA3FF'/></Trigger>
   <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='0.4'/></Trigger>
  </ControlTemplate.Triggers>
 </ControlTemplate>
 <ControlTemplate x:Key='DisclosureHeader' TargetType='{x:Type ToggleButton}'>
  <Border x:Name='Frame' Background='Transparent' BorderBrush='Transparent' BorderThickness='1'
   CornerRadius='6' Padding='8,7' SnapsToDevicePixels='True'>
   <Grid>
    <Grid.ColumnDefinitions><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
    <Path x:Name='Chevron' Data='M 2,0 L 6,4 L 2,8' Width='8' Height='8'
     Stroke='#8A8A99' StrokeThickness='1.5' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
     HorizontalAlignment='Left' VerticalAlignment='Center'/>
    <ContentPresenter Grid.Column='1' ContentSource='Content' HorizontalAlignment='Stretch' VerticalAlignment='Center'/>
   </Grid>
  </Border>
  <ControlTemplate.Triggers>
   <Trigger Property='IsChecked' Value='True'><Setter TargetName='Chevron' Property='Data' Value='M 0,2 L 4,6 L 8,2'/></Trigger>
   <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Background' Value='#14FFFFFF'/></Trigger>
   <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#4DA3FF'/></Trigger>
   <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='0.5'/></Trigger>
  </ControlTemplate.Triggers>
 </ControlTemplate>
 <ControlTemplate x:Key='Disclosure' TargetType='{x:Type Expander}'>
  <StackPanel>
   <ToggleButton x:Name='HeaderSite' Content='{TemplateBinding Header}' Foreground='{TemplateBinding Foreground}'
    FontSize='{TemplateBinding FontSize}' FontFamily='{TemplateBinding FontFamily}' MinHeight='38'
    HorizontalContentAlignment='Stretch' Cursor='Hand' Template='{StaticResource DisclosureHeader}'
    IsChecked='{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'/>
   <ContentPresenter x:Name='EvidenceBody' ContentSource='Content' Visibility='Collapsed' Margin='27,0,9,10'/>
  </StackPanel>
  <ControlTemplate.Triggers>
   <Trigger Property='IsExpanded' Value='True'><Setter TargetName='EvidenceBody' Property='Visibility' Value='Visible'/></Trigger>
  </ControlTemplate.Triggers>
 </ControlTemplate>
 <ControlTemplate x:Key='ModeToggle' TargetType='{x:Type ToggleButton}'>
  <Border x:Name='Frame' Background='#0EFFFFFF' BorderBrush='Transparent' BorderThickness='1' CornerRadius='7' Padding='7,4'>
   <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
  </Border>
  <ControlTemplate.Triggers>
   <Trigger Property='IsChecked' Value='True'><Setter TargetName='Frame' Property='Background' Value='#332C86DC'/><Setter TargetName='Frame' Property='BorderBrush' Value='#4DA3FF'/></Trigger>
   <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Background' Value='#22FFFFFF'/></Trigger>
   <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#4DA3FF'/></Trigger>
   <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='0.5'/></Trigger>
  </ControlTemplate.Triggers>
 </ControlTemplate>
</ResourceDictionary>");
        internal static ControlTemplate Disclosure { get { return (ControlTemplate)Resources["Disclosure"]; } }
        internal static ControlTemplate ModeToggle { get { return (ControlTemplate)Resources["ModeToggle"]; } }
        internal static ControlTemplate SmallButton { get { return (ControlTemplate)Resources["SmallButton"]; } }
        internal static ControlTemplate PredictionSmall { get { return (ControlTemplate)Resources["PredictionSmall"]; } }
        internal static ControlTemplate PredictionDots { get { return (ControlTemplate)Resources["PredictionDots"]; } }
    }
}
