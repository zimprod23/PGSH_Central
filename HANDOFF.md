# HANDOFF.md

> ## ▶ Start here — next session
>
> What is actually waiting. Sessions follow, newest first; anything closed has moved to
> [`HANDOFF-ARCHIVE.md`](HANDOFF-ARCHIVE.md).
>
> | # | Do this | Why it is not done |
> |---|---|---|
> | **0aw** | **Donner à la délocalisation de masse une annulation de masse.** `CancelDelocalizationCommand` est **par étudiant** et c'est le seul chemin de retour : défaire ce qu'un clic a fait sur un roster entier demande autant de clics qu'il y a d'étudiants. Mêmes pièces que l'acte : `StudentTargets`, aperçu + `ConfirmedCount`, rapport refus en tête — et le refus qui compte est déjà écrit, `Delocalizations.AlreadyMarked` (la note est la seule trace du stage, l'annuler la supprimerait). | ⚠ **Rencontré pour de vrai le 08/09/2026** : remettre à zéro la 4ᵉ MED après un test bute dessus, parce qu'une période de délocalisation naît **`IsStarted && IsComplete`** — donc `AffectationToll.IsUnderway` est vrai, « Réinitialiser les cohortes » refuse, « Dépublier » laisse délibérément les périodes **ad hoc** en place, et il ne reste que l'annulation une par une. ⚠ **C'est la règle maison appliquée à moitié** : « tout import de masse a besoin d'une échappatoire par ligne **et** d'un retour en arrière à côté » — le retour existe, mais pas à l'échelle de l'acte. ⚠ Ne pas en faire un acte destructeur de plus sans son propre `ConfirmedCount` : il tombe sur des étudiants dont personne n'a tapé le nom, exactement comme l'aller. |
> | **0at** | **Finir le balayage d'invalidation RTK sur les actes qui changent l'appartenance à un roster.** Le 06/09/2026 le défaut a été mesuré dans le navigateur et corrigé sur **deux** actes — `changeStudentGroup` et `swapStudentGroups` nomment `group-{cible}` **et** `group-{source}`, et le commentaire au-dessus raconte pourquoi. Les autres ont été oubliés : `transferStudent` (⚠ **revu le 08/09/2026 dans le navigateur** : après un transfert définitif la fiche du groupe source listait toujours l'étudiant, 12 au lieu de 11, jusqu'au rechargement), `assignStudentToGroup`, et `applyBulkRosterAssignment` qui nomme la cible mais aucune source. | ⚠ **`Level/GROUPS` ne suffit pas** : `getGroupById` fournit `group-<id>`, une étiquette différente de celle de la liste — c'est exactement ce que dit le commentaire de `changeStudentGroup`. Le geste se lit comme un bouton qui n'a rien fait alors que la base est juste. ⚠ **Le transfert et le rattachement portent déjà la cible dans leur requête** ; la source, non — la passer depuis l'appelant, comme `ChangeStudentGroupRequest.sourceGroupId` le fait déjà. ⚠ **Pour l'acte de masse les sources sont multiples et le serveur les connaît** (le planner lit l'`AcademicGroupId` de chaque inscription) : les faire remonter dans `BulkRosterAssignmentReport` plutôt que de les deviner côté client. ⚠ **Vérifier mais ne pas corriger à l'aveugle `delocalizeStudent` / `cancelDelocalization`** — une délocalisation **ne change pas** l'appartenance au roster, donc l'absence de `group-<id>` y est peut-être correcte ; c'est la grille du stage qu'elles doivent rafraîchir. |
> | **0au** | **Supprimer `AutoArrangeResult`, la seconde forme de `RotationArrangeResult` — puis balayer ses semblables.** Les deux records vivent dans **la même couche** (`PGSH.Application`), l'« interne » est déjà `public`, et la copie ne porte ni vocabulaire ni contrat propre : c'est une recopie à une frontière qui n'en est pas une. Faire porter au `AutoArrangeStageScheduleCommand` le record de l'arrangeur directement. Puis un passage sur les autres résultats de commande pour voir s'il en reste. | ⚠ **C'est exactement ce qui a coûté le défaut du 08/09/2026** : étendre `RotationArrangeResult` a laissé la copie derrière, donc `PinnedCellsKept` et `ReservedServices` étaient calculés, portés à travers le handler, puis **jetés au bord de l'API** — la réponse ne portait pas les champs et aucun écran n'aurait pu les afficher. Corrigé en `c0e5755` **en ajoutant les champs des deux côtés**, ce qui répare l'instance et laisse la classe de défaut en place. ⚠ **Ne pas confondre avec `MacroPlanResult`**, qui n'est pas une copie : il agrège sur plusieurs blocs et a un sens propre. ⚠ Si un type de frontière est vraiment voulu quelque part, alors la parité se prouve par un test de réflexion — mais supprimer la copie vaut mieux que détecter sa dérive. |
> | **0av** | **Un seul propriétaire pour la phrase du serveur : le refus s'affiche deux fois.** Vu le 08/09/2026 sur une répartition refusée — « Données invalides » depuis `errorMiddleware` **et** « Erreur » depuis le `catch` de la page, la même phrase, deux bandeaux. C'est la moitié non balayée de la correction de la session 52 (« le client ne réécrit plus la phrase du serveur ») : là c'était une *devinette* par-dessus, ici c'est un *doublon*. Le middleware global est le propriétaire ; le `catch` de la page ne garde que son état local (jeter le rapport, refermer). | ⚠ **Ne pas supprimer les `catch` en bloc** : certains ajoutent un contexte que le serveur n'a pas (« relancez l'aperçu »), et ceux-là restent — ce qui part est la re-notification de la **même** phrase. ⚠ Le bruit est le vrai coût : deux bandeaux pour un refus, c'est la règle « une alerte qui se déclenche quoi qu'il arrive est du bruit, et le bruit est ignoré » appliquée aux refus. |
> | **0ao** | **Redémarrer l'AppHost, puis ajouter « Santé Publique » et « Simulation Médicale » au CNPN 1650.25 de la 3ᵉ MED** — le sélecteur les offrira enfin (voir la session 51). ⚠ **Puis s'arrêter et lire la ligne suivante avant de toucher à la planification.** | Sans redémarrage l'API porte encore le plafond de page à 100 et `GET /stages?levelId=3&pageSize=200` répond **400** : la liste des stages revient vide et le champ reste grisé en disant « Tous les stages du niveau sont listés ». Rien à sauvegarder — enregistrer un CNPN n'écrit que `Curriculums`/`CurriculumStages`. |
> | **0ap** | ⚠ **Poser les axes AVANT de répartir, maintenant que reposer un axe est gratuit.** Mesuré le 07/09/2026 après la remise à zéro : 2026-2027 porte **0 roster, 0 cohorte, 0 cellule**, et il reste **71 créneaux** — 5ᵉ MED (7 stages × 9 colonnes) et 5ᵉ Pharmacie (2 × 4). L'axe de la 3ᵉ MED a disparu avec son bloc. **Rien n'est publié nulle part**, donc `ApplyRotationCycleCommand` ne refusera sur aucune promotion : c'est exactement le moment où les nouvelles durées peuvent entrer dans la grille. | ⚠ **Une durée n'entre dans l'axe que par *k*ₛ**, jamais par une colonne plus large : les colonnes d'un axe ont toutes la même largeur, c'est ce qui rend le croisement possible. La 3ᵉ MED a maintenant **8 stages** au catalogue (30 j. pour Médecine et Chirurgie, 15 pour les six autres) : si les huit sont exigés par le texte, *T* = 2+2+1+1+1+1+1+1 = **10 colonnes de 15 jours**. ⚠ **Un stage exigé par le CNPN et sans créneau est dû et jamais servi** — donc l'ordre est : arrêter le texte (item 0ao), puis poser l'axe, puis découper, puis répartir, puis publier. [`docs/planning-rotation.md`](docs/planning-rotation.md). |
> | **0ar** | **Repartir de zéro sur 2026-2027 : découper chaque promotion, poser son axe, répartir, publier.** Les **6 839** inscriptions de l'année sont toutes détachées (0 roster), donc `AutoArrangeGroupsCommand` les ramassera intégralement — c'est la condition qu'il exige et elle est remplie partout. Promotions à servir : 3ᵉ MED 933, 4ᵉ MED 925, 5ᵉ MED 842, 6ᵉ MED 701, 7ᵉ MED 1 347, 5ᵉ Pharma 212, 6ᵉ Pharma 314, plus les petites années. | ⚠ **La 7ᵉ MED a 0 stage au catalogue** — rien ne peut l'y placer, et ses 510 stages dus sont tous revalidables (item A7). ⚠ **L'historique importé est intact** : 13 793 cohortes, 98 555 affectations, 105 626 périodes sur les six années passées — la remise à zéro n'a touché que 2026-2027, ce qui est le comportement attendu et vérifié le 07/09/2026. Point de sauvegarde avant chaque « Générer le plan ». |
> | **0aq** | **Redémarrer l'AppHost, puis dérouler `SMOKE-TEST.md` §53** — « Supprimer les groupes » est désormais scopé sur la promotion affichée. ⚠ **La base a été remise à zéro côté planification par l'utilisateur** (07/09/2026), donc les nombres de §51 et des items 0an / 0ap ne valent plus : les relire sur l'écran avant de s'en servir. | Sans redémarrage l'API ignore le nouveau `levelId` (un paramètre de requête inconnu ne se lie à rien) et l'acte reste annuel — donc toujours refusé dès qu'un étudiant est rattaché n'importe où dans l'année. ⚠ C'est un acte **destructeur** : il supprime les rosters **et leurs cohortes**. Point de sauvegarde avant, même sur une base sans planification. |
> | **A1** | **Phase 18.2 — la restauration, pour de vrai.** 18.1 est **livré** (session 40) : dumps planifiés, points nommés, manifeste, plan de restauration chiffré, et la bannière « y a-t-il un retour en arrière ? » dans la déliberation, la réinscription et l'application d'un axe. Reste : **une restauration que quelqu'un a réellement exécutée** (contre une base de rebut), `pgsh-snapshot`/`pgsh-restore` en scripts hors API, l'assertion des effectifs en SQL, le volume **Keycloak**, et l'undo par acte pour la déliberation et le rouleau. | ⚠ **`BackupVerification.Restored` est une valeur que rien ne pose aujourd'hui** — l'application sait relire la table des matières d'une archive (`pg_restore -l`), ce qui attrape une archive tronquée et rien de plus. **Une sauvegarde que personne n'a restaurée est une hypothèse.** Et il n'y a **volontairement pas** de bouton « Restaurer » : un processus ne peut pas remplacer la base dont il se sert ; le plan affiche la commande, la pile arrêtée. `PHASES.md` §18.2, `SMOKE-TEST.md` §41. |
> | **A2** | **Phase 17.1 — the mid-flight reschedule.** « On est en P3, peut-on changer P7 ? » ⚠ **Phase 17 itself is done (session 49)**, and it built the calendar half this needs: a promotion's window now joins its own `WorkingDayCalendar` and the axis laid afterwards steps over it, in jours ouvrables, with no date pushed onto any assignment. What remains is the *other* direction — moving a column that is **already published**. ⚠ **Measured on the live base 06/09/2026 and no longer a guess**: the 3ᵉ MED holds **804 published cells**, so « Appliquer l'axe » is disabled and `ApplyRotationCycleCommand` refuses on `PublishedCells > 0` — a promotion pause declared after publication therefore has **no remedy at all** today, and the preview now says so instead of prescribing a refused button. Today: no, and `UpdateStageSlotCommandHandler` is the dangerous half: it has **no published-guard at all** and rewrites a window without touching the périodes published from it. `SetCohortSlotAssignment` refuses on « the cohorte is published » where `PublishedCells.IsCellPublishedAsync` would answer « is *this cell* published », and `UnpublishCohortSchedule` has no period scope, so undoing P7 undoes P1-P10 and `Force` takes the marks and attendance with it. | Filed as `PHASES.md` §17.1 because "shift the later périodes *and their cells*" is one operation. ⚠ Phase 17's answer does **not** cover it: a declared window changes what a *future* axis is laid against, and says nothing about a column already published. ⚠ `SingleService` complicates all three: the *kₛ* cells fold into one `ServicePeriod`, so editing a column mid-run splits a stay. |
> | **A3** | **Faire écrire les six actes destructeurs qui n'écrivent rien.** `DeleteAllCohortsCommand` (« Réinitialiser les cohortes »), `DeleteCohortCommand`, `UnpublishCohortScheduleCommand`, `UnpublishStageScheduleCommand`, `StageSlotCommands` : aucun ne porte `IAuditableCommand`. ✅ **`DeleteAllGroupsCommand` est sorti de cette liste le 07/09/2026** (session 52) — il écrit `YEAR_GROUPS_DELETED` / `PROMOTION_GROUPS_DELETED`, ce qui laisse **cinq** actes muets, tous du côté cohorte. Ce sont ceux qui suppriment affectations, périodes, **évaluations et présences** — « Dépublier » avec `Force` détruit les notes. Chaque entrée doit porter ce qui a été détruit (le `AffectationToll` est déjà calculé sur place). | ⚠ **Trouvé le 07/09/2026 en cherchant des données orphelines, et c'est la moitié manquante de la réponse.** La base est **propre** — 34 contrôles, 0 ligne dans chaque classe de résidu qui compte (`NOTES.md`, session 50) — mais le registre ne peut pas dire ce qui a été fait : `ROTATION_CYCLE_DELETED` est enregistré, « Réinitialiser les cohortes » ne l'est pas. La phase 20 avait comblé les cinq actes côté **roster** et laissé le côté **cohorte** entier. `PHASES.md` §20. |
> | **A4** | **Distinguer, sur « Signalements », les absents encore `Active` des diplômés.** ✅ **La règle est tranchée (07/09/2026) et le comportement ne bouge pas** : le fichier Excel est la seule liste de ceux qui se réinscrivent, tout absent est exclu / diplômé / non pris en considération / une anomalie, et la page est le **registre** où on les garde — pas une file à vider. Ce qui reste est de la lisibilité : `RegistrationHoldsPage` filtre par raison et par état du signalement, **jamais par statut d'inscription**, donc les **49** absents encore `Active` ne se distinguent pas des **1 217** diplômés. | Petit : `RegistrationHoldResponse.RegistrationStatus` est **déjà envoyé** et déjà affiché sur la ligne ; il manque le filtre (et le compte par statut au-dessus de la liste). ⚠ Ne **pas** exempter les diplômés ni leur inventer une raison à part — la raison est la même, c'est le statut qui dit lequel des quatre cas c'est. `docs/year-closing.md`, section `RegistrationHold`. |
> | **A7** | ✅ **Règle tranchée le 07/09/2026, à implémenter — « un stage acquis ne se ressert jamais ».** Trois pièces, dans cet ordre. **①** `StudentAffectationService` cesse de créer une affectation pour un stage que l'étudiant a déjà validé dans une année non annulée, en lisant **la même règle** qu'`OutstandingStageFinder` (une seule source, sinon deux écrans divergent sur « acquis »), et **nomme** ce qu'il écarte — « 3 étudiants non affectés : stage déjà acquis » — via le `BulkResponse` par ligne. **②** Généraliser « Revalider » en « servir un stage dû » : retirer la précondition `NothingToRevalidate` pour qu'un stage **jamais tenté** ou resté `NonÉvalué` puisse être confié, en gardant le refus sur un stage validé (= item 13). **③** Élargir « ce qu'il doit » à *exigé par le CNPN − validé* dans `OutstandingStageFinder` — le fichier dit déjà que c'est l'endroit naturel. | ⚠ **① est un no-op sur la base d'aujourd'hui, ce qui en fait le moment le moins cher pour l'installer** : les seuls redoublants des promotions planifiées de 2026-2027 sont **27 étudiants** dont **toutes** les tentatives antérieures sont `NonÉvalué`. ⚠ **② et ③ vont ensemble et attendent l'item 1** (les jeux d'exigences 1650.25) : tant que « dû » = « toutes les tentatives échouées », ce qui est dû est exactement ce qui est revalidable — c'est cohérent, et ③ seul créerait des dettes sans bouton. ⚠ **La 7ᵉ année n'est concernée par aucune des trois** : elle a 0 stage au catalogue, donc rien ne peut l'y replacer, et ses **510** stages dus (242 étudiants) sont **tous** revalidables aujourd'hui. `docs/progression.md`. |
> | **A8** | **Faire saisir les 4 196 tentatives `NonÉvalué` des 7ᵉ année Médecine** — servies, jamais notées. **Ce n'est pas du code**, c'est l'import d'évaluations et la liste de travail des chefs. | ⚠ **C'est le vrai risque sur « ce qu'il lui reste »**, et il est invisible : un stage servi mais non noté n'est ni acquis (23 569 le sont) ni dû (510 le sont) — il est en limbes, sur aucune des deux listes. Le jour où la faculté tranche que « non noté » vaut « non fait », 4 196 lignes deviennent des dettes d'un coup, sur des étudiants qui se croyaient finis. Mesuré le 07/09/2026 sur les 1 347 inscrits de 7ᵉ MED 2026-2027. |
> | **0as** | **Dérouler `SMOKE-TEST.md` §54 — il ne reste qu'une ligne à voir.** L'AppHost a été redémarré le 08/09/2026 et le reste de §54 a été **vérifié à l'écran** (motif du groupe, partition K, cohortes, bascule « Réservé », épinglage, et l'assertion : la cellule épinglée a survécu à une répartition sur **toutes** les partitions, le service réservé est resté à 0/20 et le compte de services saturés est passé de 3 à 2). Ce qui n'a pas pu être lu est le **libellé** des deux nombres dans le bandeau : `Grille de planning` → filtre **Toutes** → `Répartition auto.` → fenêtre **P1** → `Répartir` doit annoncer « N cellule(s) épinglée(s) conservée(s), N service(s) réservé(s), hors rotation ». | Les champs eux-mêmes sont épinglés par `NominativePlacementEndpointTests.The_arrange_response_carries_what_it_left_alone` (morsure vérifiée), et l'API qui tourne porte le correctif `c0e5755` depuis le redémarrage — il ne reste que le rendu. ⚠ **À confirmer au passage** : dans le sélecteur de service d'une cellule de la grille, un **clic souris** sur une option n'a déclenché aucune requête là où le **clavier** (↓ puis Entrée) a écrit la cellule. Ce peut être un défaut de coordonnées de l'automatisation et non un défaut de l'écran — le vérifier à la main avant d'ouvrir quoi que ce soit. |
> | **A5** | **Reprendre les cinq constats du balayage du 07/09/2026 laissés « à vérifier plus tard »** (`NOTES.md`, session 50 — chacun a sa requête) : ① **189 cohortes** dont le stage n'est pas de la promotion du roster (`Interne CHU Médecine` et `Retrait` n'ont aucun stage à eux — histoire réelle, mais elles se comptent dans deux promotions selon le chemin de lecture) ; ② **19 étudiants sans aucune inscription** (état supporté, mais invisibles de toute promotion et rien ne dit qu'ils sont incomplets) ; ③ **10 paires de périodes qui se chevauchent** pour un même étudiant, toutes issues de l'import Access ; ④ **10 631 périodes qui débordent la fin de leur année** (toutes *après* le 31 août — l'année COVID, surtout) ; ⑤ **36 346 inscriptions `Active` sur des années passées** (= le trou connu, Phase 14.3). | **Aucun n'est un résidu produit par PGSH** et aucun ne bloque quoi que ce soit aujourd'hui — c'est pourquoi ils sont ici et non plus haut. Le balayage complet (34 contrôles) est à **0** sur toutes les classes de résidu qui comptent : l'affectation qui survit au pointeur, le roster affiché vide qui porte encore des affectations, l'inscription rattachée à une autre année ou promotion, la cellule d'une autre cohorte. ⚠ ③ et ④ ne sont **pas** réparables sans une décision de la faculté : ce sont des faits enregistrés sur des années closes. |
> | **0am** | **Relancer l'AppHost (migration `ExternalServices`), créer le service « Stage hors CHU — Kénitra », puis dérouler `SMOKE-TEST.md` §49.** Sans la migration l'API interroge une colonne qui n'existe pas et la liste des services répond **500**. Back **et** front sont faits : 31 tests neufs (1 650 verts), `tsc`, `eslint` et `npm run build` propres. Rien n'a été **cliqué**. | ⚠ **Aucun service externe n'existe dans la base** : le drapeau vaut `false` partout et rien de la phase n'est visible tant que la ligne n'est pas créée. La seule étape destructrice est §49.5 (elle supprime les rotations planifiées des étudiants nommés) — **point de sauvegarde avant**. Le contrôle qui compte est le `ConfirmedCount` : lancer l'aperçu, inscrire un étudiant de plus dans le roster depuis un autre onglet, appliquer → doit **refuser** en nommant les deux nombres, et n'écrire **rien**. `PHASES.md` §25. |
> | 0ae | **Relancer l'AppHost, puis dérouler `SMOKE-TEST.md` §41.** Les routes `/api/backups*` n'existent pas dans un processus antérieur à cette session : le contrôle qui distingue « route absente » de « non authentifié » est que `safe-point` répond **404** sur l'ancien processus et **401** sans jeton. | Rien dans §41 n'écrit dans la base (un `pg_dump` est une lecture) — sauf les entrées d'audit `BACKUP_POINT_*`. Les deux étapes qui comptent : **arrêter Docker** et vérifier que le bandeau dit « service indisponible » et non « aucune sauvegarde », et **prendre un point depuis la bannière de la déliberation** sans perdre le fichier chargé. ⚠ Ne **pas** exécuter la commande de restauration sur la base vivante ; §18.2 est exactement ce qui manque pour l'éprouver proprement. |
> | 0ad | **Relancer l'AppHost** pour le pic corrigé et les barres empilées par promotion. Sans ça le document affiche encore « pic du 07/09 au 06/10 » (un mois pour un plateau de six) et des barres grises au lieu de la répartition 3ᵉ/4ᵉ année. Le repli est en place, donc la page fonctionne — elle est seulement moins juste. | Corrigé côté serveur : `PeakStart/PeakEnd` sont l'**enveloppe** du pic et non le premier intervalle qui l'atteint, `PeakDays` dit le temps réellement passé à ce niveau (les intervalles au pic ne sont pas contigus — l'axe 3MED a des coupures de 2 jours), et `Months[].Levels` porte le découpage par promotion lu sur l'intervalle de pic du mois. 1 407 tests verts. |
> | 0aa | **RESTART the AppHost, then drive the two new backend-dependent screens.** The API process predates this session's build, so `GET /services/occupancy-report` 404s and `?status=` on `/students` is silently ignored (an unknown query param binds to nothing — the filter appears to do nothing rather than erroring). | Built and green (1 405 tests) but only one of the three features is verified in a browser: the student file's **Stages** tab reads endpoints that already existed and was walked end to end on Houda Aamoud — 21/21 stages, 3ᵉ année 2/6 with the other four « jamais tenté » and *not* « à revalider », which is the distinction the tab exists to draw. The status filter's dropdown renders with the right five verdicts; the **filtering** and the whole **Charge des services** report are unverified against a running API. |
> | 0 | **Finish `SMOKE-TEST.md` §28 f/g**, then remove `SMOKETEST01`/`SMOKETEST02` from the base (SQL is in §28). | The session expired mid-step on 2026-08-30. **`PriorEnrolments` is still 0 rows**, so the équivalence — the whole reason the table exists, and the row a future widening of « ce qu'il doit » will depend on — has never been written outside a test. Everything else in §28 passes. |
> | 0ak | **Redémarrer l'AppHost (migration `StageAllowedServiceRank`), puis dérouler `SMOKE-TEST.md` §44** — l'ordre des services d'un stage est désormais **choisi** et c'est lui qui décide quels groupes vont où. Rien du tableau n'a été piloté au navigateur. | Serveur couvert par 19 tests, dont 5 par le vrai pipeline HTTP, et la morsure vérifiée deux fois (casser l'ordre → 2 échecs ; casser la garde de permutation → 3 échecs). ⚠ La migration **remplit le rang depuis `ORDER BY "ServiceId"`**, donc l'appliquer ne change aucun plan existant — mais sans elle l'index unique `(StageId, Rank)` échoue d'entrée, les 146 lignes portant toutes 0. L'étape 5 est la seule qui écrit des cellules : point de sauvegarde avant. |
> | 0c | **Restart the API and re-check one caption** — the exports' « N inscription(s) » printed « 5.932 » because `:N0` used the host's `CurrentCulture`. Fixed to `ExportLabels.Fr`, but the running process predates the fix. | Cosmetic and confined to the caption row — the cells are typed values, not formatted strings — but it is a document that leaves the system, and « 5.932 » reads as five-point-nine-three-two. |
> | 0b | **Click « Supprimer le bloc » once** (same as item 2), and re-check the two cosmetic fixes from §29 in a browser. | `SMOKE-TEST.md` §29 ran 2026-08-30: steps 0-6 **pass** against the live base, store provably unchanged after every refusal. Step 7 was **not** run — the base holds 0 published cells, so it would *succeed* and destroy a real axis. The double-toast removal and the « nomme l'année » fix are type-clean but were not re-driven through the UI (the year picker stopped responding to automation). |
> | 1 | **Enter 1650.25's requirement sets — the stage list per level — *before* opening 2026-2027 for registrations.** ⚠ **Awaiting the list from the faculty.** | `RegistrationCnpnStamper` reads the effectivity rule once, at the creation of a registration. Open the year first and every 3ᵉ année of 2026-2027 gets a stamp pointing at a text that requires nothing — `CohortProvisioner` then stands aside silently and the promotion plans as if it owed no stage. `PHASES.md` §15.2. |
> | 2 | **Click « Supprimer le bloc » once, in a foreground tab** — `SMOKE-TEST.md` §25 step 7. | The only thing built in session 27 that no human path has exercised. Server side is covered by ten tests; what is unverified is the confirmation dialog, its counts, and that the promotion's other block survives. ⚠ Do it on a block whose loss costs nothing, or be ready to re-apply and re-plan — Med6's 1 000 cells are one *Générer le plan* away, but they are not free. |
> | 3 | **Decide whether `LateArrivalScheduler` should materialise périodes for an *unpublished* grid.** | It materialises every open cell of the roster whether or not the répartition was published, so a newcomer can hold périodes for a plan nobody published — and `SchedulePublisher` will then skip his assignment as `SkippedAlreadyServed`. The coverage half of this was fixed in session 26; this half is a design question, not a bug. |
> | 5 | **Review three 6MED service calls** on the Stage page — *Pédiatrie CCP*, *Urgences (Moulay Youssef)*, and everything at *Azzamouri*. | All three were excluded by the recency rule and all three are arguable. `SMOKE-TEST.md` §22c.5 names them and the one-line undo. |
> | 6 | **Close 2025-2026 for real** — Clôture & réinscription, exceptions canvas, confirm, apply. | 6 057 verdicts with no undo but a restore. It is the user's click, not ours. **Take a `pg_dump -Fc` first.** |
> | 7 | **Walk the defence roll** — name a handful of 7ᵉ année students « Diplômé » and check they graduate while the rest stay put. | The 14.3e rule is verified by tests and by the preview's numbers; nobody has used the flow it now depends on. |
> | 9 | **Testcontainers.** | Carried since session 22. ✅ The macro-plan path is now swept for *translation* (session 28, twelve cases, all compile, no second defect), so what is left is strictly what no amount of compiling can answer: whether the SQL returns the right **rows**, plus FK behaviour, unique indexes (`NULLS NOT DISTINCT` on the roster keys) and `OnDelete` — which is what every delete guard in the system is written against. |
> | 10 | **Sweep other screens for stale data** — `loadingMiddleware`'s re-entrant dispatch (fixed, `SMOKE-TEST.md` §20f) silently staled whichever query settled *last* on any page, for the whole life of the middleware. | The bug is fixed; nobody has checked what else it was quietly breaking. |
> | 11 | **Reprendre, étudiant par étudiant, les 57 inscriptions dont le texte n'est pas du programme du niveau.** ⚠ **Pas un `UPDATE` en masse** — décision de l'utilisateur 04/09/2026 : les déformations de l'ancienne donnée se traitent avec précision. Le geste attendu est d'afficher les 9 parcours complets et de trancher ligne par ligne. `SMOKE-TEST.md` §20g a la requête. | **Remesuré le 04/09/2026 et l'ancienne lecture était fausse sur le fond.** Ce ne sont pas 56 lignes éparses d'un backfill : ce sont **57 inscriptions appartenant à 9 étudiants nommés**, et **tous les 9 ont fait Pharmacie *et* Médecine** — ce sont de vraies réorientations, estampillées avant que `RegistrationCnpnStamper.Fallback` cesse d'être aveugle au programme. ✅ **Leurs inscriptions 2026-2027 sont toutes correctes** (2174.18 pour les sept en 7ᵉ année, 1650.25 pour celle en 3ᵉ), donc aucun verdict de dernière année n'est faussé aujourd'hui : le décalage est **historique seulement**, et son coût est une colonne CNPN trompeuse sur les années passées. ⚠ Corollaire mesuré : les « 6 inscriptions au-delà de la portée de leur texte » de `CLAUDE.md` sont désormais **0** — la reconstruction du 01/09/2026 les a effacées. |
> | 12 | **Finish the final-year gate's walk-through** — `SMOKE-TEST.md` §21 steps 2, 3, 5, 6, 7, 9. | The rule itself was run against the real base 2026-08-26 (§24): 60 of the 686 6ᵉ année Médecine owe a stage and all 60 are refused entry to the 7ᵉ. What nobody has exercised on real data is the déliberation/réinscription legs, the unstamped student, the dérogation and the revalidation. |
> | 13 | **Close the revalidation flexibility hole** — no way to hand a student a stage he never attempted, and no generic "assign this student to this cohort". | Identified in session 24. `RevalidateStageCommand` needs a prior *failed* attempt; every other creation path is bulk or specific. |
> | 15 | **Give RTK Query a request timeout.** | A hung API is today indistinguishable from an empty year: `fetchBaseQuery` sets no `timeout`, so a request that never answers leaves every screen on a skeleton with no error — `errorMiddleware` never fires, because nothing rejects. Seen 2026-08-26, when the API sat paused on a breakpoint and the frontend showed nothing at all. ⚠ Not a blanket value: `stages/macro-plan` legitimately runs long, and aborting a mutation client-side does not stop the server writing — though since session 33 it is at least *atomic*, so an abort costs the run rather than leaving half a plan. A generous global default with explicit per-endpoint overrides for the heavy writes. |
> | 16 | **Fold `ReinscriptionPlanner`'s own copy of the final-year decision into `FinalYearGuard`.** ⚠ Session 37 extracted the *other* half — « peut-être sa dernière année ? » is now `FinalYearTest`, shared by the déliberation and the réinscription roll — so what is left under this number is strictly the predicate-scoped debt lookup. | The planner builds its `FinalYearGate` from three lookups of its own rather than from the guard, so one rule now has two implementations. Deliberate for now: the planner is scoped by the *predicate* that selects the promotion — 8 077 registrations — and the guard's batch takes a list of ids, which is exactly what must not be shipped down for a promotion. Folding them means teaching the guard to take a predicate. |
> | 18 | **The pre-validation export.** | Agreed with the user as a second document: the same population without the note/verdict columns, showing where everyone *is going*. `onlyEvaluated` is already the switch and `ExportWorkbook` already the shape, so it is a column set and a caption, not a second pipeline. Deferred deliberately — the post-validation one was the ask. |
>
>

> ✅ **Vérifié au navigateur sur la base vivante, le 07/09/2026**, après redémarrage par
> l'utilisateur. **§52 est passé** :
>
> - **CNPN 1650.25 / 3ᵉ MED → « Modifier le texte »** : le champ « Ajouter un stage » est **actif**
>   et lit « Choisir un stage » ; il propose **Santé Publique** et **Simulation Médicale**, les deux
>   seules du niveau absentes du texte. C'était la panne signalée, et elle est levée.
> - **Aucun bandeau à l'ouverture.** « Données invalides · One or more validation errors occurred »
>   se déclenchait exactement là, à chaque ouverture.
> - **Le repère du catalogue nomme le texte qui diverge** (`Admin → Stages`, MED3 Chirurgie) :
>   colonne **Durée** → « **2174.18** — durée différente du catalogue », `= 1650.25 : 30j`,
>   `≠ 2174.18 : 66j` ; colonne **Coefficient** → « **1650.25** — coefficient différent du
>   catalogue », `≠ 1650.25 : 1`, `= 2174.18 : 3`. Les deux repères désignent des textes
>   **différents**, ce qui est toute la réponse au signalement.
> - **Le tableau de comparaison confirme la lecture SQL** : Chirurgie et Médecine « recoté » 3 → 1
>   et 66 → 30 ; les quatre autres « ajouté », coef 1, 15 j.
>
> ⚠ **Rien n'a été écrit** — les deux stages n'ont **pas** été ajoutés au texte (c'est la décision
> 0ap), et le tiroir d'un stage a été fermé sans enregistrer. Vérifié en base : les 8 stages de MED3
> portent toujours **0 objectif**.
>
> ⚠ **Un défaut trouvé à l'écran et corrigé** : le repère lisait « coefficient différent**e** du
> catalogue » — accord au féminin sur un nom masculin. La phrase porte désormais son propre accord.
> Revérifié au navigateur après rechargement. `tsc` et `eslint` propres.
>
> ⚠ **§53 n'a pas pu être déroulé** : l'année 2026-2027 porte **0 roster**, donc le bouton ne
> s'affiche pas (`totalCount > 0` le conditionne). Il faut découper deux promotions d'abord — c'est
> l'item 0ar, et c'est un clic de l'utilisateur.
>
> ⚠ **Une étape du smoke-test était fausse et a été corrigée** : §52.7 prescrivait de provoquer un
> 400 avec un objectif au libellé vide. `StagesPage.handleSave` **filtre** ces objectifs avant
> l'envoi, donc la requête ne part pas. Le contrôle réel est passif (aucun bandeau) et la
> démonstration est côté serveur.

## Session 57 — 2026-09-10 · La fenêtre d'une délocalisation appartient à la cohorte

**Item `0ax` fermé.** Le signalement venait du calendrier — « pourquoi le stage Cardio de la 4ᵉ MED va
du 16/11/2026 au 25/03/2027 ? » — et la cause était deux crans plus bas.

- **Le défaut.** `DelocalizationWindow.ResolveAsync` répondait `min`/`max` sur **tous** les créneaux du
  stage. Sur un axe croisé, une colonne par partition et un passage par partition, c'était autant de
  fois trop long qu'il y a de partitions : **14/09/2026 → 25/03/2027** écrit dans douze dossiers pour
  un stage servi en un mois. ⚠ **Pas une décision à prendre** — `docs/delocalization.md` disait déjà
  « la période que le stage occupe officiellement » ; le code avait cessé de calculer sa propre règle.
- **La seconde moitié, que l'entrée de file n'avait pas vue.** `BulkDelocalizationPlanner` résolvait
  **une** fenêtre **avant même de savoir qui était nommé** — la résolution des étudiants venait après.
  Deux partitions dans un lot recevaient donc les mêmes dates, dont l'une au moins fausse par
  construction. La fenêtre est maintenant **une par cohorte** ; `BulkDelocalizationPlan` n'en porte
  plus aucune.
- **Le correctif.** `DelocalizationWindow` devient une **valeur** (`Start`, `End`, `Source`) et
  `DelocalizationWindowResolver` la lit sur les cellules de chaque cohorte, en deux requêtes **plates**
  repliées en mémoire (l'agrégation `Min`/`Max` par cohorte en SQL est la forme que Npgsql refuse —
  deux cas ajoutés à `SqlTranslationTests`). Le handler unitaire résout la **cohorte avant** la
  fenêtre, ce qui est le correctif de son côté.
- **La provenance voyage avec les dates.** `Named` (saisies — elles gagnent), `Cohort` (le passage),
  `StageAxis` (⚠ tout l'axe, faute de cellule). Une cohorte non répartie **retombe** sur l'axe
  délibérément — délocaliser un stage non planifié reste soutenu — mais l'écran porte un badge « tout
  le stage » et un bandeau comptant les lignes. ⚠ **L'alternative, refuser et demander les dates, est
  une décision de la faculté** : elle n'a pas été prise ici, elle est nommée dans le document.
- **Le rapport n'annonce plus une paire unique.** `StartDate`/`EndDate` nullables, remplies seulement
  si toutes les lignes applicables partagent une fenêtre ; `DistinctWindowCount` distingue 0 / 1 /
  plusieurs, et `StageWideWindowCount` compte les lignes datées par l'axe. La modale a une colonne
  **Période**.
- **`GetYearTimelineQueryHandler` n'a pas été touché**, et c'est volontaire : étirer une bande jusqu'à
  la dernière période de ses étudiants est juste. C'était l'**entrée** qui était fausse.

**Couverture.** +9 : 6 sur l'acte de masse (dont « chaque cohorte est datée par son propre passage »
et « l'application écrit chacun sous sa fenêtre »), 1 sur l'acte unitaire, 1 de traduction SQL, 1 de
frontière (`DelocalizationEndpointTests` — les dates et la provenance traversent l'API, avec son
contrôle). **1 750 verts.** ⚠ **Morsure vérifiée** : le résolveur cassé, la nouvelle couverture tombe
en disant exactement le défaut — *« Expected rowA.EndDate to be 2026-12-13, but found 2027-03-25 »*.
Et le test qui existait passait **en cimentant le défaut** (sa cohorte n'avait aucune cellule) : il est
resté, renommé pour dire qu'il mesure le **repli**.

**Front** : `tsc`, `eslint`, `npm run build` propres. ⚠ **Rien n'a été cliqué** — `SMOKE-TEST.md` §55,
qui demande une promotion à **axe croisé** : sur un stage à fenêtre unique l'ancien calcul et le
nouveau donnent le même nombre et l'écran ne prouverait rien.

**File** : **0aw**, **0at**, **0au**, **0av**, **0as**. ⚠ **0aw devient plus intéressant qu'avant** —
c'est le seul item qui reste sur la délocalisation, et il touche le même document.

## Session 56 — 2026-09-10 · 2026-2027 remise à blanc, et mesurée

L'utilisateur a remis l'année à zéro par l'écran. **Sauvegarde prise et vérifiée avant**
(`pg_dump -Fc`, 22 Mo, `pg_restore -l` liste les 38 tables ; hors dépôt).

**État mesuré après, et c'est la référence pour la suite :**

| 2026-2027 | |
|---|---|
| rosters · cohortes · affectations · cellules · créneaux | **0 · 0 · 0 · 0 · 0** |
| inscriptions | **6 839**, dont **0** rattachée à un roster |
| services `Reserved` au catalogue | **0** |

⚠ **Les onze contrôles de résidu sont à 0** — cohorte sans roster, affectation sans cohorte ou sans
inscription, période sans affectation, cellule sans cohorte ou sans créneau, adhésion orpheline,
couverture sans période ou sans cellule, inscription pointant un roster disparu. **Et le registre est
intact** : 105 626 périodes et 13 793 cohortes sur les années passées, 537 entrées d'audit, 7 262
lignes de dossier. C'est ce qu'il ne faut **jamais** supprimer, et rien n'y a touché.

- ⚠ **« Dépublier » a été sauté sur plusieurs stages, et c'était correct.** `DeleteAllCohortsCommand`
  supprime les périodes lui-même (`ServicePeriods` → `CohortMembership` → `InternshipAssignments` →
  `CohortSlotAssignments` → `Cohorts`), et sa garde `toll.IsUnderway` **refuse en bloc** plutôt que de
  supprimer à moitié — donc sur une année entièrement `Planned` l'étape 1 n'a rien à faire. C'est ce
  que dit déjà [`docs/planning-rosters.md`](docs/planning-rosters.md) ; c'est maintenant vérifié.
- ⚠ **Le chemin par script a été refusé, et c'est bien.** Piloter les 28 « Réinitialiser » par le
  navigateur s'est révélé non fiable (le viewport se remet à l'échelle entre deux appels, des clics ne
  s'enregistrent pas — mesuré : un premier « Réinitialiser » n'était jamais parti alors que la modale
  était ouverte). L'appel direct des mêmes endpoints, authentifié comme l'admin, a été **bloqué par le
  classificateur** : 28 `DELETE` en boucle est un acte destructeur en masse. ⚠ **Ne pas contourner en
  SQL** — cela sauterait les gardes et laisserait le registre muet sur la destruction d'une
  planification annuelle, exactement ce que la phase 20 existe pour empêcher, et exactement le bouton
  unique que `EmptyAllYearGroupsCommand` refuse d'offrir.

**Aucun code touché.** La file est inchangée : **0ax**, **0aw**, **0at**, **0au**, **0av**, **0as**.

## Session 55 — 2026-09-10 · Ce que l'écran a dit, et ce que la suite n'avait pas vu

Le placement nominatif a été **piloté de bout en bout dans le navigateur** sur la 4ᵉ MED le 08/09,
puis la file a été remise en ordre pour la suite.

- **Les quatre flux demandés passent.** ① Répartition scopée : 13 cellules, « 3 service(s) saturé(s)
  — il manque 96 place(s) » ; l'appel non scopé est bien refusé d'abord (garde `SingleService`).
  ② Transfert définitif : Groupe 1 **12 → 11**. ③ Délocalisation de masse : la ligne du roster passe à
  « 12 étud. · **dont 12 hors CHU** » et Cardiologie "A" tombe de **60/20 à 47/20** — un seul nombre
  qui confirme ② et ③ ensemble. ④ Placement nominatif : motif, partition K, cohortes, bascule
  « Réservé », épinglage — puis **l'assertion** : la cellule épinglée a survécu à une répartition sur
  *toutes* les partitions, le service réservé est resté à **0/20**, et le compte de services saturés
  est passé de **3 à 2**.
- ⚠ **Un défaut que la suite ne pouvait pas voir, trouvé en pilotant l'écran.** `AutoArrangeResult`
  est une **seconde forme** de `RotationArrangeResult` dans laquelle le handler recopie : étendre le
  record de l'arrangeur a laissé la copie derrière, donc `PinnedCellsKept` et `ReservedServices`
  étaient calculés, portés, puis **jetés au bord de l'API**. Aucun écran n'aurait pu les afficher, et
  tous les tests de handler restaient verts parce qu'ils lisent le record de l'arrangeur directement.
  Corrigé (`c0e5755`) avec un contrôle **à la frontière**, morsure vérifiée. La classe de défaut,
  elle, reste — item **0au**.
- ⚠ **Et un piège de build à connaître** : restaurer un fichier par `mv` depuis un `.bak` **conserve
  son ancienne date**, donc MSBuild saute la recompilation et l'on teste un assembly périmé. C'est ce
  qui a fait passer une morsure pour un échec pendant plusieurs essais. `touch` après toute
  restauration de ce genre.
- **Quatre items en tête de file** : **0aw** (la délocalisation de masse n'a pas d'annulation de
  masse — rencontré en voulant remettre la 4ᵉ MED à zéro), **0at** (le balayage d'invalidation RTK, laissé à moitié le
  06/09 — `transferStudent` a été revu défaillant dans le navigateur), **0au** (supprimer la copie de
  record), **0av** (un seul propriétaire pour la phrase du serveur : un refus s'affiche deux fois).
- **`docs/planning-rosters.md`** dit désormais ce que les cinq actes de démontage **ne** touchent
  pas : ⚠ **un service `Reserved` le reste après une remise à zéro complète** — le mode est porté par
  le catalogue, invariant à l'année, et rien sur l'écran de planification ne dira pourquoi la
  répartition refuse de le pourvoir.

**Aucun code applicatif touché cette session** — une correction de frontière commitée, la file et la
documentation remises à jour.

## Session 54 — 2026-09-07 · Une cellule que quelqu'un a choisie, et un service tenu pour ceux-là

Phase 19.2, spécifiée le matin et livrée entière. La demande : « les services de Kénitra sont passés
au GST — professeurs chefs, services dans la base — nous avons la liste des volontaires, et ces
services leur sont réservés. »

- **① `CohortSlotAssignment.Source` (`Arranged` / `Pinned`).** L'arrangeur traite une cellule
  épinglée exactement comme une publiée, et **compte** ce qu'il a laissé (`PinnedCellsKept`, repris
  par le plan macro). ⚠ C'était le défaut le plus silencieux du lot : la répartition supprimait et
  réécrivait toute cellule non publiée à sa portée, donc un placement nominatif était détruit au clic
  suivant avec un `Assigned = N` parfaitement normal. ⚠ Et `SetCohortSlotAssignment` épingle **aussi
  quand elle écrase** — écraser le choix de l'arrangeur *est* la décision humaine.
- **② `StageAllowedService.PlacementMode` (`Rotation` / `Reserved`).** Seule pièce nécessaire à la
  justesse : un service doit être dans la liste autorisée pour être épinglable, et c'est exactement
  ce qui le mettait dans le vivier. ⚠ **Un service ne peut pas se réserver en le remplissant
  d'abord** — `saturatedServices` est calculé *après* `SaveChangesAsync`, en rapport ; le placement
  pèse par capacité et ne lit jamais l'occupation. Noté dans `NOTES.md`.
- **③ `AcademicGroup.Purpose`** — texte libre. Rien d'autre n'enregistre pourquoi un groupe existe.
- **④ L'acte de masse** — `POST /groups/assign/bulk[/preview]`, deux verbes décidés par étudiant
  (rattaché / déplacé sans trace), neuf états de ligne, `ConfirmedCount`. ⚠ `AlreadyThere` n'est **ni**
  travail **ni** refus : renvoyer une liste corrigée est l'usage normal, et le compter comme refus
  ferait passer un second passage réussi pour un échec.
- ⚠ **`StudentSelectionResolver` promu** hors de la délocalisation (`Students/Selection/`) : deux
  actes posaient la même question — « quels étudiants l'opérateur a-t-il désignés » — et le troisième
  (le choix FIFO) la posera aussi. Même précédent que `RosterScope` en session 52.
- ⚠ **L'engagement est lu par lot**, en deux requêtes plates groupées sur (inscription, groupe) :
  demandé par étudiant, cent volontaires font cent allers-retours, sur l'acte dont la raison d'être
  est que cent de quoi que ce soit est trop. Épinglé par `SqlTranslationTests` — les deux groupent
  sur une clé construite depuis une navigation, et celle des périodes y arrive par un `SelectMany`
  puis deux navigations en remontant.

**Front** : modale « Affectation nominative » sur la fiche du groupe, motif sur les deux formulaires,
bascule « Réservé » sur les services autorisés du stage, épingle sur la grille, et les deux nombres
(`pinnedCellsKept`, `reservedServices`) dits sur **tous** les retours de répartition, pas seulement
les bons.

**Tests** : 28 neufs, **1 740 verts**. Morsure vérifiée sur six gardes, une par cas. `tsc`, `eslint`,
`npm run build` propres. ⚠ **Rien n'a été cliqué** — item 0as.

## Session 53 — 2026-09-07 · Un hôpital partenaire n'est pas un hôpital extérieur

Deux sessions livrées et non commitées (51 et 52) sont d'abord entrées dans l'historique — API
(`9e8c313`, 1 712 tests verts, un `using` en double retiré au passage) et client (`8543cc7`, `tsc` /
`eslint` / `vite build` propres). **Rien n'a été cliqué** : les items 0ao / 0aq attendent toujours le
redémarrage.

Puis une demande : *« les services de Kénitra appartiennent maintenant au GST, leurs professeurs sont
chefs, leurs services sont dans la base ; nous avons une liste de volontaires pour trois stages, et
ces services leur sont réservés — c'est comme la délocalisation de masse, mais vers un groupe. »*

- **Ce n'est pas une délocalisation, et le dossier l'avait déjà tranché.** Un hôpital que la faculté
  peut superviser se répond par un **roster**, pas en sortant les étudiants de l'application
  ([`docs/planning-rotation.md`](docs/planning-rotation.md)). La faculté l'a déjà joué : cinq rosters
  entièrement au HMIMV en 2024-2025.
- ⚠ **Trois quarts de la demande passent aujourd'hui, sans une ligne de code**, et c'est mesuré : la
  suppression de l'arrangeur est **portée aux cohortes visées**, donc un label de partition à part
  (« K ») + épinglage + « Répartir » en décochant K rend exactement le flux décrit — les volontaires
  restent, les autres sont rééquilibrés.
- ⚠ **Ce qui ne passe pas est « réservé », et pour une raison qui n'était pas écrite** :
  `saturatedServices` est calculé **après** `SaveChangesAsync`, en rapport. Le placement, lui, pèse
  par `CapacityFor(levelId)` et **ne lit jamais l'occupation**. Donc on ne peut pas réserver un
  service en le remplissant d'abord : l'arrangeur ne regarde pas. Noté dans `NOTES.md`.
- **Livré : `PHASES.md` §19.2 réécrit en spécification complète** — les quatre pièces, ce qu'il ne
  faut *pas* construire (un solveur de contraintes ; un quota de niveau à 0, qui sort bien le service
  du vivier mais écrit un mensonge dans la base), l'ordre de construction, et la procédure
  intérimaire en cinq étapes. `planning-rotation.md` et `delocalization.md` portent le renvoi croisé.
- ⚠ **La numérotation est une contrainte d'impression, pas un détail** : `GroupNumberRanges` replie
  des numéros de **roster**, donc les rosters volontaires doivent être numérotés d'un seul tenant
  pour imprimer « 48-60 » plutôt qu'une pluie de nombres isolés.

**Aucun code applicatif touché** — la session est un commit de rattrapage et une spécification.

## Session 52 — 2026-09-07 · Le dernier acte de roster qui sautait à l'année

Signalement : « supprimer les groupes d'une promotion répond *One or more groups in this year have
students assigned* — mais quand j'ai vidé les groupes de **toutes** les promotions et réessayé, ça a
marché. Il y a un problème de portée sur les actions de groupe. » C'est exact, et c'était l'item A6.

- **Le défaut.** `DeleteAllGroupsCommand` ne prenait qu'un `AcademicYearId`. Tous les autres actes de
  roster sont par promotion — le découpage, la répartition, le bloc, et « Vider » depuis la
  session 50 — parce qu'un roster est clé (année, promotion, numéro). Supprimer était le dernier à
  sauter directement à l'année : la garde lisait les inscriptions de **toute** l'année, donc elle
  refusait sur les étudiants d'autres promotions, et la seule issue était de vider l'année entière.
- **Et le refus ne disait rien d'utilisable.** C'était un `Error.Conflict` écrit sur place, en
  anglais, formulé à l'échelle de l'année sur un acte joué à l'échelle d'une promotion, et **sans
  aucun nombre**. Côté écran, `GroupsPage` remplaçait par-dessus la phrase du serveur par une devinette
  (« des étudiants sont affectés **ou** des affectations ont démarré ») — deux bandeaux, aucun fait.
- **Livré** : `DeleteAllGroupsCommand.LevelId` (optionnel, narrows), refus nommant la portée et
  comptant ce qui bloque (`AcademicGroups.HasStudents` porte désormais le nombre d'étudiants **et** de
  groupes ; `AcademicGroups.PromotionRostersUnderway` est le jumeau par promotion de
  `YearRostersUnderway`), niveau inconnu → `Levels.NotFound`, et **deux codes d'audit distincts**
  `YEAR_GROUPS_DELETED` / `PROMOTION_GROUPS_DELETED` — l'acte n'écrivait **rien** au registre jusque-là
  (moitié de l'item A3). `DELETE /groups/all?academicYearId=&levelId=`. Côté écran le bouton, son
  info-bulle et sa confirmation disent leur portée, comme « Vider », et le client ne réécrit plus la
  phrase du serveur.
- ⚠ **Le prédicat de portée est extrait** (`RosterScope.Query`) et partagé par les deux actes. Ils
  posaient la même question en y répondant séparément — c'est exactement comme ça que l'un a reçu la
  portée par promotion en session 50 et l'autre non. Épinglé par `SqlTranslationTests` (`LevelId` est
  **nullable** : « Non réparti » n'est d'aucune promotion, donc la comparaison est levée, pas traduite
  telle quelle — et un acte scopé promotion l'enjambe, ce qui est la bonne coupure).
- ⚠ **Un `ExecuteDelete` final relisait l'année** au lieu des rosters déjà sélectionnés. Corrigé —
  il aurait supprimé les rosters de toutes les promotions après un contrôle qui n'en avait regardé
  qu'une. **Cette ligne-là n'est couverte par aucun test** : le fournisseur en mémoire refuse
  `ExecuteDelete`, donc seul Testcontainers (item 9) pourrait la voir.

**Tests** : 4 cas neufs sur le handler + 1 de traduction SQL, **1 712 verts**. Morsure vérifiée :
portée retirée de la sélection des rosters → le cas tombe, restaurée → verts. `tsc`, `eslint`,
`npm run build` propres. ⚠ **Rien n'a été cliqué** — item 0aq.

## Session 51 — 2026-09-07 · Un plafond écrit deux fois, et un refus que personne ne pouvait lire

Signalement : « le CNPN ne veut toujours pas qu'on ajoute un stage ; j'ai créé Santé Publique et
Simulation Médicale en MED03 ; j'ai mis Médecine et Chirurgie à 30 jours et les autres à 15 ;
pourquoi j'ai une icône d'avertissement sur le stage alors que je l'ai alignée sur le CNPN, dans le
même CNPN ; et ça affiche toujours *Données invalides — One or more validation errors occurred* ».

**Quatre symptômes, trois défauts, et un quatrième point qui n'en est pas un.**

- ⚠ **Le sélecteur du CNPN était vide parce que la requête était refusée.** `CurriculumEditor` demande
  le catalogue du niveau en une page — `GET /stages?levelId=3&pageSize=200`. La règle du plafond
  existait **deux fois** : `QueryableExtensions.MaxPageSize` = **200**, qui *écrête* (la réponse
  garde le vrai `TotalCount`, rien n'est caché), et quatre validateurs qui écrivaient chacun leur
  propre **100** à la main, et qui *refusaient*. La requête répondait **400** à chaque ouverture, la
  liste revenait vide, `addable` était vide, et le champ s'affichait désactivé avec la phrase
  « Tous les stages du niveau sont listés » — qui veut dire l'exact contraire de ce qui se passait.
  Livré : `PaginationRules.IsAPageSize()` / `.IsAPageNumber()`, un seul nombre, lu depuis la seule
  autorité ; côté client `MAX_PAGE_SIZE` nomme le même nombre une fois.
- ⚠ **Et le refus ne pouvait pas être lu.** `Results.Problem(extensions: …)` remplit
  `ProblemDetails.Extensions`, qui est `[JsonExtensionData]` : les membres sont écrits **à plat**,
  comme la RFC 7807 le prescrit pour les membres d'extension. Le type `ApiError` déclarait
  `extensions.errors`, donc `errorMiddleware` ne trouvait jamais le tableau et **tout** refus de
  validation affichait « Données invalides · One or more validation errors occurred » — la phrase
  fixe de `ValidationError`, qui ne nomme ni champ ni règle. `StagesPage` lisait la vraie forme
  depuis le début ; le middleware derrière elle, non. ⚠ Corollaire trouvé au passage :
  `StageDetailPage.extractErrorCode` ne lisait que `errors[0].code`, alors qu'un refus ordinaire
  (`Error.Conflict("Schedule.AlreadyPublished", …)`) ne produit **aucun** tableau `errors` — son code
  est dans `title`. Les deux branches étaient du code mort et « Publier » disait toujours
  « Erreur lors de la publication du planning ».
- **L'icône d'avertissement avait raison, et elle était illisible.** Lu sur la base le 07/09/2026 :
  MED3 Chirurgie et Médecine lisent 30 j. au catalogue **et** dans 1650.25 — l'alignement de
  l'utilisateur a bien été enregistré — mais **66** dans 2174.18, et le coefficient est **3** au
  catalogue contre **1** dans 1650.25. Le repère signalait donc l'*autre* texte et l'*autre* chiffre.
  `StageCatalogueFigure` nomme désormais le ou les codes qui divergent et marque chaque ligne `≠`/`=`.
  ⚠ **Second défaut au même endroit** : `saveCurriculum` n'invalidait pas `Stage/LIST` alors que
  c'est la ligne du catalogue qui porte `textFigures` — le repère survivait à la modification qui
  l'aurait résolu. Les deux sens sont câblés.
- ✅ **Le CNPN lui-même n'avait aucun défaut.** `SaveCurriculumCommandHandler` accepte l'ajout d'un
  stage sans réserve ; ses quatre gardes (programme, portée du texte, stage d'un autre niveau, stage
  inconnu) sont justes et aucune n'était atteinte. Il n'y avait rien à « débloquer » côté règle.

**Tests** : 6 cas neufs (`PaginationBoundsEndpointTests`, par le vrai pipeline HTTP, plafond compris
sur `/stages`, `/levels` et `/students`), **1 706 verts**. Morsure vérifiée : plafond remis à 100 →
3 cas tombent, restauré → verts. `tsc`, `eslint` et `npm run build` propres.

⚠ **Rien n'a été cliqué**, et l'API tourne sur un processus antérieur — items **0ao** et **0ap**.

## Session 50 — 2026-09-07 · Une garde qui avait raison, dans une portée qui n'était pas la sienne

Signalement : « on avait convenu que réinitialiser les cohortes et supprimer le bloc de rotation
effacent toute la planification d'un stage — je l'ai fait, et vider un groupe me répond **11 916
affectations / 11 407 périodes** ».

**Ce que la base disait** (lu le 07/09/2026, aucune écriture) :

| promotion 2026-2027 | rosters | inscrits | cohortes | affectations | périodes |
|---|---|---|---|---|---|
| **4ᵉ Médecine** | 116 | 925 | **0** | **0** | **0** |
| 3ᵉ Médecine | 134 | 933 | 804 | 5 598 | 4 665 |
| 5ᵉ Médecine | 121 | 842 | 847 | 5 894 | 5 894 |
| 5ᵉ Pharmacie | 71 | 212 | 142 | 424 | 848 |

5 598 + 5 894 + 424 = **11 916**, et 4 665 + 5 894 + 848 = **11 407** : les nombres du refus, au
chiffre près. La promotion sur laquelle l'utilisateur travaillait — la 4ᵉ MED, dont
`ROTATION_CYCLE_DELETED` porte l'heure (10h31) — ne portait **rien**. Il était refusé sur la
planification de trois autres promotions.

- ⚠ **Deux malentendus, et un seul est un défaut.** Supprimer le bloc de rotation retire les
  `StageSlot` et cascade les **cellules** ; une affectation pend à la **cohorte** et lui survit. Seul
  « Réinitialiser les cohortes » efface des affectations, et c'est par (stage, année). Cela, c'est le
  modèle, et il est juste — [`docs/planning-rosters.md`](docs/planning-rosters.md) le dit maintenant à
  l'endroit où on lit l'ordre de démontage.
- **Le défaut : « Vider » n'avait aucune portée entre un roster et l'année entière.** Tous les autres
  actes sur les rosters sont par promotion — le découpage, la répartition en groupes, le bloc — et
  `AutoArrangeGroupsCommand` ne ramasse que les inscriptions dont `AcademicGroupId` est **null** :
  re-découper une promotion **exige** de vider ses rosters d'abord. C'était le seul acte à sauter
  directement à l'année. Et la page portait un filtre **Niveau** que le bouton d'à côté ignorait.
- **Livré** : `EmptyAllYearGroupsCommand.LevelId` (optionnel, narrows), lu par
  `AffectationTollReader.ForPromotionRostersAsync`, refus nommant la promotion
  (`AcademicGroups.PromotionRostersHaveAffectations`), code d'audit **`PROMOTION_GROUPS_EMPTIED`**
  distinct — un registre qui appelle les deux actes pareil ne peut pas dire lequel a été joué.
  `DELETE /groups/all/students?academicYearId=&levelId=`. Côté écran, le bouton et sa confirmation
  **disent leur portée** : « Vider la promotion » quand le filtre est posé, « Vider toute l'année »
  sinon, avec la phrase qui invite à filtrer.
- ⚠ **Un niveau inconnu refuse** (`Levels.NotFound`) au lieu de retomber sur « aucun niveau nommé » :
  c'est l'élargissement-sur-absence, sur le seul acte ici qui écrit à l'échelle d'une année.
- **Tests** : 3 cas neufs (2 sur le handler, 1 sur la traduction SQL de la requête à deux sauts avec
  `LevelId` nullable), **1 700 verts**. Morsure vérifiée : garde dé-scopée → le cas tombe.
  ⚠ Le succès reste couvert « il a atteint l'écriture » — `ExecuteUpdate` n'existe pas sur le
  fournisseur en mémoire, et écrit en `IsSuccess` le cas serait inatteignable.

## Session 49 — 2026-09-06 · Une semaine d'examens appartient à une promotion, et elle ne pousse aucune date

> ✅ **Vérifié au navigateur sur la base vivante, le 06/09/2026**, après redémarrage de l'AppHost par
> l'utilisateur. Migration `PromotionPauses` appliquée, `GET /calendar/promotion-pauses` → **200**.
> Déroulé : le sélecteur n'offre pas « Retrait » ; l'aperçu de la 3ᵉ MED sur le 18→22/01/2027 donne
> **5 jours ouvrables**, **6 créneaux** (un par stage), **0 rotation en cours**, **933 étudiants** —
> l'effectif exact de la promotion, ce qui vérifie le cadrage par l'inscription — et chaque stage passe
> de **30 à 25** jours ouvrables sur sa colonne ; l'aperçu n'écrit rien (vérifié au réseau : seul
> `POST …/preview` part) ; déclaration **201**.
>
> **L'A/B qui compte, à entrées identiques** (3ᵉ MED, départ 07/09/2026, 30 j. ouvr.) :
>
> | | avec la fenêtre | après retrait |
> |---|---|---|
> | C3 | déc 2 → **janv 15** | déc 2 → **janv 15** (inchangée, avant la fenêtre) |
> | C4 | **janv 25 → mars 5** | **janv 18 → févr 26** |
> | C5 | mars 8 → avril 20 | mars 1 → avril 13 |
> | C6 | avril 21 → juin 3 | avril 14 → mai 27 |
>
> L'axe **enjambe** la semaine d'examens — C4 démarre le lundi suivant — et les six colonnes gardent
> leurs 30 jours ouvrables. Le retrait le repose **exactement** sur l'axe stocké. La base est revenue
> à son état d'origine ; il ne reste que les deux entrées d'audit, ce qui est le but.
>
> ⚠ **Un défaut trouvé à l'écran et corrigé** : la mention « aucun jour férié n'est enregistré » était
> mesurée **sur la fenêtre**, donc s'affichait sur presque toutes — du bruit, exactement ce que la
> règle maison interdit. Elle porte désormais sur l'**année universitaire**. Morsure prouvée.
> **1 696 tests verts.** ✅ **Correctif revérifié au navigateur après un second redémarrage** : sur le
> même aperçu la mention a disparu (l'année porte 14 fériés) et il ne reste que l'avertissement qui
> sert à quelque chose.
>
> **Le reste de §50, déroulé au second passage :**
> - **Chevauchement** : une seconde fenêtre 22→26/01 pour la 3ᵉ MED — elle ne *touche* la première que
>   le 22 — est **refusée** en nommant l'existante et ses dates. Rien n'est écrit. Bornes incluses.
> - **Le contrôle** : les **mêmes** dates pour la **4ᵉ** MED sont acceptées. Le garde porte bien sur le
>   couple (année, niveau) et non sur les dates.
> - **Correction sans mouvement** : ne cocher que « dates confirmées » → **un seul toast**, aucun
>   message sur les créneaux (la garde `DatesMoved`), et la ligne prend le badge « provisoire » —
>   dessiné sur l'état **rare** seulement (§1k).
> - **Correction avec mouvement** : janvier → mars annonce **12 créneaux**, soit les 6 colonnes de la
>   fenêtre quittée **plus** les 6 de celle rejointe, comptées une fois. C'est l'union, exactement.
> - ⚠ **Et un chiffre qui vaut confirmation** : la fenêtre déplacée au 08→12/03/2027 coûte **3** jours
>   ouvrables et non 5, parce que l'Aïd al-Fitr estimé tombe les 9 et 10. Une fenêtre posée sur des
>   jours déjà fériés coûte moins que sa longueur, et l'écran le dit.
> - **Deux toasts au maximum, jamais un doublon** (§1e), sur les six actes déclenchés.
>
> **La base est revenue à son état d'origine** aux deux passages : 0 fenêtre déclarée, seules les
> entrées d'audit demeurent.
>
> ### ⚠ §50.5 a été déroulé, et il a corrigé la phase
>
> Troisième passage, sur autorisation explicite de l'utilisateur, **précédé d'un `pg_dump -Fc`**
> (`pgsh-avant-relais-axe-3med-20260906-233106.dump`, 22 Mo, 298 entrées de table des matières,
> **sorti du conteneur** vers `C:/Users/LEGION/pgsh-backups/`).
>
> **Reposer l'axe est refusé sur une promotion publiée.** « Simuler » passe, puis le bandeau dit
> « 804 créneau(x) déjà publiés — ce bloc ne peut plus être redéfini » et **« Appliquer l'axe » est
> désactivé** : `ApplyRotationCycleCommand` refuse sur `PublishedCells > 0`, pour le bloc **entier**.
> Donc le geste que la documentation prescrivait — « reposez l'axe, il rattrape » — **n'existe pas
> pour la promotion qu'une fenêtre tardive pénalise**. C'est §17.1, et §17.1 n'est pas fait.
>
> **Le manque, lui, s'affiche correctement** : « Durée réelle par stage » passe de « 30 – 30 » à
> **« 25 – 30 »** jours ouvrables dès la fenêtre déclarée, sur les six stages. La grille garde ses
> dates et perd bien les cinq jours. Store vérifié inchangé après la simulation : 36 créneaux, 804
> cellules, 5 598 couvertures, P4 toujours 18/01 → 26/02.
>
> **Corrigé en conséquence** — le rapport ne prescrit plus un bouton qui refuse :
> `PromotionPauseQueries.PublishedCellsQuery` (à travers la table de couverture, jamais par le FK de
> tête), `PromotionPauseImpactResponse.PublishedCellsInGrid`, l'avertissement qui **branche** dessus,
> la tuile « Cellules publiées » sur l'aperçu, et les deux toasts qui disaient « reposez l'axe ».
> Les commentaires de `PromotionPause`, du lecteur d'impact, de `planning-rotation.md` et de
> `PHASES.md` §17 disaient tous la même chose fausse : réécrits. **1 697 tests verts** (+1), morsure
> prouvée. `SMOKE-TEST.md` §50.5 est réécrit autour de ce qui s'est réellement passé.
>
> ⚠ **Le bandeau de sauvegarde a bien fait son travail au passage** : il a signalé que le dernier point
> datait de la migration `StageAllowedServiceRank` alors que la base tourne sous `PromotionPauses`, et
> que le restaurer demanderait donc une étape de schéma. C'est exactement le cas que §18.1 existe pour
> nommer.
>
> ✅ **Correctif revérifié à l'écran après redémarrage**, en aperçu seul (n'écrit rien), avec son
> contrôle :
>
> | | 3ᵉ MED (publiée) | 4ᵉ MED (répartie, non publiée) |
> |---|---|---|
> | Cellules publiées | **804**, en rouge | **0** |
> | Avertissement | « l'axe … est déjà publié (804 cellule(s)), donc le reposer est refusé … déplacer une colonne déjà publiée n'est pas encore possible » | « **Reposez l'axe** de la promotion pour que les colonnes l'enjambent » |
> | Étudiants concernés | 933 | 0 — la 4ᵉ n'a aucune période, elle n'est pas publiée |
>
> La prescription n'apparaît donc plus que là où elle est réalisable. Les autres chiffres sont
> inchangés (5 jours ouvrables, 6 créneaux sur la 3ᵉ, 5 sur la 4ᵉ), et le rapport se vide bien quand la
> promotion change dans le formulaire — un aperçu décrit la fenêtre saisie, pas la précédente.
>
> **Reste non exécuté** : reposer un axe pour de vrai. Ce n'est possible que sur une promotion **non
> publiée** — dépublier d'abord emporterait notes et présences. La 4ᵉ MED est la candidate naturelle
> le jour où on voudra l'éprouver.

**Phase 17**, prise en tête de file après que l'utilisateur a demandé de suivre la recommandation.
Elle traînait depuis la session 40, où il l'avait posée en une phrase : *« a pause and a matter of
exams … is a matter of whole promotion because some promos does not have exams while others have »*.

### La décision, parce que c'est elle qui fait la phase

**Une suspension est un fait de calendrier, pas un second mécanisme qui pousse des dates.** `Holiday`
et le nouveau `PromotionPause` implémentent tous deux `ICalendarClosure` — « des jours où les gens
qu'elle couvre ne sont pas en service » — et `WorkingDayCalendar` se construit désormais depuis des
*fermetures* et non depuis des jours fériés. Le travail réel est le partage dans `WorkingDayProvider` :
`BuildAsync` pour la faculté, `ForPromotionAsync(année, niveau)` pour une promotion. **Cinq lecteurs**
ont changé de calendrier — l'axe, l'aperçu du bloc de rotation, l'export d'un stage (`LevelId ??
stage.LevelId`), la revalidation (la promotion **de l'étudiant**, pas celle du stage) — et deux gardent
volontairement le calendrier facultaire.

**La compensation est donc en jours ouvrables et se produit au moment où l'axe est posé.** Déclarée en
septembre, la fenêtre fait qu'une colonne de quinze jours ouvrables se termine une semaine plus tard
sur le calendrier mural ; la grille est écrite depuis cet axe et les périodes publiées depuis la
grille. Le défaut « la grille dérive de ce qui a été publié » devient impossible sur ce chemin.

⚠ **Et déclarée sur une grille déjà posée, elle ne déplace rien — volontairement.** C'est l'inverse de
`ResumePeriod`, qui **accumule** : c'est exactement pourquoi celui-là ne peut être ni corrigé ni
révoqué, et pourquoi celui-ci le peut. Les créneaux gardent leurs dates ; l'aperçu compte ce qu'ils y
perdent, stage par stage et colonne par colonne, avec les rotations traversées réparties par état de
cycle de vie. **Reposer l'axe est le geste qui rattrape**, et c'est un clic parce qu'il écrit des
cellules.

### Livré

`PromotionPause` (agrégat, `Entity`, `init` sur champs explicites) · `ICalendarClosure` +
`CalendarClosureScope` · `WorkingDayCalendar.With(closure)` et `ProposedClosure`, pour que « ce que
coûterait cette fenêtre » et « ce que coûte cette fenêtre » soient la **même** arithmétique · les
quatre actes (aperçu / déclarer / corriger / retirer) + la liste paginée et scopée à l'année ·
`PromotionPauseCalendarGuard` (le partage d'`AcademicYearCalendarGuard`) · migration `PromotionPauses`
(table neuve, deux FK **RESTRICT**, aucun backfill).

⚠ **Deux pièges que la mesure impose**, tous deux écrits dans le code : le coût d'une fenêtre se mesure
sur un calendrier qui **ne la contient pas** (sinon toute fenêtre coûte 0 — l'aperçu d'une correction
l'annonçait tranquillement), et `MissingReligious` ne compte que les fermetures **facultaires**, sans
quoi une suspension nommée « Aïd al-Fitr » éteindrait l'avertissement qui signale la date manquante.

⚠ **Pas d'événement de domaine sur le retrait**, et c'est dit dans le code : il supprime la racine
d'agrégat, EF détache une entité supprimée avant qu'`ApplicationDbContext` ne relève les événements —
un événement levé là serait perdu sans trace. Le registre porte `PROMOTION_PAUSE_REVOKED`.

**1 695 tests verts** (+46) : 15 de domaine pur, 19 de handler, 12 par le vrai pipeline HTTP, 2 cas de
traduction SQL (le créneau atteint par `Stage.LevelId` et la période atteinte par
`InternshipAssignment.Registration` sont des jointures que la base n'avait jamais eu à faire). Les deux
gardes neuves ont été cassées puis restaurées pour prouver qu'elles mordent.

### Côté écran

`PromotionPausesPanel` sur la page Calendrier — c'est là qu'il va, parce que c'est ce que c'est : un
second calendrier, plus étroit. Déclaration, aperçu, correction, retrait, et un rapport d'impact
**borné par construction** (des lignes par créneau et par stage ; cohortes, rotations et étudiants
**comptés**, jamais listés — la forme des 4 725 étudiants, §1b).

⚠ **Et le changement sans lequel toute la phase est invisible** : `RotationCyclePage` envoie désormais
`levelId` au générateur d'axe, et le bouton « Générer les fenêtres » est **désactivé tant qu'aucune
promotion n'est choisie**. Les colonnes sont posées sur *son* calendrier ; sans le niveau elles sont
posées sur celui de la faculté et tombent en plein sur une semaine d'examens, sans que rien ne le dise.
`GeneratedAxisColumn.Pauses` est rendu à part de `Holidays` : un jour férié est celui de tout le monde.

## Session 48 — 2026-09-06 · Une promotion entière peut faire son stage hors faculté, et délocaliser libère enfin la place

**Demandé par l'utilisateur, hors file d'attente**, et précédé d'une discussion qu'il a explicitement
réclamée avant toute implémentation. La demande : un formulaire propose aux étudiants d'aller à
**Kénitra** vu la saturation ; créer un service « KENITRA », y affecter les étudiants stage par stage,
et saisir à la main les validations rapportées sur papier.

### Ce que la discussion a tranché

La demande était **déjà la bonne** à un détail près : la délocalisation existait et son handler
**exige** un service externe au catalogue, qu'il ne contraint délibérément *pas* à la liste des
services autorisés du stage. Ce qui a été écarté, c'est de passer par la **répartition** — un service
sans chef laisse ses rotations `Planned` dans la liste de personne, un plafond inventé fausse la
saturation que l'opération doit soulager, et `IsDelocalized` est ce qui fait dire au dossier « fait
hors faculté ».

⚠ **Une affirmation faite à l'utilisateur pendant la discussion était fausse et a été corrigée dans
le fil** : le canevas d'évaluation **atteint déjà** les délocalisés — il refuse les périodes *non
closes*, et une délocalisation naît close. Aucun second import à construire.

### ⚠ Le défaut que la demande a fait apparaître

**Délocaliser ne libérait pas la place.** `ServiceOccupancyCalculator.EntriesQuery` comptait
`a.Cohort.Assignments.Count` — tous les membres de la cohorte, présents ou non — et une délocalisation
retire les périodes mais laisse l'étudiant dans sa cohorte (c'est ce qui rend l'annulation possible).
Envoyer soixante étudiants à Kénitra soulageait la grille, l'équilibrage de l'arrangeur et la garde de
pré-publication de **rien du tout**. Corrigé dans les **cinq** endroits qui mesurent cette charge.
Morsure vérifiée : remettre l'ancien compte fait tomber le test, seul.

### Livré

`Service.IsExternal` (migration `ExternalServices`, défaut `false` sur 148 lignes) · `Delocalize` ne
refuse plus que sur une **note** · dates facultatives retombant sur la fenêtre du stage, et **refus
nommé** quand le stage n'a aucun créneau · verdict dans les **trois** modes · aperçu + application en
masse (rosters + noms + liste collée, gardés par `ConfirmedCount`) · **annulation**, qui n'existait pas
du tout · `DelocalizedCount` sur la grille · « hors faculté » dans la colonne chef de l'export.

**1 649 tests verts** (+30) : 12 par le vrai pipeline HTTP, 2 cas de traduction SQL, et la morsure du
compte d'occupation prouvée.

### Côté écran

Interrupteur « Service hors faculté » (formulaire, liste, fiche), `BulkDelocalizationModal` (aperçu →
application, le bouton porte le nombre de l'aperçu), la note /20 et **l'annulation à côté de l'acte**
dans la modale d'un étudiant, et « dont N hors CHU » sur la ligne d'un roster de la grille.

⚠ **Deux règles maison ont mordu en écrivant l'écran**, toutes deux dans du code *existant* :
le `catch` de la modale faisait un `notify.error` en plus de celui d'`errorMiddleware` (§1e, double
toast), et l'aperçu renvoyait **une ligne par étudiant dans un objet unique** — la forme exacte des
4 725 étudiants (§1b). Le serveur plafonne désormais à 200 lignes, refus d'abord, compteurs mesurés
avant le plafond.

### Un ajustement demandé après le rechargement

*« Pour tout — délocalisation, transfert, changement de groupe — ne montrez pas tous les groupes,
juste ceux de la promotion concernée. »* Fait, **côté serveur** (`/groups?levelId=`) et non par un
filtre sur la page.

⚠ **Et les trois écrans croyaient déjà le faire.** Ils lisaient la promotion en cherchant le roster
courant **dans la liste d'options** — laquelle demande 200 des 1 003 rosters : au-delà de cette page
la recherche renvoyait `undefined`, la promotion valait `null`, et le garde retombait sur « tous les
groupes de l'année ». `GroupDetailResponse` porte désormais `LevelId` / `LevelLabel`. Même famille que
« ne jamais calculer un total depuis une page » : **un cadrage dérivé d'une page est un cadrage qui
s'élargit en silence**. Règle ajoutée en `PGSH.Frontend/CLAUDE.md` §1b.

⚠ Corollaire : `?levelId=` est volontairement plus large que « les rosters de cette promotion » — il
attrape aussi « Non réparti » (sans niveau) pour que le bucket reste trouvable là où la scolarité
affecte. Les sélecteurs écartent donc eux-mêmes les rosters sans niveau.

### La relecture de fin de session

Quatre corrections de forme, sans changement de comportement, suite verte à chaque étape :
`DelocalizationTargetResolver` sorti du planificateur (« quels étudiants&nbsp;? » et « que leur
arrive-t-il&nbsp;? » échouent pour des raisons sans rapport), l'écrivain de verdict sorti du fichier de
son DTO, l'annulation dans son propre fichier d'endpoint, et les erreurs passées par
`RegistrationErrors` / `ServiceErrors` plutôt que construites à la main.

⚠ **Une seule a changé un résultat, en mieux** : `PreflightDelocalization` restait une **cinquième**
écriture à la main du triplet de flags d'une période. Elle passe par
`ServicePeriodLifecycle.IsPlanned` — l'autorité du domaine — ce qui attrape au passage le cas que la
version écrite à la main manquait : une période *close sans avoir jamais démarré*, état que le store
peut porter, n'est pas « planifiée ».

### Ce qui reste

- **Rien n'a été piloté au navigateur** — `SMOKE-TEST.md` §49.
- **Aucun service externe dans la base**, donc la phase est aujourd'hui invisible : §49 commence par
  créer « Stage hors CHU — Kénitra ».
- **La validation par objectif ne se saisit pas dans la modale** (les objectifs sont indexés par
  période, laquelle n'existe pas encore au moment de l'enregistrement) — elle se saisit après coup
  depuis le dossier de l'étudiant, par le chemin normal.
- Les cellules ne sont **pas** effacées quand un roster part : c'est ce qui rend l'acte réversible.

---

## Session 47 — 2026-09-06 · CLAUDE.md ne rentrait plus, et deux archives se faisaient passer pour des documents vivants

**Demandé par l'utilisateur, hors file d'attente.** *« CLAUDE.md est au-dessus de la limite de
150,0k caractères (241,1k) ; je voudrais corriger ça et nettoyer les scories des fichiers .md, à
condition que tout reste propre. »*

**Aucun code touché.** Documentation seule, aucune migration, aucun test.

### Ce que la mesure a donné avant d'écrire quoi que ce soit

`CLAUDE.md` faisait **252 108 caractères**, dont **232 000 sous une seule rubrique**
(« Application Layer Conventions »). ⚠ **Le recoupement mot pour mot avec les six autres documents
est de 12 lignes sur 2 466** — la prose n'était donc pas dupliquée : il n'y avait rien à dédupliquer,
seulement à **déplacer**. C'est ce qui a écarté d'emblée l'idée de « résumer » : un résumé aurait
détruit exactement ce que ces pages valent.

### Le découpage

Le noyau reste dans `CLAUDE.md` — architecture, contrat de test, patterns handler/endpoint, et les
pièges qui mordent sur **n'importe quel** handler. Le reste part dans `docs/`, **au mot près**, avec
une table d'aiguillage qui donne pour chaque domaine la phrase qui doit donner envie de l'ouvrir.

| | avant | après |
|---|---|---|
| `CLAUDE.md` | 252 108 | **39 410** (limite 150 000) |
| `docs/` (10 fichiers) | — | 229 000 |

⚠ **La contrepartie est réelle et il faut la nommer** : ce qui sort de `CLAUDE.md` n'est plus chargé
d'office. C'est pourquoi la table d'aiguillage dit *pourquoi* ouvrir chaque fichier plutôt que ce
qu'il contient, et pourquoi les règles transversales — année omise, `Any` unique, pagination,
sous-requête dans une projection, `AsNoTracking`, `Include` oublié — restent **dans** `CLAUDE.md`.

### Trois défauts de structure trouvés en chemin, et ce sont eux le vrai nettoyage

1. ⚠ **La liste « Shared helpers — always use these » était coupée en deux.** Les huit puces qui
   comptent — `LocalizationMapper`, `ServiceChefDirectory`, `StageScoring`, `ServicePeriodLifecycle`,
   `ExecutionAuthorizer`, `SlotOverlapGuard`, le pic d'occupation, la capacité — vivaient **2 000
   lignes plus bas**, échouées à la queue de « Managing the year itself », sous un titre qui ne les
   annonçait pas. La rubrique dont tout le propos est « ne réécris pas ça à la main » était donc la
   moins trouvable du fichier. Rapatriées sous leur propre titre.
2. ⚠ **Les règles de portée par année étaient au même endroit, pour la même raison** — « un `Any` et
   non deux », le `RegistrationStatus` qui s'y joint, le filtre global écarté. Remises sous « The year
   is constitutive », qui est la rubrique qui les énonce.
3. **Deux `§42` dans `SMOKE-TEST.md`.** Le second (le chef de service, 03/09) devient `§42b` ; les
   renvois existants visaient tous « §42 g », donc celui des placements, qui garde son numéro.

### Les deux archives

`HANDOFF.md` tenait **2 200 lignes sans un seul titre** — l'archive des sessions 9 à 44 imbriquée
dans une citation — plus 23 lignes de file d'attente déjà barrées et présentées comme en attente.
`SMOKE-TEST.md` portait 79 sections dont §0–§19, toutes exécutées, sous un en-tête intitulé
« sessions 11 → 32 » alors que le fichier va jusqu'à 48.

| | avant | après | archive |
|---|---|---|---|
| `HANDOFF.md` | 314 403 | **63 362** | `HANDOFF-ARCHIVE.md` |
| `SMOKE-TEST.md` | 298 133 | **222 791** | `SMOKE-TEST-ARCHIVE.md` |

La file ne garde que ses **27 lignes ouvertes**, les sessions sont dans un seul format et du plus
récent au plus ancien, et « Rollback est en bas » est redevenu vrai — il était au milieu.

### Ce qui prouve que rien n'a été perdu

Le découpage est mécanique, pas retapé, et **chaque ligne de contenu des cinq fichiers d'origine est
recherchée dans l'ensemble résultant**. Il en reste 65 introuvables, et ce sont les 65 réécritures
voulues : la phrase remplacée par la table d'aiguillage, l'en-tête de `SMOKE-TEST.md`, cinq titres de
session reformatés, le `§42` renuméroté, et huit renvois repointés. **0 lien markdown cassé** sur les
19 fichiers, **0 bloc de code déséquilibré**, et aucun renvoi vivant ne vise une section archivée
(tous visent §20+).

⚠ **Les renvois inter-documents ont dû être repointés**, sans quoi le découpage fabrique des
« voir plus haut » qui ne désignent plus rien : cinq dans `docs/`, trois dans `NOTES.md`, cinq dans
`PHASES.md`.

**Docs à jour** : `CLAUDE.md`, `HANDOFF.md`, `SMOKE-TEST.md`, `NOTES.md`, `PHASES.md`, plus les dix
`docs/*.md` et les deux archives. Sans objet : `SCHEMA.md` et `PLANNING.md` (aucun renvoi vers une
rubrique déplacée), `PGSH.Frontend/*` (dépôt séparé, et son `CLAUDE.md` est sous la limite).

---

## Session 46 — 2026-09-06 · un plafond que la case à cocher ne franchit pas

**Demandé par l'utilisateur, hors file d'attente.** *« Quand on publie une cohorte on nous propose
d'autoriser le dépassement de capacité des services, mais certains chefs n'aiment pas ça — une petite
case sur le service pour ne pas l'autoriser (autorisé par défaut), et à la publication on ne dépasse
que ceux qui ne l'ont pas refusé. »*

**Livré** : `Service.AllowsOverCapacity` (migration `ServiceOverCapacityPolicy`, une seule
instruction), la troisième règle dans `SchedulePublisher.EnsureIntakeAsync`,
`Schedule.OverCapacityRefusedByService`, `SaturatedCellResponse.Forceable`, et quatre écrans. 12 tests
neufs, **1 608 verts**. `PHASES.md` §24, `SMOKE-TEST.md` §48.

### Pourquoi la demande tombe juste

C'est le même argument qui avait servi le 17/08 pour sortir l'admissibilité du champ de la case :
**233 des 353 cellules planifiées dépassent la capacité (66 %)**, donc « autoriser le dépassement »
se coche par réflexe — *une règle qu'on n'applique que lorsque personne n'a besoin de la contourner
n'est pas appliquée*. Mais la moitié « effectif » **doit** rester franchissable : les 148 services
portent la `Capacity = 20` par défaut de l'import, que personne n'a saisie, et la 4ᵉ MED ne publierait
pas une cellule sans la case. Les deux faits ensemble ne laissent qu'une issue — la décision est **par
service**, prise par la personne que le nombre concerne.

### ⚠ Le raccourci qu'il a fallu défaire

Depuis le 17/08, la case cochée voulait dire *ne construis même pas la table d'occupation*. Sous ce
raccourci **un service ferme est injoignable** : son nombre n'est jamais lu, donc son refus n'existe
pas. La table est maintenant bâtie sur **exactement** les services fermes de l'appel
(`FirmServicesAmong`), donc une publication qui n'en touche aucun — le cas courant, et le seul
aujourd'hui — ne mesure toujours rien. Plusieurs tests publient avec `allowOverCapacity: true` pour
cette seule raison, et ce sont eux qui tombent quand on rétablit le raccourci (vérifié : 3 échecs).

### Ce que le drapeau ne fait pas

- **Il lie la publication, pas la planification.** `RotationArranger` remplira un service ferme
  au-delà de son nombre ; le plan est un brouillon, et refuser de le dessiner ne laisserait nulle part
  où voir le problème. Il apparaît alors dans le rapport de saturation, marqué « non forçable ».
- **Il ne touche rien de publié**, et le formulaire le dit.
- **Il n'est pas porté sur « Charge des services »** — décision, pas oubli : cette page et son
  document imprimable devraient s'accorder, et les quatre endroits d'où une publication se décide sont
  couverts.

### ⚠ Piloté le jour même, et le clic a trouvé ce que rien d'autre ne trouve

Essai sur *Cardiologie B* (Maternité Souissi) — pic de **118** venant de trois promotions contre une
capacité de 20 — rendu ferme puis remis. Le refus est arrivé mot pour mot avec la case **cochée**,
**0 période écrite**, la grille l'annonçait avant le clic, et le contrôle (drapeau remis → mêmes 18
saturations, badge disparu) montre que le marqueur suit le drapeau et non les nombres.

**Deux défauts, tous deux corrigés.** ① **Le mien** :
`onChange={(e) => setForm((p) => ({ …e.currentTarget.checked }))}` — React remet `currentTarget` à
`null` une fois l'événement propagé, l'updater fonctionnel s'exécute au rendu **suivant**, donc il lit
`null` et la fenêtre tombe dans l'ErrorBoundary. Invisible au type-check (la propriété est typée
non-nullable), au lint et aux tests. ② **Le même motif ailleurs, préexistant, et il cassait une
page** : cinq autres occurrences, dont « Date confirmée » de `HolidaysPage` — vérifiée en cliquant,
elle faisait tomber « Ajouter un jour férié », c'est-à-dire que **saisir un jour férié était
impossible**, sur la page où les fêtes lunaires *ne peuvent qu'être* saisies à la main. Règle écrite
en `PGSH.Frontend/CLAUDE.md` §1l ; le bon repère existait déjà dans le dépôt (`EvaluationModal`).

### Deux détails qui sont la moitié du travail

⚠ **`true` par défaut à *chaque* couche** — l'entité, les deux commandes (paramètre optionnel final),
et le `Request` de `PUT services/{id}` où le champ est **nullable** pour qu'une omission se lise « le
client n'en dit rien » plutôt que de se lier à `false`. La colonne arrive sur 148 services dont aucun
chef n'a été consulté ; `false` aurait refusé la prochaine publication de toutes les promotions sur
une restriction que personne n'a écrite.

⚠ **`Forceable` est envoyé, jamais recalculé côté client.** Les chiffres d'un service ferme et d'un
service permissif sont identiques — seul le service sait lequel est lequel — donc le dériver de
`Reason` serait une seconde règle sur l'autre rive du réseau, ce que `ServicePeriodResponse.State`
interdit déjà. C'est ce qui permet au dialogue de publication de **nommer** les services par lesquels
il va être refusé, avant le clic.

---

---

## Session 45 — 2026-09-06 · un déplacement qui affirme « il a toujours été là »

**Demandé par l'utilisateur, hors file d'attente.** Il existait le transfert (temporaire / définitif)
et la délocalisation, tous deux tracés dans l'historique. Ce qui manquait est le cas le plus banal :
*la répartition s'est trompée de groupe*, et il faut que le dossier dise ce qui est vrai — qu'il est
dans ce groupe-là et qu'il y a toujours été. « Un changement complet, en silence, sans historique, en
repointant toutes les FK vers le nouveau groupe », plus « échanger deux étudiants ».

**Livré** : `AcademicGroups/GroupChange/` — `ChangeStudentGroupCommand`, `SwapStudentGroupsCommand`,
`StudentGroupRelocator` partagé par les deux (un échange **est** deux changements), les deux routes,
les deux fenêtres sur la fiche de groupe. `PHASES.md` §23, `SMOKE-TEST.md` §47. **Aucune migration** :
la phase ne crée ni table ni colonne, elle réécrit des lignes existantes.

### La décision qui gouverne tout le reste

⚠ **« Sans historique » nomme le dossier, jamais le registre**, et les deux mots n'ont pas le même
lecteur. Le *dossier* est le récit de l'étudiant et ne doit rien montrer — aucun événement de domaine,
donc aucune ligne `HistoryType.GroupTransfer`, et la `CohortMembership` ouverte réécrite sur place au
lieu d'être close et remplacée. Le *journal des actions* est la trace des actes d'administration :
l'acte étant **irréversible** — le groupe d'origine n'est plus écrit nulle part après coup — son
entrée `STUDENT_GROUP_CHANGED` est le **seul** endroit où ce groupe survit. Le supprimer là aussi
rendrait invisible un acte destructeur, ce qui est exactement ce que le registre existe pour empêcher.

C'est un choix d'interprétation sur la formulation de la demande, et il est signalé comme tel plutôt
que fait en silence.

### Ce qu'il faut savoir avant d'y toucher

- ⚠ **Pas de `Force`, et le refus désigne le transfert.** Une correction cesse d'être vraie dès qu'une
  rotation a commencé ; à ce moment-là l'acte juste est celui qui garde la trace, précisément parce
  qu'il y a quelque chose à tracer. Même règle que `RosterAffectationsUnderway`.
- ⚠ **Les `CohortMembership` déjà closes restent.** Ne pas écrire sa propre trace n'autorise pas à
  effacer celle d'un transfert qui a réellement eu lieu.
- ⚠ **La garde de l'agrégat est `Status` et rien d'autre.** Période démarrée, note et présence pendent
  de collections qu'un chargement sans `Include` rapporte **vides** ; le store est interrogé par le
  handler (`AffectationTollReader.ForRegistrationInRosterAsync`, une portée de plus sur le lecteur
  existant) et l'agrégat décide. Division de `CnpnSpanFloor`.
- ⚠ **Les affectations manquantes sont créées dans le relocateur, pas par
  `StudentAffectationService.AssignRegistrationAsync`** : celle-ci demande au store où l'étudiant est
  déjà, et les affectations qui viennent d'être repointées ne sont **pas sauvegardées** — elle en
  créerait une seconde par stage.

### Deux choses trouvées en chemin

- **`CohortStayFolder`** — le pli des cellules d'une cohorte en séjours était **privé dans
  `SchedulePublisher`**. Écrit une seconde fois, il aurait divergé sur `SingleService` : l'étudiant
  déplacé aurait tenu *kₛ* périodes là où ses camarades en tiennent une. Sorti dans le domaine, pur ;
  `SchedulePublisher` le lit désormais.
- ⚠ **Défaut latent corrigé : `MidStageTransferRescheduler` n'écrivait aucune ligne
  `ServicePeriodSlotCoverage`** sur ses trois créations de période. La cellule d'un étudiant transféré
  se lisait donc **libre** pour `PublishedCells` — réécrite par le prochain auto-arrangement, et
  `DeleteStageSlot` laissait la colonne partir sous ses pieds. `SchedulePublisher` et
  `LateArrivalScheduler` l'ont toujours écrite.

### Mesures du jour (base réelle, lecture seule)

- **2026-2027 : 12 340 périodes, 0 démarrée, 0 évaluation, 0 journée de présence.** La correction est
  donc disponible exactement là où on en a besoin. **2025-2026 : 17 752 périodes, toutes démarrées et
  closes** — l'acte y est refusé, ce qui est le comportement voulu.
- **Tous les rosters d'une promotion portent le même nombre de cohortes** (7/7 en 5ᵉ MED, 6/6 en 3ᵉ
  MED, 5/5 en 4ᵉ MED, 2/2 en 5ᵉ Pharmacie), donc `TargetRosterMissingStage` ne mord pas en pratique.

### Couverture

**1 565 → 1 596 verts.** 7 sur `CohortStayFolderTests`, 17 sur `StudentGroupChangeTests`, 7 dans
`Integration/GroupChangeEndpointTests.cs`, plus les cas de traduction SQL. Morsure vérifiée **trois
fois, une cassure à la fois**, chacune faisant tomber **exactement un** test : remplacer
`ReassignToGroup` par `TransferToGroup` → le test de silence ; ne plus plier les runs `SingleService`
→ le test du pli ; matérialiser les cellules non publiées → le test de la cohorte non publiée.

⚠ **Le contrôle du silence est un test à part**
(`A_transfer_of_the_same_student_does_raise_the_event_that_writes_history`) : « aucun événement » ne
vaut rien si la fixture ne peut pas en produire un.

### Non piloté

La session ouverte au navigateur était une session **étudiant** — l'espace admin répond 403 comme il
doit — et la base est celle de la faculté, donc l'acte n'a **pas** été exécuté pour vérifier.
`SMOKE-TEST.md` §47 est la marche à suivre ; prendre un point de sauvegarde avant le premier essai.

---

## Session 44b — la liste des services et le portail étudiant nommaient encore la clé étrangère.

Backend suite **1 565 green** (1 562 → 1 565). **Aucune migration.**

**La dette explicite de la session 42.** La fiche du service avait été corrigée ; les deux autres
écrans qui nomment un chef ne l'avaient pas été. Tous deux lisaient `Service.ServiceChefId` — le
*rattachement*, **null sur les 148 services** — donc la liste affichait « — » partout et le portail
« aucun chef de service désigné », pendant que la fiche, la répartition et l'export nommaient
quelqu'un pour **140** d'entre eux. Un étudiant lisait « aucun chef » sur la page d'un service dont
sa propre répartition imprime le chef.

⚠ **Le portail n'a demandé aucun changement serveur.** Il appelle `/services/{id}` — *la même
route* que la fiche admin, qui porte `chefAttribution` depuis la session 42. La bonne réponse était
déjà dans la charge utile et l'écran ne la lisait pas : la forme la plus discrète de ce défaut, et
celle qu'aucun test serveur ne peut voir.

**La liste résout sur les ids de la page**, jamais sur la requête filtrée — un annuaire bâti pour
tous les services correspondants grandit avec le catalogue pour des lignes que personne ne regarde.
Même règle que la grille lisant ses cellules publiées sur les ids qu'elle vient de renvoyer.

⚠ **`ServiceChefAttributionResponse` a déménagé** dans `Application/Hospitals/Chefs/`, hors du
dossier d'une requête, avec `From(annuaire, serviceId, asOf)`. Trois écrans l'impriment ; et la
fabrique tient les **deux** appels — `For` et `HasWithheldLinkedChef` — sur une seule date
d'observation. Séparés, l'un des appelants finit par oublier le second, et un écran incapable de
dire « quelqu'un est rattaché, et ce n'est pas ce nom-là » est celui qui a produit la confusion
d'origine.

⚠ **Trois façons de ne nommer personne, trois phrases** : « Information non disponible » (champ
absent d'une API antérieure — *inconnu* n'est pas *vide*), « Non communiqué » (quelqu'un est
rattaché, rien n'est imprimé) et « Aucun chef de service désigné ». Dire la troisième dans le
deuxième cas est faux.

⚠ **La carte de l'étudiant n'habille pas une note en fiche de personnel** : pas de « Dr. », pas
d'initiales d'avatar, pas de grade pour un nom venu de la note — il n'y a aucun `Employee` derrière.

**La morsure est l'équivalence, pas la valeur.** `The_services_list_names_exactly_what_the_fiche_names`
compare la ligne de liste à la réponse de la fiche pour le **même** service : elle tombe le jour où
l'un des deux se remet à classer les sources. Vérifié en cassant — la liste remise à nommer la FK
fait tomber **3** tests.

**Docs à jour** : `CLAUDE.md`, `NOTES.md`, `PHASES.md` §22, `SMOKE-TEST.md` §46,
`PGSH.Frontend/API.md`. Sans objet : `SCHEMA.md` (aucune table, colonne ou index — on lit ce qui
existe déjà), `PLANNING.md`, `PGSH.Frontend/CLAUDE.md` et `PGSH.Frontend/PHASES.md` (aucun motif
client nouveau : « la classification vient du serveur » y est déjà écrit, et ces deux écrans ne
font que l'appliquer enfin).

---

## Session 44 — une grille vide avait trois causes, et la publication était lue à la mauvaise granularité.

Backend suite **1 562 green** (1 555 → 1 562). **Aucune migration**, **aucune écriture** : les deux
changements sont dans une lecture.

**Le symptôme, rapporté par l'utilisateur.** « Il y a des périodes mais elles n'apparaissent pas
dans la grille de planning ». Mesuré le 04/09 : de 2017-2018 à 2025-2026 la base tient **105 626
périodes pour 0 créneau et 0 cellule** — l'import Access portait les rotations *servies*, la base
source n'ayant aucune grille à porter. **Rien n'est abîmé** (0 période pointe vers une cellule
disparue) ; ce qui manquait était la **phrase**. « Rien n'est planifié » et « cette année n'a jamais
été planifiée ici » appellent des gestes opposés, et lire le second comme le premier invite à poser
un axe sur une année terminée.

**Ce qui a été construit.** `StageScheduleNotes` (pur, à côté d'`ExportNotes`) ·
`StageScheduleSummary.DeclaredSlotCount` / `.ServedPeriodCount` / `.EmptyGridNote` ·
`SlotCellResponse.IsPublished` · `ServedPeriodsQuery` (nommée, donc compilable par
`SqlTranslationTests`) · l'alerte et le marqueur côté écran.

⚠ **Trois causes, pas deux** — et une quatrième séparée à l'intérieur de la dernière : un axe posé
sur une promotion **sans cohorte** appelle « découpez, provisionnez », pas « répartissez ». C'est
exactement la leçon du panneau de faisabilité de la session précédente, où une promotion sans aucun
groupe recevait le message du second geste.

⚠ **`ServedPeriodCount` vaut `null`, jamais 0**, quand la question n'a pas été posée — toute grille
qui a un axe. « Aucune période » est une réponse, « on n'a pas regardé » n'en est pas une. Chaque
compte n'est lu que s'il doit être imprimé, donc la requête ordinaire ne paie rien pour la note.

⚠ **La note parle du stage et de l'année, jamais de la sélection filtrée.** Sous un filtre de
partition, le vide à l'écran est le fait du filtre ; y dire « aucune cohorte » envoie défaire un
découpage correct. Lue depuis `PartitionSlotUseQuery`, déjà non filtrée par construction.

⚠ **L'année est lue sur l'inscription** dans `ServedPeriodsQuery`, jamais déduite des dates de la
période : les deux règles divergent sur 7 030 des 105 626 périodes et l'inscription a raison à
chaque fois.

⚠ **La publication par cellule ne peut pas se lire sur la FK.** `ServicePeriod.CohortSlotAssignmentId`
ne nomme que la **première** cellule d'un pli `SingleService` : mesuré sur *Gynécologie Obstétrique*
2026-2027, **363 cellules, 121 nommées par la clé, 363 couvertes** — un marqueur bâti dessus lirait
**242 cellules publiées comme libres**. Le drapeau de *ligne* reste inchangé et reste juste : une
période suffit à rendre vraie une affirmation strictement plus faible.

⚠ **Et c'est un marqueur, pas une garde nouvelle.** L'édition reste refusée **par ligne**, parce que
`SetCohortSlotAssignmentCommandHandler` refuse sur « la cohorte tient une période liée à la
grille » : la desserrer par cellule échangerait un bouton grisé contre un toast rouge. Ce que le
drapeau ajoute vraiment est le refus *en amont* du **retrait** d'une cellule publiée, que
`ClearCohortSlotAssignmentCommandHandler` refusait déjà sans que rien ne le dise avant le clic.

**La morsure, dans les deux sens, une cassure = un test** : le drapeau relu depuis la FK fait tomber
le cas de la cellule de queue et lui seul ; la note lue sur `pairs` (la sélection filtrée) fait
tomber le cas du filtre et lui seul.

**Piloté le 05/09/2026, et le chiffre est celui qui était prédit.** Sur *Gynécologie Obstétrique*
2026-2027, page 1 : **75 cellules, 75 couvertes, 25 nommées par la FK** — l'écran marque les 75, un
marqueur bâti sur la clé en aurait montré 25 et **laissé 50 cellules publiées passer pour libres**.
Page 5 : 63/63 marquées (4 × 75 + 63 = 363, le compte de couverture). Contrôle négatif sur
*Pédiatrie* 4ᵉ MED, répartie et non publiée : 50 cellules, **0** marquée, 50 croix actives. Et sur
CHIRURGIE 2024-2025 la phrase nomme **627 périodes**, le compte exact.

⚠ **Trois des neuf lignes de §45 n'ont pas pu être pilotées parce que l'état n'existe pas** :
aucun (stage, année) n'a des cohortes sans créneau *et* sans période servie ; aucune partition de
2026-2027 n'a de cohortes sans cellules ; et **le bouton « Grille de planning » n'est rendu que si
le stage a des cohortes cette année-là**, donc « un axe sur une promotion sans cohorte » est
inatteignable depuis cet écran — la fiche y répond déjà par « Aucune cohorte pour ce stage ».

**Docs à jour** : `CLAUDE.md`, `NOTES.md`, `PHASES.md` §21, `SMOKE-TEST.md` §45,
`PGSH.Frontend/API.md`. Sans objet : `SCHEMA.md` (aucune table, aucune colonne, aucun index —
`ServicePeriodSlotCoverage` existait déjà et c'est elle qu'on lit), `PLANNING.md` (l'arithmétique du
croisement est intacte), `PGSH.Frontend/CLAUDE.md` et `PGSH.Frontend/PHASES.md` (aucun motif client
nouveau : la règle « la classification vient du serveur, on ne la redérive pas » y est déjà écrite,
et cet écran ne fait que l'appliquer).

---

## Session 43 — l'ordre des services était celui de l'import Access, et il décidait la répartition.

Backend suite **1 543 green** (1 481 → 1 543). **Une migration**, `StageAllowedServiceRank`, non
appliquée : le stack tourne.

**La demande.** Pouvoir choisir, depuis le frontend, quel service est le 1ᵉʳ, le 2ᵉ… — parce que
les demandes nominatives (« ces étudiants au HMIMV pour ce stage ») ne se réglaient qu'en
retouchant une cellule sur la grille, **et que la répartition annuelle imprimée montre cette
retouche** : `GroupNumberRanges` refuse de fusionner par-dessus le trou laissé, donc « 21-27 »
devient « 21-23, 25-27 » face à un « 24 » isolé, sur une page de plages nettes.

**Ce que la lecture du code a confirmé, et qui est la raison d'être de la fonctionnalité.**
`RotationArranger` parcourait `OrderBy(Service.Id)` — l'ordre de création au catalogue, donc
l'ordre de l'import Access. `BuildServiceQueue` émet le bloc de chaque service **d'un seul tenant**
et la première colonne de l'empreinte prend la phase 0, donc `offset = 0` : **les premiers groupes
de la première période tombaient dans le service au plus petit id catalogue.** Personne n'avait
choisi cet ordre, et rien à l'écran ne le disait — la fiche du stage affichait en plus un
**quatrième** ordre, alphabétique par hôpital.

**Ce qui a été construit.** `StageAllowedService.Rank` (jointure à charge, derrière la même skip
navigation, donc les dix lectures existantes de `Stage.AllowedServices` sont intactes) ·
`ServiceRotationOrder` **pur** · `ServiceRankWriter` · `PUT stages/{id}/allowed-services/order`
(`IAuditableCommand`, `STAGE_SERVICE_ORDER_SET`) · la liste renvoyée **en ordre de rotation** avec
la position · les flèches ↑↓ et la phrase qui dit ce que la position décide.

⚠ **L'index unique `(StageId, Rank)` n'est pas différé**, donc un échange 1↔2 s'écrit en **deux
instructions** — les lignes sont garées sur leurs rangs négatifs d'abord. Même forme que la
rétrogradation-avant-promotion de `SetCurrentAcademicYear`. ⚠ **La suite in-memory ne peut pas voir
ce défaut** : elle n'applique aucun index ; ce qui est épinglé est l'état final.

⚠ **Deux défauts de cette session-ci, trouvés en relisant et corrigés** — la première version
écrivait les deux `SaveChanges` **hors transaction**, donc une connexion coupée entre les deux
laissait *tous* les rangs négatifs, c'est-à-dire « personne n'a choisi », c'est-à-dire un retour
silencieux à l'ordre des id sur un stage qu'on venait de classer à la main. Et
`ExecuteAtomicallyAsync` ouvre chaque tentative par `ChangeTracker.Clear()`, donc les entités
chargées **avant** le bloc auraient été détachées et `SaveChanges` n'aurait rien écrit — le défaut
de `CnpnTargetPlanner` à l'identique. Le writer charge désormais ses lignes dans sa propre
transaction, et re-vérifie la permutation à l'intérieur (`Stages.ServiceOrderIsStale`) plutôt que
de laisser une autorisation concurrente ressortir en 500 nommant un index.

⚠ **`0` trie en dernier, jamais en premier.** La colonne vaut 0 par défaut, donc une ligne écrite
par un script correctif serait passée **devant** tous les services placés à la main et aurait reçu
la première plage de groupes — l'exact contraire de ce que « personne n'a choisi » veut dire.

⚠ **Une liste partielle est refusée, jamais complétée**, et la refus nomme laquelle des trois
causes s'applique (manquant / inconnu / doublon) : elles appellent des gestes différents, et la
cause la plus probable d'une liste courte est une page ouverte avant qu'un autre n'autorise un
service.

⚠ **Ce que le levier ne fait pas, et c'est écrit à l'écran comme dans les docs** : il déplace la
**promotion entière**, jamais un groupe ; sa granularité est le **bloc** (largeur = capacité propre
du service), donc on choisit quel service couvre une position, pas quel numéro de groupe exact ;
deux demandes contradictoires sur un même stage restent insatisfaisables. L'épingle nominative
reste `PHASES.md` §19.2.

**Docs à jour** : `CLAUDE.md`, `NOTES.md`, `PHASES.md` §19.3, `PLANNING.md` §11 ③, `SCHEMA.md`,
`SMOKE-TEST.md` §44, `PGSH.Frontend/API.md`. Sans objet : `PGSH.Frontend/CLAUDE.md`,
`ARCHITECTURE.md`, `DESIGN.md`, `PGSH.Frontend/PHASES.md` — aucun motif nouveau côté client, la
carte existait déjà et n'a gagné que des flèches.

---

## Session 42 — deux chefs de service enregistrés, et ce sont des comptes de test.

Backend suite **1 481 green**. **No migration** — the change is one constant and the code that
reads it.

**The ask.** On the Excel export reached from the Répartition annuelle page, the chef de service is
to be taken from the **note d'import (`Service.Description`) alone**, and never from an affectation:
the base holds **2** `ServiceChefAssignment` rows and both were linked to try the mechanism out. A
document resolving them prints a test account's name beside real students.

**What was built.** `ServiceChefSourcePolicy` (`Authority` | `SourceNoteOnly`) with
`ServiceChefPolicy.InForce` naming which is in force — `SourceNoteOnly` as of today.
`ServiceChefDirectory` skips the tenure and the sitting chef under it. ⚠ **The provider still loads
the trail** — it briefly did not, and that made `HasWithheldLinkedChef` answer `false` on exactly
the two services that need `true`: the policy narrows what a document may *name*, never what the
directory *knows*.

⚠ **Applied to the répartition document too, not to the export alone.** The whole reason the
resolution order was extracted from `GetLevelRepartitionQueryHandler` is that two pages of one
faculty must not name different people for one service. Narrowing one of them rebuilds exactly that
drift — so the constant is read by both, and flipping it back is one line.

⚠ **What the policy costs is on the page, not left to be discovered.** A service whose only chef is
a link now prints **no name at all** — deliberate: a blank says less wrongly than the wrong name —
and `ExportNotes.ChefSourceNote` states that under the caption of the two sheets that print a chef
(never on Synthèse, which has no such column). Silent under `Authority`: a note that fires whatever
the policy says is noise, and noise is dismissed.

⚠ **« Origine du chef » now reads « Note (import) » on every row, and that is the honest column.**
Narrowing the sources is not a licence to stop saying the name is undated. On the répartition the
`chefIsFromSourceNote` flag is only a native `title` tooltip, so nothing on the printed document
becomes visibly noisier — **no frontend change was needed**.

**Tests.** The authority order stays covered where it lives — `ServiceChefDirectoryTests`, now
policy-parameterised, plus three cases for the narrowed one — while the two handler suites assert
the narrowing (`LevelRepartitionTests`, `ExportTests`). ⚠ **Checked by flipping the constant to
`Authority`: 5 handler tests fail, 0 directory tests do**, which is what makes the flip back a
one-line change rather than a rediscovery.

**⚠ Found while answering « pourquoi Alaoui apparaissait en Pédiatrie 1 et 2 ? ».** Measured on the
live base: those are the **two** `ServiceChefAssignment` rows — both **Youssef Alaoui**, open since
29/08/2026 — and `Services.ServiceChefId` is **NULL on all 148 services**. The document resolved the
tenure (« Affectation »); the service page reads the *sitting* FK, then the note, and shows the open
tenure under « **Historique** » — so it headlined Pédiatrie1 as « Pr.N.Elhafidi » and Pédiatrie2 as
« Pr.A.Mdaghri Alaoui » (a **different** Alaoui, which is what made it look half-right). Both were
faithful to their own reading of the order; nothing on either screen said the other existed. This
session's change makes them agree by accident — item **0af** is the actual defect.

**Then the second half, once the user reloaded and asked the right question** (« pourquoi Alaoui
apparaissait en Pédiatrie 1 et 2 ? ») — item **0af**, fixed in the same session.
`ServiceDetailResponse.ChefAttribution` carries the resolved name, its origin, **and**
`LinkedChefWithheld`, and `ServiceDetailPage` prints all three instead of ranking the sources
itself. ⚠ Without that third flag the narrowed policy only *moves* the confusion: an « en cours »
tenure under a headline naming somebody else, beside « Désignez un chef de service » — advice the
service had already satisfied. Backend suite **1 492 green**; `tsc --noEmit` and `eslint` clean.

**⚠ Two defects were in the first cut of that fix and are worth remembering, because both are the
same mistake the fix is about.** ① The page stopped referencing `serviceChef` at all, so a chef
linked through the **FK** rather than through a dated tenure was nowhere on it, while the alert
said « voir l'historique » — a section listing tenures only. The link now prints on its own
« rattaché » line whatever the attribution says. ② The client fallback for an API predating
`chefAttribution` filled it in from `chefFromSourceNote` — a second resolution order on the client.
Absent now means *unknown* and says so. The constant was also renamed `ForDocuments` → **`InForce`**:
a screen reads it too, and a name that misdescribes its callers is how the next reader gets it wrong.

**Still waiting, and it is the reason this is temporary:** link the real professors in Personnel,
then set `InForce` to `Authority`. Nothing else changes — the notes and both documents follow
the constant. `PHASES.md` §15.14.

---

## Session 41 — une demande nominative se résout par un groupe, et rien ne permettait de le voir.

Backend suite **1 474 green** (+67). **No migration** — the whole change is two reads.

**The question.** Three real requests, brought by the user: « Sbai fait tous ses stages à l'hôpital
militaire », « Aya et Rihab ensemble, stage A en S1 et stage B en S2 », and des fratries dans le même
service. Asked whether the system could express them, and whether a *transfert définitif* was better
than cutting a groupe.

**Measured before answering, on the live base.** The faculty already solves this by grouping: in
2024-2025, 6ᵉ année Médecine held **five rosters of 6-7 students entirely at the HMIMV** (groupes 102,
116, 130, 144, 158). They were **normal-sized**, not tiny. HMIMV is the largest hospital in the base
(**35 services**) and covers **every** 6ᵉ année stage — and six of the seven 5ᵉ année ones, because
**Santé Publique authorises a single service and it is elsewhere**.

**What was answered.** A définitive transfer does not send a student to a *service*, it sends him to a
*roster* — so « groupe ou transfert » is not a choice: the transfer is how somebody gets into a group.
The user's instinct was right for a reason that is in the code: `RotationArranger.BuildServiceQueue`
weights a service by how many *whole average-sized* cohorts it holds and a cohort is atomic, so a
two-student roster spends a full cohort's worth of a service's intake on two people — silently.
**The cheapest answer is always « transfer him into a roster that already goes there », and that
answer was unreachable**: nothing could be asked « quel groupe est au HMIMV ? ».

**Built.** `GET groups/placements` (paged, per promotion, filterable by service or hospital, with
`match=Anywhere|Exclusively`) and `GET hospitals/{id}/stage-coverage` (can this hospital host this
promotion's whole rotation, and which stages it cannot). Two pure domain classifiers,
`RosterHospitalPlacement` and `StageHospitalCoverage`, each existing because a blank means two things:
a roster nobody arranged satisfies « toutes ses cellules au HMIMV » **vacuously**, and a stage with an
empty allowed-services list is *open to everything*, not closed to this hospital.

⚠ **Proved the guard bites**: removing the « au moins une cellule » half of `Exclusively` fails **five**
tests, including the endpoint one — without it, on a base holding 0 cells, every roster in the faculty
is returned as an exact match for « tout au militaire ».

⚠ **A blind spot found, and it is the inverse of the usual one.** `SelectMany` over a **skip
navigation** (`Stage.AllowedServices`) throws `NotImplementedException` on the *in-memory* provider
while Npgsql translates it fine — the mirror of the translation trap this suite was built against. The
coverage read uses `Include` + an in-memory filter for the **names**, and keeps the **verdict** on
`StagesQuery`'s SQL aggregates: a forgotten `Include` then degrades to « couvert, mais aucun service
nommé » instead of silently reporting every stage as unauthored.

**The screen** (same session): `/admin/placements` — Académique → Placements. It carries the
cheapest-first order, because that is the question somebody actually arrives with. Two UI rules
worth keeping: the mutually-exclusive Hôpital/Service filters **clear each other** rather than
being disabled — the state the server refuses with a 400 is made unrepresentable — while
« Exclusivement » *is* disabled with its reason, because there it is a missing precondition, not
a contradiction. And the empty result has two colours: orange « rien n'est encore réparti »
(`placedRosters === 0`) against grey « aucun groupe ne correspond ». `tsc --noEmit` and
`npm run lint` clean. ⚠ **Not driven in a browser** — the extension was disconnected and the
stack was down; `SMOKE-TEST.md` §42 g is written and unexecuted.

**Still open, and re-ranked by this session:** the *pin marker* on `CohortSlotAssignment` (a hand-set
cell is deleted by the next auto-arrange in its reach — `RotationArranger.cs` `staleIds` — with no count
and no warning) and a *reason* on `AcademicGroup` (nothing records that Groupe 102 is the military
roster). See `PHASES.md` §19.

---

## Session 40 — 2026-09-03 · un point de sauvegarde que personne n'a besoin de penser à prendre

**Phase 18.1.** La demande : « the app is up and working so the data is super important and we dont
want to mess it up ». Ce qui manquait n'était pas `pg_dump` — il a fonctionné trois fois — mais le
fait qu'un humain devait y penser, le jour même où il écrit une promotion entière.

Suite backend **1 435 verte** (+28), `tsc --noEmit` et `npm run lint` propres. **Aucune migration** —
voir plus bas, c'est une conséquence de la conception et non un raccourci.

### Ce qui est livré

| | |
|---|---|
| `Domain/Backups/` | `BackupManifest`, `SchemaFingerprint`, `DatabaseCensus`, `SafePointEvaluator` — pur, comme `WorkingDayCalendar` et `OccupancyTimeline` |
| `Application/Backups/` | le port `IBackupArchive`, `SafePointTaker`, les 3 commandes et les 3 lectures |
| `Infrastructure/Backups/` | `PgDumpBackupArchive` (`docker exec pg_dump -Fc -f` puis `docker cp`), `ScheduledBackupService`, `EfSchemaFingerprintProvider` |
| API | `GET backups`, `GET backups/safe-point`, `POST backups`, `POST backups/{id}/verify`, `GET backups/{id}/restore-plan`, `DELETE backups/{id}` |
| Frontend | `/admin/sauvegardes` + `SafePointBanner` dans la déliberation, la réinscription par fichier et l'application d'un axe |

### Les trois décisions qui ont porté le reste

**1 · Le registre est le dossier, pas une table.** Un registre gardé *dans* la base serait rembobiné
par la restauration même qu'il décrit : après un retour en arrière, tous les points pris depuis
disparaîtraient de la liste **alors que leurs fichiers sont sur le disque**. Le manifeste JSON à côté
du dump survit à l'acte qu'il documente — et c'est pourquoi ceci se livre sur une base vivante sans
migration.

**2 · « Indisponible » n'est pas « aucune sauvegarde ».** Docker arrêté et dossier vide appellent des
gestes opposés. `SafePointState` en a cinq, et `SchemaChanged` passe **devant** `Stale` : un dump
d'une heure sous une autre migration est une restauration qui *refuse*, un dump de trois jours sous la
bonne migration est une restauration qui *marche et coûte trois jours*. Même famille que « année
omise lue comme toutes les années ».

**3 · Le manifeste vaut plus que le dump.** Un `.dump` seul ne dit pas sous quel schéma il a été
écrit. `SchemaFingerprint` lit *inconnu* comme « ne peut rien certifier », jamais comme « identique ».

### Ce que l'API ne fait pas, volontairement
**Aucun bouton « Restaurer ».** Un processus ne peut pas remplacer la base dont il se sert :
`pg_restore --clean` supprime des objets que l'API tient ouverts, et un endpoint qui prétendrait le
contraire échouerait à mi-course, sur la base vivante. Le plan rend le **coût chiffré** — recensement
du manifeste contre la base actuelle, dans les deux sens — et la commande exacte, la pile arrêtée.
⚠ Il **n'échoue pas** sur un désaccord de schéma : le refus doit pouvoir nommer le
`dotnet ef database update` qui rend le point utilisable.

### La bannière est la fonctionnalité
« Créer un point maintenant » est **dans** la confirmation de l'acte, pas sur une page à côté. ⚠ Elle
ne bloque pas : sans point exploitable l'acte demande une case cochée, comme `ConfirmedDefaultCount`.
Bloquer sèchement voudrait dire que le jour où Docker est en panne, la faculté ne peut plus clôturer.

### Rôles, là où le risque est
*Prendre* un point est `Roles.Administrative` — scolarité applique les actes en masse, un verrou
qu'elle ne passerait pas mettrait le bouton hors de portée de la seule personne qui en a besoin.
*Supprimer* est `SuperUser`, et le **plus récent** est refusé à tout le monde : c'est celui que lisent
les confirmations. Les deux garde-fous ont été **cassés puis restaurés** pour vérifier qu'ils mordent.

### Ce que la suite ne prouve pas
`ApiFactory` remplace l'archive par une fausse et coupe le planificateur — sinon `dotnet test`
prendrait un dump de la base vivante en effet de bord, et le planificateur en prendrait un par heure
tant que l'hôte de test vit. La moitié `docker exec` se vérifie à la main : `SMOKE-TEST.md` §41.

---

---

## Session 38 — 2026-09-02 · the roll is applied whole, and the disagreement is recorded

**The user's correction, on the report session 37 produced from the real file.** The rollover was
right about the rules and wrong about what to do with the answer: it *refused* the rows PGSH's own
record disagreed with, and the faculty's document is the more authoritative of the two.

> « the excel should create registration, even tho some conditions are not fulfilled — why, because
> in most cases they already validated / revalidated everything but we did not add the evaluations
> yet, so we can just flag the students so we can later come back to them. »

**What the refusal cost, measured on the real roll:** 182 of the 651 7ᵉ MED it re-registers were
skipped *before session 37's « entrer » fix* — ⚠ **with that fix in place the live preview holds 60**,
verified in the browser 2026-09-02. 182 is the motivation, 60 is today's count. The refusal — a quarter of the promotion, every one named by the faculty as coming back — and in most of
them the stage was served and only the évaluation is missing.

### Built

- **`RegistrationHold`** (`Domain/Registrations/`, table `RegistrationHolds`). A registration created
  but withdrawn from planning: no roster cut, no cohort affectation, no published période. It keeps
  its status, its verdict and everything already published under it — taking those away is
  `UnpublishCohortScheduleCommand`'s act, which names what it costs and asks twice.
  - Two reasons: `OutstandingPriorStages` (60 live) and `AbsentFromReinscriptionRoll` (**all 1 267**
    absentees, the 1 217 inferred graduations included).
  - Evidence is a **snapshot**, never re-derived on read — same bargain as `FinalYearEntryWaiver`.
  - **Released by hand, with a required note**; the row survives its release so the file can say who
    cleared him. Idempotent per reason, because the roll is re-runnable. No bulk release.
- **`RegistrationHoldPolicy`** — one expression, delegates compiled from it, composed into
  `AutoArrangeGroupsCommandHandler`, `StudentAffectationService` and `CohortProvisioner`. Pinned by
  `SqlTranslationTests`: in a predicate it is an `EXISTS`, which translates.
- **`POST reinscription/sheet/export`** — Synthèse · Lignes · Absents, **uncapped**, written from the
  plan rather than the capped report. Writes nothing, so it is offered before the confirmation and on
  a roll the apply would refuse.
- **Signalements page** (`/admin/signalements`) + `GET registrations/holds` and
  `POST registrations/holds/{id}/release`.

### Decided, and worth re-reading before changing

- ⚠ **The 1 217 graduations are held too**, at the user's explicit instruction — and the reasoning is
  better than the original proposal to hold only the 50 undecidable ones. The graduation is *our
  inference*, read off a blank cell. A partial roll would end the cursus of people still enrolled with
  nothing saying a human had looked; holding costs a real graduate nothing, and it catches what an
  absence most often is — a réinscription that has not arrived.
- **Errors still refuse the whole file** (duplicate code, unknown level code, level mismatch, level
  regression, « Retrait »). Those say the *file* is mistaken, not that our data is behind.
- **The manual registration paths still refuse**, with the waiver as the override. The roll is the
  faculty's own document; a hand-typed form is not, and per-student ceremony does not scale to 182.
- **Holds need no confirmed count**, unlike `WillGraduate`: a hold is released in a click, a
  graduation ends a cursus. Confirm what cannot be undone.

### Renamed — the frontend was updated with it

`ReinscriptionSheetRowStatus.FinalYearBlocked` → `WillRegisterHeld`; the report's `finalYearBlocked`
→ `willRegisterHeld`, plus a new `absenteesHeld`. ⚠ The **other** réinscription path
(`ReinscriptionAction.FinalYearBlocked`, the déliberation-derived rollover) still refuses and keeps
its name — do not "tidy" the two into one.

### Left undone, deliberately

- **The *examens cliniques* are not modelled.** They are real — they open once a student's stages are
  done, he can fail them, and he is re-registered to sit them again — so PGSH cannot today tell
  « still finishing stages » from « stages done, waiting on the exams ». The user said the logic is
  complex and did not describe it; inventing it would put a state on screen nobody can act on.
- **When a 1650.25 student starts revalidating** — parked at the user's request. `FinalYearTest` asks
  `level.Year == TotalYears` per student, which reproduces the old text's behaviour on the new one.
  Nothing hard-codes 7. Not urgent: the first 1650.25 promotion is in its 3ᵉ année.

### Ce que l'exécution a trouvé (02/09/2026)

Le backend est passé au premier essai — tous les chiffres écrits sont ceux de la simulation. Les trois
défauts trouvés étaient **côté écran**, et deux d'entre eux sont de la même famille : *une information
que le serveur envoie et que la page jette*.

- ⚠ **Le découpage annonçait « 60 non assigné(s) » sans un motif**, alors que `BulkResponse.items`
  porte une erreur par étudiant avec le constat du signalement. Corrigé : `GroupsPage` liste les
  motifs et renvoie vers Signalements.
- ⚠ **`BulkItemResult.error` était typé `ApiError`** au lieu du `Error` du domaine — **défaut
  préexistant**, qui rendait toute erreur d'item illisible. Typé `DomainError`.
- ⚠ **`Modal` de Mantine refuse de se monter sur la page Signalements** (racine rendue vide). Non
  diagnostiqué. Contourné par un panneau inline au-dessus du tableau — ce qui règle aussi le défaut
  d'origine : rendu *sous* 60 lignes, le formulaire de levée était invisible.
- Et une double-notification (`CLAUDE.md` §1e) retirée du handler de levée.

### 15.16b — the 26 the file names and PGSH never held (same session)

The roll now **creates** them, from the Apogée and the name, flagged `IncompleteStudentFile`.

- ⚠ **That flag is advisory.** The user wanted them flagged *and* partitioned with everyone else, and
  a signalement freezes by construction — so blocking became a property of the **reason**
  (`RegistrationHoldReasonExtensions.Blocking`). `Plannable` = no *blocking* hold; `Flagged` = any
  hold. The page marks « Gelé » vs « Planifié — à compléter ».
- Only the **e-mail** is invented (NOT NULL UNIQUE, and a login): allocated in the planner against the
  store, printed on the row. **No CNE** — the row has an Apogée and CNE is optional. `BacYear` left
  empty rather than guessed.
- ⚠ Two defects the compiler could not see: `Students.Add(student)` left the registration **untracked**
  (add the registration — the graph is whole only from that end), and `ReleaseHold` on an unsaved hold
  lifted an arbitrary one, because store-generated keys are `Guid.Empty` until saved. Both fixed, both
  covered.

### ⚠ The smoke test of 15.16b found a destructive idempotency bug — fixed, needs a restart

Re-uploading the roll (which is now the normal path, since it is how the 26 newcomers get created)
previewed **8 077 signalements and 791 « Diplômé » déduits**, against 1 267 and 1 217 on the first
pass. Cause: `Skip()` dropped the source registration id, so a row skipped as « déjà inscrit » stopped
counting as *covered by the file*; `ReadAbsence` read that as « ne revient pas » and inferred a
soutenance. It would have ended the cursus of 791 students the same file had re-registered minutes
before.

- **Pre-existing** — `Skip` has always passed null — but invisible while the roll had only ever been
  applied once.
- Fixed: the already-registered branch carries its closing-year registration. Covered by
  `Re_running_does_not_turn_already_registered_students_into_absentees`, **proved to bite** (reverted,
  failed, restored).
- ⚠ **The running API still has the old code.** Restart the stack before touching « Réinscription par
  fichier » — a re-upload on the current process still offers those 791 graduations, and nothing on
  screen says the number is wrong.

### ⚠ The apply found a second defect: duplicated absentee flags

The re-run raised a **second** `AbsentFromReinscriptionRoll` on all 1 267 absentees (2 534 rows).
`PlaceOnHold` is idempotent per reason by reading `Registration.Holds`, and the closing-year query did
not `Include` it — an un-Included collection is indistinguishable from an empty one. ⚠ **The
in-memory suite cannot see this**: it fixes navigations up from the change tracker, so the idempotency
test passed throughout.

- Fixed with the `Include` **and** a unique index, `IX_RegistrationHold_Registration_Reason_Active`
  (migration `RegistrationHoldUniquePerReason`), so the next omission is a constraint violation.
- **The 1 267 duplicates were deleted from the live base** inside a transaction that asserted the
  invariant before committing.
- ⚠ **The migration has not been applied** — it lands on the next Aspire startup. The base is already
  deduplicated, so it will create cleanly.

**Tests: 1 391 green** (was 1 342). New: `RegistrationHoldTests` (domain, incl. two Theory sets),
`RegistrationHoldWorkflowTests` (7), 3 in `ReinscriptionSheetTests`, 2 translation cases. The roster
freeze was proven to bite — guard removed, test failed, guard restored.
