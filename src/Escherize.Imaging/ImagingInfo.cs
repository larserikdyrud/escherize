namespace Escherize.Imaging;

/// <summary>
/// Static information about the imaging front end (PNG to contour, SPEC §4.1).
/// </summary>
/// <remarks>Placeholder introduced in phase F0; the decoder arrives in phase F1.</remarks>
public static class ImagingInfo
{
    /// <summary>Pixels with an alpha value below this threshold count as background (SPEC §4.1).</summary>
    public const int AlphaBackgroundThreshold = 128;
}
