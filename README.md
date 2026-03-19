# RailSim — Simulateur Ferroviaire à Événements Discrets

Projet académique de simulation ferroviaire développé en C# (.NET 8),
implémentant un moteur de simulation à événements discrets avec optimisation
par recherche opérationnelle pour la gestion des conflits de circulation.

---

## Contexte

Projet réalisé dans le cadre d'un projet personnel en tant qu'étudiant
en 4ème année de développement logiciel. L'objectif est de modéliser la
circulation de trains sur un réseau SNCF réel, de détecter les conflits
potentiels et de les résoudre automatiquement via des heuristiques d'optimisation.

---

## Fonctionnalités

- Simulation à événements discrets sur un réseau de 22 gares et 44 segments SNCF
- Détection automatique de 3 types de conflits ferroviaires :
  - **Headway** : deux trains trop proches sur le même segment
  - **HeadOn** : nez-à-nez sur voie unique (sens opposés)
  - **PlatformConflict** : quai plein en gare
- Résolution par 2 heuristiques de recherche opérationnelle :
  - **MinimalWait** : le train moins prioritaire attend le minimum nécessaire
  - **AntiDelayPropagation** : protection contre la propagation en cascade des retards
- Base de données SQLite avec données SNCF réelles (LGV Sud-Est, Nord, Atlantique, Rhône-Alpes)
- Menu interactif : simulation libre ou démonstration préconfigurée
- Sauvegarde automatique des résultats et conflits en base de données

---

## Architecture
```
RailSim/
├── Core/
│   ├── Models/           # Entités métier (Train, Station, TrackSegment, Conflict...)
│   ├── Simulation/       # Moteur de simulation (Simulator, EventQueue, SimConfig, SimStats)
│   ├── Optimization/     # Heuristiques RO (ConflictDetector, ConflictResolver, HeuristicEngine)
│   └── Database/         # Couche d'accès SQLite (RailSimDatabase)
└── Program.cs            # Point d'entrée, menu interactif
```

---

## Stack technique

| Technologie | Usage |
|---|---|
| C# / .NET 8 | Langage principal |
| SQLite (Microsoft.Data.Sqlite) | Persistance des données |
| SortedSet | File de priorité pour les événements |
| Recherche Opérationnelle | Heuristiques de résolution de conflits |

---

## Prérequis

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 ou VS Code

---

## Installation et lancement
```bash
# Cloner le dépôt
git clone https://github.com/Tadoriya/RailSim.git
cd RailSim/RailSim

# Restaurer les dépendances
dotnet restore

# Lancer l'application
dotnet run
```

---

## Utilisation

Au lancement, deux modes sont disponibles :
```
[1] Nouvelle simulation      → Choisir les trains et configurer les itinéraires manuellement
[2] Simulation de démonstration → Scénario préconfigué avec conflits garantis
```

### Mode démonstration

Le mode démo configure automatiquement 6 trains pour démontrer les 3 types de conflits :
```
✓ TGV6601   TGV 6601 Paris-Marseille   départ 06h00  ← prioritaire
✓ TGV6603   TGV 6603 Paris-Marseille   départ 06h01  ← Headway avec TGV6601
✓ TGV6605   TGV 6605 Paris-Lyon        départ 06h03  ← AntiDelayPropagation
✓ TER8420   TER 8420 Lyon-Annecy       départ 05h28  ← HeadOn sur CHB↔ANN
✓ TER8421   TER 8421 Annecy-Lyon       départ 06h10  ← HeadOn sur CHB↔ANN
✓ IC3601    Intercités 3601 Paris-Lyon  départ 05h55
```

### Exemple de sortie
```
06h01 | TGV6603  | Depart de Paris Gare de Lyon vers Lyon Part-Dieu
  [C0001] à 06h01 | Headway → MinimalWait
    TGV6603 attend 6min → TGV6601 prioritaire.

══════════════════════════════════════════════════
  BILAN
──────────────────────────────────────────────────
  Conflits détectés  : 6
  Retard moyen       : 8,2 min
  Trains terminés    : 6/6

  Stratégies utilisées :
    MinimalWait                  : 3×
    AntiDelayPropagation         : 3×
```

---

## Réseau modélisé

| Ligne | Gares | Vitesse | Type |
|---|---|---|---|
| LGV Sud-Est | PAR → LYO → AVG → AIX → MRS | 300 km/h | Double voie |
| Via Dijon | PAR → DJN → MAC → LYO | 160 km/h | Double voie |
| LGV Nord | PAR → ARS → LIL | 300 km/h | Double voie |
| LGV Atlantique | PDM → LEM → BDX | 300 km/h | Double voie |
| Rhône-Alpes | LYO → CHB → ANN | 120 km/h | **Voie unique** CHB↔ANN |
| Puy-de-Dôme | LYO → VHY → RIU → CFD | 120 km/h | **Voie unique** VHY→CFD |

---

## Concepts clés

### Simulation à événements discrets
Le moteur traite les événements (`TrainDeparture`, `TrainArrival`) dans l'ordre
chronologique via une `SortedSet` triée par (temps ASC, priorité DESC).
Après chaque événement, une fenêtre de 200 minutes est analysée pour détecter
les conflits futurs.

### Heuristiques de recherche opérationnelle
Chaque conflit reçoit un score pour chaque stratégie disponible.
La stratégie avec le score minimal (coût de retard le plus faible) est appliquée.
```
MinimalWait score      = max(2, waitNeeded) × pénalité si > seuil
AntiPropagation score  = seuil - (retard × 0.5)  → double.MaxValue si retard faible
```

### Priorités des trains
```
TGV (priorité 4) > Intercités (priorité 3) > TER (priorité 2) > Fret (priorité 1)
```

---

## Base de données

La base SQLite `railsim.db` est créée automatiquement au premier lancement.

| Table | Contenu |
|---|---|
| `stations` | 22 gares avec capacité quais et possibilité dépassement |
| `segments` | 44 segments (22 + 22 inverses auto) avec type de voie |
| `trains` | 22 trains avec type, vitesse et priorité |
| `train_routes` | Itinéraires planifiés avec horaires |
| `sim_results` | Statistiques de chaque simulation |
| `sim_conflicts` | Détail de chaque conflit résolu |

---

## Auteur

**Tadoriya** — Étudiant Bac+4 Développement Logiciel  
[GitHub](https://github.com/Tadoriya)

---

## Licence

Projet personnel — usage personnel et éducatif.