# Le canevas des affectations

> Téléverser un fichier qui dit, ligne par ligne, **quel étudiant sert quel stage, dans quel service,
> entre quelles dates** — et laisser PGSH en tirer les cohortes, les affectations, les périodes et les
> délocalisations.
>
> C'est **l'acte le plus destructeur de l'application**, et il est fait pour servir. Lisez ce document
> avant d'y toucher.

Construit le 13/09/2026, à la demande de l'utilisateur, en regard du canevas de découpage (item 0ba) :
l'un dit **qui est dans quel groupe**, celui-ci dit **où va chacun et quand**.

---

## 1. Pourquoi il existe, et pourquoi le refuser serait pire

La faculté planifie dans un tableur. Elle l'a toujours fait. Refuser d'en accepter un en retour
n'empêcherait pas le tableur — cela empêcherait seulement PGSH de savoir ce qui a été planifié.

Le marché n'est donc pas « rendre la chose difficile ». Il est :

1. **réversible là où c'est possible** — renvoyer le fichier corrigé remplace ce qu'il décrit ;
2. **refusé là où ça ne l'est pas** — une note ne se détruit jamais ;
3. **jamais appliqué à autre chose que ce qu'on a montré à l'opérateur** — deux nombres confirmés.

## 2. Les trois routes, dans l'ordre où on les emploie

| | Route | Ce qu'elle fait |
|---|---|---|
| ① | `GET  affectations/sheet/template?levelId=…[&stageId=…][&academicYearId=…]` | Le canevas **pré-rempli** : une ligne par (étudiant, stage du niveau), les périodes déjà servies remplies, le reste en blanc. |
| ② | `POST affectations/sheet/preview?levelId=…` | L'aperçu. N'écrit rien. |
| ③ | `POST affectations/sheet?levelId=…&confirmedCount=…&confirmedDroppedPeriods=…` | L'application. Tout ou rien. |

⚠ **L'aperçu et l'application reparsent toutes deux le fichier.** Ce qui est appliqué est ce que
l'utilisateur a téléversé, jamais un aller-retour de lignes que le navigateur aurait pu modifier entre
les deux. Les seules choses qui voyagent d'un appel à l'autre sont les **deux nombres**.

## 3. Le document

Onze colonnes, et seulement sept sont lues.

| Colonne | Lue ? | Rôle |
|---|---|---|
| Apogée, CNE | ✅ | L'appariement indexe **les deux**. Le CNE manque sur 46 % du rôle ; l'Apogée est celui qui est en pratique toujours là. |
| Nom, Prénom, Groupe | ❌ | Pour l'humain qui relit. |
| Stage | ✅ | Apparié au nom, **dans le niveau de la promotion**. |
| Service | ✅ | Apparié au nom. |
| Hôpital | ✅ *(partiellement)* | Lu **uniquement** pour départager deux services de même nom. |
| Début, Fin | ✅ | La fenêtre de la période. |
| Motif hors faculté | ✅ | Obligatoire ssi le service est externe. |

**Une ligne = une période, pas un stage.** Une rotation qui traverse trois services est trois lignes
portant le même étudiant et le même stage ; c'est le planificateur qui les replie en une affectation.

### ⚠ Une ligne blanche veut dire « pas encore planifié », et c'est un saut

Le canevas sort avec une ligne par (étudiant, stage du niveau). Planifier **un** stage d'une promotion
veut donc dire téléverser un fichier dont toutes les autres lignes sont vides. Les refuser obligerait à
supprimer plusieurs centaines de lignes avant chaque envoi — et un fichier pénible à renvoyer cesse
d'être renvoyé, ce qui est la façon dont une correction n'est jamais appliquée.

**Mais une ligne à moitié remplie est un refus.** Blanc partout = « je n'y ai pas touché » ; un service
sans dates = quelqu'un qui a commencé et s'est arrêté, et l'ignorer laisserait un étudiant non planifié
sans que rien ne le dise.

## 4. ⚠ Ce que le fichier écrit est **hors grille**

C'est le point que personne ne devinera tout seul, et il a sa place dans le rapport en toutes lettres.

Les périodes créées ici ne portent **aucun `CohortSlotAssignmentId`** — rien dans la grille ne les a
produites. Conséquences, toutes vraies en même temps :

- elles **apparaissent** dans le dossier de l'étudiant, sur la page du service, dans l'export des
  stages, dans la liste de travail du chef ;
- elles **n'apparaissent pas** dans la grille de planning, et **ne comptent pas** dans la charge que la
  grille affiche : `ServiceOccupancyCalculator` lit les **cellules** (`CohortSlotAssignments`), pas les
  périodes ;
- **« Dépublier » ne les reprend pas.** `RemovePublishedPeriods` est l'inverse de la publication et ne
  touche que ce que la publication a fait ;
- **republier ne rétablira pas** une répartition que ce fichier a écrasée : `SchedulePublisher` saute
  toute affectation qui porte déjà une période.

C'est pourquoi `PublishedPeriodsToDrop` est compté **à part** de `PeriodsToDrop` : écraser une
répartition publiée et remplir un stage vide sont deux actes sans rapport, et le second nombre est le
seul qui le dise.

## 5. Le sort d'une ligne

Quatre issues qui écrivent ou sautent, quatorze qui refusent.

### Ce qui passe

| Statut | Ce que ça fait |
|---|---|
| `WillCreate` | L'étudiant n'a pas d'affectation pour ce stage : elle est créée avec cette période. |
| `WillReplace` | Il en a une, et le fichier dit autre chose : ses périodes sont **supprimées** et réécrites. |
| `WillDelocalize` | Service externe + motif : une période unique, déjà commencée et terminée, via `InternshipAssignment.Delocalize`. |
| `Unchanged` | L'affectation dit déjà exactement cela. Sauté. |
| `NotPlanned` | Ligne blanche. Sauté et compté. |

⚠ **`Unchanged` est testé *avant* la note.** Un fichier identique décrit, correctement, la rotation même
pour laquelle ces notes ont été données ; une promotion dont les évaluations arrivent doit rester
re-téléversable.

### Ce qui refuse

`NoIdentifier` · `DuplicateRow` · `StudentNotFound` · `WrongPromotion` · `OnHold` · `NoRoster` ·
`UnknownStage` · `AmbiguousStage` · `UnknownService` · `AmbiguousService` · `MissingDates` ·
`BadDateOrder` · `AlreadyMarked` · `DelocalizationWithoutReason` · `MalformedDelocalization` ·
`AmbiguousAffectation`

⚠ **Un seul refus, n'importe où, refuse le fichier entier.** C'est l'inverse du marché de la
délocalisation de masse — et c'est délibéré. Là-bas, un étudiant écarté reste où il était, un état que
quelqu'un a voulu. Ici l'acte **construit** les enregistrements d'exécution d'une année : appliquer
800 lignes et en refuser 12 laisse une promotion à moitié planifiée, et à moitié planifiée se lit
exactement comme planifiée.

Quelques-uns méritent un mot :

- **`AlreadyMarked`** — la seule chose que cet acte ne détruira jamais. Même marché que
  `Delocalize` : une note est la seule chose ici que rien ne remet.
- **`OnHold`** — `RegistrationHoldPolicy`. Un tableur est exactement le chemin par lequel un étudiant
  gelé se ferait planifier quand même.
- **`NoRoster`** — le découpage vient d'abord ; il n'y a pas de cohorte où accrocher l'affectation.
  C'est l'articulation avec le canevas de découpage (item 0ba).
- **`AmbiguousAffectation`** — un rattrapage est une **seconde** affectation sur le même stage. C'est un
  état réel, pas une corruption ; « la plus récente » réécrirait un rattrapage sur la foi d'un ordre de
  lignes que personne n'a choisi.
- **`WrongPromotion` vs `StudentNotFound`** — distingués par une lecture de plus, parce que « aucun
  étudiant ne porte cet identifiant » sur quelqu'un qui est dans la base envoie l'opérateur chercher
  un fantôme.

### Ce qui est **signalé** et non refusé

**Le CNPN.** Un stage que le texte de l'étudiant n'exige pas de son niveau est porté ligne par ligne
(`OutsideCnpn`) et compté, jamais refusé. Le découpage automatique, lui, le refuse — mais ce canevas
**est** la dérogation humaine, et les jeux d'exigences de l'arrêté 1650.25 ne sont pas tous saisis : une
garde ici refuserait des fichiers sur la foi de données que personne n'a encore tapées.

## 6. Les deux nombres

```
confirmedCount           = le nombre d'AFFECTATIONS que l'aperçu a annoncées   (pas de lignes)
confirmedDroppedPeriods  = le nombre de PÉRIODES que l'aperçu a dit détruire
```

⚠ **Confirmés séparément, parce qu'ils bougent pour des raisons différentes.** Une période évaluée entre
l'aperçu et l'application change ce qui est détruit sans changer ce qui est écrit — et c'est la
destruction qui est définitive. Une case à cocher « oui j'ai vérifié » ne peut attraper ni l'une ni
l'autre ; les nombres le peuvent.

Et le compte est en **affectations**, pas en lignes : une rotation est plusieurs lignes, et le nombre à
l'écran doit être le nombre qu'on autorise.

## 7. Comment l'écriture se déroule

Le tout dans `IAuditTrail.RunAtomicallyAsync` — jamais `ExecuteAtomicallyAsync` directement, parce
qu'une reprise vide le change tracker et que seule la piste sait quelle version de l'entrée en attente
est la bonne.

1. planifier (le même `AffectationSheetPlanner` que l'aperçu) ;
2. refuser si le rapport porte la moindre erreur ;
3. refuser si l'un des deux nombres a bougé ;
4. **créer les cohortes manquantes** et les enregistrer — `Cohort` a une clé `int` générée par le
   magasin et les affectations en ont besoin par valeur (le `CohortMembership` la porte ainsi) ;
5. charger les affectations existantes **tracked**, avec `ServicePeriods` *et* leurs `Evaluation`
   (⚠ une navigation non-`Include`d est indiscernable d'une absente : la garde répondrait « rien à
   perdre » sur un stage qui porte une note) ;
6. par affectation, passer par l'agrégat : `Delocalize` ou `DeclareRotation` ;
7. `IAuditTrail.RecordOutcome` — créées, réécrites, délocalisées, périodes écrites, périodes détruites,
   cohortes créées, inchangées ;
8. `SaveChangesAsync`.

⚠ **Sur une promotion entière, attendez-vous à ce que ça dure.** Chaque affectation lève
`AffectationImportedDomainEvent` et les événements se publient **après** le commit, un par un, chacun
écrivant une ligne d'historique. La transaction est rapide ; les entrées de dossier qui apparaissent
ensuite sont un avancement, pas un blocage.

## 8. `InternshipAssignment.DeclareRotation`

L'agrégat, pas une boucle dans l'importeur — parce que supprimer des périodes déplace trois choses à la
fois : la note de stage calculée sur des évaluations qui n'existent plus, le statut de cycle de vie
dérivé de périodes qui n'existent plus, et — pour un étudiant en prêt — l'adhésion temporaire qui se
ferme quand le stage se termine.

⚠ **Contrairement à `RemovePublishedPeriods`, il prend aussi les périodes ad-hoc**, et c'est toute la
différence : dépublier est l'inverse de publier, donc ne touche que ce que publier a fait ; ici un
humain corrige le dossier, donc ce qui est remplacé est **tout** ce que le dossier dit de ce stage.

Les périodes sont créées **non commencées** : une rotation déclarée est un plan comme un autre, et c'est
l'administration qui la démarre. Un tableur ne peut pas livrer un stage déjà « terminé » — donc
évaluable — sans que personne y ait mis les pieds. La seule exception est la délocalisation, qui est
servie avant d'être enregistrée et passe par `Delocalize`, avec un motif.

## 8bis. Ce que cela coûte, mesuré

Sur la base vivante, le 13/09/2026 :

| | |
|---|---|
| Canevas de la 4ᵉ Pharmacie — 232 lignes | téléchargement **2,1 s**, 15,5 Ko |
| Canevas de la 6ᵉ MED — **4 206 lignes** (701 étudiants × 6 stages) | téléchargement **685 ms**, 85 Ko |
| Aperçu de ces 4 206 lignes | **545 ms** |
| Application de 2 affectations (dont 1 cohorte créée) | 6,6 s |

**La moitié lecture tient l'échelle réelle**, et sans surprise : les huit requêtes sont à plat et
paramétrées par tableau (`= ANY(@p)`, donc aucun risque de plafond de paramètres), et le travail par
ligne ensuite est une poignée de recherches dans des dictionnaires. Le rapport est borné par
construction — 500 lignes nommées, `RowsTruncated`, un poste par stage — donc un fichier dix fois plus
gros ne fait pas dix fois plus de réponse.

⚠ **La moitié écriture n'a pas été mesurée à cette échelle et ne le sera pas sur la base vivante.** Ce
qui est connu : la transaction est rapide, et les entrées de dossier s'écrivent **après** elle, une par
une (`AffectationImportedDomainEvent` → une ligne `History` chacune). C'est le même N+1 que la
déliberation et le rouleau de réinscription, et il est accepté ici pour la même raison — mais au-delà
de **200 affectations** l'aperçu le **dit** désormais, parce qu'un acte qui a réussi et qui a l'air de
traîner est exactement ce qu'on interrompt. Fermer l'onglet ne défait pas ce qui est écrit — c'est
validé — mais laisse les dossiers restants sans trace de l'acte.

## 9. Ce qui n'existe pas encore

- **Une annulation en masse.** Le fichier corrigé remplace ce qu'il décrit, ce qui couvre la faute de
  frappe ; il ne peut pas retirer une affectation qui n'aurait jamais dû exister. Un « défaire cet
  import » demanderait de marquer le lot (une migration) — même forme que l'item 0aw pour la
  délocalisation.
- **Écrire la grille.** Décidé le 13/09/2026 : périodes seules. Écrire aussi les `StageSlot` et les
  `CohortSlotAssignment` ferait du canevas un arrangeur — plus puissant, et il faudrait réconcilier les
  fenêtres par (stage, année, période) sous `SlotOverlapGuard`, qui est **niveau-et-année**.
- **Créer un étudiant.** Le rouleau de réinscription le fait ; celui-ci ne le fera pas. Une identité
  créée par un fichier de planning serait invisible de toute promotion.

## 10. Le code

| | |
|---|---|
| Contrats, statuts, rapport, port du parseur | `PGSH.Application/Stages/InternshipAssignments/Sheet/AffectationSheetContracts.cs` |
| Refus | `…/AffectationSheetErrors.cs` |
| Le planificateur (aperçu **et** application) | `…/AffectationSheetPlanner.cs` |
| L'aperçu | `…/PreviewAffectationSheetQuery.cs` |
| L'application | `…/ApplyAffectationSheetCommand.cs` |
| Le canevas téléchargé | `…/GetAffectationSheetTemplateQuery.cs` |
| L'entrée de dossier | `…/AffectationImportedEventHandler.cs` |
| L'agrégat | `PGSH.Domain/Stages/InternshipAssignment.DeclareRotation`, `DeclaredPeriod`, `AffectationImportedDomainEvent` |
| Le .xlsx | `PGSH.Infrastructure/Stages/ClosedXmlAffectationSheetParser.cs` |
| Les routes | `PGSH.API/Endpoints/Stages/AffectationSheet.cs` |
| Tests | `PGSH.Tests/Application/AffectationSheetTests.cs` (23), `PGSH.Tests/Integration/AffectationSheetEndpointTests.cs` (9), `SqlTranslationTests` (6 cas) |
