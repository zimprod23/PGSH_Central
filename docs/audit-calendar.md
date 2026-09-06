# The audit register, and jours ouvrables

> Read before adding an audited act or an act code, and before anything that measures a duration in worked days.
>
> Split out of `CLAUDE.md` on 2026-09-06 — the text is unchanged. See [`CLAUDE.md`](../CLAUDE.md) for the always-on rules and the map of these documents.

## Le registre des actes, et la moitié qui manquait — `Audit/`
`IAuditableCommand` + `AuditLogPipelineBehavior` écrivent depuis longtemps ; `GetAuditLogQuery`
(`GET audit-log`) est ce qui les **relit**, et c'est arrivé en dernier.

- ⚠ **La table était en écriture seule.** Trente-cinq commandes y écrivaient, et il n'existait ni
  route ni écran pour l'ouvrir : la seule façon de consulter la trace était d'interroger la base à
  la main. Le 02/09/2026 la question s'est posée pour de vrai — 66 rosters étaient apparus sur la
  7ᵉ MED, l'utilisateur a demandé d'où, et le journal tenait **deux lignes pour toute la session**.
  Ajouter des entrées à un registre que personne ne peut ouvrir n'aurait rien répondu ; c'est la
  lecture qui manquait le plus.
- ⚠ **La couverture était simplement lacunaire, pas « destructif contre constructif ».**
  `PARTITIONS_CLEARED` et `GROUP_DELETED` étaient enregistrés ; `AssignRotationGroups`,
  `AutoArrangeGroups`, `CreateGroup`, **`EmptyGroup`** et `EmptyAllYearGroups` ne l'étaient pas — et
  vider un groupe est destructeur. Les cinq le sont depuis le 04/09/2026.
- **Un acte refusé n'écrit rien**, et ce n'est pas un choix du pipeline : la ligne est ajoutée au
  contexte *avant* le handler et n'est validée que par le `SaveChanges` de celui-ci. Propriété juste
  et fragile, donc épinglée par `AuditLogEndpointTests.A_refused_act_writes_no_entry` — un registre
  qui listerait les tentatives noierait ce qui a eu lieu.
- ⚠ **`AuditLog` était un sac de propriétés, et une entrée d'audit est immuable par nature.**
  `set` public sur chaque membre : tout code tenant l'instance pouvait réécrire l'auteur ou la date
  après coup — or *un registre qui se corrige après coup n'est pas un registre*. Accesseurs `init`
  au-dessus de champs explicites depuis le 04/09/2026, comme `AcademicYear` et `CnpnVersion`, pour
  la même raison et avec le même bénéfice : l'initialiseur d'objet de la migration et des tests
  continue de marcher, et rien ne change une entrée *ensuite*.
- ⚠ **…et l'horloge n'est plus dans l'entité.** `CreatedAt` s'initialisait à `DateTime.UtcNow` dans
  son propre initialiseur : l'instant d'un acte était intestable et le domaine dépendait de l'heure
  de la machine, alors que tout le reste passe le temps en paramètre (`SafePointEvaluator.Evaluate(…,
  nowUtc)`, `WorkingDayCalendar` « pure — no store, no clock »). `AuditLog.Record` le reçoit ; c'est
  le pipeline qui tient l'`IDateTimeProvider`. **Pas d'héritage d'`Entity`**, délibérément : une
  entrée n'a pas d'invariant sur des enfants et n'a rien à faire observer — lever un événement de
  domaine *à propos de l'enregistrement d'un acte* serait de la symétrie pour la symétrie.
- **Deux codes de plus depuis le 06/09/2026** : `BULK_DELOCALIZATION_APPLIED` (entité `Stage`, la
  métadonnée porte le service, le motif, le nombre confirmé et la taille de chaque sélection) et
  `DELOCALIZATION_CANCELLED` (entité `Registration`). ⚠ **L'annulation est auditée parce que l'acte de
  masse existe** : une délocalisation appliquée à un roster entier tombe sur des étudiants dont
  personne n'a tapé le nom, donc le retour en arrière est un acte à part entière et pas une
  correction de saisie. Voir [`delocalization.md`](delocalization.md).
- ⚠ **Les codes d'actes sont des littéraux dispersés dans autant de fichiers, et
  `AuditLogVocabularyTests` est ce qui les tient ensemble.** Une faute de frappe crée un type d'acte
  de plus, une copie de fichier en fusionne deux, et le journal — dont tout l'intérêt est qu'on
  puisse y chercher — devient inutilisable sans que rien n'échoue. Le balayage par réflexion exige
  que chacun soit non vide, en SCREAMING_SNAKE, et **unique**. ⚠ Le contrôle est là et **pas à
  l'exécution** : une garde qui lèverait au moment d'enregistrer ferait tomber *l'acte* — une
  réinscription, une déliberation — pour un défaut de programmation dans sa ligne de journal. Même
  raisonnement que `AuditMetadataJson`, qui ne peut pas lever.
- ⚠ **`AuditMetadataJson` plutôt qu'un JSON interpolé à la main.** Les commandes d'origine écrivent
  `$$"""{"levelId":{{LevelId}}}"""`, ce qui tient tant que chaque valeur est un entier ; un
  **libellé saisi par un admin** contenant un guillemet produirait une métadonnée qui n'est pas du
  JSON, dans la seule colonne dont le métier est d'être relue plus tard. `CreateGroupCommand` porte
  exactement un tel libellé. Le helper ne peut pas lever — l'entrée est écrite dans l'unité de
  travail de l'acte, donc une sérialisation qui échoue ferait tomber l'acte avec elle.
- ⚠ **L'auteur se résout en deux lectures plates.** `AuditLog.PerformedByUserId` est un `Guid` (le
  `sub` Keycloak) et `User.IdentityProviderId` une `string`, **sans clé étrangère entre les deux** :
  une jointure demanderait à Npgsql de traduire un `Guid.ToString()` dans un prédicat. Les ids sont
  convertis en mémoire puis remis dans un `IN`. Conséquence voulue : **un auteur introuvable ne fait
  pas disparaître son entrée** — le compte peut avoir été supprimé, ou la base restaurée sans son
  royaume Keycloak (`Backups:KeycloakRealmCovered` est `false`), et l'écran distingue « non résolu »
  de « système » de « quelqu'un ».
- ⚠ **L'agrégat des types d'actes reste en SQL, le tri non — et c'est le fournisseur *in-memory* qui
  l'impose.** Un `OrderByDescending` sur une propriété projetée depuis un `GroupBy` est refusé en
  mémoire alors que PostgreSQL le traduit : le miroir de l'angle mort habituel, comme `SelectMany`
  au-dessus d'une skip navigation. Le `Count()` reste côté base ; le classement se fait sur une
  liste bornée par le nombre de commandes auditées.
- **Non scopé par année universitaire, délibérément.** Une entrée est datée d'une *horloge*, pas
  d'une année académique — le geste qui touche 2026-2027 a pu être fait en juillet. La règle « une
  année omise vaut l'année en cours » parle des lectures scopées *par* l'année ; celle-ci ne l'est
  pas, et son filtre est une plage de temps.
- ⚠ **Cette plage est en *instants UTC*, jamais en jours — et c'est le correctif d'un défaut
  mesuré.** Les bornes ont d'abord été des `DateOnly` résolus à minuit **UTC**, alors que l'écran
  affiche l'heure du **navigateur** : une entrée écrite le 02/09 à 22:16 UTC se lit « 03/09 00:16 »
  à Casablanca, et un filtre « du 3 au 3 » la faisait disparaître. La date lue et la date filtrée
  n'étaient pas la même — trouvé en pilotant l'écran le 04/09/2026, sur le journal réel, et
  invisible à toute la suite parce que les fixtures et les assertions vivaient dans le même repère.
  **La journée appartient au calendrier de celui qui lit**, donc c'est le client qui la traduit
  (« au 3 inclus » → « < 4 septembre 00:00 *locale* », envoyé en UTC) ; le serveur compare des
  instants et ne suppose aucun fuseau — ce qu'il ne saurait pas faire, le Maroc passant à UTC+0
  pendant le ramadan. `AsUtc` traite séparément les trois `DateTimeKind` que la liaison peut
  produire : un `SpecifyKind(Utc)` uniforme prendrait une heure locale pour de l'UTC.

## Jours ouvrables — the calendar is entered, and half of it cannot be computed
`WorkingDayCalendar` in `Domain/Calendar/` is the single answer to "how long is this really": calendar
days minus the weekly rest days (`WorkingWeek.Moroccan` = Sat + Sun) minus every declared `Holiday`.
Pure and immutable, built once by `WorkingDayProvider`, which loads the **whole** table (~15 rows a
year — a date range would need an unknowable forward margin anyway).

- ⚠ **`Stage.DurationInDays` is already in worked days for 25 of 27 stages — measured 2026-08-13.**
  The distribution is 14×7, 22×7, 30×2, 42×3, 44×6, 66×2: 22 is a month of worked days, 44 two, 66
  three, 14 about three weeks. Only the two 30s (pharmacie officine, stage hospitalier d'initiation)
  are ambiguous — 30 worked days is six weeks, so they are most likely calendar days left over from the
  import. **Consequence: author the axis in `WorkingDays` and the catalogue durations are met exactly**
  (Med6 at 22 j.o./column gives CHIRURGIE k=2 → 44, its stated figure, while its calendar span swings
  60–67 days). Nothing is converted regardless: the calendar generates where the unit is stated **at the
  point of use**, and everywhere else it *reports*:
  `RotationCyclePreview.DurationChecks` gives each stage's worked and calendar days against its stated
  number, as a **range** (partitions take different runs of the axis) and never as a guard.
- ⚠ **National dates are law; religious dates are observation.** `MoroccanPublicHolidays.FixedFor`
  generates the ten fixed Gregorian days (and Nouvel An Amazigh only from 2024, when the décret first
  took effect). Aïd al-Fitr, Aïd al-Adha, 1ᵉʳ Moharram and Mawlid follow the Hijri calendar, turn on
  observation of the crescent, and are announced by decree — **they cannot be generated, only entered**.
  Their absence is reported (`MissingReligious`) rather than left to surface as a stage that ran long.
- ⚠ **An empty calendar is not a neutral one.** With no holidays recorded, « jours ouvrables » quietly
  means "minus weekends" — narrower than it says, and the normal state of a fresh base. Hence
  `CalendarIsEmpty` on both the axis and the preview: a silent best effort here is a wrong end date.
- **`Holiday.IsConfirmed` is load-bearing.** A provisional lunar date still blocks its days — you plan
  on the best estimate — but every window laid over one is flagged, so the répartition can be reprinted
  when the decree lands instead of being quietly a day out. Same shape as `OutcomeSource`.
- **A holiday spans days** (`StartDate`…`EndDate`, inclusive) because the ones that matter are
  multi-day: Aïd is two, vacances are two weeks. `WorkingDaysLost` is counted against the
  *weekend-only* calendar — measured against a calendar that already contains the holiday, every
  holiday costs zero — so a férié falling on a Sunday correctly reads 0.
- **A window opens and closes on a worked day.** Asked to start on a Saturday it starts Monday, and it
  never swallows a trailing weekend, so consecutive columns cannot overlap the rest day between them.
- ⚠ **A per-service working week is deliberately not modelled.** Many services run Saturday mornings
  and a garde runs every day. This calendar answers a *planning* question about a promotion; attendance
  is recorded per day against `AttendanceRecord` and is never derived from it.
- Deleting a holiday breaks no link, but any `StageSlot` laid over it keeps dates that no longer
  reproduce from the count that produced them — `SlotsSpanning` says how many, so the confirmation can
  name the number.
- ⚠ **Moving a date is the same event as deleting it, and it is the path that actually happens** —
  the September estimate corrected the day the décret names Aïd. So `UpdateHolidayCommand` reports
  `SlotsSpanning` too, over the union of the span it **left** and the span it **arrived at**: the
  first was laid around a holiday no longer there, the second has just gained one it never counted.
  Counted once where they overlap (the usual one-day correction), and counted **before** the write.
  - `DatesMoved` gates it. Ticking « Date confirmée » on a span already right moves no day count, and
    reporting slots there teaches the user to dismiss the one report that matters.
