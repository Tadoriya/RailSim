using RailSim.Core.Models;
using RailSim.Core.Simulation;

namespace RailSim.Core.Optimization
{
    /// <summary>
    /// Applique la stratégie de résolution choisie par HeuristicEngine
    /// et met à jour les retards des trains concernés.
    /// Conserve l'historique de tous les conflits résolus.
    /// </summary>
    public class ConflictResolver
    {
        /// <summary>Historique de tous les conflits résolus durant la simulation.</summary>
        public List<Conflict> ResolvedConflicts { get; } = new();

        /// <summary>
        /// Résout un conflit : choisit la stratégie via HeuristicEngine,
        /// applique le délai correspondant et enregistre le conflit.
        /// Retourne le délai en minutes appliqué au train retardé.
        /// </summary>
        public double Resolve(Conflict conflict, double currentTime)
        {
            // Sélectionner la stratégie selon le type de conflit
            ResolutionStrategy strategy = conflict.Type switch
            {
                ConflictType.Headway          => HeuristicEngine.BestStrategy(conflict),
                ConflictType.HeadOn           => ResolutionStrategy.MinimalWait,
                ConflictType.PlatformConflict => ResolutionStrategy.MinimalWait,
                _                             => ResolutionStrategy.MinimalWait
            };

            conflict.Resolution = strategy;

            // Appliquer la stratégie et récupérer le délai généré
            double delay = strategy switch
            {
                ResolutionStrategy.MinimalWait          => ApplyMinimalWait(conflict),
                ResolutionStrategy.AntiDelayPropagation => ApplyAntiPropagation(conflict),
                _                                       => 0
            };

            conflict.DelayImpactMinutes = delay;
            conflict.IsResolved         = true;
            ResolvedConflicts.Add(conflict);
            PrintResolution(conflict);
            return delay;
        }

        /// <summary>
        /// Stratégie MinimalWait : le train bloquant (TrainA) attend
        /// le temps minimum nécessaire pour que le train prioritaire (TrainB) passe.
        /// Le délai est plafonné au seuil maximal configuré dans HeuristicEngine.
        /// </summary>
        private double ApplyMinimalWait(Conflict c)
        {
            Train waiter = c.TrainA; // Train qui attend
            Train passer = c.TrainB; // Train prioritaire qui passe

            double wait = HeuristicEngine.EstimateWaitNeeded(passer, waiter);
            wait = Math.Min(wait, HeuristicEngine.MinWaitThreshold); // Plafonner

            waiter.CurrentDelayMinutes   += wait;
            waiter.TotalDelayAccumulated += wait;
            waiter.WaitCount++;

            c.ResolutionDetails = $"{waiter.Id} attend {wait:F0}min → {passer.Id} prioritaire.";
            return wait;
        }

        /// <summary>
        /// Stratégie AntiDelayPropagation : le train le plus retardé obtient
        /// la priorité temporaire. L'autre train cède 2 minutes pour éviter
        /// que le retard se propage à la chaîne sur l'ensemble du réseau.
        /// </summary>
        private double ApplyAntiPropagation(Conflict c)
        {
            // Le train le plus retardé est protégé (priorité temporaire)
            Train delayed = c.TrainA.CurrentDelayMinutes >= c.TrainB.CurrentDelayMinutes
                            ? c.TrainA : c.TrainB;
            Train other   = delayed == c.TrainA ? c.TrainB : c.TrainA;

            double shield = Math.Min(2.0, HeuristicEngine.AntiPropagationThreshold);
            other.CurrentDelayMinutes   += shield;
            other.TotalDelayAccumulated += shield;

            c.ResolutionDetails =
                $"Anti-propagation : {delayed.Id} (retard={delayed.CurrentDelayMinutes:F0}min) " +
                $"priorité temporaire. {other.Id} cède {shield:F0}min.";
            return shield;
        }

        /// <summary>Affiche la résolution d'un conflit dans la console.</summary>
        private void PrintResolution(Conflict c)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"   [{c.Id}] à {SimStats.ToHour(c.DetectedAtMinutes)} | {c.Type} → {c.Resolution}");
            Console.WriteLine($"     {c.ResolutionDetails}");
            Console.ResetColor();
        }
    }
}