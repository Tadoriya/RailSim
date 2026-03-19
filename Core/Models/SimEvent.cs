namespace RailSim.Core.Models
{
    public enum EventType
    {
        TrainDeparture,
        TrainArrival,
        TrainOvertake,
        TrainWaiting,
        ConflictDetected,
        ConflictResolved
    }

    /// Un événement discret dans la simulation.
    /// IComparable permet au SortedSet de trier par (temps, priorité).
    public class SimEvent : IComparable<SimEvent>
    {
        public double TimeMinutes { get; set; }
        public EventType Type { get; set; }
        public Train Train { get; set; }
        public Station Station { get; set; }
        public TrackSegment Segment { get; set; }
        public string Description { get; set; }

        public int CompareTo(SimEvent other)
        {
            int cmp = TimeMinutes.CompareTo(other.TimeMinutes);
            if (cmp != 0) return cmp;
            // À même temps : train plus prioritaire traité en premier
            cmp = other.Train.Priority.CompareTo(Train.Priority);
            if (cmp != 0) return cmp;
            // Tiebreak sur l'Id pour éviter les doublons dans le SortedSet
            return string.Compare(Train.Id, other.Train.Id, StringComparison.Ordinal);
        }
    }
}