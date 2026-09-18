# Le realm Keycloak, en clair

> `pgsh-realm.json` **est** le realm. Il n'est pas une copie de ce qui tourne : c'est la source, et le
> volume Keycloak en est la conséquence.

## Pourquoi ce fichier existe

Le 17/09/2026, Docker Desktop a été réinitialisé et son `.vhdx` a disparu avec **tous** les volumes.
La base a été récupérée — les points de sauvegarde vivent dans `%LOCALAPPDATA%\PGSH\backups`, hors du
volume, et le plus récent avait deux jours. **Le realm Keycloak, lui, n'avait aucune copie** : il
vivait dans le volume `keycloak-data` et nulle part ailleurs. Les comptes, les rôles et le client
étaient à refaire à la main, de mémoire.

`PHASES.md` §18.2 posait la question dans ces termes — « **Keycloak's volume, either dumped with the
base or established in writing as independent** ». Ce fichier est la deuxième branche, et c'est la
bonne des deux :

- un *dump* du volume serait une seconde chose à se rappeler de prendre, et le jour où elle manque on
  est exactement où on était le 17/09 ;
- **un realm écrit est reproductible sans que personne ait rien à se rappeler.** Perdre le volume
  cesse d'être un incident : il se reconstruit au démarrage suivant.

⚠ **Ce que cela veut dire pour `KeycloakRealmCovered`** : le drapeau reste `false` et la page
Sauvegardes continue de dire que le realm n'est pas dans le dump — **c'est toujours vrai**. Ce qui a
changé n'est pas que le realm soit sauvegardé, c'est qu'il n'ait plus besoin de l'être. Passer le
drapeau à `true` dirait au lecteur d'aller chercher le realm dans un `.dump` où il n'est pas.

## ⚠ Il n'y a pas de commentaire dans ce fichier, et ce n'est pas un oubli

La première version portait une clé `"_comment"` en tête — le réflexe, JSON n'ayant pas de
commentaires. **Keycloak a refusé de démarrer.**

```
ERROR: Failed to import realms
ERROR: Unrecognized field "_comment" (class org.keycloak.representations.idm.RealmRepresentation),
       not marked as ignorable (143 known properties: …)
```

`RealmRepresentation` n'ignore **aucune** propriété qu'elle ne connaît pas : l'import échoue, et
l'import qui échoue arrête le serveur. Le conteneur sort en code 1 et l'application entière n'a plus
de fournisseur d'identité — pour une note laissée à un lecteur.

**Donc les explications vivent ici, dans un fichier que rien ne désérialise.** Et la règle est tenue
par un test plutôt que par la mémoire : `KeycloakRealmFileTests` échoue sur toute clé préfixée `_`,
où qu'elle soit dans l'arbre. Il vérifie aussi les rôles contre `Roles`, le client PKCE, et
l'appariement e-mail ↔ `Seeder` décrit plus bas.

## ⚠ Un seul client, et tout ce qui se connecte le nomme

Ce realm déclare **`pgsh-frontend`** et rien d'autre : public, flux standard, PKCE `S256`, et des
`redirectUris` en `http://localhost:*/*` et `https://localhost:*/*` — donc le port de l'API, qu'Aspire
choisit, est déjà admis sans nouvelle entrée.

**Les écrans de documentation de l'API — Scalar et Swagger — passent par ce même client**, nommé une
seule fois dans `ApiDocumentationAuth.ClientId`. Ils étaient configurés pour un `pgsh-swagger` qui
n'existait que dans le volume perdu le 17/09/2026 : après la reconstruction, « Authorize » ouvrait la
page **« Client not found »** de Keycloak, ce qui se lit comme un bouton cassé et ne nomme aucun remède.

⚠ **Ne pas ajouter un second client pour les rendre indépendants.** Ce serait recréer les deux listes
qui doivent s'accorder, exactement la forme du défaut. Le joint est tenu par un test :
`KeycloakRealmFileTests` refuse une constante qui ne nomme aucun client d'ici, et son message imprime
les clients réellement déclarés.

### ⚠ Un joker ne s'étend qu'à la **fin** d'une URI de redirection

`http://localhost:*/*`, écrit pour dire « n'importe quel port local », est pris **littéralement** par
Keycloak et n'admet donc rien. Ce fichier en portait deux ; le frontend marchait quand même parce qu'il
avait aussi `http://localhost:5173/*`, un vrai joker terminal, et ce sont les écrans de documentation de
l'API — servis sur le port de l'API — qui ont récolté **« Invalid redirect URI »**, juste après
« Client not found », sur le même bouton.

Les ports sont donc énumérés, et ils viennent de `PGSH.API/Properties/launchSettings.json` :

| URI | Pourquoi |
|---|---|
| `http://localhost:5173/*` | le frontend |
| `https://localhost:7014/*` | Scalar et Swagger (l'API en https) |
| `http://localhost:5199/*` | la même en http |

⚠ **Et la première version du test se trompait exactement de la même façon** : elle vérifiait que le
*fichier* contenait `https://localhost:*`, pas que Keycloak en ferait quelque chose — elle passait au
vert sur le motif cassé. C'est le piège énoncé plus bas (« il vérifie le contrat, pas la
configuration »), rencontré dans ce fichier même. La règle épinglée est désormais celle que le serveur
applique : aucune URI ne cache de joker ailleurs qu'en dernier caractère, et chaque atterrissage réel
est admis par l'une d'elles.

### ⚠ Modifier ce fichier ne change pas le realm déjà importé

`.WithRealmImport` n'importe **que si le realm est absent** (`PGSH.AppHost/Program.cs` le dit aussi).
Corriger les redirections ici ne répare donc rien sur une instance qui tourne. Deux gestes :

- **Tout de suite, sans rien perdre** : admin console `http://localhost:8082` → realm `pgsh` → *Clients*
  → `pgsh-frontend` → *Valid redirect URIs* → ajouter `https://localhost:7014/*`. Effet immédiat, pas de
  redémarrage.
- **Proprement** : supprimer le volume Keycloak pour que le fichier soit réimporté — ⚠ cela emporte tout
  ce qu'un humain a réglé à la main dans l'admin console, ce que le fichier ne sait pas reconstruire.

## ⚠ Trois défauts en un jour, et pourquoi il y a maintenant un test qui démarre Keycloak

Ce fichier a été écrit à la main, et il a cassé trois fois de suite — chaque fois découvert par un
humain regardant une exception :

| # | Le défaut | Ce qu'on voyait |
|---|---|---|
| 1 | une clé `"_comment"` | **le conteneur sortait** : l'import refuse toute propriété inconnue |
| 2 | pas de `default-roles-pgsh` sur les comptes | jeton **sans `aud`** → 401 → `keycloak.logout()` : boucle de connexion |
| 3 | `defaultClientScopes` écrit à la main **sans `basic`** | jeton **sans `sub`** : valide, signé, et ne nommant personne |

**Les trois ont une seule cause : on ne peut pas deviner ce que Keycloak émet en lisant le fichier.**
Les vérifications de forme (`KeycloakRealmFileTests`) n'en auraient attrapé aucune — ce sont des faits
sur le comportement du serveur, et seul le serveur y répond.

✅ **D'où `KeycloakRealmContractTests`** : il démarre un vrai Keycloak (Testcontainers), lui monte **ce
dossier-ci** en import, demande un jeton pour **chacun** des sept comptes et vérifie ce que
l'application lit réellement — un `sub`, l'audience `account`, l'e-mail, les rôles. Les défauts 2 et 3
ont été réintroduits pour vérifier qu'il les attrape : **8 cas sur 11 tombent** dans les deux cas.

⚠ **Il vérifie le contrat, pas la configuration.** Il ne dit nulle part « le client doit porter le
scope `basic` » : il dit que le jeton doit porter un `sub`. Si une version de Keycloak déplace la
responsabilité de `sub` ailleurs, le test reste juste et c'est le fichier qui devra suivre.

⚠ **Et c'est pour cela que `defaultClientScopes` n'est plus listé du tout.** Les énumérer à la main,
c'est écraser les valeurs par défaut du realm et devoir se souvenir de chacune — le défaut 3 est
exactement cela. Sans la clé, Keycloak applique ses défauts, `basic` compris.

## Comment il est chargé

`PGSH.AppHost/Program.cs` : `.WithRealmImport("../keycloak")`. Aspire monte le dossier sur
`/opt/keycloak/data/import` et Keycloak l'importe au démarrage.

⚠ **L'import ne s'applique qu'à un realm absent.** Keycloak saute un realm qui existe déjà — donc
modifier ce fichier ne change **rien** à un Keycloak déjà peuplé. C'est voulu : ce qu'un humain a
réglé dans l'admin console n'est pas écrasé par un fichier au démarrage suivant. Pour réappliquer :

```powershell
# ⚠ détruit les comptes et les sessions du realm ; rien d'autre.
docker volume rm <volume keycloak>   # le nom est donné par : docker volume ls
```

…puis relancer l'AppHost.

## Les comptes

**Mot de passe : `123`, sur tous.** ⚠ Realm de **développement**. Il n'y a ni politique de mot de
passe, ni protection contre le bourrage (`bruteForceProtected: false`), et `sslRequired` est `none`.
Rien de tout cela ne doit atteindre une installation réelle — un realm de production se déclare
ailleurs et ne part pas de ce fichier.

| Compte | Rôles | Ce qu'il sert à voir |
|---|---|---|
| `admin.pgsh@um5.ac.ma` | `Scolarite`, `SuperUser`, `Employee` | tout l'administratif : planification, canevas, sauvegardes |
| `employee.test@um5.ac.ma` | `Professor`, `Employee` | la liste de travail d'un chef |
| `chef.cardio@um5.ac.ma` | `Professor`, `Employee` | un second chef, pour voir que le cloisonnement par service tient |
| `secretaire.test@um5.ac.ma` | `Secretaire`, `Employee` | les présences, **sans** la portée administrative |
| `amine.bennani@um5.ac.ma` | `Student` | le portail étudiant |
| `etudiant.test2@um5.ac.ma` | `Student` | un deuxième étudiant |
| `etudiant.test3@um5.ac.ma` | `Student` | un troisième |

## ⚠ `default-roles-pgsh` n'est pas décoratif : sans lui, tout le monde est déconnecté

Chaque compte porte **`default-roles-pgsh`** en plus de ses rôles PGSH. La première version ne le
mettait pas, et voici ce que cela faisait :

1. Keycloak n'accorde alors aucun rôle du client `account` ;
2. le jeton sort donc **sans aucune revendication `aud`** ;
3. l'API exige l'audience `account` (`Keycloak:Audience`), et répond **401** ;
4. `errorMiddleware` transforme un 401 en `keycloak.logout()`.

Résultat : on se connecte, la connexion réussit, et on est renvoyé à l'écran de connexion la seconde
d'après. Rien, nulle part, ne dit pourquoi.

⚠ **C'est le défaut symétrique de celui d'en dessous, et les deux se ressemblent de l'extérieur.**

| ce qui manque | statut | ce qu'on voit |
|---|---|---|
| la ligne `User` (e-mail inconnu) | **403** | la page « profil absent » |
| `default-roles-pgsh` (pas d'`aud`) | **401** | déconnexion immédiate |

Les deux se racontent « je me suis connecté et je ne peux pas utiliser l'application ». Seul le jeton
les sépare — `aud` présent ou non. `KeycloakRealmFileTests` épingle les deux.

## ⚠ Un realm reconstruit émet de *nouveaux* `sub` — et la base garde les anciens

C'est le troisième visage du même problème, et il ne se voit qu'**après** une connexion réussie.

Une base restaurée porte les `IdentityProviderId` qu'émettait le realm **détruit**. Le realm refait en
émet d'autres pour les mêmes personnes. `UserContext.SyncAsync` existe pour les réunir — il cherche par
`IdentityProviderId`, échoue, puis retombe sur l'e-mail — et jusqu'au 17/09/2026 il n'y arrivait pas :
`User.LinkIdentity` levait « User already linked » sur la valeur périmée. **Tout utilisateur s'étant
déjà connecté une fois était donc définitivement bloqué par une reconstruction du realm**, avec un
message qui ne nommait aucun remède.

`User.RelinkIdentity` est l'acte qui manquait. ⚠ Il **garde ce qu'il remplace**
(`UserIdentityRelinkedDomainEvent.PreviousProviderId`) : l'ancien `sub` n'existe nulle part la seconde
d'après, et sans lui personne ne peut dire si un compte a été rattaché une fois par une reconstruction
ou plusieurs fois par autre chose. Le refus de `LinkIdentity` sur un *autre* sujet reste — repointer un
dossier vers une autre identité par accident est exactement ce qu'il faut empêcher ; ce qui a changé,
c'est qu'il existe maintenant un acte nommé pour le faire exprès.

⚠ **Rien de tout cela n'est visible sans le vrai pipeline** : `SyncUserMiddleware` tourne entre
l'authentification et l'endpoint. → `UserIdentityLinkTests` (la règle) et
`RebuiltIdentityProviderEndpointTests` (qu'elle s'applique là).

## ⚠ Un compte Keycloak ne suffit pas : il lui faut une ligne `User`

C'est le piège de cet endroit, et il ne se voit qu'à la connexion.

`UserContext.SyncAsync` relie le `sub` Keycloak à un `User` local par `IdentityProviderId`, et **à
défaut par l'e-mail** — auquel cas il appelle `user.LinkIdentity(...)` et enregistre, si bien qu'un
realm refait se réaccroche tout seul aux lignes existantes. Mais si **aucune** ligne `User` ne porte
cet e-mail, la connexion réussit côté Keycloak et l'application répond **403 « Profile Not Found »**.

Les e-mails ci-dessus sont donc exactement ceux que `Seeder.SeedStaticUsersOnlyAsync` garantit —
« Static users : always the same IDs and emails so Keycloak email-matching works », et ce seeding-là
tourne **même sur la base réelle** (`Seeding:StaticUsers = true`), précisément parce que ces comptes
sont la seule porte d'entrée. Les deux listes se lisent ensemble : **ajouter un compte ici sans
ajouter sa ligne là produit un 403 que rien sur l'écran n'explique.**

## Ce que ce fichier ne fait pas

- **Il ne rend aucun employé chef de service.** `Service.AssignChef` exige que l'employé soit dans le
  `Staff` du service et **clôt la tenure du chef en place** (`ServiceChefAssignment`). Sur la base
  réelle, cela réécrit qui dirige un service de la faculté : c'est un acte du domaine, pas une
  fixture, et il se fait depuis l'écran du service.
- **Il ne crée aucune inscription.** Un étudiant sans `Registration` n'apparaît dans aucune promotion.
