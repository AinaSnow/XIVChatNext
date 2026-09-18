using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using XIVChatStorage;

namespace XIVChat_Desktop;

public sealed class PrivateCardText : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, string language) {
        if (value is not CardFavorite card) return "";
        var display = ((App)Application.Current).Presentation;
        var context = display.Context(card.Source, card.OwnerKey);
        return (parameter as string) switch {
            "OwnerKey" => display.OwnerLabel(card.Source, card.OwnerKey),
            "Source" => display.Enabled ? "" : card.Source,
            "Note" => display.Text(card.Note, context),
            "Group" => display.Text(card.Group, context),
            "Tags" => display.Text(card.Tags, context),
            _ => display.Text(card.Name, context),
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
