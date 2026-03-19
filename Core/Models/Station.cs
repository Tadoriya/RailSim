namespace RailSim.Core.Models
{
    public class Station
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double PositionKm { get; set; }
        public int PlatformCount { get; set; }
        public bool HasOvertakingCapability { get; set; }

        // État en temps réel — mis à jour par le simulateur
        public List<Train> TrainsPresent { get; } = new();

        public bool HasFreePlatform() => TrainsPresent.Count < PlatformCount;

        public override string ToString() =>
            $"[{Id}] {Name} (km {PositionKm:F0}, quais:{PlatformCount}, dépassement:{HasOvertakingCapability})";
    }
}