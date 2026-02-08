using Rng = Unity.Mathematics.Random;

public static class RngManager
{
    private static Rng _rng = new Rng(1234);

    public static Rng Shared;

    static RngManager()
    {
        Shared = CreateRng();
    }


    // Note: only deterministic if calls come in deterministic order, which is not guaranteed between scripts
    public static Rng CreateRng()
    {
        return new Rng(_rng.NextUInt());
    }
}