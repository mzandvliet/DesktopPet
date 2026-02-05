
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