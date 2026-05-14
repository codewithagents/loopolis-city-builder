namespace Loopolis.Core.Charters;

public static class CharterLibrary
{
    public static readonly IReadOnlyList<CharterDefinition> AllTownCharters = new[]
    {
        new CharterDefinition(CharterType.Merchant, "Merchant Charter",
            "Your city's identity is built on commerce. Markets thrive here.",
            "Commercial growth ×1.30 · Land value +6%", "Town"),
        new CharterDefinition(CharterType.Industrial, "Industrial Charter",
            "The forge and the factory define your skyline. Workers flock here for steady jobs.",
            "Industrial growth ×1.35 · +10 jobs per factory tile", "Town"),
        new CharterDefinition(CharterType.Civic, "Civic Charter",
            "Your citizens demand well-run streets and good schools. This city is livable.",
            "Service radius +3 · Parks give 2× happiness", "Town"),
    };

    public static CharterDefinition? Find(CharterType type) =>
        AllTownCharters.FirstOrDefault(c => c.Type == type);
}
