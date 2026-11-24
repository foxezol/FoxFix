using System.Windows.Markup;
using FoxFix.Core;

namespace FoxFix.App;

/// <summary>
/// {local:T Key} in XAML: the text of that key in the language the program shows (Strings.resx / Strings.en.resx).
/// The language is chosen before any window is built and a change restarts the program, so a plain value is enough.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
