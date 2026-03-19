using RailSim.Core.Models;

namespace RailSim.Core.Simulation
{
    public class SimStats
    {
        public int ConflictCount  { get; set; }
        public int CompletedTrains { get; set; }

        private readonly List<double> _finalDelays = new();

        public double AverageDelayMinutes =>
            _finalDelays.Count > 0 ? _finalDelays.Average() : 0;

        public void RecordTermination(Train train)
        {
            CompletedTrains++;
            _finalDelays.Add(train.TotalDelayAccumulated);
        }

        public void Print(List<Train> trains, List<Conflict> conflicts)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n" + new string('═', 50));
            Console.WriteLine("  BILAN");
            Console.WriteLine(new string('─', 50));
            Console.ResetColor();
            Console.WriteLine($"  Conflits détectés  : {ConflictCount}");
            Console.WriteLine($"  Retard moyen       : {AverageDelayMinutes:F1} min");
            Console.WriteLine($"  Trains terminés    : {CompletedTrains}/{trains.Count}");

            Console.WriteLine("\n  Par train :");
            foreach (var t in trains.OrderByDescending(x => x.TotalDelayAccumulated))
            {
                Console.ForegroundColor = t.TotalDelayAccumulated > 10
                    ? ConsoleColor.Red : ConsoleColor.Green;
                Console.WriteLine(
                $"    {t.Name,-22} retard={Math.Round(t.TotalDelayAccumulated):F0}min ");
            }

            if (conflicts.Count > 0)
            {
                Console.ResetColor();
                Console.WriteLine("\n  Stratégies utilisées :");
                foreach (var g in conflicts.GroupBy(c => c.Resolution).OrderByDescending(g => g.Count()))
                    Console.WriteLine($"    {g.Key,-28} : {g.Count()}×");
            }
            Console.ResetColor();
        }
        public static string ToHour(double minutes)
        {
            int h = (int)(minutes / 60) % 24;
            int m = (int)(minutes % 60);
            return $"{h:D2}h{m:D2}";
        }
    }
}