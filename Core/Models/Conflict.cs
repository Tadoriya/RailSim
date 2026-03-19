using RailSim.Core.Optimization;

namespace RailSim.Core.Models
{
    public enum ConflictType
    {
        Headway,          // train rapide rattrape train lent
        HeadOn,           // nez-à-nez sur voie unique
        PlatformConflict  // quai plein
    }

    public class Conflict
    {
        public string Id { get; set; }
        public ConflictType Type { get; set; }
        public Train TrainA { get; set; }   // train "bloqué" souvent le rapide
        public Train TrainB { get; set; }   // train "bloquant" souvent le lent
        public Station Location { get; set; }
        public double DetectedAtMinutes { get; set; }

        // Rempli par ConflictResolver
        public ResolutionStrategy Resolution { get; set; }
        public double DelayImpactMinutes { get; set; }
        public bool IsResolved { get; set; } = false;
        public string ResolutionDetails { get; set; }
    }
}