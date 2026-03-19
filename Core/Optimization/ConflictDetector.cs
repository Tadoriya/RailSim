using RailSim.Core.Models;

namespace RailSim.Core.Optimization
{
    /// <summary>
    /// Analyse la file d'événements à venir et détecte les conflits potentiels.
    /// Quatre types de conflits sont détectés :
    /// - Headway    : deux trains trop proches sur le même segment
    /// - HeadOn     : deux trains en sens opposés sur une voie unique
    /// - Platform   : quai plein, train ne peut pas entrer en gare
    /// - Convergence: deux trains de vitesses différentes convergent vers la même gare
    /// </summary>
    public class ConflictDetector
    {
        private readonly RailNetwork _network;
        private int _counter = 0; // Compteur pour générer des IDs de conflits uniques

        /// <summary>Espacement minimum entre deux trains sur le même segment (en minutes).</summary>
        public double HeadwayMinimumMinutes { get; set; } = 5.0;

        public ConflictDetector(RailNetwork network)
        {
            _network = network;
        }

        /// <summary>
        /// Point d'entrée principal. Détecte tous les types de conflits
        /// sur la fenêtre d'événements fournie et retourne la liste complète.
        /// </summary>
        public List<Conflict> DetectAll(List<SimEvent> upcoming, double currentTime)
        {
            var conflicts = new List<Conflict>();
            conflicts.AddRange(DetectHeadway(upcoming, currentTime));
            conflicts.AddRange(DetectHeadOn(upcoming, currentTime));
            conflicts.AddRange(DetectPlatformConflicts(upcoming, currentTime));
            conflicts.AddRange(DetectConvergence(upcoming, currentTime));
            return conflicts;
        }

        // ── Rattrapage (Headway) ──────────────────────────────

        /// <summary>
        /// Détecte les conflits de rattrapage : deux trains sur le même segment
        /// avec un écart d'arrivée inférieur au headway minimum (5 min).
        /// Le train qui arrive en second (derrière) est TrainA (bloquant).
        /// Le train qui arrive en premier (devant) est TrainB (prioritaire).
        /// </summary>
        private List<Conflict> DetectHeadway(List<SimEvent> upcoming, double currentTime)
        {
            var conflicts = new List<Conflict>();

            // Grouper les arrivées par segment
            var bySegment = upcoming
                .Where(e => e.Type == EventType.TrainArrival && e.Segment != null)
                .GroupBy(e => e.Segment.Id);

            foreach (var group in bySegment)
            {
                // Trier par heure d'arrivée croissante
                var sorted = group.OrderBy(e => e.TimeMinutes).ToList();

                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    var first  = sorted[i];      // train devant (arrive en premier)
                    var second = sorted[i + 1];  // train derrière (arrive après)

                    double gap = second.TimeMinutes - first.TimeMinutes;

                    // Conflit si l'écart est inférieur ou égal au headway minimum
                    if (gap <= HeadwayMinimumMinutes)
                    {
                        conflicts.Add(new Conflict
                        {
                            Id            = $"C{++_counter:D4}",
                            Type          = ConflictType.Headway,
                            TrainA        = second.Train,  // derrière = bloquant
                            TrainB        = first.Train,   // devant   = prioritaire
                            Location      = second.Station ?? _network.Stations.FirstOrDefault(),
                            DetectedAtMinutes = currentTime
                        });
                    }
                }
            }
            return conflicts;
        }

        // ── Nez-à-nez sur voie unique ─────────────────────────

        /// <summary>
        /// Détecte les conflits nez-à-nez : deux trains circulant en sens opposés
        /// sur le même segment de voie unique avec un chevauchement temporel.
        /// La direction est déterminée par la gare d'arrivée de l'événement.
        /// </summary>
        private List<Conflict> DetectHeadOn(List<SimEvent> upcoming, double currentTime)
        {
            var conflicts = new List<Conflict>();

            foreach (var seg in _network.Segments.Where(s => s.Type == TrackType.SingleTrack))
            {
                // Tous les événements d'arrivée sur ce segment voie unique
                var onSeg = upcoming
                    .Where(e => e.Type == EventType.TrainArrival && e.Segment?.Id == seg.Id)
                    .ToList();

                if (onSeg.Count < 2) continue;

                // dir1 : trains allant dans le sens CHB→ANN (arrivent à seg.To)
                // dir2 : trains allant dans le sens ANN→CHB (arrivent à seg.From)
                var dir1 = onSeg.Where(e => e.Station?.Id == seg.To.Id).ToList();
                var dir2 = onSeg.Where(e => e.Station?.Id == seg.From.Id).ToList();

                if (dir1.Count == 0 || dir2.Count == 0) continue;

                foreach (var ev1 in dir1)
                {
                    foreach (var ev2 in dir2)
                    {
                        // Calculer les fenêtres temporelles d'occupation du segment
                        double travel1 = seg.TravelTimeMinutes(ev1.Train.MaxSpeedKmH);
                        double travel2 = seg.TravelTimeMinutes(ev2.Train.MaxSpeedKmH);

                        double dep1 = ev1.TimeMinutes - travel1; // entrée train1 sur segment
                        double arr1 = ev1.TimeMinutes;           // sortie train1 du segment
                        double dep2 = ev2.TimeMinutes - travel2;
                        double arr2 = ev2.TimeMinutes;

                        // Chevauchement temporel = collision potentielle sur voie unique
                        bool overlap = dep1 <= arr2 && dep2 <= arr1;
                        if (!overlap) continue;

                        // Éviter les doublons (même paire dans les deux sens)
                        if (conflicts.Any(c =>
                            (c.TrainA.Id == ev1.Train.Id && c.TrainB.Id == ev2.Train.Id) ||
                            (c.TrainA.Id == ev2.Train.Id && c.TrainB.Id == ev1.Train.Id)))
                            continue;

                        conflicts.Add(new Conflict
                        {
                            Id            = $"C{++_counter:D4}",
                            Type          = ConflictType.HeadOn,
                            TrainA        = ev1.Train,
                            TrainB        = ev2.Train,
                            Location      = seg.From,
                            DetectedAtMinutes = currentTime
                        });
                    }
                }
            }
            return conflicts;
        }

        // ── Conflit de quai ───────────────────────────────────

        /// <summary>
        /// Détecte les conflits de quai : une gare reçoit plus de trains
        /// que sa capacité en quais disponibles.
        /// Les trains en excès sont en conflit avec le premier arrivant.
        /// </summary>
        private List<Conflict> DetectPlatformConflicts(List<SimEvent> upcoming, double currentTime)
        {
            var conflicts = new List<Conflict>();

            foreach (var station in _network.Stations)
            {
                // Quais libres = capacité totale - trains actuellement présents
                int freeSlots = station.PlatformCount - station.TrainsPresent.Count;

                var arriving = upcoming
                    .Where(e => e.Type == EventType.TrainArrival && e.Station?.Id == station.Id)
                    .OrderBy(e => e.TimeMinutes)
                    .ToList();

                // Les trains au-delà de la capacité disponible sont en conflit
                for (int i = freeSlots; i < arriving.Count; i++)
                {
                    conflicts.Add(new Conflict
                    {
                        Id            = $"C{++_counter:D4}",
                        Type          = ConflictType.PlatformConflict,
                        TrainA        = arriving[i].Train,
                        TrainB        = arriving[0].Train,
                        Location      = station,
                        DetectedAtMinutes = currentTime
                    });
                }
            }
            return conflicts;
        }

        // ── Convergence inter-segments ────────────────────────

        /// <summary>
        /// Détecte les conflits de convergence : deux trains arrivant dans la même gare
        /// depuis des segments différents, avec un écart faible et des vitesses différentes,
        /// et se dirigeant vers la même prochaine destination.
        /// Filtre les fausses convergences (trains venant d'axes différents).
        /// </summary>
        private List<Conflict> DetectConvergence(List<SimEvent> upcoming, double currentTime)
        {
            var conflicts = new List<Conflict>();

            var byStation = upcoming
                .Where(e => e.Type == EventType.TrainArrival && e.Station != null)
                .GroupBy(e => e.Station.Id);

            foreach (var group in byStation)
            {
                var sorted = group.OrderBy(e => e.TimeMinutes).ToList();

                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    var first  = sorted[i];
                    var second = sorted[i + 1];

                    // Ignorer si même segment → déjà géré par DetectHeadway
                    if (first.Segment?.Id == second.Segment?.Id) continue;

                    double gap = second.TimeMinutes - first.TimeMinutes;

                    // Conflit seulement si : écart faible + train rapide derrière train lent
                    if (gap <= HeadwayMinimumMinutes &&
                        second.Train.MaxSpeedKmH > first.Train.MaxSpeedKmH)
                    {
                        // Même prochaine destination → ils vont se suivre sur le prochain segment
                        bool sameNextDestination =
                            first.Train.NextStationId != null &&
                            first.Train.NextStationId == second.Train.NextStationId;

                        if (!sameNextDestination) continue;

                        // Même origine (même axe) → sinon c'est une convergence normale
                        string fromFirst  = first.Segment?.From?.Id  ?? "";
                        string fromSecond = second.Segment?.From?.Id ?? "";
                        if (fromFirst != fromSecond) continue;

                        conflicts.Add(new Conflict
                        {
                            Id            = $"C{++_counter:D4}",
                            Type          = ConflictType.Headway,
                            TrainA        = second.Train,
                            TrainB        = first.Train,
                            Location      = second.Station,
                            DetectedAtMinutes = currentTime
                        });
                    }
                }
            }
            return conflicts;
        }
    }
}