using AvalonDock.Layout;
using ParticleEmitterDesigner.ViewModels.Documents;
using ParticleEmitterDesigner.ViewModels.Tools;

namespace ParticleEmitterDesigner.Services;

/// <summary>
/// Controls where AvalonDock inserts tool windows and documents that are not
/// already present in the (possibly restored) layout. Anchorable tools are
/// routed to the dock matching their <see cref="ToolViewModel.PreferredLocation"/>
/// (left / right / bottom) and documents fall into the first available document
/// pane. The insert point is also used to copy the view-model title onto the
/// layout model so tab headers render correctly.
/// </summary>
public sealed class LayoutInitializer : ILayoutUpdateStrategy
{
    public bool BeforeInsertAnchorable(LayoutRoot layout, LayoutAnchorable anchorableToShow,
        ILayoutContainer destinationContainer)
    {
        if (anchorableToShow.Content is not ToolViewModel tool)
        {
            return false;
        }

        // Never hijack an anchorable that is being floated or moved by the user.
        if (destinationContainer?.FindParent<LayoutFloatingWindow>() is not null)
        {
            return false;
        }

        anchorableToShow.Title = tool.Title;

        LayoutAnchorablePane? pane = FindAnchorablePane(layout, tool.PreferredLocation);
        if (pane is null)
        {
            return false;
        }

        pane.Children.Add(anchorableToShow);
        return true;
    }

    public void AfterInsertAnchorable(LayoutRoot layout, LayoutAnchorable anchorableShown)
    {
    }

    public bool BeforeInsertDocument(LayoutRoot layout, LayoutDocument documentToShow,
        ILayoutContainer destinationContainer)
    {
        if (documentToShow.Content is EmitterDocumentViewModel document)
        {
            documentToShow.Title = document.Title;
        }

        LayoutDocumentPane? pane = layout.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault();
        if (pane is null)
        {
            return false;
        }

        pane.Children.Add(documentToShow);
        return true;
    }

    public void AfterInsertDocument(LayoutRoot layout, LayoutDocument documentShown)
    {
    }

    private static LayoutAnchorablePane? FindAnchorablePane(LayoutRoot layout, DockSide side)
    {
        string preferred = side switch
        {
            DockSide.Right => "RightPane",
            DockSide.Bottom => "BottomPane",
            _ => "LeftPane"
        };

        return layout.Descendents().OfType<LayoutAnchorablePane>()
                   .FirstOrDefault(pane => pane.Name == preferred)
               ?? layout.Descendents().OfType<LayoutAnchorablePane>().FirstOrDefault();
    }
}
