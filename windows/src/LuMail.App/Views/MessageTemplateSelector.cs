using LuMail.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LuMail.App.Views;

/// <summary>Nachricht oder Zwischenüberschrift („Weitere Treffer auf dem Server“).</summary>
public partial class MessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? MessageTemplate { get; set; }
    public DataTemplate? HeaderTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item) =>
        (item is MessageItem { IsHeader: true } ? HeaderTemplate : MessageTemplate) ?? MessageTemplate!;

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
