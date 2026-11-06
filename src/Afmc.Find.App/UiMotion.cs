using System.Windows;
using System.Windows.Media.Animation;

namespace Afmc.Find.App;

// UI-thread only, finite transitions. No render loop, timer or persistent subscription.
public static class UiMotion
{
    private static bool enabled=true;
    private static readonly Dictionary<UIElement,DoubleAnimation> active=[];
    public static bool Enabled
    {
        get=>enabled;
        set {
            enabled=value;
            if(!value)foreach(var element in active.Keys.ToArray())Stop(element);
        }
    }
    public static void Reveal(UIElement element)
    {
        Stop(element);
        if(!Enabled || !SystemParameters.ClientAreaAnimation || !element.IsVisible)return;
        var target=(double)element.GetAnimationBaseValue(UIElement.OpacityProperty);
        var animation=new DoubleAnimation(Math.Min(0.72,target),target,TimeSpan.FromMilliseconds(150)) {
            FillBehavior=FillBehavior.Stop,
            EasingFunction=new QuadraticEase {EasingMode=EasingMode.EaseOut}
        };
        active[element]=animation;
        element.IsVisibleChanged+=VisibilityChanged;
        animation.Completed+=(_,_)=>{
            if(active.TryGetValue(element,out var current)&&ReferenceEquals(current,animation))Stop(element);
        };
        element.BeginAnimation(UIElement.OpacityProperty,animation,HandoffBehavior.SnapshotAndReplace);
    }
    private static void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)
    {
        if(sender is UIElement element && !element.IsVisible)Stop(element);
    }
    private static void Stop(UIElement element)
    {
        if(!active.Remove(element))return;
        element.IsVisibleChanged-=VisibilityChanged;
        element.BeginAnimation(UIElement.OpacityProperty,null);
    }
}
