namespace RailSim.Core.Models
{
    public enum TrainType { TGV, Intercites, TER, Fret }
    public enum TrainStatus { Waiting, Running, InStation, Delayed, Terminated }

    public class Train
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public TrainType Type { get; set; }
        public int MaxSpeedKmH { get; set; }
        public int Priority { get; set; }

        // Itinéraire planifié
        public List<string> PlannedRoute { get; set; } = new();
        public Dictionary<string, double> PlannedArrivalMinutes { get; set; } = new();
        public Dictionary<string, double> PlannedDepartureMinutes { get; set; } = new();

        // État temps réel
        public TrainStatus Status { get; set; } = TrainStatus.Waiting;
        public int RouteIndex { get; set; } = 0;
        public double CurrentDelayMinutes { get; set; } = 0;
        public double TotalDelayAccumulated { get; set; } = 0;

        // Métriques pour le bilan
        public int OvertakeCount { get; set; } = 0;
        public int WaitCount { get; set; } = 0;
        public List<string> ConflictLog { get; } = new();

        // Helpers lecture
        public string CurrentStationId =>
            RouteIndex < PlannedRoute.Count ? PlannedRoute[RouteIndex] : null;
        public string NextStationId =>
            RouteIndex + 1 < PlannedRoute.Count ? PlannedRoute[RouteIndex + 1] : null;

        // Priorité par défaut selon le type
        public static int DefaultPriority(TrainType t) => t switch
        {
            TrainType.TGV        => 4,
            TrainType.Intercites => 3,
            TrainType.TER        => 2,
            TrainType.Fret       => 1,
            _                    => 0
        };

        public override string ToString() =>
            $"[{Id}] {Name} | {Type} | {MaxSpeedKmH}km/h | priorité={Priority} | retard={CurrentDelayMinutes:F1}min";
    }
}