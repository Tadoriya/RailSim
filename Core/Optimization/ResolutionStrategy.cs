using RailSim.Core.Models;

namespace RailSim.Core.Optimization
{
    public enum ResolutionStrategy
    {
        MinimalWait,         // attente minimale
        AntiDelayPropagation // priorité anti-cascade
    }
}