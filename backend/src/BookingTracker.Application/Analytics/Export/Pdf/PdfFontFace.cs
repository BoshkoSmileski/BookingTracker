namespace BookingTracker.Application.Analytics.Export.Pdf;

/// <summary>
/// The two faces the report uses. Both are PDF base-14 standard fonts
/// (Helvetica and Helvetica-Bold), which every conforming reader already has -
/// so nothing is embedded, and the produced file has no font dependency at all.
/// </summary>
public enum PdfFontFace
{
    Regular,
    Bold,
}
