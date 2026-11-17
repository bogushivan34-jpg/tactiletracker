// A slim, rounded overlay-style scrollbar (closer to the browser look than the classic WPF one).
using System.Windows;
using System.Windows.Markup;

namespace Tactile
{
    public static class SlimScrollBar
    {
        const string Xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='ScrollBar'>
  <Setter Property='Width' Value='12'/>
  <Setter Property='MinWidth' Value='12'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Grid Background='Transparent' x:Name='Root'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton>
              <RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/>
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/>
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
              <Thumb Focusable='False'>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Border x:Name='Bar' Background='#CBD5E1' CornerRadius='3' Width='6' HorizontalAlignment='Right' Margin='0,4,3,4'/>
                    <ControlTemplate.Triggers>
                      <Trigger Property='IsMouseOver' Value='True'>
                        <Setter TargetName='Bar' Property='Background' Value='#94A3B8'/>
                        <Setter TargetName='Bar' Property='Width' Value='8'/>
                      </Trigger>
                      <Trigger Property='IsDragging' Value='True'>
                        <Setter TargetName='Bar' Property='Background' Value='#64748B'/>
                        <Setter TargetName='Bar' Property='Width' Value='8'/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

        static Style style;

        public static Style Style()
        {
            if (style == null)
            {
                style = (Style)XamlReader.Parse(Xaml);
                style.Seal();
            }
            return style;
        }
    }
}
