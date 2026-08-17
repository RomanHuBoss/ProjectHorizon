using System;

public static class AccessibilityControlPolicy
{
    public const float DefaultGamepadDeadZone = 0.20f;
    public const float MinimumGamepadDeadZone = 0.05f;
    public const float MaximumGamepadDeadZone = 0.45f;
    public const float DefaultGamepadResponseExponent = 1.25f;
    public const float MinimumGamepadResponseExponent = 0.75f;
    public const float MaximumGamepadResponseExponent = 2.00f;
    public const float DefaultSubtitleScale = 1.00f;
    public const float MinimumSubtitleScale = 0.80f;
    public const float MaximumSubtitleScale = 1.50f;

    public static float NormalizeDeadZone(float value) =>
        Math.Clamp(value, MinimumGamepadDeadZone, MaximumGamepadDeadZone);

    public static float NormalizeResponseExponent(float value) =>
        Math.Clamp(value, MinimumGamepadResponseExponent, MaximumGamepadResponseExponent);

    public static float NormalizeSubtitleScale(float value) =>
        Math.Clamp(value, MinimumSubtitleScale, MaximumSubtitleScale);

    public static float ShapeScalar(float value, float responseExponent)
    {
        if (!float.IsFinite(value))
        {
            return 0.0f;
        }
        float magnitude = Math.Clamp(Math.Abs(value), 0.0f, 1.0f);
        if (magnitude <= 0.0001f)
        {
            return 0.0f;
        }
        float exponent = NormalizeResponseExponent(responseExponent);
        float shaped = MathF.Pow(magnitude, exponent);
        return MathF.CopySign(shaped, value);
    }

    public static string SeverityToken(double ratio)
    {
        if (!double.IsFinite(ratio))
        {
            return "?";
        }
        if (ratio <= 0.18)
        {
            return "CRIT";
        }
        if (ratio <= 0.35)
        {
            return "LOW";
        }
        return "OK";
    }
}
