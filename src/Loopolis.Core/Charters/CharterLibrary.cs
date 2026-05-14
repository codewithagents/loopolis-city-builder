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

    public static readonly IReadOnlyList<CharterDefinition> AllCityCharters = new[]
    {
        new CharterDefinition(CharterType.InnovationHub, "Innovation Hub",
            "Your city embraces density. Smart zoning fills every building to capacity.",
            "Residential capacity +20% · Tax revenue +8%", "City"),
        new CharterDefinition(CharterType.GreenCanopy, "Green Canopy",
            "Green infrastructure networks define your skyline. Parks and clean tech heal your city.",
            "Pollution impact ×0.5 · Park radius +2 tiles", "City"),
        new CharterDefinition(CharterType.TradeCorridors, "Trade Corridors",
            "Markets and trade routes flow through your city. The commercial sector never sleeps.",
            "Commercial growth +25% · Land value +8%", "City"),
    };

    public static readonly IReadOnlyList<CharterDefinition> AllMetropolisCharters = new[]
    {
        new CharterDefinition(CharterType.NexusCity, "Nexus City",
            "Every district is connected. A web of services and infrastructure that no other city can match.",
            "Service radius +5 · Residential capacity +30% · Tax revenue +8%", "Metropolis"),
        new CharterDefinition(CharterType.GreenUtopia, "Green Utopia",
            "Industrial age is over. Your city runs on clean energy, parks, and clear skies.",
            "Pollution impact ×0.1 · Park happiness ×3.0 · Park radius +3 tiles", "Metropolis"),
        new CharterDefinition(CharterType.EmpireOfSteel, "Empire of Steel",
            "Your factories define the continent's economy. What your city makes, the world buys.",
            "Industrial growth ×1.60 · +25 jobs per factory tile · Commercial growth ×1.30", "Metropolis"),
    };

    public static CharterDefinition? Find(CharterType type) =>
        AllTownCharters.FirstOrDefault(c => c.Type == type)
        ?? AllCityCharters.FirstOrDefault(c => c.Type == type)
        ?? AllMetropolisCharters.FirstOrDefault(c => c.Type == type);
}
