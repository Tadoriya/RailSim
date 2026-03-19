using RailSim.Core.Models;
using RailSim.Core.Optimization;

namespace RailSim.Core.Simulation
{
    /// <summary>
    /// Moteur principal de la simulation à événements discrets.
    /// Gère la boucle d'événements, le traitement des départs/arrivées,
    /// et déclenche la détection/résolution des conflits après chaque événement.
    /// </summary>
    public class Simulator
    {
        // ── Dépendances ───────────────────────────────────────
        private readonly RailNetwork      _network;   // Réseau ferroviaire (gares + segments)
        private readonly SimConfig        _config;    // Paramètres de simulation
        private readonly EventQueue       _queue = new(); // File d'événements triée par temps
        private readonly ConflictDetector _detector;  // Détecteur de conflits
        private readonly ConflictResolver _resolver;  // Résolveur de conflits (heuristiques)
        private readonly List<Train>      _trains = new(); // Trains actifs dans la simulation
        private int _conflictCounter = 1; // Compteur global pour les IDs de conflits HeadOn

        /// <summary>Statistiques de la simulation (retards, conflits, trains terminés).</summary>
        public SimStats Stats { get; } = new();

        /// <summary>Temps courant de la simulation en minutes depuis minuit.</summary>
        private double _now;

        public Simulator(RailNetwork network, SimConfig config = null)
        {
            _network  = network;
            _config   = config ?? new SimConfig();
            _detector = new ConflictDetector(network);
            _resolver = new ConflictResolver();
        }

        /// <summary>
        /// Ajoute un train à la simulation et planifie son premier événement de départ.
        /// </summary>
        public void AddTrain(Train train)
        {
            _trains.Add(train);
            if (train.PlannedRoute.Count > 0 &&
                train.PlannedDepartureMinutes.TryGetValue(train.PlannedRoute[0], out double dep))
            {
                _queue.Enqueue(new SimEvent
                {
                    TimeMinutes = dep,
                    Type        = EventType.TrainDeparture,
                    Train       = train,
                    Station     = _network.GetStation(train.PlannedRoute[0]),
                    Description = $"Départ initial {train.Name}"
                });
            }
        }

        // ── Boucle principale ─────────────────────────────────

        /// <summary>
        /// Lance la simulation. Dépile les événements un par un,
        /// les traite, puis vérifie et résout les conflits détectés.
        /// S'arrête quand la file est vide ou que l'heure limite est atteinte.
        /// </summary>
        public void Run()
        {
            _now = _config.StartTimeMinutes;
            PrintHeader();

            while (_queue.Count > 0)
            {
                var ev = _queue.Dequeue();
                _now = ev.TimeMinutes;
                if (_now > _config.EndTimeMinutes) break;

                ProcessEvent(ev);

                // Après chaque événement, analyser la fenêtre future pour détecter des conflits
                if (_config.EnableOptimization)
                    HandleConflicts();
            }

            Stats.Print(_trains, _resolver.ResolvedConflicts);
        }

        // ── Détection / résolution ────────────────────────────

        /// <summary>
        /// Analyse une fenêtre de 200 minutes à partir du temps courant,
        /// détecte les conflits potentiels et les résout via les heuristiques.
        /// Les conflits déjà traités (même couple de trains, même type, dans les 60 dernières minutes)
        /// sont ignorés pour éviter les résolutions en boucle.
        /// </summary>
        private void HandleConflicts()
        {
            var window    = _queue.Snapshot(_now, 200);
            var conflicts = _detector.DetectAll(window, _now);

            foreach (var conflict in conflicts)
            {
                if (AlreadyHandled(conflict)) continue;

                // Filtrer les conflits avec impact négligeable (< 0.5 min)
                double estimatedDelay = EstimateDelay(conflict);
                if (estimatedDelay <= 0.5) continue;

                Stats.ConflictCount++;
                double delay = _resolver.Resolve(conflict, _now);

                // Décaler tous les événements futurs du train retardé
                if (delay > 0)
                {
                    Train delayed = GetDelayedTrain(conflict);
                    if (delayed != null)
                        _queue.ShiftTrainEvents(delayed.Id, delay);
                }
            }
        }

        /// <summary>
        /// Estime le délai probable qu'un conflit va générer,
        /// selon son type et les vitesses des trains impliqués.
        /// Utilisé pour filtrer les conflits avec impact négligeable.
        /// </summary>
        private double EstimateDelay(Conflict conflict)
        {
            if (conflict.TrainA == null || conflict.TrainB == null) return 0;

            return conflict.Type switch
            {
                // Pour un Headway : délai = temps nécessaire pour que le train derrière
                // ne rattrape pas le train devant (minimum 2 min)
                ConflictType.Headway =>
                    Math.Max(2.0, HeuristicEngine.EstimateWaitNeeded(conflict.TrainB, conflict.TrainA)),

                // Pour un HeadOn : délai fixe = seuil minimal d'attente
                ConflictType.HeadOn =>
                    HeuristicEngine.MinWaitThreshold,

                // Pour un conflit de quai : délai selon capacité de dépassement
                ConflictType.PlatformConflict =>
                    conflict.Location?.HasOvertakingCapability == true ? 2.0 : 3.0,

                _ => 0
            };
        }

        /// <summary>
        /// Vérifie si un conflit a déjà été traité récemment (dans les 60 dernières minutes)
        /// pour le même couple de trains et le même type de conflit.
        /// Évite les résolutions en boucle sur un conflit persistant.
        /// </summary>
        private bool AlreadyHandled(Conflict c) =>
            _resolver.ResolvedConflicts.Any(r =>
                r.TrainA.Id == c.TrainA.Id &&
                r.TrainB.Id == c.TrainB.Id &&
                r.Type      == c.Type &&
                Math.Abs(r.DetectedAtMinutes - c.DetectedAtMinutes) < 60.0);

        /// <summary>
        /// Identifie quel train doit être retardé selon la stratégie de résolution choisie.
        /// - MinimalWait : le train le moins prioritaire attend
        /// - AntiDelayPropagation : le train avec le moins de retard cède
        /// </summary>
        private Train GetDelayedTrain(Conflict c) => c.Resolution switch
        {
            ResolutionStrategy.MinimalWait =>
                c.TrainA.Priority <= c.TrainB.Priority ? c.TrainA : c.TrainB,
            ResolutionStrategy.AntiDelayPropagation =>
                c.TrainA.CurrentDelayMinutes >= c.TrainB.CurrentDelayMinutes
                ? c.TrainB : c.TrainA,
            _ => null
        };

        // ── Traitement des événements ─────────────────────────

        /// <summary>Dispatch l'événement vers le bon gestionnaire selon son type.</summary>
        private void ProcessEvent(SimEvent ev)
        {
            switch (ev.Type)
            {
                case EventType.TrainDeparture: HandleDeparture(ev); break;
                case EventType.TrainArrival:   HandleArrival(ev);   break;
            }
        }

        /// <summary>
        /// Gère le départ d'un train depuis une gare.
        /// Si le segment suivant est une voie unique occupée, le train attend 2 minutes
        /// et un conflit HeadOn est enregistré (une seule fois par blocage).
        /// Sinon, le train s'engage sur le segment et un événement d'arrivée est planifié.
        /// </summary>
        private void HandleDeparture(SimEvent ev)
        {
            var train = ev.Train;
            var from  = ev.Station;

            // Si plus de gare suivante → terminus
            if (train.NextStationId == null) { Terminate(train, from); return; }

            var to  = _network.GetStation(train.NextStationId);
            var seg = _network.GetSegment(from.Id, to.Id);

            // ── Voie unique occupée → attente physique ────────
            if (seg.Type == TrackType.SingleTrack && seg.IsOccupied)
            {
                // Enregistrer le conflit HeadOn une seule fois par blocage
                bool alreadyReported = _resolver.ResolvedConflicts.Any(c =>
                    c.Type == ConflictType.HeadOn &&
                    c.TrainA.Id == train.Id &&
                    c.TrainB?.Id == seg.OccupiedBy?.Id);

                if (!alreadyReported)
                {
                    var headOnConflict = new Conflict
                    {
                        Id                = $"C{_conflictCounter++:D4}",
                        Type              = ConflictType.HeadOn,
                        TrainA            = train,
                        TrainB            = seg.OccupiedBy,
                        Location          = from,
                        DetectedAtMinutes = _now,
                        Resolution        = ResolutionStrategy.MinimalWait,
                        IsResolved        = true,
                        ResolutionDetails = $"{train.Id} attend → voie unique occupée par {seg.OccupiedBy?.Id}"
                    };

                    Stats.ConflictCount++;
                    _resolver.ResolvedConflicts.Add(headOnConflict);

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"   [{headOnConflict.Id}] à {SimStats.ToHour(_now)} | HeadOn → MinimalWait");
                    Console.WriteLine($"     {headOnConflict.ResolutionDetails}");
                    Console.ResetColor();
                }

                // Attente de 2 minutes, re-planifier le départ
                double wait = 2.0;
                train.CurrentDelayMinutes   += wait;
                train.TotalDelayAccumulated += wait;
                train.WaitCount++;

                _queue.Enqueue(new SimEvent
                {
                    TimeMinutes = _now + wait,
                    Type        = EventType.TrainDeparture,
                    Train       = train,
                    Station     = from
                });
                return;
            }

            // ── Départ normal ─────────────────────────────────
            from.TrainsPresent.Remove(train);

            // Marquer le segment voie unique comme occupé
            if (seg.Type == TrackType.SingleTrack)
            { seg.IsOccupied = true; seg.OccupiedBy = train; }

            train.Status = TrainStatus.Running;

            double travelTime  = seg.TravelTimeMinutes(train.MaxSpeedKmH);
            double arrivalTime = _now + travelTime;

            // Planifier l'arrivée à la gare suivante
            _queue.Enqueue(new SimEvent
            {
                TimeMinutes = arrivalTime,
                Type        = EventType.TrainArrival,
                Train       = train,
                Station     = to,
                Segment     = seg
            });

            Console.ResetColor();
            Console.Write($"  {SimStats.ToHour(ev.TimeMinutes)} | {ev.Train.Id,-8} | ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"Depart de {from.Name} vers {to.Name} (il faut {travelTime:F0}min pour arriver)");
            Console.ResetColor();
        }

        /// <summary>
        /// Gère l'arrivée d'un train en gare.
        /// Libère le segment voie unique, vérifie la disponibilité d'un quai,
        /// met à jour le retard réel, puis planifie le prochain départ après le temps d'arrêt.
        /// </summary>
        private void HandleArrival(SimEvent ev)
        {
            var train = ev.Train;
            var sta   = ev.Station;
            var seg   = ev.Segment;

            // Libérer le segment voie unique occupé par ce train
            if (seg?.IsOccupied == true && seg.OccupiedBy == train)
            { seg.IsOccupied = false; seg.OccupiedBy = null; }

            // Quai plein → attente de 3 minutes avant re-tentative
            if (!sta.HasFreePlatform())
            {
                double wait = 3.0;
                train.CurrentDelayMinutes   += wait;
                train.TotalDelayAccumulated += wait;
                _queue.Enqueue(new SimEvent
                {
                    TimeMinutes = _now + wait,
                    Type        = EventType.TrainArrival,
                    Train       = train,
                    Station     = sta,
                    Segment     = seg
                });
                return;
            }

            sta.TrainsPresent.Add(train);
            train.Status = TrainStatus.InStation;
            train.RouteIndex++;

            // Calculer le retard réel par rapport à l'horaire planifié
            if (train.PlannedArrivalMinutes.TryGetValue(sta.Id, out double planned))
            {
                double realDelay = _now - planned;
                if (realDelay > train.CurrentDelayMinutes)
                {
                    train.CurrentDelayMinutes   = realDelay;
                    train.TotalDelayAccumulated = Math.Max(train.TotalDelayAccumulated, realDelay);
                }
            }

            Console.ResetColor();
            Console.Write($"  {SimStats.ToHour(_now)} | {ev.Train.Id,-8} | ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"arrivé à {sta.Name} (retard={Math.Round(train.CurrentDelayMinutes):F0}min)");
            Console.ResetColor();

            // Planifier le prochain départ si le train n'est pas à son terminus
            if (train.NextStationId != null)
            {
                double dwell   = DwellTime(train);
                // Le temps de départ intègre le dwell 
                double depTime = _now + dwell ;
                _queue.Enqueue(new SimEvent
                {
                    TimeMinutes = depTime,
                    Type        = EventType.TrainDeparture,
                    Train       = train,
                    Station     = sta
                });
            }
            else Terminate(train, sta);
        }

        /// <summary>
        /// Termine la simulation d'un train : met à jour le retard final,
        /// enregistre dans les statistiques et affiche le résultat.
        /// </summary>
        private void Terminate(Train train, Station sta)
        {
            train.Status = TrainStatus.Terminated;
            sta.TrainsPresent.Remove(train);

            // Calcul du retard final par rapport à l'heure d'arrivée planifiée au terminus
            string lastSta = train.PlannedRoute[train.PlannedRoute.Count - 1];
            if (train.PlannedArrivalMinutes.TryGetValue(lastSta, out double plannedFinal))
            {
                double finalDelay = _now - plannedFinal;
                train.TotalDelayAccumulated = Math.Max(
                    train.TotalDelayAccumulated, Math.Max(0, finalDelay));
            }

            Stats.RecordTermination(train);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(
                $"  FIN {train.Name,-22} terminé à {SimStats.ToHour(_now)} " +
                $"| retard={train.TotalDelayAccumulated:F0}min");
            Console.ResetColor();
        }

        // ── Helpers ───────────────────────────────────────────

        /// <summary>
        /// Retourne le temps d'arrêt en gare (dwell time) selon le type de train.
        /// TGV=3min, Intercités=4min, TER=2min, Fret=10min (chargement/déchargement).
        /// </summary>
        private double DwellTime(Train t) => t.Type switch
        {
            TrainType.TGV        => 3.0,
            TrainType.Intercites => 4.0,
            TrainType.TER        => 2.0,
            TrainType.Fret       => 10.0,
            _                    => 3.0
        };

        /// <summary>Affiche l'en-tête de la simulation avec le nombre de trains et le mode.</summary>
        private void PrintHeader()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════════╗");
            Console.WriteLine("║   RailSim — Simulation + Optimisation    ║");
            Console.WriteLine("╚══════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine($"  Trains : {_trains.Count}  |  Optimisation : {(_config.EnableOptimization ? "ON" : "OFF")}");
            Console.WriteLine(new string('─', 44));
        }

        /// <summary>Retourne la liste des conflits résolus (pour sauvegarde BDD).</summary>
        public List<Conflict> GetResolvedConflicts() => _resolver.ResolvedConflicts;
    }
}