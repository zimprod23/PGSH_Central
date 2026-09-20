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
- **Trois codes de plus le 06/09/2026 (session 49)** : `PROMOTION_PAUSE_DECLARED` (entité `Level` —
  l'acte nomme une promotion, et la métadonnée porte l'année, les bornes, le type, le motif et le
  drapeau « confirmée »), `PROMOTION_PAUSE_CORRECTED` et `PROMOTION_PAUSE_REVOKED` (entité
  `PromotionPause`). ⚠ **Le retrait est audité parce que c'est la *seule* trace qu'il laisse** : il
  supprime la racine d'agrégat, et EF détache une entité supprimée **avant** qu'`ApplicationDbContext`
  ne relève les événements de domaine — un événement levé là serait perdu sans bruit. La déclaration,
  elle, lève `PromotionPauseDeclaredDomainEvent` : c'est l'acte le plus large de la planification et
  rien d'autre ne l'observe.
- **Un code de plus le 13/09/2026** : `AFFECTATION_SHEET_APPLIED` (entité `Level` — la promotion, qui
  est la portée d'un canevas). La métadonnée porte l'année, le nombre de lignes et les **deux** nombres
  confirmés ; le constat déposé par `IAuditTrail.RecordOutcome` porte ce qui a réellement eu lieu :
  `affectationsCreated`, `affectationsRebuilt`, `delocalizations`, `periodsWritten`, **`periodsDropped`**,
  `cohortsCreated`, `unchanged`. ⚠ **C'est `periodsDropped` qui fait exister l'entrée.** Le même code,
  sur une promotion vierge et sur une promotion publiée, recouvre deux événements sans rapport — et
  après l'écriture il ne reste rien à compter. Le dossier de chaque étudiant porte sa moitié à lui, via
  `HistoryType.AffectationImported`. Voir [`affectation-sheet.md`](affectation-sheet.md).
- **Deux codes de plus depuis le 06/09/2026** : `BULK_DELOCALIZATION_APPLIED` (entité `Stage`, la
  métadonnée porte le service, le motif, le nombre confirmé et la taille de chaque sélection) et
  `DELOCALIZATION_CANCELLED` (entité `Registration`). ⚠ **L'annulation est auditée parce que l'acte de
  masse existe** : une délocalisation appliquée à un roster entier tombe sur des étudiants dont
  personne n'a tapé le nom, donc le retour en arrière est un acte à part entière et pas une
  correction de saisie. Voir [`delocalization.md`](delocalization.md).
- **Deux codes de plus le 07/09/2026 (phase 19.2)** : `STUDENTS_ASSIGNED_TO_ROSTER` (entité
  `AcademicGroup`, la métadonnée porte l'année, le nombre confirmé, la taille de chaque sélection et
  le motif) et `STAGE_SERVICE_PLACEMENT_MODE_SET` (entité `Stage`, avec le service et le mode). ⚠ **Le
  second est audité alors qu'il n'écrit qu'une colonne** : réserver un service retire des places à la
  rotation de toute une promotion sans toucher une seule cellule, donc l'effet apparaît à la
  répartition suivante, longtemps après le clic, et « pourquoi ce service n'est-il plus utilisé ? »
  n'a aucune autre réponse. Même raison que `STAGE_SERVICE_ORDER_SET`.
- **Dix codes de plus le 10/09/2026 (`HANDOFF.md` A3) — tout le côté cohorte et toute la grille.**
  Côté cohorte : `STAGE_COHORTS_RESET` (« Réinitialiser les cohortes », entité `Stage`),
  `COHORT_DELETED`, `COHORT_SCHEDULE_UNPUBLISHED` (⚠ la métadonnée porte **`forced`** : forcé, cet
  acte détruit des notes de chef et des journées de présence, et une dépublication ordinaire ne doit
  pas se lire pareil) et `STAGE_SCHEDULE_UNPUBLISHED` (avec les partitions visées, l'acte étant
  scopable). Côté grille : `STAGE_SLOT_CREATED`, `STAGE_SLOT_UPDATED`, `STAGE_SLOT_DELETED`,
  `COHORT_SLOT_PINNED`, `COHORT_SLOT_CLEARED`, `STAGE_SLOT_CELLS_CLEARED`.
  - ⚠ **Les actes qui *construisent* sont audités aussi**, pour la raison qui a fait auditer
    `AutoArrangeGroupsCommand` : « qui a posé cet axe » est la même question que « qui l'a
    supprimé », et une campagne de répartition rejoue les deux en boucle. Épingler est le cas le plus
    net — `COHORT_SLOT_PINNED` écrit une décision humaine que la répartition automatique n'aura plus
    le droit de réécrire, donc le registre est la moitié « qui » de ce que `PinnedCellsKept` compte.
- ⚠ **Et « Publier » n'était pas audité du tout, alors que « Dépublier » l'était — 11/09/2026.**
  Le registre tenait donc le *défaire* sans le *faire*, sur l'acte qui crée les `ServicePeriod` :
  tout ce que les chefs notent et tout ce que les présences visent. La seule trace d'une publication
  était l'absence de sa dépublication. Deux codes de plus : `COHORT_SCHEDULE_PUBLISHED` (entité
  `Cohort`) et `STAGE_SCHEDULE_PUBLISHED` (entité `Stage`, avec les partitions et les périodes
  visées, l'acte étant scopable comme son inverse).
  - ⚠ **`allowOverCapacity` voyage avec l'entrée**, exactement comme `forced` sur la dépublication.
    Un service peut refuser d'être dépassé (`Service.AllowsOverCapacity`) ; passer outre est un geste
    posé sciemment **contre** ce refus, et c'est précisément ce qu'on viendra demander au registre.
    Une publication forcée et une publication ordinaire étaient indiscernables.
  - ⚠ **Le `SaveChanges` du handler est inconditionnel**, là où le publisher n'écrit que s'il a des
    périodes à poser. « Publier » rejoué sur un stage déjà publié — l'acte le plus banal d'une
    campagne — n'aurait sinon rien laissé : c'est la forme *conditionnelle* du défaut ci-dessous,
    celle que le balayage du 10/09 avait laissée de côté (`HANDOFF.md` **0bd**). Zéro cohorte publiée
    n'est pas un non-acte, c'est le constat que tout l'était déjà.
  - `PublishCohortAsync` répond désormais un **nombre** de périodes et non un `Result` nu : un acte
    audité doit dire *combien*, et « Publier » sur une cohorte de quatre et sur une de quarante
    écrivaient la même ligne. Épinglé par `PublishScheduleAuditTests`, dont le cas mordant est le
    stage déjà publié.
- ⚠ **`STAGE_DELETED` — 11/09/2026.** Supprimer un stage du catalogue n'était pas audité, alors que
  poser un créneau (`STAGE_SLOT_CREATED`) et ordonner ses services (`STAGE_SERVICE_ORDER_SET`) le
  sont — et que la suppression emporte **les deux** en cascade, pour toutes les années. Le registre
  tenait la construction sans la destruction. L'entrée porte `slotsRemoved`,
  `allowedServicesRemoved`, `objectivesRemoved` : la réponse est un `204` et ne peut rien porter, donc
  c'est le seul endroit où l'ampleur de la cascade se relit. ⚠ Les compteurs sont lus **avant** le
  `Remove` — après, il n'y a plus rien à compter.
- ⚠ **`SERVICE_DELETED` — 11/09/2026.** Même asymétrie, un cran plus bas : supprimer un service
  emportait en cascade ses quotas (`ServiceLevelCapacity`), l'historique de ses chefs
  (`ServiceChefAssignment`) et le rattachement de son personnel, sans une ligne au registre. Accorder
  un quota et nommer un chef sont des décisions humaines que rien d'autre ne conserve. L'entrée porte
  `serviceName`, `hospitalId`, `quotasRemoved`, `chefTenuresRemoved`, `staffDetached` — lus, comme
  ci-dessus, **avant** le `Remove`. ⚠ Et l'acte n'était pas seulement muet : il n'avait **aucune
  garde**, donc un service porté par la grille ou par des périodes sortait en **500** au lieu d'un
  refus. Voir [`docs/services.md`](services.md).
- ⚠ **Deux actes portaient le marqueur et n'écrivaient rien — mesuré le 10/09/2026.**
  `DeleteAllGroupsCommand` (« Supprimer les groupes ») et `EmptyAllYearGroupsCommand` (« Vider les
  groupes ») déclaraient `YEAR_GROUPS_DELETED` / `PROMOTION_GROUPS_DELETED` et leurs jumeaux depuis
  la phase 20, et **la table restait vide** : tout leur écrit passe par `ExecuteDelete` /
  `ExecuteUpdate`, qui contournent le change tracker, et **rien n'appelait `SaveChanges`** — la ligne
  mise en attente par le pipeline mourait avec la portée de la requête. La propriété « un acte refusé
  n'écrit rien » et le défaut « un acte réussi n'écrit rien » ont exactement la même cause, et rien
  ne les distinguait. Les deux handlers terminent maintenant par un `SaveChangesAsync` explicite.
  - ⚠ **Aucun test ne pouvait le voir**, et ce n'est pas un oubli de couverture : le fournisseur
    *in-memory* **refuse** `ExecuteDelete`, donc le chemin de succès de ces actes n'était atteignable
    par aucun test du dépôt. C'est `TestHarness.NewSqliteContext` qui l'ouvre — voir `CLAUDE.md`,
    « Known blind spot », et `ExecuteDeleteAuditTests`.
- ⚠ **Six sorties de plus, trouvées le 14/09/2026 — le `SaveChanges` *conditionnel*.** Le balayage du
  10/09 n'avait retenu que les handlers n'appelant **jamais** `SaveChanges`. Restaient ceux dont
  l'appel est sous une garde, et la garde est toujours la même : `if (count > 0)`. L'entrée ne
  s'écrit alors pas sur le run où l'acte n'a **rien changé** — c'est-à-dire sur le **rejeu ordinaire
  du bouton**.

  | Acte | La sortie muette |
  |---|---|
  | `AssignRotationGroupsCommandHandler` | promotion sans roster ; **et** promotion dont chaque roster porte déjà sa partition |
  | `ClearRotationGroupsCommandHandler` | promotion sans roster ; **et** promotion déjà non partitionnée |
  | `SeedNationalHolidaysCommandHandler` | année déjà semée — resemer après une correction à la main est la façon normale de s'en servir |
  | `CloneCnpnCurriculaCommandHandler` | texte cible portant déjà ses programmes |
  | `SetAllowedServicePlacementModeCommandHandler` | mode reposé à l'identique — ce que fait un double-clic |

  - ⚠ **`PARTITIONS_ASSIGNED` est le cas qui pique le plus.** Le code d'acte avait été ajouté
    *exprès* pour que « qui a découpé cette promotion, et quand ? » ait une réponse — la question
    qu'ont soulevée 66 rosters apparus sur la 7ᵉ MED le 02/09/2026 — et c'est justement le rejeu,
    celui qui n'étiquette plus rien, qui ne laissait aucune ligne.
  - **Corrigé de la même façon partout** : le chemin zéro dépose ses zéros par `RecordOutcome` puis
    sauvegarde, la forme que `DeleteAllGroupsCommandHandler` avait déjà. Les deux actes de partition
    ne déposaient d'ailleurs **aucun** constat, même sur le chemin normal : l'entrée disait ce qui
    avait été *demandé* (`partitionCount`, `strategy`) et jamais ce qui avait été *fait*.
  - ⚠ **Le défaut reste invisible à la compilation et aux tests de handler**, pour la raison dite
    plus haut : rien ne relie l'entrée mise en attente au `SaveChanges` qui la valide. Le filet est
    `PGSH.Tests/Integration/NoEffectAuditEndpointTests.cs`, et son témoin — un acte **refusé**
    n'écrit toujours rien — est ce qui l'empêche de passer pour une raison inverse.
- ⚠ **Une entrée doit dire *combien*, et la commande ne peut pas le savoir — `IAuditTrail`.**
  `IAuditableCommand` décrit ce qui a été **demandé** ; sur un acte destructeur la question posée au
  registre trois mois plus tard est « combien cela a-t-il emporté ». « Réinitialiser les cohortes »
  sur une promotion vierge et sur une promotion publiée écrivent le même code : deux événements sans
  rapport, une seule ligne. C'est la règle « dire ce que le blanc veut dire », appliquée au journal.
  - **Le mécanisme.** Le behavior *ouvre* l'entrée sur une piste de portée requête ; le handler y
    dépose son constat par `RecordOutcome(("cohortsRemoved", n), …)` **avant** son `SaveChanges`.
    L'immuabilité d'`AuditLog` reste entière : la piste **remplace** l'entité en attente au lieu de la
    modifier — tant que l'insertion n'est pas validée, la compléter n'est pas corriger le registre
    après coup, c'est finir la phrase avant de la valider. Et c'est aussi ainsi que l'**année
    réellement atteinte** entre dans l'entrée : une année omise vaut l'année en cours, et seule la
    résolution du handler sait laquelle.
  - ✅ **Vérifié sur la base vivante le 11/09/2026.** Le démontage du décor de §58 a écrit ses deux
    lignes **avec leur ampleur** : `YEAR_GROUPS_EMPTIED` → `{"rostersInScope": 36,
    "registrationsDetached": 1564}`, `YEAR_GROUPS_DELETED` → `{"cohortsDeleted": 0,
    "rostersDeleted": 36}`. ⚠ **Ce sont précisément les deux actes qui n'écrivaient rien du tout**
    depuis la phase 20 — c'est la mesure qui ferme la boucle, et aucun test de ce dépôt ne pouvait
    la produire. Au même passage, `GROUPS_AUTO_ARRANGED` porte désormais `"askedBy": "size"` ou
    `"count"` : **l'unité demandée** est au journal, pas seulement le nombre de groupes obtenu — les
    deux unités produisent le même résultat et le registre doit pouvoir dire laquelle a été dite.
  - ✅ **Et cela compose avec une transaction depuis le 12/09/2026 — par
    `IAuditTrail.RunAtomicallyAsync`, jamais par `IApplicationDbContext.ExecuteAtomicallyAsync`
    directement.** L'enveloppe vide le change tracker à chaque nouvelle tentative, donc l'entrée mise
    en attente avant le handler disparaît avec lui. Le contexte en relevait autrefois une photographie
    à l'entrée et la remettait telle quelle : impossible ici, puisque le constat **remplace** l'entrée
    en attente — la tentative suivante remettait donc celle d'avant, sans constat, pendant que la
    piste tenait la remplaçante. Deux lignes pour un acte, dont une fausse. C'est la piste qui remet
    la sienne, parce qu'elle est seule à savoir laquelle est la bonne ; le contexte ne remet plus
    rien. Épinglé par `AtomicUnitOfWorkTests`.
  - ✅ **Et un acte dont le *hasard* fait partie doit déposer ce hasard — `drawSeed`, 12/09/2026.**
    Depuis que la composition des groupes est tirée au sort (`RosterDraw`), l'ordre dans lequel les
    inscriptions ont été déposées n'est plus déductible du code : « pourquoi cet étudiant dans le
    groupe 41 ? » n'aurait plus aucune réponse. `GROUPS_AUTO_ARRANGED` porte donc le numéro du tirage
    à côté de `levelId` / `askedBy` / `groupCount`, déposé **avant** le premier `SaveChanges` de
    l'acte — après, le constat coûterait un DELETE suivi d'un INSERT. ⚠ **Un mélange muet serait un
    recul par rapport au tri qu'il remplace** : le tri, lui, était explicable. Voir
    [`planning-rosters.md`](planning-rosters.md).
  - **Un acte sans effet s'enregistre quand même, avec son zéro.** Sans cela l'absence de ligne
    recouvre « personne ne l'a joué » et « quelqu'un l'a joué sur une promotion déjà vide », qui
    appellent des lectures opposées. Un acte **refusé**, lui, continue de n'écrire rien : ce n'est
    pas la même chose, et c'est la distinction que le registre existe pour tenir.
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
days minus the weekly rest days (`WorkingWeek.Moroccan` = Sat + Sun) minus the declared
`ICalendarClosure`s. Pure and immutable, built once by `WorkingDayProvider`, which loads the **whole**
holiday table (~15 rows a year — a date range would need an unknowable forward margin anyway).

### ⚠ It answers **two** questions, and they are two methods — not one `IsWorkingDay`
The faculty's rule, given 10/09/2026: **the only planning constraint is that a période neither begins
nor ends on a rest day or a closure.** A closure may therefore be *crossed* — and crossing it need not
lengthen the window that crosses it. That is two different facts about a day, and a single predicate
could not hold them both:

| | `CountsTowardDuration` | `CanBoundAWindow` |
|---|---|---|
| asks | is somebody expected in a service | may a période begin or end here |
| rest day | no | no |
| closure, ordinary | no | no |
| closure, `CountsAsWorkingDay` | **yes** | **no** |

- ⚠ **The two are nested, never independent**: every bounding day counts, not every counted day bounds.
  A day that could bound a window without counting would be a window whose own last day is not in it.
  Pinned by a sweep over seven months in `WorkingDayCalendarTests`.
- **`ICalendarClosure.CountsAsWorkingDay` is on the interface, not on `Holiday` alone.** The interface's
  own sentence is that the two implementations « differ in scope and in nothing else »; a flag on one of
  them would have quietly made that false. `PromotionPause` answers `false` **unconditionally and
  without a column** — a promotion sitting an exam is not in a service, and a settable flag there would
  allow a window that suspends nobody. `ProposedClosure` computes it from the scope for the same reason:
  it exists so a preview cannot report what the act it previews is unable to be.
- ⚠ **`false` everywhere is the old arithmetic line for line**, and it is what every row in the base
  says. The migration is additive with `DEFAULT false`, so no date already posed moves — which is what
  lets it land on a base carrying a published promotion.
- ⚠ **`Lay` can now return a window holding *more* than was asked, and it says so.** If the Nᵗʰ counted
  day falls on a worked closure — counted, never bounding — `End` advances to the next day that can
  bound, and the worked days crossed on the way are **counted** rather than dropped. Dropping them
  would leave `Count(Start, End)` disagreeing with `WorkingDays` *for the same window*: one number
  standing for two facts, the defect class this repository is measured against. The divergence is
  carried by `WorkingDayWindow.RunsLongerThanAsked`, so a caller sees a wider column instead of
  discovering it on a published table.
  - ⚠ **This revises the sketch the item was written from**, which said to extend « sans le compter ».
    Taken literally that is what produces the contradiction its own next clause warns about — the day
    the window ends on is itself a worked day, so it cannot be excluded from the count of a window that
    contains it. The reason is kept; the mechanism is the other one.
  - Unreachable on the base as it stands: nothing is flagged, so every window holds exactly what it was
    asked for.

### ⚠ There are **two** calendars, and which one a caller gets is decided by whether it holds a promotion
Two things are "days on which the people covered are not in a service", and they differ in **scope and
in nothing else** — so both implement `ICalendarClosure` rather than growing a second, parallel notion:

| | scope | who declares it |
|---|---|---|
| `Holiday` | the faculty | law (national), decree (religious), the faculty itself (academic) |
| `PromotionPause` | one **(année, niveau)** | the faculty, per promotion — an exam session |

- `WorkingDayProvider.BuildAsync` is the faculty calendar; `ForPromotionAsync(yearId, levelId, ct)`
  adds that promotion's windows. ⚠ **Two promotions rotate through the same services on the same
  morning and only one of them is sitting an exam**, so a calendar showing both promotions' windows to
  both of them would be wrong for each. `levelId: null` — a caller that genuinely spans promotions —
  gets the faculty calendar, because quietly picking one promotion's exam weeks is worse than counting
  none. **That split is the whole mechanism; the CRUD around it is not.**
- ⚠ **Declaring a window moves no date.** It is a calendar fact: the axis laid afterwards steps over
  it, in worked days, and the grid and the périodes published from it are laid against the same days.
  Declared *after* a grid exists it leaves every créneau where it is — the preview counts what each one
  then loses. Full reasoning in [`planning-rotation.md`](planning-rotation.md) and `PHASES.md` §17.
- ⚠ **The preview therefore has to say what the remedy *is*, and it depends on the promotion — three
  cases, not two.** ① Nothing published: re-lay the axis, and it steps over the window by itself.
  ② Published: re-laying is refused for the whole year, and the repair is moving the crossed columns
  one at a time (§17.1) — so the report carries **`SlotsMovable`**, how many of them that act would
  accept, counted against `ServicePeriodLifecycle.Movable`, the rule the aggregate itself refuses on.
  ③ Rotations crossing the window with **no créneau** crossing it — périodes written *hors grille* —
  which **neither** remedy reaches; those are corrected by re-sending the canevas des affectations.
  - ⚠ **Case ② said « pas encore possible » for a year after it became possible**, and case ③ had no
    branch at all: on a promotion at rest it produced *no warning*, and on one with rotations under way
    it prescribed « reposez l'axe », a gesture that succeeds and changes nothing for them. Both fixed
    17/09/2026. **Naming no remedy where one exists reads as « les jours sont perdus »** — the same
    defect as prescribing a refused button, arrived at from the other side.
  - ⚠ **And « déplaçable » is not « réparé ».** A move shifts one column; nothing cascades to the ones
    after it. The warning says so, because an operator who moves P7 expecting P8 to follow will have
    the next move refused for breaking the run's order.
  - ⚠ **Et « déplaçable » n'était que la moitié de la question — la moitié qui ne couvre pas le cas
    qui compte.** Une fenêtre déclarée en cours d'année tombe sur des rotations **commencées**, que
    `ServicePeriodLifecycle.Movable` refuse, donc sur cette promotion-là le compte des déplaçables
    est petit ou nul et le rapport disait en substance « rien n'est rattrapable ». Depuis le
    18/09/2026 la classe pose **deux** questions : `Movable` (« puis-je déplacer le début ? ») et
    `Extendable` (« puis-je repousser la fin ? »). Allonger une rotation en cours ne réécrit rien de
    ce qui a eu lieu, donc le remède existe là où le rapport n'en voyait aucun.
    - ⚠ **Les présences interdisent le déplacement et pas l'allongement** — une journée pointée vit
      entre le début et l'ancienne fin, et une fenêtre qui ne fait que croître la contient toujours.
      Le refus du raccourcissement n'est donc pas une garde d'état mais le **nom de l'acte** :
      `InternshipAssignment.ExtendTo` ne sait que repousser.
    - ⚠ **Les deux règles sont emboîtées** (`Movable` ⊂ `Extendable`), comme `CountsTowardDuration`
      et `CanBoundAWindow` — et c'est un théorème vérifié sur les 32 combinaisons, pas une
      coïncidence : `IsInterrupted` a été ajouté à `Movable` pour cela, une rotation coupée par un
      transfert ayant ses **deux** bouts pour faits.
  - ⚠ **A column the window *empties* is a fourth thing, and it is not a variant of the three.**
    `SlotsEmptied` + `CellsInEmptiedSlots`: how many crossed columns come out with **no worked day at
    all**, and how many cells sit in them. A column keeping 12 of its 15 days is caught up by a shift; a
    column keeping **0** is a rotation during which its students serve nothing while its cells and its
    périodes still stand — a hole, not a short week, and the remedy differs in kind.
    - **Found by driving the real screen, 17/09/2026.** A window over December 2026 on the 3ᵉ MED — 23
      worked days against columns of 15 — empties **one column of every one of the 8 stages** (3 × 15 =
      45, less 23, split 0 + 12 + 10). `Warnings()` was never given `MinWorkingDaysAfter`, so the only
      trace was the left end of a « 0 – 10 » range in a per-stage table cell.
    - ⚠ **The sentence is *added* to the remedy, never substituted for it** — an emptied column happens
      whether the axis is published, under way or at rest. And the count is taken on the **whole** slot
      list, before the `MaxSlotRows` truncation: a column emptied past the display cap is exactly the one
      nobody would see.
    - ⚠ **`CellsInEmptiedSlots` travels beside the count** for the usual reason: « an emptied column
      nobody is in » and « an emptied column holding a hundred students » are the two states the count
      alone does not separate.
- ⚠ **A window's cost is measured on a calendar that does *not* contain it.** Asked of one that does,
  every window ever declared costs zero — the same trap `HolidayResponse.WorkingDaysLost` avoids by
  counting against the weekend-only calendar. Hence `ForPromotionAsync(..., excludingPauseId)` and
  `WorkingDayCalendar.With(closure)`: the cost is the difference between two calendars.
- ⚠ **A window's « aucun jour férié » caption is about the academic *year*, not about the window** —
  corrected on 2026-09-06 after driving the real screen. An exam week holds no jour férié in the
  ordinary case, so measured on its own five days the flag fired on nearly every window and said
  nothing worth acting on; measured on the year it says the one thing that is: nobody has entered this
  year's calendar. Same widening, and the same reason, as `MissingReligious` being asked of whole
  Gregorian years. **A caption that fires whatever the data says is noise, and noise is dismissed.**
- ⚠ **`MissingReligious` counts faculty closures only.** A pause named « Aïd al-Fitr » is a promotion
  saying it is out that week, not the decree naming the date; counting it would report the faculty's
  calendar complete on the strength of one promotion's own window, and silence the one report that
  says a lunar date is still missing.

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
  - ⚠ **And it is a *difference* rather than a count**: `weekendsOnly.Count(span)` minus
    `weekendsOnly.With(holiday).Count(span)`. A worked férié removes nothing, and the subtraction takes
    that from the calendar's own rule instead of restating it as an `if` that would drift.
  - ⚠ **`WorkingDaysLost = 0` now has two opposite meanings, so the flag travels beside it.** « férié
    chômé tombé un dimanche » is a row to leave alone; « férié travaillé » is a row somebody flagged and
    may want to unflag. `HolidayResponse.CountsAsWorkingDay` and `HolidayCoverageResponse
    .WorkedThroughCount` are what separate them — the number alone would read as a broken count.
  - ⚠ **Toggling the flag opens the `SlotsSpanning` report exactly as moving the dates does**, and
    `UpdateHolidayResult` names the two separately (`DatesMoved`, `CountingChanged`) because the
    sentences differ. Gating on `DatesMoved` alone would have made the one change that gives days
    *back* the only silent one.
  - ⚠ **`UpdateHolidayCommand.CountsAsWorkingDay` is `bool?`, and null means « unchanged ».** This is a
    full-replace PUT and the screen lives in another repository: a plain `bool` could not tell a client
    asking for « chômé » from one that has never heard of the field, so saving a holiday's *name* from
    an older screen would silently undo a flag somebody set. `IsConfirmed` beside it is not nullable,
    because every client that exists already sends it.
- **A window opens and closes on a day that can *bound* one.** Asked to start on a Saturday it starts
  Monday, and it never swallows a trailing weekend, so consecutive columns cannot overlap the rest day
  between them. ⚠ Never merely "a worked day": a closure the faculty works through is worked and still
  bounds nothing.
- 📋 **La règle de la faculté, dite le 10/09/2026 : la *seule* contrainte de planification est qu'une
  période ne commence ni ne finisse un week-end ou un jour férié.** Un férié peut donc être
  **traversé** sans allonger la fenêtre — ce que le modèle ne sait pas encore dire, un `Holiday` étant
  aujourd'hui chômé ou inexistant. ⚠ **Rien n'est implémenté** : `HANDOFF.md` **0ay**, `PHASES.md`
  §27.1. Ce que la règle demande n'est pas une colonne mais une **scission** : `IsWorkingDay` répond
  à deux questions à la fois — « ce jour compte-t-il dans la durée » (`Count`, l'avance de `Lay`) et
  « une fenêtre peut-elle s'ouvrir ou se fermer ici » (`NextWorkingDay`, le `End` de `Lay`) — et un
  férié drapeau `CountsAsWorkingDay` répond **oui** à la première et **non** à la seconde. Le
  week-end répond non aux deux, et une `PromotionPause` aussi : une semaine d'examens ne compte
  jamais comme ouvrée.
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

## Un code de plus, et son inverse (13/09/2026)

`AFFECTATION_IMPORT_REVERSED` (entité `AffectationImport`). Le constat porte
`affectationsRemoved`, `rotationsRestored`, `periodsRestored` et `alreadyGone`.

⚠ **C'est `affectationsRemoved` qui fait exister l'entrée**, pour la même raison que `periodsDropped`
sur l'aller : annuler un import qui n'avait rien remplacé et annuler celui qui a réécrit une promotion
publiée sont le même code et deux événements sans rapport.

⚠ **Et l'acte annulé n'est pas effacé du registre métier non plus** : `AffectationImport` reste, marqué
`Reversed`, avec sa date et ses entrées. Le registre d'audit dit **qui** a annulé ; le registre d'import
dit **quoi**, et les deux sont nécessaires — une ligne qui disparaît en se défaisant répond « il ne
s'est rien passé » à la question du dossier.

## Rattraper une fenêtre déclarée trop tard (20/09/2026)

Déclarer une suspension n'écrit **aucune date** — c'est ce qui la rend révocable — donc une fenêtre
posée après que la grille a été bâtie laisse les colonnes où elles sont, plus courtes de ce qu'elle
leur prend. `POST levels/{id}/axis-relay` les repose, et fait suivre les rotations publiées.

- ⚠ **Ce n'est pas une annulation, dans aucun des deux sens.** L'axe est un **calcul** — date
  d'ancrage + longueur de colonne + calendrier de la promotion — et l'acte **écrase** ce qui est
  stocké par ce que ce calcul rend aujourd'hui. Supprimer la fenêtre de janvier ne défait rien : le
  même calcul, sur un calendrier qui ne la contient plus, rend des dates antérieures, et on les
  réécrit. Il n'y a ni historique ni état précédent stocké.
- ⚠ **Ce qui manquait n'était que la capacité d'écrire dans ce sens.** `ExtendTo` repousse une fin et
  jamais ne la ramène — à raison : les journées pointées entre la nouvelle fin et l'ancienne
  seraient des présences à des dates que la rotation ne couvre plus. D'où `ShortenTo`, **avec sa
  propre garde**, et `FirstDivergentColumn` qui cherche désormais une colonne trop longue autant
  qu'une trop courte.
- **La longueur d'une colonne est dérivée, jamais demandée** — par le **mode** des colonnes mesurées
  sur le calendrier courant. Elle ne se lit pas sur les dates stockées : ce sont elles que la fenêtre
  a abîmées, donc une colonne amputée de cinq jours se reposerait à dix-sept et la perte deviendrait
  définitive.
- **Deux comptes se confirment, pas un** : ce que l'acte réécrit dans la grille et ce qu'il réécrit
  dans les dossiers bougent pour des raisons différentes.
- ⚠ **Pourquoi c'est un acte séparé, et non un effet de la déclaration.** Déclarer écrirait alors
  ~4 600 lignes, et révoquer devrait les défaire — toute la propriété qui fait tenir `PromotionPause`
  s'effondre. Trois raisons de plus : une fenêtre déclarée **avant** la pose de l'axe n'appelle aucun
  recalcul (l'axe l'enjambe) ; deux fenêtres déclarées de suite doivent donner **un** recalcul et non
  deux ; et l'acte change la date de fin de l'année universitaire, qui est une décision.
- **Mais les deux écrans se tiennent par un lien** : le compte des colonnes traversées, dans le
  panneau des suspensions, pointe sur « Recalcul de l'axe » avec la promotion déjà choisie. Nommer un
  dégât sans montrer le remède est ce que ce document reproche ailleurs aux rapports de pause.

→ `AxisRelayPlanner` (pur), `AxisRelayReader`, `AxisRelayCrossingReader`, `PGSH.Frontend`
  → `AxisRelayPage`
