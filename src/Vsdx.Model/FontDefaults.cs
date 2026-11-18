namespace Vsdx.Model;

/// <summary>
/// The font a diagram gets when neither the object nor the diagram names one.
///
/// Left empty, the writer emits no Font cell and the package inherits whatever the reader machine
/// uses. That is not reproducible, and for Chinese labels the inherited face may not contain the
/// glyphs at all — a diagram that looks correct on the machine that made it and wrong elsewhere.
///
/// Microsoft YaHei is chosen because it covers Latin and CJK in one family, which keeps mixed
/// labels on a single face instead of falling back mid-string. Where it is absent, the consumer
/// substitutes; naming a sensible family is still better than naming none, because the intent
/// travels with the file.
/// </summary>
public static class FontDefaults
{
    /// <summary>Family requested when a diagram declares no font.</summary>
    public const string Family = "Microsoft YaHei";
}
