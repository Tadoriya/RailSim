using RailSim.Core.Models;

namespace RailSim.Core.Simulation
{
    public class SimConfig
    {
        public double StartTimeMinutes       { get; set; } = 360;   // 6h00
        public double EndTimeMinutes         { get; set; } = 840;   // 14h00
        public bool   EnableOptimization     { get; set; } = true;
        public bool   VerboseLogging         { get; set; } = true;
    }
}