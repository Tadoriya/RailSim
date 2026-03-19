using RailSim.Core.Models;

namespace RailSim.Core.Optimization
{
    /// <summary>
    /// Moteur heuristique de recherche opérationnelle.
    /// Calcule un score pour chaque stratégie de résolution disponible
    /// et retourne celle avec le coût minimal (minimisation du retard total).
    /// Les seuils sont configurables pour permettre l'ajustement des heuristiques.
    /// </summary>
    public static class HeuristicEngine
    {
        /// <summary>Seuil au-delà duquel une attente est pénalisée (en minutes).</summary>
        public static double MinWaitThreshold         { get; set; } = 8.0;

        /// <summary>Retard minimum pour activer l'anti-propagation (en minutes).</summary>
        public static double AntiPropagationThreshold { get; set; } = 5.0;

        /// <summary>
        /// Choisit la meilleure stratégie de résolution pour un conflit Headway.
        /// Compare les scores de MinimalWait et AntiDelayPropagation,
        /// et retourne la stratégie avec le score le plus bas (coût minimal).
        /// </summary>
        public static ResolutionStrategy BestStrategy(Conflict c)
        {
            double wait          = EstimateWaitNeeded(c.TrainB, c.TrainA);
            double scoreMinWait  = ScoreMinimalWait(wait);
            double scoreAntiProp = ScoreAntiPropagation(c);

            if (scoreAntiProp < scoreMinWait)
                return ResolutionStrategy.AntiDelayPropagation;

            return ResolutionStrategy.MinimalWait;
        }

        /// <summary>
        /// Calcule le score de la stratégie MinimalWait.
        /// Score = temps d'attente imposé au train bloquant.
        /// Pénalité de 50% si l'attente dépasse le seuil maximal.
        /// Un score faible = stratégie préférable.
        /// </summary>
        private static double ScoreMinimalWait(double wait)
        {
            wait = Math.Max(2.0, wait); // Attente minimum réaliste = 2 min
            if (wait > MinWaitThreshold)
                wait = wait * 1.5; // Pénalité si attente trop longue
            return wait;
        }

        /// <summary>
        /// Calcule le score de la stratégie AntiDelayPropagation.
        /// Retourne double.MaxValue si le retard du train A est faible
        /// (anti-propagation inutile dans ce cas).
        /// Score décroît avec le retard → plus le retard est élevé,
        /// plus cette stratégie est avantageuse.
        /// </summary>
        private static double ScoreAntiPropagation(Conflict c)
        {
            // Inutile si le retard accumulé est en dessous du seuil
            if (c.TrainA.CurrentDelayMinutes < AntiPropagationThreshold)
                return double.MaxValue;

            // Bénéfice proportionnel au retard déjà accumulé
            double benefit = c.TrainA.CurrentDelayMinutes * 0.5;
            return Math.Max(0, MinWaitThreshold - benefit);
        }

        /// <summary>
        /// Estime le temps d'attente nécessaire pour qu'un train rapide
        /// ne rattrape pas un train lent sur un segment typique de 30 km.
        /// Retourne 6 min minimum (headway de sécurité même à vitesse égale).
        /// </summary>
        public static double EstimateWaitNeeded(Train fast, Train slow)
        {
            // Même vitesse ou train "rapide" plus lent → headway minimum de sécurité
            if (fast.MaxSpeedKmH <= slow.MaxSpeedKmH)
                return 6.0;

            // Calculer l'écart de temps sur un segment typique
            const double typicalSegmentKm = 30.0;
            double t_slow = (typicalSegmentKm / slow.MaxSpeedKmH) * 60.0;
            double t_fast = (typicalSegmentKm / fast.MaxSpeedKmH) * 60.0;

            // Temps d'attente = différence de temps de parcours (min 6 min)
            return Math.Max(6.0, t_slow - t_fast);
        }
    }
}