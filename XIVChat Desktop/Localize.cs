using System;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace XIVChat_Desktop {
    /// <summary>Resource bindings that refresh on language changes without retaining closed UI.</summary>
    public static class Localize {
        public static readonly DependencyProperty TextProperty = Register("Text");
        public static readonly DependencyProperty ContentProperty = Register("Content");
        public static readonly DependencyProperty HeaderProperty = Register("Header");
        private static readonly ConditionalWeakTable<FrameworkElement, Registration> Registrations = new();

        public static string GetText(DependencyObject target) => (string)target.GetValue(TextProperty);
        public static void SetText(DependencyObject target, string key) => target.SetValue(TextProperty, key);
        public static string GetContent(DependencyObject target) => (string)target.GetValue(ContentProperty);
        public static void SetContent(DependencyObject target, string key) => target.SetValue(ContentProperty, key);
        public static string GetHeader(DependencyObject target) => (string)target.GetValue(HeaderProperty);
        public static void SetHeader(DependencyObject target, string key) => target.SetValue(HeaderProperty, key);

        private static DependencyProperty Register(string name) => DependencyProperty.RegisterAttached(
            name, typeof(string), typeof(Localize), new PropertyMetadata(null, (target, _) => {
                if (target is FrameworkElement element) Registrations.GetValue(element, e => new Registration(e)).Refresh();
            }));

        private sealed class Registration {
            private readonly FrameworkElement element;
            public Registration(FrameworkElement element) {
                this.element = element;
                element.Loaded += (_, _) => { LocalizationHelper.LanguageChanged -= Refresh; LocalizationHelper.LanguageChanged += Refresh; Refresh(); };
                element.Unloaded += (_, _) => LocalizationHelper.LanguageChanged -= Refresh;
                if (element.IsLoaded) LocalizationHelper.LanguageChanged += Refresh;
            }
            public void Refresh() {
                Apply(TextProperty, "Text"); Apply(ContentProperty, "Content"); Apply(HeaderProperty, "Header");
            }
            private void Apply(DependencyProperty property, string name) {
                if (element.GetValue(property) is string key && key.Length > 0)
                    element.GetType().GetProperty(name)!.SetValue(element, LocalizationHelper.GetString(key));
            }
        }

        public static void BindWindow(Window window, Action refresh) {
            LocalizationHelper.LanguageChanged += refresh;
            window.Closed += (_, _) => LocalizationHelper.LanguageChanged -= refresh;
            refresh();
        }
    }
}
