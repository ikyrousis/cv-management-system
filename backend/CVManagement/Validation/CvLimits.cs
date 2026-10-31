namespace CVManagement.Validation;

/// Size and shape rules for CV uploads. The request pipeline enforces the byte cap on the way in and
/// the service checks the values it was handed, so each rule is stated once and holds on either side
/// of binding. Names are capped here rather than in the database because they are stored encrypted.
public static class CvLimits
{
    public const long MaxCvBytes = 20 * 1024 * 1024;

    public const int MaxCvFileNameChars = 255;

    public static string MaxCvSizeDescription => $"{MaxCvBytes / (1024 * 1024)} MB";
}
