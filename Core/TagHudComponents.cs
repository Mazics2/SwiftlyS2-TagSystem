using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;

namespace TagSystem.Core;

/// <summary>
/// CustomHUD Panorama yonergelerini (MenuRawNode("customhud.panorama", ...))
/// Core.Menu bilesenlerine temiz sekilde ekleyen yardimci siniflar.
/// Diger renderer'lar (CenterHTML, Chat) bu dugumleri guvenle yoksayar.
/// </summary>
internal static class HudMenuNodeHelper
{
    public const string RendererKey = "customhud.panorama";

    public static MenuNode AppendDirective(MenuNode node, string? directive)
    {
        if (string.IsNullOrWhiteSpace(directive))
            return node;

        var raw = new MenuRawNode(RendererKey, directive);
        return node is MenuLineNode line
            ? line with { Children = [.. line.Children, raw] }
            : MenuLineNode.Of(node, raw);
    }

    public static string CleanValue(string? value, int maxLen = 40)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var cleaned = value
            .Replace(';', ',')
            .Replace('\t', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        return cleaned.Length > maxLen ? cleaned[..maxLen] + "..." : cleaned;
    }
}

internal sealed class HudTextComponent : TextComponent
{
    public string? HudDirective { get; init; }
    public Func<IMenuSession, string?>? HudDirectiveProvider { get; init; }

    public HudTextComponent(string text = "", string? id = null) : base(text, id)
    {
    }

    public override MenuNode Render(IMenuComponentRenderContext context)
    {
        var directive = HudDirectiveProvider?.Invoke(context.Session) ?? HudDirective;
        return HudMenuNodeHelper.AppendDirective(base.Render(context), directive);
    }
}

internal sealed class HudButtonComponent : ButtonComponent
{
    public string? HudDirective { get; init; }
    public Func<IMenuSession, string?>? HudDirectiveProvider { get; init; }

    public HudButtonComponent(
        string text = "",
        Func<MenuActionContext, ValueTask>? onSelect = null,
        string? id = null)
        : base(text, onSelect, id)
    {
    }

    public override MenuNode Render(IMenuComponentRenderContext context)
    {
        var directive = HudDirectiveProvider?.Invoke(context.Session) ?? HudDirective;
        return HudMenuNodeHelper.AppendDirective(base.Render(context), directive);
    }
}

internal sealed class HudSubmenuComponent : SubmenuComponent
{
    public string? HudDirective { get; init; }
    public Func<IMenuSession, string?>? HudDirectiveProvider { get; init; }

    public HudSubmenuComponent(string text, IMenu submenu, string? id = null)
        : base(text, submenu, id)
    {
    }

    public HudSubmenuComponent(string text, Func<IMenuSession, IMenu?> factory, string? id = null)
        : base(text, factory, id)
    {
    }

    public override MenuNode Render(IMenuComponentRenderContext context)
    {
        var directive = HudDirectiveProvider?.Invoke(context.Session) ?? HudDirective;
        return HudMenuNodeHelper.AppendDirective(base.Render(context), directive);
    }
}

internal sealed class HudToggleComponent : ToggleComponent
{
    public string? HudDirective { get; init; }
    public Func<IMenuSession, string?>? HudDirectiveProvider { get; init; }

    public HudToggleComponent(
        string text = "",
        bool defaultValue = false,
        Action<MenuValueChangedContext<bool>>? onChanged = null,
        string? id = null)
        : base(text, defaultValue, onChanged, id)
    {
    }

    public override MenuNode Render(IMenuComponentRenderContext context)
    {
        var directive = HudDirectiveProvider?.Invoke(context.Session) ?? HudDirective;
        return HudMenuNodeHelper.AppendDirective(base.Render(context), directive);
    }
}

internal sealed class HudInputComponent : InputComponent
{
    public string? HudDirective { get; init; }
    public Func<IMenuSession, string?>? HudDirectiveProvider { get; init; }

    public HudInputComponent(
        string text = "",
        string defaultValue = "",
        Func<string, bool>? validator = null,
        Action<MenuValueChangedContext<string>>? onChanged = null,
        string? id = null)
        : base(text, defaultValue, validator, onChanged, id)
    {
    }

    public override MenuNode Render(IMenuComponentRenderContext context)
    {
        var directive = HudDirectiveProvider?.Invoke(context.Session) ?? HudDirective;
        return HudMenuNodeHelper.AppendDirective(base.Render(context), directive);
    }
}

internal sealed class HudSliderComponent : SliderComponent
{
    public string? HudDirective { get; init; }

    public HudSliderComponent(
        string text,
        float min,
        float max,
        float defaultValue,
        float step = 1f,
        Action<MenuValueChangedContext<float>>? onChanged = null,
        string? id = null)
        : base(text, min, max, defaultValue, step, onChanged, id)
    {
    }

    public override MenuNode Render(IMenuComponentRenderContext context)
        => HudMenuNodeHelper.AppendDirective(base.Render(context), HudDirective);
}

internal sealed class HudSelectorComponent<T> : SelectorComponent<T>
{
    public string? HudDirective { get; init; }

    public HudSelectorComponent(
        string text,
        IReadOnlyList<T> choices,
        int defaultIndex = 0,
        Action<MenuValueChangedContext<T>>? onChanged = null,
        string? id = null)
        : base(text, choices, defaultIndex, onChanged, id)
    {
    }

    public override MenuNode Render(IMenuComponentRenderContext context)
        => HudMenuNodeHelper.AppendDirective(base.Render(context), HudDirective);
}

