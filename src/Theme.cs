// 메뉴 다크 테마.
// WPF 기본 MenuItem 템플릿에는 흰색 아이콘 컬럼과 흰색 하위메뉴 배경이 박혀 있어서
// Background/Foreground 속성만 바꿔서는 어둡게 만들 수 없다. 템플릿 자체를 교체한다.
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;

namespace DeskWidget
{
    internal static class Theme
    {
        private const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <SolidColorBrush x:Key='MenuBg'    Color='#F52A2A31'/>
  <SolidColorBrush x:Key='MenuEdge'  Color='#33FFFFFF'/>
  <SolidColorBrush x:Key='MenuFg'    Color='#EDEDF2'/>
  <SolidColorBrush x:Key='MenuHover' Color='#26FFFFFF'/>
  <SolidColorBrush x:Key='MenuCheck' Color='#7FC4FF'/>
  <SolidColorBrush x:Key='MenuDim'   Color='#8A8A99'/>
  <SolidColorBrush x:Key='MenuSep'   Color='#22FFFFFF'/>

  <ControlTemplate x:Key='DarkMenuItem' TargetType='{x:Type MenuItem}'>
    <Grid>
      <Border x:Name='Bd' Background='Transparent' CornerRadius='5'
              Padding='7,5,9,5' SnapsToDevicePixels='True'>
        <Grid>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width='15'/>
            <ColumnDefinition Width='*'/>
            <ColumnDefinition Width='Auto'/>
          </Grid.ColumnDefinitions>
          <TextBlock x:Name='Chk' Grid.Column='0' Text='&#x2713;' FontSize='11'
                     Foreground='{StaticResource MenuCheck}' Visibility='Hidden'
                     VerticalAlignment='Center' HorizontalAlignment='Left'/>
          <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True'
                            VerticalAlignment='Center' Margin='2,0,0,0'/>
          <TextBlock x:Name='Arw' Grid.Column='2' Text='&#x25B8;' FontSize='10'
                     Foreground='{StaticResource MenuDim}' Visibility='Collapsed'
                     Margin='14,0,0,0' VerticalAlignment='Center'/>
        </Grid>
      </Border>

      <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='-2' VerticalOffset='-5'
             IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'
             AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
        <Border Background='{StaticResource MenuBg}' BorderBrush='{StaticResource MenuEdge}'
                BorderThickness='1' CornerRadius='9' Padding='4' Margin='0,0,12,12'>
          <Border.Effect>
            <DropShadowEffect BlurRadius='14' ShadowDepth='3' Direction='270' Opacity='0.5' Color='Black'/>
          </Border.Effect>
          <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
        </Border>
      </Popup>
    </Grid>

    <ControlTemplate.Triggers>
      <Trigger Property='Role' Value='SubmenuHeader'>
        <Setter TargetName='Arw' Property='Visibility' Value='Visible'/>
      </Trigger>
      <Trigger Property='IsChecked' Value='True'>
        <Setter TargetName='Chk' Property='Visibility' Value='Visible'/>
      </Trigger>
      <Trigger Property='IsHighlighted' Value='True'>
        <Setter TargetName='Bd' Property='Background' Value='{StaticResource MenuHover}'/>
      </Trigger>
      <Trigger Property='IsEnabled' Value='False'>
        <Setter Property='Foreground' Value='#6A6A76'/>
      </Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>

  <Style TargetType='{x:Type MenuItem}'>
    <Setter Property='Foreground' Value='{StaticResource MenuFg}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Template' Value='{StaticResource DarkMenuItem}'/>
  </Style>

  <Style TargetType='{x:Type ContextMenu}'>
    <Setter Property='Foreground' Value='{StaticResource MenuFg}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='HasDropShadow' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ContextMenu}'>
          <Border Background='{StaticResource MenuBg}' BorderBrush='{StaticResource MenuEdge}'
                  BorderThickness='1' CornerRadius='9' Padding='4' SnapsToDevicePixels='True'>
            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 얇고 어두운 스크롤바. 기본 스크롤바는 밝고 두꺼워 다크 카드에서 튄다. -->
  <Style TargetType='{x:Type ScrollBar}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Width' Value='6'/>
    <Setter Property='MinWidth' Value='6'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ScrollBar}'>
          <Grid Background='Transparent' SnapsToDevicePixels='True'>
            <Track x:Name='PART_Track' IsDirectionReversed='True' Focusable='False'>
              <Track.Thumb>
                <Thumb Focusable='False'>
                  <Thumb.Template>
                    <ControlTemplate TargetType='{x:Type Thumb}'>
                      <Border Width='4' CornerRadius='2' Background='#3E3E48'
                              HorizontalAlignment='Center' SnapsToDevicePixels='True'/>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False' IsTabStop='False'>
                  <RepeatButton.Template>
                    <ControlTemplate TargetType='{x:Type RepeatButton}'>
                      <Border Background='Transparent'/>
                    </ControlTemplate>
                  </RepeatButton.Template>
                </RepeatButton>
              </Track.IncreaseRepeatButton>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False' IsTabStop='False'>
                  <RepeatButton.Template>
                    <ControlTemplate TargetType='{x:Type RepeatButton}'>
                      <Border Background='Transparent'/>
                    </ControlTemplate>
                  </RepeatButton.Template>
                </RepeatButton>
              </Track.DecreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='{x:Type Separator}'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type Separator}'>
          <Border Height='1' Background='{StaticResource MenuSep}'
                  Margin='8,4,8,4' SnapsToDevicePixels='True'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

</ResourceDictionary>";

        public static void Apply(Application app)
        {
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(Xaml)))
                {
                    var dict = (ResourceDictionary)XamlReader.Load(ms);
                    app.Resources.MergedDictionaries.Add(dict);
                }
            }
            catch
            {
                // 테마 적용에 실패해도 위젯 자체는 동작해야 한다 (메뉴만 기본 모양이 된다)
            }
        }
    }
}
