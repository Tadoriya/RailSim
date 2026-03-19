using RailSim.Core.Database;
using RailSim.Core.Models;
using RailSim.Core.Simulation;

// Encodage UTF-8 pour afficher correctement les caractères spéciaux (accents, symboles)
Console.OutputEncoding = System.Text.Encoding.UTF8;

// ── 1. Connexion BDD ──────────────────────────────────────────
// Réinitialise et recharge les données SNCF à chaque lancement
// pour garantir un état propre de la base de données
using var db = new RailSimDatabase("railsim.db");
//db.Reset();
db.SeedRealData();

// ── 2. Charger réseau + trains depuis la BDD ──────────────────
var network   = db.LoadNetwork();
var allTrains = db.LoadTrains();

// ── 3. Menu principal ─────────────────────────────────────────
PrintBanner();

Console.WriteLine("\n  [1] Nouvelle simulation");
Console.WriteLine("  [2] Simulation de démonstration (conflits garantis)");
Console.Write("\n  Votre choix : ");

string mode = Console.ReadLine()?.Trim() ?? "1";

List<Train> chosenTrains;

if (mode == "2")
{
    // Mode démo : trains et horaires préconfigurés pour garantir des conflits
    chosenTrains = SimulationDemo(allTrains, network);
}
else
{
    // Mode libre : l'utilisateur choisit les trains et configure les itinéraires
    chosenTrains = ChooseTrains(allTrains);
    if (chosenTrains.Count == 0)
    {
        Console.WriteLine("  Aucun train sélectionné. Au revoir !");
        return;
    }
    ConfigureItineraries(chosenTrains, network);
}

// ── 4. Simulation ─────────────────────────────────────────────
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  Lancement de la simulation...");
Console.WriteLine(new string('─', 44));
Console.ResetColor();

// Configuration : simulation sur 24h (1440 min) avec optimisation activée
var config = new SimConfig
{
    StartTimeMinutes   = 0,
    EndTimeMinutes     = 1440,
    EnableOptimization = true,
    VerboseLogging     = true
};

var sim = new Simulator(network, config);
foreach (var train in chosenTrains)
    sim.AddTrain(train);

sim.Run();

// ── 5. Sauvegarder résultats ──────────────────────────────────
// Persiste les statistiques et les conflits résolus en base de données
int simId = db.SaveSimulationResult(sim.Stats, chosenTrains.Count);
db.SaveConflicts(simId, sim.GetResolvedConflicts());
Console.WriteLine($"\n  [DB] Résultats sauvegardés — simulation #{simId}");


// ══════════════════════════════════════════════════════════════
//  FONCTIONS
// ══════════════════════════════════════════════════════════════

/// <summary>Affiche la bannière d'accueil de l'application.</summary>
static void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("╔══════════════════════════════════════════╗");
    Console.WriteLine("║         RailSim — Configuration         ║");
    Console.WriteLine("╚══════════════════════════════════════════╝");
    Console.ResetColor();
}

// ── Étape 1 : choisir les trains ──────────────────────────────

/// <summary>
/// Affiche la liste de tous les trains disponibles et demande à l'utilisateur
/// de saisir les IDs des trains à simuler (séparés par des espaces ou "tous").
/// Retourne la liste des trains sélectionnés.
/// </summary>
static List<Train> ChooseTrains(List<Train> allTrains)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n  TRAINS DISPONIBLES");
    Console.WriteLine(new string('─', 44));
    Console.ResetColor();

    foreach (var t in allTrains)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"  {t.Id,-10}");
        Console.ResetColor();
        Console.Write($"  {t.Name,-35}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {t.MaxSpeedKmH}km/h  priorité={t.Priority}");
        Console.ResetColor();
    }

    Console.WriteLine();
    Console.WriteLine("  Entrez les IDs des trains à simuler.");
    Console.WriteLine("  Séparez par des espaces (ex: TGV6601 TER8310 IC3601)");
    Console.WriteLine("  Ou tapez 'tous' pour tous sélectionner.");
    Console.WriteLine();
    Console.Write("  Votre choix : ");

    string input = Console.ReadLine()?.Trim() ?? "";

    if (input.ToLower() == "tous")
        return allTrains;

    var chosen = new List<Train>();
    var ids    = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    foreach (var id in ids)
    {
        var train = allTrains.Find(t => t.Id == id);
        if (train != null)
            chosen.Add(train);
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ✗ Train '{id}' introuvable — ignoré.");
            Console.ResetColor();
        }
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"\n  {chosen.Count} train(s) sélectionné(s).");
    Console.ResetColor();

    return chosen;
}

// ── Étape 2 : configurer itinéraire + heure ───────────────────

/// <summary>
/// Pour chaque train sélectionné, demande à l'utilisateur de choisir
/// un sous-itinéraire (gare de départ → gare d'arrivée) et une heure de départ.
/// Reconstruit ensuite les horaires planifiés via RebuildTrain.
/// </summary>
static void ConfigureItineraries(List<Train> trains, RailNetwork network)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n  CONFIGURATION DES ITINÉRAIRES");
    Console.WriteLine(new string('─', 44));
    Console.ResetColor();

    foreach (var train in trains)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  ╔══ Train {trains.IndexOf(train) + 1}/{trains.Count} ══════════════════════════╗");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  ║  {train.Name} ({train.Id})");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  ╚══════════════════════════════════════════╝");
        Console.ResetColor();

        Console.Write("  Itinéraire disponible : ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(string.Join(" → ", train.PlannedRoute));
        Console.ResetColor();

        List<string> chosenRoute = ChooseRoute(train);
        double depMinutes        = ChooseDeparture(train);

        RebuildTrain(train, chosenRoute, depMinutes, network);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✓ {train.Id} : {string.Join("→", chosenRoute)} départ {SimStats.ToHour(depMinutes)}");
        Console.ResetColor();
    }
}

// ── Choisir le sous-trajet ────────────────────────────────────

/// <summary>
/// Demande à l'utilisateur de saisir un sous-itinéraire valide pour un train.
/// Valide que toutes les gares saisies appartiennent à l'itinéraire du train
/// et que leur ordre est cohérent (croissant ou décroissant dans la route).
/// Boucle jusqu'à obtenir une saisie valide.
/// </summary>
static List<string> ChooseRoute(Train train)
{
    while (true)
    {
        Console.Write("  Votre itinéraire (ex: LYO AIX) : ");
        string input = Console.ReadLine()?.Trim().ToUpper() ?? "";
        var stops    = input.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (stops.Count < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ✗ Il faut au moins 2 gares.");
            Console.ResetColor();
            continue;
        }

        // Vérifier que chaque gare saisie appartient à l'itinéraire du train
        bool valid = true;
        foreach (var stop in stops)
        {
            if (!train.PlannedRoute.Contains(stop))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ La gare '{stop}' n'est pas sur cet itinéraire.");
                Console.ResetColor();
                valid = false;
                break;
            }
        }
        if (!valid) continue;

        // Vérifier que l'ordre des gares est cohérent (sens normal ou sens inverse)
        var indices     = stops.Select(s => train.PlannedRoute.IndexOf(s)).ToList();
        bool isNormal   = IsIncreasing(indices);
        bool isReversed = IsDecreasing(indices);

        if (!isNormal && !isReversed)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ✗ L'ordre des gares n'est pas cohérent.");
            Console.ResetColor();
            continue;
        }

        return stops;
    }
}

// ── Choisir l'heure de départ ─────────────────────────────────

/// <summary>
/// Demande à l'utilisateur de saisir une heure de départ au format XXhXX.
/// Valide le format et les plages (heures 0-23, minutes 0-59).
/// Retourne l'heure en minutes depuis minuit.
/// Boucle jusqu'à obtenir une saisie valide.
/// </summary>
static double ChooseDeparture(Train train)
{
    while (true)
    {
        Console.Write("  Heure de départ (format XXhXX, ex: 06h30) : ");
        string input = Console.ReadLine()?.Trim() ?? "";

        if (input.Length == 5       &&
            int.TryParse(input[..2], out int h) &&
            input[2] == 'h'         &&
            int.TryParse(input[3..], out int m) &&
            h >= 0 && h <= 23       &&
            m >= 0 && m <= 59)
        {
            return h * 60.0 + m; // Convertir en minutes depuis minuit
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  ✗ Format invalide. Utilisez XXhXX (ex: 06h30).");
        Console.ResetColor();
    }
}

// ── Simulation de démonstration ───────────────────────────────

/// <summary>
/// Configure automatiquement une simulation de démonstration avec des horaires
/// précis pour garantir l'apparition des différents types de conflits :
/// - Headway + MinimalWait    : TGV6601 vs TGV6603 (1 min d'écart)
/// - AntiDelayPropagation     : TGV6605 protégé par TGV6603 retardé
/// - HeadOn sur voie unique   : TER8420 vs TER8421 sur CHB↔ANN
/// </summary>
static List<Train> SimulationDemo(List<Train> allTrains, RailNetwork network)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n  Chargement de la simulation de démonstration...");
    Console.ResetColor();

    var chosen = new List<Train>();

    // Helper local : retrouve un train par ID, reconstruit son itinéraire et l'ajoute
    void Add(string id, string[] stops, double depMin)
    {
        var t = allTrains.Find(x => x.Id == id);
        if (t == null) return;
        RebuildTrain(t, stops.ToList(), depMin, network);
        chosen.Add(t);
    }

    // ── Headway + MinimalWait ─────────────────────────────────
    // TGV6601 part à 06h00, TGV6603 part à 06h01 → gap=1min < headway=5min
    // → TGV6603 doit attendre (MinimalWait)
    Add("TGV6601", new[] { "PAR", "LYO", "AVG", "AIX", "MRS" }, 360); // 06h00
    Add("TGV6603", new[] { "PAR", "LYO", "AVG", "AIX", "MRS" }, 361); // 06h01

    // ── AntiDelayPropagation ──────────────────────────────────
    // TGV6605 part à 06h03, derrière TGV6603 déjà retardé
    // → AntiDelayPropagation protège TGV6605 de la propagation du retard
    Add("TGV6605", new[] { "PAR", "LYO" }, 363); // 06h03

    // ── HeadOn — voie unique CHB↔ANN ─────────────────────────
    // TER8420 part de LYO à 05h28 → sur S_CHB_ANN à partir de 06h21
    // TER8421 part d'ANN à 06h10 → sur S_CHB_ANN (sens inverse) de 06h10 à 06h32
    // → chevauchement temporel sur voie unique → HeadOn détecté
    Add("TER8420", new[] { "LYO", "CHB", "ANN" }, 328); // 05h28
    Add("TER8421", new[] { "ANN", "CHB", "LYO" }, 370); // 06h10

    // ── IC pour diversifier la simulation ────────────────────
    // Intercités Paris→Lyon via Dijon, itinéraire différent des TGV
    Add("IC3601",  new[] { "PAR", "DJN", "MAC", "LYO" }, 355); // 05h55

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"\n  {chosen.Count} trains configurés :");
    Console.WriteLine();
    foreach (var t in chosen)
    {
        double dep = t.PlannedDepartureMinutes.Values.FirstOrDefault();
        Console.WriteLine($"  ✓ {t.Id,-10} {t.Name,-35} départ {SimStats.ToHour(dep)}");
    }
    Console.ResetColor();

    return chosen;
}

// ── Reconstruire le train avec le nouveau trajet ──────────────

/// <summary>
/// Reconstruit les horaires planifiés d'un train pour un sous-itinéraire donné.
/// Calcule automatiquement les heures d'arrivée et de départ à chaque gare
/// en fonction des temps de parcours réels et des temps d'arrêt (dwell time).
/// Supporte les itinéraires en sens normal et en sens inverse.
/// </summary>
static void RebuildTrain(Train train, List<string> chosenStops,
                         double depMinutes, RailNetwork network)
{
    var fullRoute = new List<string>(train.PlannedRoute);

    int startIdx = fullRoute.IndexOf(chosenStops[0]);
    int endIdx   = fullRoute.IndexOf(chosenStops[chosenStops.Count - 1]);

    // Si la gare de départ est après la gare d'arrivée → sens inverse
    bool reversed = startIdx > endIdx;
    if (reversed)
    {
        fullRoute.Reverse();
        startIdx = fullRoute.IndexOf(chosenStops[0]);
        endIdx   = fullRoute.IndexOf(chosenStops[chosenStops.Count - 1]);
    }

    // Extraire le sous-itinéraire entre les deux gares choisies
    var subRoute = fullRoute.GetRange(startIdx, endIdx - startIdx + 1);

    // Réinitialiser les horaires du train
    train.PlannedRoute.Clear();
    train.PlannedDepartureMinutes.Clear();
    train.PlannedArrivalMinutes.Clear();

    double currentTime = depMinutes;

    for (int i = 0; i < subRoute.Count; i++)
    {
        string stId = subRoute[i];
        train.PlannedRoute.Add(stId);

        if (i == 0)
        {
            // Première gare : seulement l'heure de départ
            train.PlannedDepartureMinutes[stId] = currentTime;
        }
        else
        {
            // Gares intermédiaires et terminus : calculer arrivée + départ
            string prevId = subRoute[i - 1];
            var seg       = network.GetSegment(prevId, stId);
            double travel = seg.TravelTimeMinutes(train.MaxSpeedKmH);

            train.PlannedArrivalMinutes[stId] = currentTime + travel;

            if (i < subRoute.Count - 1)
            {
                // Gare intermédiaire : ajouter le dwell time avant de repartir
                double dwell  = GetDwellTime(train);
                currentTime  += travel + dwell;
                train.PlannedDepartureMinutes[stId] = currentTime;
            }
            else
            {
                // Terminus : pas de départ planifié
                currentTime += travel;
            }
        }
    }
}

// ── Helpers ───────────────────────────────────────────────────

/// <summary>
/// Vérifie que les indices d'une liste sont strictement croissants.
/// Utilisé pour valider que l'ordre des gares saisies correspond au sens normal.
/// </summary>
static bool IsIncreasing(List<int> list)
{
    for (int i = 1; i < list.Count; i++)
        if (list[i] <= list[i - 1]) return false;
    return true;
}

/// <summary>
/// Vérifie que les indices d'une liste sont strictement décroissants.
/// Utilisé pour valider que l'ordre des gares saisies correspond au sens inverse.
/// </summary>
static bool IsDecreasing(List<int> list)
{
    for (int i = 1; i < list.Count; i++)
        if (list[i] >= list[i - 1]) return false;
    return true;
}

/// <summary>
/// Retourne le temps d'arrêt en gare (dwell time) selon le type de train.
/// TGV=3min (arrêt court), Intercités=4min, TER=2min, Fret=10min (chargement).
/// Utilisé dans RebuildTrain pour calculer les horaires planifiés.
/// </summary>
static double GetDwellTime(Train t) => t.Type switch
{
    TrainType.TGV        => 3.0,
    TrainType.Intercites => 4.0,
    TrainType.TER        => 2.0,
    TrainType.Fret       => 10.0,
    _                    => 3.0
};