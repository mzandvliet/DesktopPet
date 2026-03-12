
using DrawBehindDesktopIcons;
using UnityEngine;

public class Desktop
{
    #region Screen To World Mapping

    public const float DPI = 96f;
    public const float CM_PER_INCH = 2.54f;
    public const float PIXELS_PER_CM = DPI / CM_PER_INCH; // ≈ 37.8 DPCM
    public const float PIXELS_PER_UNIT = (PIXELS_PER_CM * 100) / 16;

    static int GetTotalScreenHeight()
    {
        // For single monitor:
        return Screen.currentResolution.height;

        // Todo: For multiple monitors
        // return monitors.Max(m => m.Bottom) - monitors.Min(m => m.Top);
    }

    public static Vector3 WorldToScreen(Vector3 worldPos)
    {
        float screenX = worldPos.x * PIXELS_PER_UNIT;
        float screenY = GetTotalScreenHeight() - (worldPos.y * PIXELS_PER_UNIT);
        return new Vector3(screenX, screenY, worldPos.z);
    }

    public static Vector3 ScreenToWorld(Vector3 screenPos)
    {
        float worldX = screenPos.x / PIXELS_PER_UNIT;
        float worldY = (GetTotalScreenHeight() - screenPos.y) / PIXELS_PER_UNIT;
        return new Vector3(worldX, worldY, screenPos.z);
    }

    public static Vector3 ScreenToWorld(Vector2Int screenPos)
    {
        float worldX = screenPos.x / PIXELS_PER_UNIT;
        float worldY = (GetTotalScreenHeight() - screenPos.y) / PIXELS_PER_UNIT;
        return new Vector3(worldX, worldY, 0);
    }

    public static Rect ScreenToWorld(RECT screenRect)
    {
        /*
        Notes:

        Window RECT is specified with:
        y=0 is top
        x,y is top-left corner

        Unity Rect is specified with
        y=0 is bottom
        x,y is bottom-left corner
        */
        var rect = new Rect(
            screenRect.X / PIXELS_PER_UNIT,
            (GetTotalScreenHeight() - screenRect.Y - screenRect.Height) / PIXELS_PER_UNIT,
            screenRect.Width / PIXELS_PER_UNIT,
            screenRect.Height / PIXELS_PER_UNIT
        );

        return rect;
    }

    #endregion
}