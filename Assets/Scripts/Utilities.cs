
using UnityEngine;

public static class Mask
{
    public static bool IsBitSet(long mask, uint bits)
    {
        return (mask & bits) != 0;
    }

    public static long SetBit(long mask, uint bits)
    {
        return mask | bits;
    }

    public static long UnsetBit(long mask, uint bits)
    {
        return mask & (~bits);
    }
}

public static class ColorExtensions
{
    public static Color WithAlpha(this Color col, float alpha)
    {
        return new Color(col.r, col.g, col.b, alpha);
    }

    public static Color WithAlphaMultiplied(this Color color, float alphaMultiplier)
    {
        color.a *= alphaMultiplier;
        return color;
    }

    public static Color WithBrightness(this Color col, float brght)
    {
        return new Color(col.r * brght, col.g * brght, col.b * brght, col.a);
    }
}