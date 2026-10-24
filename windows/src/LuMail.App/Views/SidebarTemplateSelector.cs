using LuMail.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LuMail.App.Views;

/// <summary>Wählt die Vorlage einer Sidebar-Zeile nach ihrer Art.</summary>
public partial class SidebarTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SectionTemplate { get; set; }
    public DataTemplate? ProviderTemplate { get; set; }
    public DataTemplate? AccountTemplate { get; set; }
    public DataTemplate? FolderTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
    {
        var template = (item as SidebarItem)?.Kind switch
        {
            SidebarItemKind.Section => SectionTemplate,
            SidebarItemKind.Provider => ProviderTemplate,
            SidebarItemKind.Account => AccountTemplate,
            _ => FolderTemplate,
        };
        return template ?? FolderTemplate!;
    }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
