namespace RailSim.Core.Models
{
    public class RailNetwork
    {
        public List<Station> Stations { get; } = new();
        public List<TrackSegment> Segments { get; } = new();

        public Station GetStation(string id) =>
            Stations.Find(s => s.Id == id)
            ?? throw new KeyNotFoundException($"Station inconnue : {id}");

        public TrackSegment GetSegment(string fromId, string toId) =>
            Segments.Find(s =>
                (s.From.Id == fromId && s.To.Id == toId) ||
                (s.To.Id == fromId   && s.From.Id == toId))
            ?? throw new KeyNotFoundException($"Segment introuvable : {fromId}→{toId}");
    }
}