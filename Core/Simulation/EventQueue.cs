using RailSim.Core.Models;

namespace RailSim.Core.Simulation
{
    /// <summary>
    /// File de priorité pour les événements discrets de la simulation.
    /// Utilise un SortedSet avec un comparateur personnalisé pour garantir
    /// un ordre déterministe : temps ASC, priorité train DESC, ID train ASC (tiebreak).
    /// Cette structure permet une simulation à événements discrets efficace.
    /// </summary>
    public class EventQueue
    {
        private readonly SortedSet<SimEvent> _set = new(
            Comparer<SimEvent>.Create((a, b) =>
            {
                // 1. Trier par temps croissant
                int c = a.TimeMinutes.CompareTo(b.TimeMinutes);
                if (c != 0) return c;

                // 2. À même temps : priorité train décroissante (TGV avant TER)
                c = b.Train.Priority.CompareTo(a.Train.Priority);
                if (c != 0) return c;

                // 3. Tiebreak alphabétique sur l'ID pour éviter les doublons dans le SortedSet
                return string.Compare(a.Train.Id, b.Train.Id, StringComparison.Ordinal);
            })
        );

        /// <summary>Nombre d'événements actuellement dans la file.</summary>
        public int Count => _set.Count;

        /// <summary>Ajoute un événement dans la file (insertion triée automatique).</summary>
        public void Enqueue(SimEvent ev) => _set.Add(ev);

        /// <summary>
        /// Retire et retourne l'événement le plus prioritaire
        /// (le plus tôt dans le temps, puis le train le plus prioritaire).
        /// </summary>
        public SimEvent Dequeue()
        {
            var min = _set.Min;
            _set.Remove(min);
            return min;
        }

        /// <summary>Consulte le prochain événement sans le retirer de la file.</summary>
        public SimEvent Peek() => _set.Min;

        /// <summary>
        /// Retourne une copie des événements dans une fenêtre temporelle donnée.
        /// Utilisé par le ConflictDetector pour analyser les conflits futurs
        /// sans modifier la file d'événements.
        /// </summary>
        public List<SimEvent> Snapshot(double fromTime, double windowMinutes) =>
            _set.Where(e => e.TimeMinutes >= fromTime &&
                            e.TimeMinutes <= fromTime + windowMinutes)
                .ToList();

        /// <summary>
        /// Décale tous les événements futurs d'un train donné de deltaMinutes.
        /// Utilisé après résolution d'un conflit pour repousser le train retardé.
        /// Nécessite de retirer et réinsérer les événements pour maintenir le tri.
        /// </summary>
        public void ShiftTrainEvents(string trainId, double deltaMinutes)
        {
            var toShift = _set.Where(e => e.Train.Id == trainId).ToList();
            foreach (var ev in toShift)
            {
                _set.Remove(ev);
                ev.TimeMinutes += deltaMinutes;
                _set.Add(ev);
            }
        }
    }
}