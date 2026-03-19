namespace RailSim.Core.Models
{
    public enum TrackType { SingleTrack, DoubleTrack }

    public class TrackSegment
    {
        public string Id { get; set; }
        public Station From { get; set; }
        public Station To { get; set; }
        public double LengthKm { get; set; }
        public TrackType Type { get; set; }
        public int MaxSpeedKmH { get; set; } = 160;

        // Voie unique : occupation exclusive
        public bool IsOccupied { get; set; } = false;
        public Train OccupiedBy { get; set; }

        /// Temps de parcours réel selon la vitesse du train (minutes)
        public double TravelTimeMinutes(int trainSpeedKmH)
        {
            int effectiveSpeed = Math.Min(trainSpeedKmH, MaxSpeedKmH);
            return (LengthKm / effectiveSpeed) * 60.0;
        }

        public override string ToString() =>
            $"{From.Id}→{To.Id} ({LengthKm:F0}km, {Type}, max:{MaxSpeedKmH}km/h)";
    }
}