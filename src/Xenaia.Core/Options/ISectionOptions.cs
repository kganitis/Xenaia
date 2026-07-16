namespace Xenaia.Core.Options;

/// <summary>
/// An options class bound from a named configuration section.
/// The section name lives on the type so registration can never
/// drift from the class that owns it.
/// </summary>
public interface ISectionOptions
{
    static abstract string SectionName { get; }
}
