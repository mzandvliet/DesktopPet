

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

public static class Swizzle
{
    public static Vector2 xy (this Vector3 v)
    {
        return new Vector2(v.x, v.y);
    }

    public static Vector2 xz(this Vector3 v)
    {
        return new Vector2(v.x, v.z);
    }

    public static Vector2 yz(this Vector3 v)
    {
        return new Vector2(v.y, v.z);
    }
}