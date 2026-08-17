using System;
using Godot;

public static class GameAccessibilityRuntime
{
    private static readonly string[] AnalogActions =
    {
        "move_forward", "move_backward", "move_left", "move_right",
        "ship_forward", "ship_reverse", "ship_strafe_left", "ship_strafe_right",
        "ship_pitch_up", "ship_pitch_down", "ship_yaw_left", "ship_yaw_right"
    };

    public static void ApplyInputMap(GameUserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        float deadZone = AccessibilityControlPolicy.NormalizeDeadZone(settings.GamepadDeadZone);
        foreach (string action in AnalogActions)
        {
            if (InputMap.HasAction(action))
            {
                InputMap.ActionSetDeadzone(action, deadZone);
            }
        }
    }

    public static Vector2 ReadVector(
        StringName negativeX,
        StringName positiveX,
        StringName negativeY,
        StringName positiveY,
        float responseExponent)
    {
        Vector2 value = Input.GetVector(negativeX, positiveX, negativeY, positiveY);
        float magnitude = Math.Clamp(value.Length(), 0.0f, 1.0f);
        if (magnitude <= 0.0001f)
        {
            return Vector2.Zero;
        }
        float shapedMagnitude = Math.Abs(
            AccessibilityControlPolicy.ShapeScalar(magnitude, responseExponent));
        return value.Normalized() * shapedMagnitude;
    }

    public static float ReadAxis(
        StringName negativeAction,
        StringName positiveAction,
        float responseExponent) =>
        AccessibilityControlPolicy.ShapeScalar(
            Input.GetAxis(negativeAction, positiveAction),
            responseExponent);

    public static float ReadStrength(StringName action, float responseExponent) =>
        AccessibilityControlPolicy.ShapeScalar(
            Input.GetActionStrength(action),
            responseExponent);
}
