# RefBoardXIV

🇬🇧 [English version](README.md)

Plugin [Dalamud](https://github.com/goatcorp/Dalamud) pour Final Fantasy XIV : un **tableau de références** dans une
fenêtre du jeu. Dépose tes images dessus, zoome, déplace-les, puis verrouille-le : seules les images restent à
l'écran et tes clics passent au jeu.

## Installation
1. En jeu, tape `/xlsettings` et ouvre l'onglet **Expérimental**.
2. Dans **Custom Plugin Repositories**, ajoute cette adresse, clique sur **+**, coche la case puis enregistre :
   ```
   https://raw.githubusercontent.com/Arzhaell/RefBoardXIV/main/repo.json
   ```
3. Tape `/xlplugins`, cherche **RefBoardXIV** et installe-le.

## Utilisation
- **Ajouter des images** : les glisser depuis l'explorateur de Windows, les coller (Ctrl+V, depuis un navigateur ou
  des fichiers copiés), ou le bouton « Ajouter des images… ». Chaque image est copiée dans le tableau : il ne casse
  pas si tu déplaces les fichiers d'origine.
- **Se déplacer** : molette pour zoomer, faire glisser le fond (ou clic molette / clic droit) pour se déplacer,
  double-clic sur le fond pour tout voir.
- **Arranger** : faire glisser une image pour la déplacer, ses coins pour la redimensionner ; clic droit pour la
  taille d'origine, devant / derrière, supprimer ; Suppr pour retirer l'image choisie, Ctrl+Z pour l'annuler.
- **Rogner** : clic droit sur une image → « Rogner » (ou le bouton de la barre, ou son raccourci), puis tirer ses
  bords ; Entrée ou un clic à côté pour valider, Échap pour annuler, « Image entière » pour défaire le rognage.
- **Plusieurs tableaux**, chacun avec son nom ; tout est enregistré automatiquement. Les images collées sont
  enregistrées en PNG.
- **Partager un tableau** : le bouton de partage l'enregistre dans un seul fichier `.refboard` (disposition, rognages
  et images) et le montre dans l'explorateur, prêt à envoyer (Discord…). Pour ouvrir un tableau reçu : « Importer un
  tableau… » en bas de la liste des tableaux, ou glisser le fichier sur ton tableau. Il arrive en nouveau tableau.
- **Mode verrouillé** : la fenêtre sert de cadre. Un cadre bleu fixe (avec sa taille en pixels) montre la zone des
  images : verrouillé, il reste exactement ce qui est dedans, à la même place (sans barre de titre, boutons ni fond),
  et les clics passent au jeu. Le cadre peut rester visible une fois verrouillé (réglage).
- `/refboard` affiche ou masque le tableau (marche aussi dans une macro) ; `/refboard verrouiller`,
  `/refboard réglages` (ou `lock`, `config`). Le tableau ne s'ouvre jamais tout seul : il s'affiche quand tu le
  demandes, et seulement connecté à un personnage.
- **Raccourcis clavier** à choisir dans l'onglet « Raccourcis » des réglages : afficher / masquer et verrouiller
  (partout dans le jeu), coller, annuler, retirer l'image, rogner, tout voir (sur le tableau). Ils ne se déclenchent
  pas pendant qu'on écrit dans la discussion.
- **Masquage automatique**, au choix : cinématiques, gpose, combat, chargements, interface du jeu masquée.
- L'opacité des images se règle dans les réglages.

Les tableaux sont enregistrés dans `%APPDATA%\XIVLauncher\pluginConfigs\RefBoardXIV\boards\` (un dossier par tableau).

Le plugin suit la langue de Dalamud (anglais ou français) ; les réglages permettent de la forcer.

## Avertissement
Comme tout outil tiers, l'utilisation de Dalamud et de ses plugins est contraire aux conditions d'utilisation de
FFXIV. Tu l'utilises à tes risques.

## Compiler et tester
- `dotnet test RefBoardXIV.Tests -c Release` : tests de la logique (enregistrement des tableaux, vue, rognage,
  tailles d'image, presse-papiers, PNG, partage, raccourcis, commandes, masquage automatique).
- `dotnet build RefBoardXIV -c Release` : pour l'essayer, ajoute `RefBoardXIV\bin\Release\RefBoardXIV.dll` aux
  emplacements des plugins de développement (`/xlsettings`, onglet **Expérimental**) ; Dalamud la recharge toute
  seule, même jeu ouvert. Il ne charge pas en même temps la version installée depuis le dépôt : désactive-la.

## Publier une nouvelle version
Pousser un tag annoté `vX.Y.Z` suffit : le workflow GitHub lance les tests, compile le plugin, crée la release (ses
notes sont le message du tag) et met à jour `repo.json`.

```bash
git tag -a v1.2.0 --cleanup=verbatim -F notes.txt
git push origin v1.2.0
```

## Crédits
- [Dalamud](https://github.com/goatcorp/Dalamud) et [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) :
  cadre des plugins et structures du jeu.
- [xUnit](https://xunit.net/) pour les tests.
- Idée inspirée de [PureRef](https://www.pureref.com/), logiciel d'Idyllic Pixel (PureRef est leur marque). Aucun lien
  avec PureRef : ce plugin ne contient rien de PureRef et ne lit pas ses fichiers `.pur`.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD. Ce plugin n'est ni affilié ni approuvé par Square Enix.
