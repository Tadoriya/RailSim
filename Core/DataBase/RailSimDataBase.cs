using Microsoft.Data.Sqlite;
using RailSim.Core.Models;
using RailSim.Core.Simulation;

namespace RailSim.Core.Database
{
    /// <summary>
    /// Couche d'accès aux données SQLite pour RailSim.
    /// Gère le schéma, le chargement des données SNCF réelles,
    /// le CRUD des entités (gares, segments, trains) et la persistance
    /// des résultats de simulation.
    /// </summary>
    public class RailSimDatabase : IDisposable
    {
        private readonly SqliteConnection _connection;

        /// <summary>
        /// Ouvre la connexion SQLite et initialise le schéma si nécessaire.
        /// </summary>
        public RailSimDatabase(string dbPath = "railsim.db")
        {
            _connection = new SqliteConnection($"Data Source={dbPath}");
            _connection.Open();
            InitSchema();
        }

        // ══════════════════════════════════════════
        //  1. SCHÉMA — création des tables
        // ══════════════════════════════════════════

        /// <summary>
        /// Crée les tables si elles n'existent pas encore.
        /// Idempotent : peut être appelé plusieurs fois sans erreur.
        /// Tables : stations, segments, trains, train_routes, sim_results, sim_conflicts.
        /// </summary>
        private void InitSchema()
        {
            ExecuteNonQuery(@"
                CREATE TABLE IF NOT EXISTS stations (
                    id          TEXT PRIMARY KEY,
                    name        TEXT NOT NULL,
                    position_km REAL NOT NULL,
                    platforms   INTEGER DEFAULT 4,
                    overtaking  INTEGER DEFAULT 1
                );
                CREATE TABLE IF NOT EXISTS segments (
                    id          TEXT PRIMARY KEY,
                    from_id     TEXT NOT NULL,
                    to_id       TEXT NOT NULL,
                    length_km   REAL NOT NULL,
                    max_speed   INTEGER NOT NULL,
                    track_type  TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS trains (
                    id          TEXT PRIMARY KEY,
                    name        TEXT NOT NULL,
                    type        TEXT NOT NULL,
                    max_speed   INTEGER NOT NULL,
                    priority    INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS train_routes (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    train_id    TEXT NOT NULL,
                    station_id  TEXT NOT NULL,
                    seq_order   INTEGER NOT NULL,
                    dep_minutes REAL DEFAULT 0,
                    arr_minutes REAL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS sim_results (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_at       TEXT DEFAULT (datetime('now')),
                    nb_conflicts INTEGER DEFAULT 0,
                    nb_overtakes INTEGER DEFAULT 0,
                    avg_delay    REAL DEFAULT 0,
                    nb_trains    INTEGER DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS sim_conflicts (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    sim_id       INTEGER,
                    conflict_id  TEXT,
                    type         TEXT,
                    train_a      TEXT,
                    train_b      TEXT,
                    station      TEXT,
                    resolution   TEXT,
                    delay_impact REAL,
                    details      TEXT
                );
            ");
        }

        // ══════════════════════════════════════════
        //  2. SEED — données initiales SNCF
        // ══════════════════════════════════════════

        /// <summary>
        /// Charge les données SNCF réelles si la base est vide.
        /// Insère 22 gares, 44 segments (22 + 22 inverses) et 22 trains.
        /// Ne recharge pas si les données sont déjà présentes (idempotent).
        /// </summary>
        public void SeedRealData()
        {
            if (CountRows("stations") > 0) return;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  [DB] Chargement des données SNCF...");
            Console.ResetColor();

            SeedStations();
            SeedSegments();
            SeedTrains();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  [DB] Données chargées avec succès.");
            Console.ResetColor();
        }

        /// <summary>
        /// Insère les 22 gares du réseau couvrant :
        /// LGV Sud-Est, Via Dijon, LGV Nord, LGV Atlantique,
        /// Rhône-Alpes et Puy-de-Dôme.
        /// </summary>
        private void SeedStations()
        {
            // ── LGV Sud-Est ───────────────────────────────────
            InsertStation("PAR", "Paris Gare de Lyon",       0,    16, true);
            InsertStation("LYO", "Lyon Part-Dieu",           512,  12, true);
            InsertStation("AVG", "Avignon TGV",              739,  4,  true);
            InsertStation("AIX", "Aix-en-Provence TGV",     783,  4,  true);
            InsertStation("MRS", "Marseille Saint-Charles",  863,  10, true);

            // ── Via Dijon (ligne classique) ───────────────────
            InsertStation("DJN", "Dijon-Ville",              315,  6,  true);
            InsertStation("MAC", "Mâcon-Loché TGV",          458,  4,  true);

            // ── LGV Nord ──────────────────────────────────────
            InsertStation("LIL", "Lille-Europe",             225,  8,  true);
            InsertStation("ARS", "Arras",                    178,  4,  true);

            // ── LGV Atlantique ────────────────────────────────
            InsertStation("PDM", "Paris Montparnasse",       0,    14, true);
            InsertStation("LEM", "Le Mans",                  204,  4,  true);
            InsertStation("NTS", "Nantes",                   385,  6,  true);
            InsertStation("BDX", "Bordeaux Saint-Jean",      581,  8,  true);

            // ── Rhône-Alpes ───────────────────────────────────
            InsertStation("GRE", "Grenoble",                 600,  6,  true);
            InsertStation("CHB", "Chambéry",                 560,  4,  true);
            InsertStation("ANN", "Annecy",                   590,  4,  true);
            InsertStation("VLS", "Valence",                  660,  4,  true);

            // ── Puy-de-Dôme ───────────────────────────────────
            InsertStation("CFD", "Clermont-Ferrand",         490,  6,  true);
            InsertStation("VHY", "Vichy",                    430,  4,  true);
            InsertStation("RIU", "Riom",                     460,  2,  false); // Pas de dépassement
            InsertStation("ISS", "Issoire",                  510,  2,  false);
            InsertStation("BRG", "Brioude",                  545,  2,  false);
        }

        /// <summary>
        /// Insère les segments du réseau. Chaque segment est inséré en double :
        /// le segment normal (A→B) et son inverse automatique (B→A, suffixe _R).
        /// Les voies uniques (SingleTrack) sont utilisées pour la détection HeadOn.
        /// </summary>
        private void SeedSegments()
        {
            // ── LGV Sud-Est (300 km/h, double voie) ──────────
            InsertSegment("S_PAR_LYO", "PAR", "LYO", 512, 300, "DoubleTrack");
            InsertSegment("S_LYO_AVG", "LYO", "AVG", 227, 300, "DoubleTrack");
            InsertSegment("S_AVG_AIX", "AVG", "AIX", 44,  300, "DoubleTrack");
            InsertSegment("S_AIX_MRS", "AIX", "MRS", 30,  160, "DoubleTrack");

            // ── Via Dijon (160 km/h, double voie) ────────────
            InsertSegment("S_PAR_DJN", "PAR", "DJN", 315, 160, "DoubleTrack");
            InsertSegment("S_DJN_MAC", "DJN", "MAC", 143, 160, "DoubleTrack");
            InsertSegment("S_MAC_LYO", "MAC", "LYO", 68,  160, "DoubleTrack");

            // ── LGV Nord (300 km/h) ───────────────────────────
            InsertSegment("S_PAR_ARS", "PAR", "ARS", 178, 300, "DoubleTrack");
            InsertSegment("S_ARS_LIL", "ARS", "LIL", 47,  300, "DoubleTrack");

            // ── LGV Atlantique (300 km/h) ─────────────────────
            InsertSegment("S_PDM_LEM", "PDM", "LEM", 204, 300, "DoubleTrack");
            InsertSegment("S_LEM_NTS", "LEM", "NTS", 181, 200, "DoubleTrack");
            InsertSegment("S_LEM_BDX", "LEM", "BDX", 377, 300, "DoubleTrack");

            // ── Rhône-Alpes ───────────────────────────────────
            InsertSegment("S_LYO_GRE", "LYO", "GRE", 107, 160, "DoubleTrack");
            InsertSegment("S_LYO_CHB", "LYO", "CHB", 102, 160, "DoubleTrack");
            InsertSegment("S_CHB_ANN", "CHB", "ANN", 45,  120, "SingleTrack"); // Voie unique CHB↔ANN
            InsertSegment("S_LYO_VLS", "LYO", "VLS", 100, 160, "DoubleTrack");
            InsertSegment("S_VLS_AVG", "VLS", "AVG", 127, 160, "DoubleTrack");

            // ── Puy-de-Dôme (voies uniques) ───────────────────
            InsertSegment("S_LYO_VHY", "LYO", "VHY", 150, 160, "DoubleTrack");
            InsertSegment("S_VHY_RIU", "VHY", "RIU", 30,  120, "SingleTrack"); // Voie unique
            InsertSegment("S_RIU_CFD", "RIU", "CFD", 15,  120, "SingleTrack"); // Voie unique
            InsertSegment("S_CFD_ISS", "CFD", "ISS", 35,  100, "SingleTrack"); // Voie unique
            InsertSegment("S_ISS_BRG", "ISS", "BRG", 30,  100, "SingleTrack"); // Voie unique
        }

        /// <summary>
        /// Insère les 22 trains avec leurs itinéraires planifiés.
        /// Couvre : LGV Sud-Est, Via Dijon, LGV Nord, LGV Atlantique,
        /// Rhône-Alpes, Puy-de-Dôme et Fret.
        /// Les horaires sont en minutes depuis minuit.
        /// </summary>
        private void SeedTrains()
        {
            // ── LGV SUD-EST — Paris ↔ Marseille ──────────────
            InsertTrain("TGV6601", "TGV 6601 Paris-Marseille",  "TGV", 300, 4);
            InsertRoute("TGV6601", new[]
            {
                ("PAR", 362.0,   0.0),
                ("LYO", 464.0, 462.0),
                ("AVG", 509.0, 507.0),
                ("AIX", 518.0, 516.0),
                ("MRS",   0.0, 527.0),
            });

            InsertTrain("TGV6602", "TGV 6602 Marseille-Paris",  "TGV", 300, 4);
            InsertRoute("TGV6602", new[]
            {
                ("MRS", 362.0,   0.0),
                ("AIX", 373.0, 371.0),
                ("AVG", 382.0, 380.0),
                ("LYO", 427.0, 425.0),
                ("PAR",   0.0, 527.0),
            });

            InsertTrain("TGV6603", "TGV 6603 Paris-Marseille",  "TGV", 300, 4);
            InsertRoute("TGV6603", new[]
            {
                ("PAR", 422.0,   0.0),
                ("LYO", 524.0, 522.0),
                ("AVG", 569.0, 567.0),
                ("AIX", 578.0, 576.0),
                ("MRS",   0.0, 587.0),
            });

            InsertTrain("TGV6604", "TGV 6604 Marseille-Paris",  "TGV", 300, 4);
            InsertRoute("TGV6604", new[]
            {
                ("MRS", 422.0,   0.0),
                ("AIX", 433.0, 431.0),
                ("AVG", 442.0, 440.0),
                ("LYO", 487.0, 485.0),
                ("PAR",   0.0, 587.0),
            });

            InsertTrain("TGV6605", "TGV 6605 Paris-Lyon",       "TGV", 300, 4);
            InsertRoute("TGV6605", new[]
            {
                ("PAR", 482.0,   0.0),
                ("LYO",   0.0, 584.0),
            });

            InsertTrain("TGV6606", "TGV 6606 Lyon-Paris",       "TGV", 300, 4);
            InsertRoute("TGV6606", new[]
            {
                ("LYO", 482.0,   0.0),
                ("PAR",   0.0, 584.0),
            });

            // ── VIA DIJON — Paris ↔ Lyon (ligne classique) ───
            InsertTrain("IC3601", "Intercités 3601 Paris-Lyon", "Intercites", 200, 3);
            InsertRoute("IC3601", new[]
            {
                ("PAR", 360.0,   0.0),
                ("DJN", 478.0, 476.0),
                ("MAC", 519.0, 517.0),
                ("LYO",   0.0, 545.0),
            });

            InsertTrain("IC3602", "Intercités 3602 Lyon-Paris", "Intercites", 200, 3);
            InsertRoute("IC3602", new[]
            {
                ("LYO", 360.0,   0.0),
                ("MAC", 385.0, 383.0),
                ("DJN", 428.0, 426.0),
                ("PAR",   0.0, 545.0),
            });

            // ── RHÔNE-ALPES — Lyon ↔ Grenoble ────────────────
            InsertTrain("TER8310", "TER 8310 Lyon-Grenoble",    "TER", 140, 2);
            InsertRoute("TER8310", new[]
            {
                ("LYO", 360.0,   0.0),
                ("GRE",   0.0, 406.0),
            });

            InsertTrain("TER8311", "TER 8311 Grenoble-Lyon",    "TER", 140, 2);
            InsertRoute("TER8311", new[]
            {
                ("GRE", 360.0,   0.0),
                ("LYO",   0.0, 406.0),
            });

            // ── RHÔNE-ALPES — Lyon ↔ Annecy (voie unique CHB↔ANN)
            InsertTrain("TER8420", "TER 8420 Lyon-Annecy",      "TER", 120, 2);
            InsertRoute("TER8420", new[]
            {
                ("LYO", 365.0,   0.0),
                ("CHB", 416.0, 414.0),
                ("ANN",   0.0, 439.0),
            });

            InsertTrain("TER8421", "TER 8421 Annecy-Lyon",      "TER", 120, 2);
            InsertRoute("TER8421", new[]
            {
                ("ANN", 360.0,   0.0),
                ("CHB", 383.0, 381.0),
                ("LYO",   0.0, 434.0),
            });

            // ── PUY-DE-DÔME — Lyon ↔ Clermont-Ferrand ────────
            InsertTrain("TER5201", "TER 5201 Lyon-Clermont",    "TER", 120, 2);
            InsertRoute("TER5201", new[]
            {
                ("LYO", 361.0,   0.0),
                ("VHY", 436.0, 434.0),
                ("RIU", 456.0, 454.0),
                ("CFD",   0.0, 471.0),
            });

            InsertTrain("TER5202", "TER 5202 Clermont-Lyon",    "TER", 120, 2);
            InsertRoute("TER5202", new[]
            {
                ("CFD", 360.0,   0.0),
                ("RIU", 375.0, 373.0),
                ("VHY", 395.0, 393.0),
                ("LYO",   0.0, 468.0),
            });

            // ── PUY-DE-DÔME — Clermont ↔ Brioude ─────────────
            InsertTrain("TER5301", "TER 5301 Clermont-Brioude", "TER", 100, 2);
            InsertRoute("TER5301", new[]
            {
                ("CFD", 480.0,   0.0),
                ("ISS", 501.0, 500.0),
                ("BRG",   0.0, 519.0),
            });

            InsertTrain("TER5302", "TER 5302 Brioude-Clermont", "TER", 100, 2);
            InsertRoute("TER5302", new[]
            {
                ("BRG", 360.0,   0.0),
                ("ISS", 378.0, 376.0),
                ("CFD",   0.0, 397.0),
            });

            // ── LGV NORD — Paris ↔ Lille ──────────────────────
            InsertTrain("TGV7201", "TGV 7201 Paris-Lille",      "TGV", 300, 4);
            InsertRoute("TGV7201", new[]
            {
                ("PAR", 360.0,   0.0),
                ("ARS", 396.0, 394.0),
                ("LIL",   0.0, 406.0),
            });

            InsertTrain("TGV7202", "TGV 7202 Lille-Paris",      "TGV", 300, 4);
            InsertRoute("TGV7202", new[]
            {
                ("LIL", 360.0,   0.0),
                ("ARS", 370.0, 368.0),
                ("PAR",   0.0, 406.0),
            });

            // ── LGV ATLANTIQUE — Paris ↔ Bordeaux ─────────────
            InsertTrain("TGV8001", "TGV 8001 Paris-Bordeaux",   "TGV", 300, 4);
            InsertRoute("TGV8001", new[]
            {
                ("PDM", 360.0,   0.0),
                ("LEM", 401.0, 399.0),
                ("BDX",   0.0, 476.0),
            });

            InsertTrain("TGV8002", "TGV 8002 Bordeaux-Paris",   "TGV", 300, 4);
            InsertRoute("TGV8002", new[]
            {
                ("BDX", 360.0,   0.0),
                ("LEM", 435.0, 433.0),
                ("PDM",   0.0, 476.0),
            });

            // ── FRET — Lyon ↔ Marseille (80 km/h, priorité 1) ─
            InsertTrain("FRT0001", "Fret Lyon-Marseille",        "Fret", 80, 1);
            InsertRoute("FRT0001", new[]
            {
                ("LYO", 120.0,   0.0),
                ("VLS", 158.0, 156.0),
                ("AVG", 253.0, 251.0),
                ("AIX", 268.0, 266.0),
                ("MRS",   0.0, 288.0),
            });

            InsertTrain("FRT0002", "Fret Marseille-Lyon",        "Fret", 80, 1);
            InsertRoute("FRT0002", new[]
            {
                ("MRS", 120.0,   0.0),
                ("AIX", 142.0, 140.0),
                ("AVG", 162.0, 160.0),
                ("VLS", 257.0, 255.0),
                ("LYO",   0.0, 332.0),
            });
        }

        // ══════════════════════════════════════════
        //  3. CHARGEMENT — BDD → objets C#
        // ══════════════════════════════════════════

        /// <summary>
        /// Charge le réseau ferroviaire depuis la BDD.
        /// Crée les objets Station et TrackSegment et les relie entre eux.
        /// Retourne un RailNetwork complet prêt à être utilisé par le simulateur.
        /// </summary>
        public RailNetwork LoadNetwork()
        {
            var network    = new RailNetwork();
            var stationMap = new Dictionary<string, Station>();

            // Charger toutes les gares
            using var cmd1 = _connection.CreateCommand();
            cmd1.CommandText = "SELECT id, name, position_km, platforms, overtaking FROM stations";
            using var r1 = cmd1.ExecuteReader();
            while (r1.Read())
            {
                var st = new Station
                {
                    Id                      = r1.GetString(0),
                    Name                    = r1.GetString(1),
                    PositionKm              = r1.GetDouble(2),
                    PlatformCount           = r1.GetInt32(3),
                    HasOvertakingCapability = r1.GetInt32(4) == 1
                };
                network.Stations.Add(st);
                stationMap[st.Id] = st;
            }

            // Charger tous les segments et les relier aux gares correspondantes
            using var cmd2 = _connection.CreateCommand();
            cmd2.CommandText = "SELECT id, from_id, to_id, length_km, max_speed, track_type FROM segments";
            using var r2 = cmd2.ExecuteReader();
            while (r2.Read())
            {
                var seg = new TrackSegment
                {
                    Id          = r2.GetString(0),
                    From        = stationMap[r2.GetString(1)],
                    To          = stationMap[r2.GetString(2)],
                    LengthKm    = r2.GetDouble(3),
                    MaxSpeedKmH = r2.GetInt32(4),
                    Type        = r2.GetString(5) == "SingleTrack"
                                  ? TrackType.SingleTrack
                                  : TrackType.DoubleTrack
                };
                network.Segments.Add(seg);
            }

            Console.WriteLine($"  [DB] Réseau : {network.Stations.Count} gares, {network.Segments.Count} segments");
            return network;
        }

        /// <summary>
        /// Charge tous les trains depuis la BDD avec leurs itinéraires planifiés.
        /// Reconstruit les dictionnaires PlannedDepartureMinutes et PlannedArrivalMinutes.
        /// </summary>
        public List<Train> LoadTrains()
        {
            var trains = new List<Train>();

            // Charger les informations de base de chaque train
            using var cmd1 = _connection.CreateCommand();
            cmd1.CommandText = "SELECT DISTINCT id, name, type, max_speed, priority FROM trains ORDER BY type DESC, max_speed DESC";
            using var r1 = cmd1.ExecuteReader();
            while (r1.Read())
            {
                trains.Add(new Train
                {
                    Id          = r1.GetString(0),
                    Name        = r1.GetString(1),
                    Type        = Enum.Parse<TrainType>(r1.GetString(2)),
                    MaxSpeedKmH = r1.GetInt32(3),
                    Priority    = r1.GetInt32(4)
                });
            }

            // Charger l'itinéraire planifié de chaque train
            foreach (var train in trains)
            {
                using var cmd2 = _connection.CreateCommand();
                cmd2.CommandText = @"
                    SELECT station_id, dep_minutes, arr_minutes
                    FROM   train_routes
                    WHERE  train_id = $id
                    ORDER  BY seq_order";
                cmd2.Parameters.AddWithValue("$id", train.Id);

                using var r2 = cmd2.ExecuteReader();
                while (r2.Read())
                {
                    string stId = r2.GetString(0);
                    double dep  = r2.GetDouble(1);
                    double arr  = r2.GetDouble(2);

                    train.PlannedRoute.Add(stId);
                    if (dep > 0) train.PlannedDepartureMinutes[stId] = dep;
                    if (arr > 0) train.PlannedArrivalMinutes[stId]   = arr;
                }
            }

            Console.WriteLine($"  [DB] {trains.Count} trains chargés.");
            return trains;
        }

        // ══════════════════════════════════════════
        //  4. CRUD — ajouter / supprimer
        // ══════════════════════════════════════════

        /// <summary>Ajoute une nouvelle gare dans la base de données.</summary>
        public void AddStation(string id, string name, double km,
                               int platforms = 4, bool overtaking = true)
        {
            InsertStation(id, name, km, platforms, overtaking);
            Console.WriteLine($"  [DB] Gare ajoutée : {name}");
        }

        /// <summary>Ajoute un nouveau segment (et son inverse automatiquement).</summary>
        public void AddSegment(string id, string from, string to,
                               double km, int speed,
                               string type = "DoubleTrack")
        {
            InsertSegment(id, from, to, km, speed, type);
            Console.WriteLine($"  [DB] Segment ajouté : {from}→{to}");
        }

        /// <summary>Ajoute un nouveau train avec son itinéraire complet.</summary>
        public void AddTrain(string id, string name, string type,
                             int speed, int priority,
                             (string stId, double dep, double arr)[] stops)
        {
            InsertTrain(id, name, type, speed, priority);
            InsertRoute(id, stops);
            Console.WriteLine($"  [DB] Train ajouté : {name}");
        }

        /// <summary>Supprime un train et son itinéraire de la base de données.</summary>
        public void RemoveTrain(string trainId)
        {
            using var cmd1 = _connection.CreateCommand();
            cmd1.CommandText = "DELETE FROM train_routes WHERE train_id = $id";
            cmd1.Parameters.AddWithValue("$id", trainId);
            cmd1.ExecuteNonQuery();

            using var cmd2 = _connection.CreateCommand();
            cmd2.CommandText = "DELETE FROM trains WHERE id = $id";
            cmd2.Parameters.AddWithValue("$id", trainId);
            cmd2.ExecuteNonQuery();

            Console.WriteLine($"  [DB] Train supprimé : {trainId}");
        }

        /// <summary>
        /// Supprime toutes les données de toutes les tables.
        /// Utilisé pour repartir d'un état propre avant SeedRealData().
        /// </summary>
        public void Reset()
        {
            ExecuteNonQuery("DELETE FROM sim_conflicts");
            ExecuteNonQuery("DELETE FROM sim_results");
            ExecuteNonQuery("DELETE FROM train_routes");
            ExecuteNonQuery("DELETE FROM trains");
            ExecuteNonQuery("DELETE FROM segments");
            ExecuteNonQuery("DELETE FROM stations");
            Console.WriteLine("  [DB] Base réinitialisée.");
        }

        // ══════════════════════════════════════════
        //  5. SAUVEGARDE — résultats simulation
        // ══════════════════════════════════════════

        /// <summary>
        /// Sauvegarde les statistiques globales d'une simulation.
        /// Retourne l'ID de la simulation créée (pour lier les conflits).
        /// </summary>
        public int SaveSimulationResult(SimStats stats, int nbTrains)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO sim_results (nb_conflicts, nb_overtakes, avg_delay, nb_trains)
                VALUES ($c, $o, $d, $t);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$c", stats.ConflictCount);
            cmd.Parameters.AddWithValue("$o", 0); // Overtake supprimé
            cmd.Parameters.AddWithValue("$d", stats.AverageDelayMinutes);
            cmd.Parameters.AddWithValue("$t", nbTrains);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Sauvegarde le détail de chaque conflit résolu lors d'une simulation.
        /// Chaque conflit est lié à la simulation via simId.
        /// </summary>
        public void SaveConflicts(int simId, List<Conflict> conflicts)
        {
            foreach (var c in conflicts)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO sim_conflicts
                        (sim_id, conflict_id, type, train_a, train_b,
                         station, resolution, delay_impact, details)
                    VALUES
                        ($sid, $cid, $type, $ta, $tb,
                         $sta, $res, $delay, $det)";
                cmd.Parameters.AddWithValue("$sid",   simId);
                cmd.Parameters.AddWithValue("$cid",   c.Id);
                cmd.Parameters.AddWithValue("$type",  c.Type.ToString());
                cmd.Parameters.AddWithValue("$ta",    c.TrainA?.Id ?? "");
                cmd.Parameters.AddWithValue("$tb",    c.TrainB?.Id ?? "");
                cmd.Parameters.AddWithValue("$sta",   c.Location?.Id ?? "");
                cmd.Parameters.AddWithValue("$res",   c.Resolution.ToString());
                cmd.Parameters.AddWithValue("$delay", c.DelayImpactMinutes);
                cmd.Parameters.AddWithValue("$det",   c.ResolutionDetails ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        // ══════════════════════════════════════════
        //  6. HELPERS internes
        // ══════════════════════════════════════════

        /// <summary>Insère une gare avec INSERT OR IGNORE (pas de doublon).</summary>
        private void InsertStation(string id, string name, double km,
                                   int platforms, bool overtaking)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO stations VALUES ($id,$name,$km,$p,$o)";
            cmd.Parameters.AddWithValue("$id",   id);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$km",   km);
            cmd.Parameters.AddWithValue("$p",    platforms);
            cmd.Parameters.AddWithValue("$o",    overtaking ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Insère un segment et crée automatiquement son inverse (suffixe _R).
        /// Ex: S_PAR_LYO crée aussi S_PAR_LYO_R (LYO→PAR).
        /// </summary>
        private void InsertSegment(string id, string from, string to,
                                   double km, int speed, string type)
        {
            using var cmd1 = _connection.CreateCommand();
            cmd1.CommandText = "INSERT OR IGNORE INTO segments VALUES ($id,$f,$t,$km,$s,$type)";
            cmd1.Parameters.AddWithValue("$id",   id);
            cmd1.Parameters.AddWithValue("$f",    from);
            cmd1.Parameters.AddWithValue("$t",    to);
            cmd1.Parameters.AddWithValue("$km",   km);
            cmd1.Parameters.AddWithValue("$s",    speed);
            cmd1.Parameters.AddWithValue("$type", type);
            cmd1.ExecuteNonQuery();

            // Segment inverse automatique
            using var cmd2 = _connection.CreateCommand();
            cmd2.CommandText = "INSERT OR IGNORE INTO segments VALUES ($id,$f,$t,$km,$s,$type)";
            cmd2.Parameters.AddWithValue("$id",   id + "_R");
            cmd2.Parameters.AddWithValue("$f",    to);   // inversé
            cmd2.Parameters.AddWithValue("$t",    from); // inversé
            cmd2.Parameters.AddWithValue("$km",   km);
            cmd2.Parameters.AddWithValue("$s",    speed);
            cmd2.Parameters.AddWithValue("$type", type);
            cmd2.ExecuteNonQuery();
        }

        /// <summary>Insère un train avec INSERT OR IGNORE (pas de doublon).</summary>
        private void InsertTrain(string id, string name, string type,
                                 int speed, int priority)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO trains VALUES ($id,$name,$type,$s,$p)";
            cmd.Parameters.AddWithValue("$id",   id);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$type", type);
            cmd.Parameters.AddWithValue("$s",    speed);
            cmd.Parameters.AddWithValue("$p",    priority);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Insère l'itinéraire d'un train (liste ordonnée de gares avec horaires).
        /// seq_order garantit l'ordre de lecture correct au chargement.
        /// </summary>
        private void InsertRoute(string trainId,
                                 (string stId, double dep, double arr)[] stops)
        {
            for (int i = 0; i < stops.Length; i++)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO train_routes
                        (train_id, station_id, seq_order, dep_minutes, arr_minutes)
                    VALUES ($tid, $sid, $seq, $dep, $arr)";
                cmd.Parameters.AddWithValue("$tid", trainId);
                cmd.Parameters.AddWithValue("$sid", stops[i].stId);
                cmd.Parameters.AddWithValue("$seq", i);
                cmd.Parameters.AddWithValue("$dep", stops[i].dep);
                cmd.Parameters.AddWithValue("$arr", stops[i].arr);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Exécute une commande SQL sans retour de résultat.</summary>
        private void ExecuteNonQuery(string sql)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        /// <summary>Retourne le nombre de lignes dans une table.</summary>
        private int CountRows(string table)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>Ferme la connexion SQLite proprement.</summary>
        public void Dispose() => _connection?.Dispose();
    }
}