# The authored countries of the 500 BCE start

This file covers the authored countries (ACH, MAC, CAR, NUB, SAB, OLM, CHV, IND, ZHO and the 11 separate Greek city-states that replaced GRK) listed in `projects/World500BCE/game/common/countries/countries.txt`. Dynamic and generated countries (rebels, released nations, civil-war splits) are not covered.

Tags, names and colors are placeholders, as the countries file says: one country stands for each large power or people.

## How to read this file

Every statement carries one of three labels. Do not mix them up.

| Label | Meaning |
|---|---|
| **GAME** | Read from the game files in this repository. Exact. |
| **HIST** | Historical background from general scholarship, NOT stored in any game file. Dates are approximate (BCE) and several are disputed. Written from memory, not looked up while writing. Check before quoting. |
| **OPEN** | Unknown, disputed, or a mismatch in the game data that needs a decision. |

Most game files hold no named rulers, no named factions, no rituals and no war doctrines. The MAC character layer is an exception: its starting ruler, heir and named royal family members are now defined in `history/characters/00_macedon.txt`. Everything else under those headings is HIST and is background for future authoring (events, characters, uniforms, AI profiles), not a description of current behavior.

## At a glance (GAME)

| Tag | Country | Culture | Religion | Capital location | States | Population | Manpower (state sum) | Start armies | Gear models |
|---|---|---|---|---|---|---|---|---|---|
| ACH | Achaemenid Empire | persian | mazdayasna | land_07110 (Mesopotamia) | 103 | 25,269,541 | 1,265 | 24 infantry divisions | Own set (`gear_*_ach.glb`) |
| ATH, SPA, COR, ARG, THB, THS, CRT, NAX, CRO, SYR, PNT | Greek city-states (split from the old GRK) | greek, western_greek, pontic_greek | hellenic_religion | see the Greek section | 10 (before the split) | 1,071,654 | 54 | 12 divisions in all | Own set (`gear_*_<tag>.glb`) |
| MAC | Macedon | ancient_macedonian | macedonian_religion | land_05247 (Macedon and Epirus) | 1 | 179,496 | 9 | 2 | Own set (`gear_*_mac.glb`) |
| CAR | Carthage | punic | canaanite_religion | land_09105 (Carthage and Tunisia) | 6 | 1,039,904 | 52 | 2 | Generic soldier |
| NUB | Kush | kushite | kushite_religion | land_08279 (Nubia and Napata) | 3 | 633,812 | 32 | 2 | Generic soldier |
| SAB | South Arabia | south_arabian | south_arabian_polytheism | land_09216 (South Arabia) | 6 | 1,583,519 | 80 | 2 | Generic soldier |
| OLM | Olmec | maya | mesoamerican_religion | land_15261 (Olmec Gulf Coast) | 9 | 2,217,462 | 112 | 3 | Own set (`gear_*_olm.glb`) |
| CHV | Chavin | chavin | andean_religion | land_11805 (Chavin Peru) | 5 | 1,361,269 | 68 | 2 | Generic soldier |
| IND | Magadha and Ganges | indo_aryan | vedic_brahmanism | land_03808 (Magadha) | 66 | 17,570,032 | 879 | 22 | Generic soldier |
| ZHO | Zhou | huaxia | zhou_ancestor_cult | land_01707 (Zhou states, Yellow River) | 146 | 36,723,283 | 1,842 | 24 | Generic soldier |

Shared start values: every country begins with 100 gold and 10,000 manpower. The default (player) country is ACH. The scenario uses these features: population, religion, social_classes, cores_claims, military, diplomacy, characters, economy, politics, ai, combat_imperator, laws, civil_wars, independence, ai_profiles, navy.

Population and manpower figures were computed from `map/states/00_states.txt` (owner and manpower) and `history/pops/00_pops.txt` (pops of owned states).

### Starting relations (GAME, `history/diplomacy.txt`)

- ACH is overlord of SAB (tributary: pays 25% of income, does not join ACH wars, base liberty desire 10).
- ACH is overlord of NUB (client state: pays 10% of income, joins ACH wars, base liberty desire 0).
- SPA and COR are allied. MAC and THS are allied. (GRK no longer exists.)
- CHV and OLM are allied.

### The four ideologies, read as 500 BCE factions (GAME + interpretation)

The politics file says: democratic = citizen assemblies and republics; neutrality = kings and priests; fascist = warrior-nationalist factions; communist = commoner and slave levelers. Every country has a popularity for all four, so each always has a "shut-out" movement that can grow into unrest, coups or civil war. The ideology modifiers are GAME: democratic gives +5 stability target and opinion; neutrality gives +5 stability target; fascist and communist give +3 political power gain and extra manpower (+15% and +10%) and are marked extremist. The faction names below are HIST labels for what each bloc plausibly stands for.

Laws available (GAME): conscription (volunteer_only, limited, extensive, service_by_requirement), economy, press, political_parties (multi_party, restricted_parties, single_party) and culture policy (equal_rights, tolerance, discrimination, forced_assimilation). Every country except ATH, ARG, NAX and CAR starts with `restricted_parties`, which bans the communist and fascist parties from seats and government.

### Classes (GAME, `common/pop_types`)

Farmers, herders, foragers, artisans, nobles, clergy, slaves. Starting class mix per country is listed in each section.

### Casus belli available to all (GAME)

Reclaim cores, cultural unification, press claims, war of independence, liberation, and others in `common/casus_belli.txt`. No country has its own.

### Military data shared by all (GAME)

Unit types are infantry, artillery and cavalry; division templates are infantry_division (6 infantry), artillery_division (5 infantry, 1 artillery) and cavalry_division (4 cavalry). Every starting army is infantry divisions only. Starting fleets for ACH, MAC, CAR, SAB, OLM, CHV, IND and ZHO use modern ship classes (destroyer, light_cruiser, battleship, transport); NUB has no fleet. **OPEN**: these ship classes and the "artillery" unit are anachronistic placeholders for 500 BCE.

---

## ACH: Achaemenid Empire

### In the game files (GAME)

- Color 120/60/150. Capital land_07110. Culture persian, religion mazdayasna.
- 103 states, 25.3 million people. Culture mix: Persian 4.78M, Babylonian 3.89M, Indo-Aryan 3.82M, Anatolian 3.27M (more cultures further down). Religion mix: mazdayasna 7.8M, Mesopotamian polytheism 3.9M, Vedic Brahmanism 3.8M, others.
- Classes: farmers 18.8M, slaves 3.0M, artisans 2.0M, nobles 0.77M, clergy 0.75M.
- Politics: ruling party neutrality. Popularity neutrality 45, fascist 30, democratic 15, communist 10. Law: restricted_parties.
- AI profile "persia" (parent: expansionist). Description: "A vast empire that wants Greece, Egypt and the steppe." Rivals ATH 2, SPA 1, MAC 1. Wants ATH 2.5, NAX 2, MAC 2, SPA 1.5, THB 1, IND 1.5, NUB 1.5. Timing: NAX -500, ATH -499, MAC -498, SPA -497.
- Army: "1st Army", 24 infantry divisions at the capital. Fleet "Home Fleet" at land_09609.
- Overlord of SAB (tributary) and NUB (client state).
- The only country with its own soldier set. Uniform: saffron tunic (0.85/0.60/0.13), brown leather, soft cloth cap, soft boots, no firearm. Pieces: tunic, boots, headgear, wicker shield (`gear_shield_ach`), spear (`gear_spear_ach`). Soldiers carry the spear upright with the shield on a raised left forearm and have two spear attacks (thrust and deep lunge), picked at random in battle.

### Rulers (HIST)

Darius I "the Great" (r. 522 to 486), king of kings, of the Achaemenid line through Hystaspes. Queen Atossa (daughter of Cyrus, wife of Darius), a power at court; other wives include Artystone. Crown prince Xerxes is a boy of about 14 in 500 (born c. 519 to 518). Key names: Artaphernes, satrap of Sardis and the king's brother; Hystaspes, the king's father, governor of Parthia; Megabates and Otanes in the Ionian theater; Mardonius, Darius's son-in-law, rises later. Exiled tyrant Hippias of Athens is at the Persian court.

### Internal politics and factions (HIST)

- **The king and the seven houses.** Darius took the throne in 522 with six noble co-conspirators (Otanes, Intaphrenes, Gobryas, Hydarnes, Megabyzus, Aspathines). Their houses keep privileges, including marriage ties to the king and the right of audience. This is the "neutrality" bloc: king, court and great nobility.
- **Satraps vs. the center.** About twenty satrapies pay fixed tribute (Darius's reform). Satraps are powerful provincial governors checked by royal secretaries and "the king's eyes and ears" inspectors. A satrap who grows too independent is the standing fear (the "fascist" warrior-nobility bloc: Persian and Median cavalry lords).
- **Priests.** The Magi are a priestly class (Median in origin), influential but still distrusted after the rule of the Magus Gaumata in 522.
- **Subject peoples.** Ionian Greek cities under tyrants friendly to Persia, Babylon (rebelled twice in 522 and 521), Egypt, Lydia, Judea, Indus provinces. The Ionian Revolt breaks out in 499. Commoner and slave unrest is the "communist" bloc.
- **Administration.** Standard weights, the gold daric and silver siglos, the Royal Road from Sardis to Susa, Aramaic as the chancery language, Old Persian cuneiform for monuments (the Behistun inscription, c. 520 to 518).

### Culture and traditions (HIST)

Persian boys are famously taught "to ride, to shoot the bow and to speak the truth" (Herodotus). Royal hunts in walled parks (paradeisoi, "paradises"), banquets with the king reclining, gift giving as political language, proskynesis (obeisance) before the king. Building at Susa and Persepolis (begun c. 518). Trousers (anaxyrides), long coat (kandys) and the felt cap are the Persian look; the Medes wear similar dress.

### Religion and ritual (HIST)

Worship of Ahuramazda, named in Darius's inscriptions (whether this is the same as later Zoroastrianism is debated). Rites are done outdoors on high places, with fire and sacrifice led by Magi; the Magi chant, hold the bundle of barsom twigs and take the sacrificial animal. Exposure of the dead to birds (practiced at least by the Magi) before burial. The king is buried in rock-cut tombs (Naqsh-e Rostam, Darius's tomb). Persian rule tolerates and funds local temples (Egypt, Babylon, Jerusalem).

### Doctrine of war (HIST)

- A multiethnic army raised by satrapy: a Persian and Median core, vassal contingents each fighting in its own gear.
- **Archers behind a wall of shields.** The sparabara ("shield bearers") plant big wicker shields (the spara) in the ground and shoot over them. This is the signature battlefield formation. The game's shield and spear pieces model it.
- **Cavalry on the wings** (Persians, Medes, Sacae, Bactrians) to outflank and harass.
- **The Immortals**, a 10,000-man guard kept at constant strength. Infantry with the bow and a short spear.
- **Navy** is Phoenician, Egyptian, Cypriot and Ionian Greek, built and crewed by subjects.
- Strategy: logistics through the satrapies, bridging and canals, diplomacy and gold to split enemies.

### Equipment (HIST, and GAME where noted)

Common Persian infantry: soft felt cap (tiara), tunic with sleeves, trousers, soft shoes, wicker shield, short spear, bow and quiver, akinakes (short dagger). Elite: scale or quilted body armor under the tunic, more ornament. Median and Persian cavalry: javelins, bow, spear. Game version (GAME): saffron tunic, leather, soft cap, soft boots, spear and wicker-style shield.

**Heavy infantry kit (GAME and OPEN):** the template `heavy_infantry_division` (`kit = heavy`, ACH only, unit type `heavy_infantry`: more organization, defense, shock and morale, slower) is drawn in its own gear next to the standard divisions; ACH starts with 10 standard and 6 heavy divisions. Gear (`gear_*_ach_heavy.glb`, `tools/assets/models/gear_persian_heavy.py`): saffron coat under iron scale with shoulder caps and a gold boss, a scale skirt, pointed iron cap with neck and ear flaps, akinakes in a gold-banded sheath on the right hip; spear, wicker shield and soft boots as the standard kit. OPEN: scale-armored Persian infantry is attested, but this cut, the iron cap and the colors are design choices; the heavy stats are not sourced.

![ACH heavy infantry, male and female](img/soldiers-ach_heavy.png)

### OPEN

- The country name "Achaemenid Empire" is a placeholder; the game could instead use "Persia" or "Parsa".
- Fleet and artillery data are anachronistic (see the shared note above).

---

## The Greek city-states (ATH, SPA, COR, ARG, THB, THS, CRT, NAX, CRO, SYR, PNT)

The old single country GRK ("Greek Poleis") no longer exists. Its 10 states were split into 11 countries, one per major polis or league. Political inner workings (parties, factions, events) are deferred: each country has only a starting ruling party, popularity and one law.

![The Greek city-states in the political map mode](img/greek-poleis-500bce.png)

### Shared facts (GAME)

- All 11 start with 100 gold, 10,000 manpower, religion hellenic_religion, a "1st Army" at their capital, and a name and color of their own. Armies: SPA 2 infantry divisions; THS 1 infantry plus 1 cavalry; every other one 1 infantry.
- Fleets (placeholder, 2 transports each): ATH, COR, NAX, SYR. The others have none. The old modern warship classes were NOT carried over.
- Relations: SPA allied with COR (the Peloponnesian League core, HIST). MAC allied with THS (a design choice, OPEN). The old GRK to MAC alliance was removed.
- AI profiles: "athens" (defensive; rivals ACH 2.5, THB 1, CAR 0.5; friend NAX), "sparta" (status_quo; rivals ARG 2, ACH 1, ATH 0.5; friend COR 2), "greek_cities" (defensive; COR ARG THB THS CRT NAX CRO PNT; rival ACH 1), "syracuse" (status_quo; rival CAR 2). ACH now wants ATH 2.5, NAX 2, MAC 2, SPA 1.5, THB 1 (awakening NAX -500, ATH -499, MAC -498, SPA -497). MAC now wants THS 2 (from -490). CAR rivals SYR 1.5 and CRO 0.5.
- Population numbers are the old GRK numbers re-divided, and are design estimates, not census data. Sparta's mix was changed on purpose: 45% slaves (the helots), 36% farmers, 12% artisans (the perioikoi, HIST), 5% nobles.
- Provinces are coarse. Athens and Boeotia in particular are small provinces, and the Boeotian plain may fall partly inside the Phocis province. Euboea (Chalcis, Eretria), Aegina and Delphi have no province of their own. The Aetolia and Acarnania state is now unowned (tribal).

| Tag | Country | Capital | States | Population | Manpower | Culture | Ruling party (popularity) | Parties law | Color |
|---|---|---|---|---|---|---|---|---|---|
| ATH | Athens | land_01113 | Attica | 98,105 | 4 | greek | democratic (dem 40, neu 22, fas 20, com 18) | multi_party | 70/120/200 |
| SPA | Sparta | land_05800 | Laconia and Messenia | 116,500 | 5 | greek | neutrality (neu 44, fas 40, com 10, dem 6) | restricted | 170/40/50 |
| COR | Corinth | land_05412 | Corinthia, Ionian Islands | 56,998 | 4 | greek | neutrality (neu 44, dem 26, com 16, fas 14) | restricted | 90/170/225 |
| ARG | Argos | land_08799 | Argolis | 45,986 | 3 | greek | democratic (dem 36, neu 28, fas 22, com 14) | multi_party | 55/85/160 |
| THB | Thebes | land_07184 | Boeotia and Phocis | 102,426 | 5 | greek | neutrality (neu 46, fas 28, dem 14, com 12) | restricted | 150/110/200 |
| THS | Thessaly | land_09076 | Thessaly | 102,426 | 5 | greek | neutrality (neu 50, fas 28, com 14, dem 8) | restricted | 100/190/170 |
| CRT | Crete | land_15921 | Crete | 83,169 | 4 | greek | neutrality (neu 42, fas 32, dem 14, com 12) | restricted | 80/150/120 |
| NAX | Naxos and the Cyclades | land_15916 | Lemnos and the North Aegean, Cyclades | 30,767 | 2 | greek | democratic (dem 38, neu 32, com 18, fas 12) | multi_party | 130/200/235 |
| CRO | Croton and Italiote Greeks | land_08888 | Magna Graecia | 203,071 | 10 | western_greek | neutrality (neu 44, dem 24, fas 18, com 14) | restricted | 110/130/190 |
| SYR | Syracuse and Eastern Sicily | land_01952 | Eastern Sicily | 70,769 | 4 | western_greek | neutrality (neu 46, fas 24, com 18, dem 12) | restricted | 190/160/225 |
| PNT | Pontic Greek Cities | land_07007 | Black Sea Greek coast | 17,591 | 1 | pontic_greek | neutrality (neu 44, dem 26, com 16, fas 14) | restricted | 120/145/95 |

All popularity values are placeholders to be revisited when the political workings are authored. Class mix follows the old GRK ratios (about 40% farmers, 22% artisans, 5% nobles, 3% clergy, 30% slaves) except Sparta.

### Soldier equipment, Greek and Roman (GAME and OPEN)

Worn in the game for ATH SPA COR ARG THB THS CRT NAX CRO SYR PNT, MAC and ROM (`gear_<piece>_<tag>.glb`, one file per tag, built by `tools/assets/models/gear_greek_roman.py`; needs numpy and pygltflib; preview with `--figure-variants <dir>`). The shared asset generator is a visual placeholder and does not determine culture, religion, language or identity. Pieces:
- **Greek hoplite:** linen linothorax with two layers of pteryges (hanging strips) and shoulder flaps; short chiton skirt; Corinthian-type bronze helmet with cheek guards, nasal bar and a horsehair crest; bronze greaves over sandals; xiphos in a scabbard on the left hip; dory spear (bronze butt spike, leaf head) held in the right hand; aspis (large round shield, bronze rim, boss) on the left forearm. No firearm.
- **Roman (ROM), early Republic Italic hoplite:** red wool tunic, bronze breastplate with three round plates, lower helmet with a short red crest comb, bronze greaves over closed leather shoes, short sword on the right hip, hasta (shorter spear), clipeus (smaller round shield).
- Colors: shield faces use each country's map color; chiton colors vary (Sparta crimson, Athens and most others undyed linen, Thessaly and Crete wool tones). OPEN: these are design choices. Real polis shield devices were painted; none are modeled. Sparta's crimson is historical, the rest are not sourced. The Roman set follows the Etruscan and Greek-influenced "hoplite phase" of the Roman army; I did not look up a source for it (OPEN).
- **Sparta (SPA) differs** (`tools/assets/models/gear_spartan_egyptian.py`): crimson tunic with a crimson cloak down the back, no body armor, bronze pilos cap, bare feet with ankle cords; sword, dory and aspis as the other Greeks. OPEN: the Spartan outfit is the classic image; whether Spartans went unarmored in 500 BCE is not sourced here.
- Not modeled: the engine's slots hold one spear and one shield, so no separate cloak, no javelin, no bow, no sling. The sword is attached to the belt piece and stays sheathed. ETR (Etruscan) has no set yet.

Renders (male and female, from `--figure-variants`):

![Athens, Greek hoplite](img/soldiers-ath.png)
![Macedon](img/soldiers-mac.png)
![Rome, early Republic](img/soldiers-rom.png)
![Sparta](img/soldiers-spa.png)

### Common to all Greek cities (HIST)

- **Culture and religion.** One language in many dialects (Attic-Ionic, Doric, Aeolic, Northwest Greek). Homer and Hesiod as shared texts, Panhellenic games (Olympic every four years, Pythian, Isthmian, Nemean), the Delphic oracle of Apollo, the twelve Olympian gods with local patrons, civic animal sacrifice followed by a shared meal, libations, omen reading, hero cults, the Eleusinian and other mysteries. Priesthoods are civic offices. Citizenship is by birth. The symposium, the gymnasium and the agora are the social centers.
- **Doctrine of war.** The citizen-hoplite phalanx: spearmen in a line 8 or more ranks deep, overlapping shields, pushing until one side breaks; one decisive battle on a flat plain in the campaign season; truces for festivals. Light troops (psiloi) and cavalry are minor. Sieges are rare and slow. At sea: penteconters and the new triremes.
- **Equipment.** Bronze helmet (Corinthian, Chalcidian or Attic), bronze or linen cuirass, greaves, round shield (aspis, about 90 cm), thrusting spear (doru, about 2.4 m) with a butt-spike, short iron sword (xiphos) or curved kopis. In the game all eleven currently wear the generic soldier; no Greek gear pieces exist yet.

---

### ATH: Athens

**Rulers (HIST).** No king. In 500 the state is a few years into the reforms of **Cleisthenes** (508 to 507): ten tribes made of local districts (demes), a Council of 500 chosen by lot, the Assembly (ekklesia), and nine yearly archons with ten generals (strategoi). The tyrant Hippias was driven out in 510 and now lives at the Persian court at Sardis. Rising names: Miltiades (Chersonese), Themistocles (born c. 524), Aristides, Xanthippus. Athens sent ships to help Ionia in 498 (after the game start).

**Internal politics and factions (HIST).** The old landed families (Eupatridai) and the supporters of Isagoras (the rival of Cleisthenes, backed by Sparta) against the new demos; the Alcmaeonid clan, which led the reform; the Peisistratid party that wants Hippias back; the poor (thetes) who row the fleet; metics and slaves with no vote. In the game these map onto democratic (demos), neutrality (old families), fascist (hoplite and returning-tyrant militarists) and communist (the landless and debtors). Deferred.

**Culture and traditions (HIST).** Panathenaic festival and games; Dionysia (tragedy since c. 534); black-figure and new red-figure pottery; the silver of Laurion; the owl coinage; Athena as patron; the oath of the ephebes (later).

**Religion and ritual (HIST).** Athena Polias on the Acropolis; the Eleusinian Mysteries; Dionysus; Apollo Patroos; the Anthesteria; the Thesmophoria (women only); ostracism is not yet used (first use 487).

**Doctrine of war (HIST).** Hoplites by tribe under a polemarch and generals; strong at home, weak at sea until the Laurion fleet of 483. In the game: defensive profile, the Great King is the main enemy.

**Equipment.** See the shared list. Cavalry are rich citizens (hippeis).

### SPA: Sparta

**Rulers (HIST).** Two hereditary kings: **Cleomenes I** (Agiad, r. c. 519 to 490) and **Demaratos** (Eurypontid, r. c. 515 to 491). Five ephors elected yearly check the kings; the council of elders (gerousia, 28 men over 60 plus the kings); the assembly of Spartiates (apella).

**Internal politics and factions (HIST).** The two royal houses quarrel (Cleomenes and Demaratos famously fall out); the ephors against the kings; about 8,000 full citizens (Spartiates) ruling several times more perioikoi (free non-citizens) and a mass of **helots**, the enslaved farmers of Laconia and Messenia who outnumber them and may revolt. In the game: a large fascist bloc (40%, warrior nationalists) beside a ruling king-and-elders bloc, and 45% slaves.

**Culture and traditions (HIST).** The agoge (state upbringing from age 7), common messes (syssitia), the krypteia (secret police against helots), iron money, limited wealth display, short speech (laconic), choral festivals (Gymnopaidiai, Hyacinthia, Carneia), strong women's role in property.

**Religion and ritual (HIST).** Apollo (Carneia festival, during which Sparta will not march), the Dioscuri (kings carry their images), Artemis Orthia (the endurance rite), Zeus and Athena; kings serve as priests and keep Delphic oracles; the king's funeral is a state-wide rite.

**Doctrine of war (HIST).** The best infantry in Greece: full-time Spartiate hoplites in a red cloak, long hair, advancing in step to the pipes; the 300 hippeis guard; perioikoi fight alongside; helots as servants. Leader of the **Peloponnesian League**: allies send contingents and follow Sparta in war. Reluctant to fight far from home (fear of helot revolt).

**Equipment.** Red cloak (phoinikis), bronze helmet and shield with the lambda (the lambda comes later), spear and short sword; no body armor in many accounts.

### COR: Corinth

**Rulers (HIST).** An oligarchy after the tyranny of the Cypselids ended in 583 (and an earlier clan, the Bacchiadai, before). Annual magistrates and a council of the rich. **Periander** is dead (c. 585). Corinth leads in the Peloponnesian League with Sparta and acted in 506 to stop Sparta from restoring Hippias in Athens (HIST).

**Internal politics and factions (HIST).** Old aristocrats against merchants and craftsmen (the banausoi); a moderate oligarchy with no tyrant; trade interests against war.

**Culture and traditions (HIST).** Wealth from the isthmus and its two harbors (Lechaion and Kenchreai), the diolkos (portage road), pottery (Corinthian ware, in decline against Attic), bronze work, the Isthmian Games, colonies at Corcyra, Syracuse, Ambracia and Leucas. Famous for temples (Apollo) and for sacred prostitutes of Aphrodite (reported, disputed).

**Religion and ritual (HIST).** Aphrodite on Acrocorinth, Poseidon (Isthmian Games), Apollo, Helios.

**Doctrine of war (HIST).** Good hoplites and the best naval builders of the old world (the trireme was reportedly first built here, c. 700). In the game its state also holds the Ionian Islands (colony sphere).

**Equipment.** The "Corinthian" helmet is named for the city.

### ARG: Argos

**Rulers (HIST).** The old kingship has faded into a magistrate-led state; the king title survives as a religious office. In 494 Argos is crushed by Cleomenes at Sepeia (after the game start), losing most of its citizens.

**Internal politics and factions (HIST).** A citizen assembly with a powerful aristocracy; the deep enmity with Sparta over the border plain (Thyreatis); strong ties with Persia later.

**Culture and traditions (HIST).** The Heraion (temple of Hera) and its festival, Argive shields (the round shield is said to be an Argive invention), bronze work, claims of descent from Heracles and Perseus.

**Religion and ritual (HIST).** Hera Argeia (festival with a procession from the city to the Heraion), Apollo Lykeios, Zeus Larissaios.

**Doctrine of war (HIST).** Hoplites; a thousand-man picked corps (the "thousand" of Argos, later). Spartan rival; at risk until the loss at Sepeia.

### THB: Thebes (Boeotia and Phocis)

**Rulers (HIST).** Thebes leads the **Boeotian League** (federation of cities with 11 elected boeotarchs) under an oligarchy; rival of Athens and of Plataea (which leaned to Athens in 519 and 509). Phocis (Delphi's neighbors) is a separate federation, in the same province in this game.

**Internal politics and factions (HIST).** The oligarchs against the smaller Boeotian towns that resist Theban leadership; a pro-Persian party grows later (Medism).

**Culture and traditions (HIST).** Poets (Pindar, born c. 518; Corinna), the sacred precinct of the Ismenion, the festival of Daphnephoria; deep ties with Delphi in the Phocis part.

**Religion and ritual (HIST).** Dionysus and Heracles (born in Thebes by myth), Apollo Ismenios, Demeter. Delphi is in the neighboring region: the oracle and the Pythian Games.

**Doctrine of war (HIST).** Strong heavy infantry in deep formations (25 ranks later) and an elite pairing, the Sacred Band (founded 378, so NOT at 500); good cavalry on the Boeotian plain.

### THS: Thessaly

**Rulers (HIST).** A confederation of cities (Larissa, Pharsalus, Pherae, Crannon) with a chosen overall leader (tagos). The great clans, such as the Aleuadae of Larissa, hold power and later side with Persia.

**Internal politics and factions (HIST).** Wealthy horse-breeding aristocrats against a dependent peasantry (the penestai, serfs); the clans against each other; the Thessalian League against Phocis.

**Culture and traditions (HIST).** Horses and cavalry, large estates, the Thessalian witches of legend, Mount Olympus and Mount Pelion nearby. The Amphictyonic Council of Delphi counts Thessaly as its first member.

**Religion and ritual (HIST).** Zeus (Olympus), Poseidon Petraios, Athena Itonia, the centaur myths.

**Doctrine of war (HIST).** The best cavalry in Greece; weak infantry; the cavalry are used as a shock force and the league can field several thousand horse. In the game: 1 infantry and 1 cavalry division.

### CRT: Crete

**Rulers (HIST).** Many independent cities (Knossos, Gortyn, Lyttos, Kydonia, Dreros) run by aristocrats called kosmoi (ten per city). The Gortyn law code (inscribed c. 450) shows later rules.

**Internal politics and factions (HIST).** Competition between cities; aristocratic common meals (andreia); serfs (klarotai) and slaves of Cretan estates.

**Culture and traditions (HIST).** Ancestors of the Minoans; Dorian customs of age-class training, mercenary service abroad, archery and the Cretan cult of the Dictaean cave.

**Religion and ritual (HIST).** Zeus born in the Dictaean or Idaean cave, Rhea, Britomartis, Apollo Delphinios, Minoan survivals.

**Doctrine of war (HIST).** Famous archers and mercenaries; pirate raids by sea; infantry less central than in the mainland.

### NAX: Naxos and the Cyclades (with Lemnos and the north Aegean)

**Rulers (HIST).** Naxos is the richest and strongest island; its tyrant **Lygdamis** (c. 540 to 524) was overthrown, and in 500 an oligarchy or a moderate regime rules. In 499 the Naxians fight off a Persian-backed siege led by Aristagoras of Miletus and Artaphernes (after the game start). The other islands (Paros, Andros, Delos, Keos) are small and hold local rule.

**Internal politics and factions (HIST).** Aristocrats and the demos fought on Naxos before the siege; exiles ask the Persians for help (which prompts the Ionian Revolt). The game's democratic ruling party reflects Naxos's popular government.

**Culture and traditions (HIST).** Marble quarries (Naxos, Paros), huge early statues (the Naxian kouros and the Lions of Delos), wine and Dionysus cult, the sanctuary of Apollo on Delos, shared by the Ionians. Lemnos (north Aegean) was taken by Persia around 510 (a Persian general) and then by Miltiades of Athens (after 500). The state holds it here as a placeholder.

**Religion and ritual (HIST).** Dionysus (Naxos), Apollo and Artemis (Delos), the Delian festival.

**Doctrine of war (HIST).** Naxos could field 8,000 hoplites and a fleet of ships (Herodotus 5.30, a number often doubted); sea power and fortified harbors.

### CRO: Croton and Italiote Greeks (Magna Graecia)

**Rulers (HIST).** Croton is the leader of the Achaean cities of southern Italy; a council of one thousand drawn from the old aristocracy governed Croton, reportedly shaped by the Pythagorean brotherhood. In 510 Croton destroyed neighboring **Sybaris** (richest city in Italy). Others: Tarentum (Spartan colony), Locri, Rhegium, Metapontum, Elea, Cumae (north).

**Internal politics and factions (HIST).** Pythagoreans (aristocrats, philosophers, an intellectual and political club) against democrats; Croton against rivals; the fall of Sybaris leaves a power vacuum and refugees.

**Culture and traditions (HIST).** Pythagoras (the school at Croton, c. 530 to 500), Milo the wrestler (many Olympic wins), doctors of Croton, athletes, the cult of Hera Lacinia, the first western coinage (incuse silver).

**Religion and ritual (HIST).** Hera Lacinia (Cape Lacinium), Apollo, Orphic and Pythagorean beliefs (reincarnation, vegetarian customs, secret teachings).

**Doctrine of war (HIST).** Hoplite armies of citizens; large numbers claimed in the Sybaris war (100,000 men, a number which is likely exaggerated); cavalry on the plains; wary of Etruscans, Lucanians and Carthaginians. Tarentum and Locri are inside this one country in the game.

### SYR: Syracuse and Eastern Sicily

**Rulers (HIST).** Syracuse is ruled by the landed oligarchy of the **Gamoroi**, until Gelon (tyrant of Gela, then of Syracuse in 485). In 500 Hippocrates of Gela (r. c. 498 to 491) is only starting to rise. The east coast cities include Catana, Naxos and Leontini, Megara Hyblaea.

**Internal politics and factions (HIST).** Gamoroi against the native serfs (the Killyrioi) and the common people (the demos); Greek cities against each other and against native Sicels and Carthage in the west.

**Culture and traditions (HIST).** Grain wealth, the Fountain of Arethusa, the earliest Greek theater in Sicily, poets (Epicharmus), fine coinage later, great temples (the Temple of Apollo).

**Religion and ritual (HIST).** Apollo Archegetes, Artemis, Demeter and Persephone (the Sicilian cult of Kore), Zeus Olympios.

**Doctrine of war (HIST).** Strong cavalry (grain and horses) and hoplites; later the greatest fleets in the west. Rival of Carthage (Himera, 480).

### PNT: Pontic Greek Cities (Black Sea coast)

**Rulers (HIST).** Small colonies on the western Black Sea coast (Apollonia Pontica, Mesembria, Odessos, Istros, later Tomis), most founded from Miletus. Each is a polis with its own magistrates. After Darius's Scythian campaign (c. 513) the Thracian coast is under Persian control, and the colonies are under Persian rule.

**Internal politics and factions (HIST).** Colonists against local Thracian and Scythian neighbors who trade and raid; the pull of Miletus (itself soon in revolt).

**Culture and traditions (HIST).** Grain, fish, slaves and hides exported to Greece, mixed Greek and Thracian settlements, Ionic dialect.

**Religion and ritual (HIST).** Apollo (healer, of Apollonia), Achilles Pontarches, Zeus, local Thracian gods.

**Doctrine of war (HIST).** Small citizen militias behind city walls; mercenary and allied native help; dependence on a sea link.

### Open points for the Greek split

- **OPEN**: the ATH to NAX friendship, SPA to COR alliance and the MAC to THS alliance are the starting values; only SPA and COR is firmly historical.
- **OPEN**: no separate country for Euboea (Chalcis, Eretria), Aegina, Plataea, Elis, Megara, Phocis or Aetolia. Add by splitting provinces when the map gets finer locations.
- **OPEN**: Magna Graecia (Croton, Tarentum, Locri, Rhegium) is a single country; split it if one country per city is wanted.
- **OPEN**: Greek gear pieces (helmet, cuirass, shield, spear) are not authored; all 11 wear the generic soldier.
- **Date notes**: Sepeia (494), the Ionian Revolt (499) and the Naxos siege (499) happen right after the 500 start; facts marked "after the game start" are not true at day one.

---

## MAC: Macedon

### In the game files (GAME)

- Color 200/80/60. Capital land_05247 ("Macedon and Epirus"). Culture `ancient_macedonian`, religion `macedonian_religion`.
- **1 state**, 179,496 people. All starting pops use the distinct `ancient_macedonian` / `macedonian_religion` identifiers. Classes: farmers 161,549, nobles 7,179, artisans 7,179, clergy 3,589. No slaves.
- Politics: ruling party neutrality. Popularity neutrality 42, **fascist 38**, democratic 12, communist 8. Law: restricted_parties.
- AI profile "macedon" (parent: aggressive). "A rising kingdom with a warrior nobility." Rival ACH 1. Wants THS 2, awakening in -490.
- Army: 2 infantry divisions. Fleet: 2 destroyers, 1 light cruiser, 1 transport (anachronistic).
- Allied with THS (design choice). The AI profile of ACH lists MAC as a target (want 2, from -498).
- The MAC soldier meshes still use the existing `gear_*_mac.glb` asset family; this is a visual placeholder, not a culture assignment.
- Flag: `gfx/flags/MAC.png`, a red `#fb3100` field with a centered gold `#ffd21a` sixteen-rayed Vergina Sun based on the user's red/gold reference. The [Vergina Sun](https://en.wikipedia.org/wiki/Vergina_Sun) page records the sixteen-ray motif and its association with Macedonian royal archaeology; the asset is a scenario emblem, not a claim that the symbol was a modern national flag in 500 BCE.
- Named characters: `mac_amyntas_i` is the starting ruler, `mac_alexander_i` is the starting heir, and `mac_gygaea` is a named daughter of Amyntas. The country definition explicitly uses `primogeniture` and an Argead name pool for future generated characters.

### Ruler and succession (GAME / HIST)

**GAME:** The 500 BCE start uses Amyntas I as ruler and Alexander I as heir, linked by `father = mac_amyntas_i`; Gygaea is also linked as Amyntas's daughter. The law is `primogeniture`, so the explicit heir and the historical father-child relationship agree with the intended succession path. The character ages are scenario approximations, not asserted birth dates.

**HIST:** Amyntas I is the appropriate ruler for the 500 BCE start: sources place his reign roughly in the late sixth century BCE, ending around 498/497 BCE. Alexander I succeeds him and rules roughly to 454 BCE. Amyntas's attested children include Alexander and Gygaea; their mother's name is not securely established here. The Argead dynasty and Aegae/Vergina are retained as the royal-house context. Use [Amyntas I of Macedon](https://en.wikipedia.org/wiki/Amyntas_I_of_Macedon), [Alexander I of Macedon](https://en.wikipedia.org/wiki/Alexander_I_of_Macedon), and [Herodotus, Book 5](https://www.perseus.tufts.edu/hopper/text?doc=Perseus%3Atext%3A1999.01.0126%3Abook%3D5) for the ruler sequence and Persian-alignment evidence; exact early dates remain approximate.

The requested [Portal: North Macedonia](https://en.wikipedia.org/wiki/Portal:North_Macedonia) is useful for modern geographic and cultural-reference navigation, but it is not used to project modern identity categories backward into the 500 BCE character data. Ancient ruler chronology and relationships are kept separate from that modern portal context.

### Internal politics and factions (HIST)

- **King and nobles.** The kingship is hereditary within the royal house, but the king depends on a warrior nobility, the Companions (hetairoi), who hunt and feast with him. This is the "neutrality" bloc and, in game terms, a very strong "fascist" warrior bloc (38%).
- **Upper Macedonian princes** (Lyncestis, Elimeia, Orestis) are semi-independent and a recurring source of challenge.
- **Greek cities on the coast** and Illyrian, Thracian and Paeonian neighbors press the borders.
- Succession by force or intrigue is common (HIST). There is no citizen assembly in the Athenian sense; the army-in-assembly acclaims kings.

### Culture and traditions (HIST / OPEN)

Macedon's court, royal house and warrior aristocracy formed a regional political culture with its own institutions, elite customs and traditions: hunting, horse breeding, banquets, royal burial in tumulus tombs and the public authority of the king. A young Macedonian noble's boar-hunt initiation is attested in later evidence and should remain **OPEN** for exact dating. Ancient source terminology overlaps with the wider Aegean world, but that overlap is not a conclusive identity label.

**OPEN/HIST — language and identity.** The status and classification of Macedonian speech, the extent of contact with neighboring dialects, and the meaning of Argead descent claims are disputed. The game therefore uses `ancient_macedonian` as a scenario identity layer without presenting the data tag as proof of a modern ethnic or nationalist conclusion. Consult [Thucydides on Macedonian geography and rulers](https://www.perseus.tufts.edu/hopper/text?doc=urn%3Acts%3AgreekLit%3Atlg0003.tlg001.1st1K-eng1%3A2.99.6), [Herodotus, Book 8](https://www.perseus.tufts.edu/hopper/text?doc=Perseus%3Atext%3A1999.01.0126%3Abook%3D8) and the modern overview [Macedonians and Greeks](https://onlinelibrary.wiley.com/doi/abs/10.1002/9781444327519.ch5); these sources should be read as evidence with competing interpretations, not as a single settled verdict.

### Religion and ritual (HIST / OPEN)

The Macedonian religious layer includes Zeus, Heracles as a royal ancestor, Dionysus and the Muses at Dion, alongside older mountain cults and royal/army sacrifice. Shared divine names and regional ritual forms show contact; they do not by themselves decide whether Macedonian religious practice should be classified as part of a wider tradition. The `macedonian_religion` identifier records this scenario distinction while leaving the historical classification **OPEN**. For institutional context, see the [Institutions of the Macedonian Kingdom](https://dev.eie.gr/en/institutes/ihr/research/sections/sgra/research/clusters/politics-institutions-and-power/political-structures-in-the-ancient-greco-roman-world/institutions-of-the-macedonian-kingdom/).

### Doctrine of war (HIST)

At 500 BCE Macedon has no famous phalanx (the sarissa and the Macedonian phalanx come with Philip II, 359 and later). Expect: noble heavy cavalry (the Companions) as the king's striking force, light infantry from the peasantry, hoplites only by imitation of the south, plus defense in rugged country. Raids against Illyrians and Thracians; as a Persian vassal, contingents may serve in the Great King's armies.

### Equipment (HIST)

Cavalry: helmet, cloak (chlamys), the broad-brimmed hat (kausia), spear and sword. Infantry: peasant levy with javelins, spears, wicker or leather shields; the best-off have bronze helmets and cuirasses.

### OPEN

- **HIST/OPEN:** Some ancient sources place Macedon within Persian influence or tributary relations around this period. The game does NOT list MAC as a subject of ACH; it is allied with THS.
- **GAME:** Macedon is represented separately from the Greek city-states with `ancient_macedonian` culture and `macedonian_religion`. **OPEN/HIST:** the language and identity evidence remains contested; the scenario label is not a nationalist conclusion.

### Macedonian historical arc beyond the 500 BCE start (HIST / OPEN)

This scenario begins before Macedon's later imperial phase. The following milestones are background for future events and characters, not current start-state data:

- **Aegae and Pella:** Aegae remained the royal ancestral centre while Pella developed into the major administrative and court city. Archaeology records Pella as a Macedonian capital with a palace complex, civic planning and regional economic connections; the [Pella archaeological overview](https://culturalegnatia.culture.gov.gr/en/route-stops/pella/) is a useful starting reference.
- **Philip II (359–336 BCE):** Philip consolidated the kingdom, brought the upper regions into a stronger royal framework, reorganized the army and expanded Macedonian diplomacy. His reforms created the conditions for the later imperial campaigns; the exact balance between older institutions and royal centralization remains **OPEN**.
- **Alexander III (336–323 BCE):** Alexander inherited Philip's kingdom and conquered the Achaemenid imperial core before his death in Babylon. This was an expansion of a Macedonian monarchy and army into a wider empire; it should not be used to collapse every subject population into a single culture.
- **Successor and Antigonid rule:** After the successor wars, Antigonid kings restored a Macedonian centre of power in the Aegean. Their kingdom remained a distinct monarchy with its own court, army and diplomacy while competing with neighboring powers.
- **Roman conquest and reorganization:** Macedon was defeated at Pydna in 168 BCE, its monarchy was dismantled, and the territory was reorganized under Roman control in stages. The timing and administrative details belong to the later-history layer, not the 500 BCE starting setup; see the study of [Macedonia after Pydna](https://revistas.usal.es/uno/index.php/0213-2052/en/article/view/32209).

These later periods are represented as **Macedonian history** in the scenario's narrative layer. The culture and religion identifiers are deliberately game-layer distinctions: they preserve Macedon's own political-cultural continuity without claiming that every question about language, cult classification, ancestry or modern identity has one uncontested answer.

---

## CAR: Carthage

### In the game files (GAME)

- Color 150/40/90. Capital land_09105 ("Carthage and Tunisia"). Culture punic, religion canaanite_religion.
- 6 states, 1.04M people, all Punic and Canaanite. Classes: farmers 442k, slaves 298k, artisans 218k, nobles 50k, clergy 31k.
- Politics: ruling party **democratic**. Popularity democratic 36, neutrality 30, communist 20, fascist 14. Law: **multi_party**.
- AI profile "carthage" (parent: imperial). "A trading power of ships." Rivals SYR 1.5, CRO 0.5. Imperial archetype favors colonies and navy (colonial 1.6, navy 1.5).
- Army: 2 infantry divisions. Fleet of 4 destroyers, 1 light cruiser, 1 battleship, 2 transports (anachronistic).
- Generic soldier.

### Rulers (HIST)

Carthage is a merchant republic. Around 500 it is led by the **Magonid** family: Mago I (mid 6th century) built the army and the empire; his sons Hasdrubal and Hamilcar (later general at Himera, 480) follow. Offices of **suffete** (two judges or "kings") are annual in later sources.

### Internal politics and factions (HIST)

- **Aristocratic merchant families** (the Magonid house and others) dominate the council. This is the "democratic" ruling label in the game: an assembly-and-council republic, not a modern democracy.
- **Council of Elders** (a senate), a larger council (the Hundred and Four, per Aristotle, date uncertain), a popular assembly with limited power.
- **Military leaders vs. civilian merchants**: generals (rab) are chosen for campaigns and can be tried afterward.
- Libyan subjects, mercenaries and slaves are restive (the "communist" 20%).
- A treaty with Rome (reported for 509) limits Roman trade, and Carthage claims Sardinia, western Sicily and north Africa.

### Culture and traditions (HIST)

Phoenician language and script, the sea as the road. Purple dye, glass, ivory, trade in metals from Spain, tin from the Atlantic. Famous voyages (Hanno, Himilco, around the 5th century). City: a circular war harbor (later) and a walled citadel (Byrsa).

### Religion and ritual (HIST)

Baal Hammon and Tanit (the main pair), Melqart (patron of Tyre, the mother city), Eshmun, Astarte. Sacrifice in sacred precincts called tophets: burial grounds with urns of children's and animal remains; whether children were routinely sacrificed is disputed among scholars. Annual embassy and gifts to Tyre's Melqart.

### Doctrine of war (HIST)

- **Money fights wars.** Citizens serve mainly as officers and the guard; armies are mercenary and levied: Libyans, Numidian cavalry, Iberians, Gauls, Balearic slingers, Campanians later.
- **Sacred Band** of citizen heavy infantry, picked youth.
- **Naval supremacy** in the western Mediterranean (pentekonters and triremes); blockade and trade-route control.
- Later: elephants and chariots (chariots are attested in Sicily).

### Equipment (HIST)

Citizen infantry: bronze helmet, linen cuirass, large round shield, long spear. Libyans: spear and large shield. Iberians: curved falcata sword, javelins. Numidian cavalry: javelins, no saddle. Slingers: leather slings, pouches of stones.

### OPEN

- Start state ownership is only 6 states for a navy-heavy power; no overseas colonies (Sardinia, Sicily) are owned.

---

## NUB: Kush

### In the game files (GAME)

- Color 60/150/90. Capital land_08279 ("Nubia and Napata"). Culture kushite, religion kushite_religion.
- 3 states, 633,812 people, all Kushite. Classes: farmers 469k, slaves 76k, artisans 51k, nobles 19k, clergy 19k.
- Politics: ruling party neutrality. Popularity neutrality 40, **communist 28**, fascist 20, democratic 12. Law: restricted_parties.
- No AI profile of its own (uses its ideology and rank archetype). ACH wants NUB (1.5).
- Army: 2 infantry divisions. **No fleet.**
- **Client state of ACH** (pays 10% of income, joins ACH wars).
- Generic soldier.

### Rulers (HIST)

The Napatan kings of Kush, who ruled Egypt as the 25th dynasty (c. 744 to 656) and then withdrew south. Napata, below the sacred hill of Gebel Barkal, is the capital. Names in the late 6th and early 5th centuries include Amani-nata-Yerike, Karkamani, Amaniastabarqa and Siaspiqa; **the king reigning in 500 BCE is OPEN** because Kushite chronology is uncertain.

### Internal politics and factions (HIST)

- **The king and the priests of Amun.** Kings were chosen from the royal kin, and the priesthood of Amun at Gebel Barkal claimed the god's oracle named the next king (reported on Napatan stelae). The "neutrality" bloc is king and priests.
- **Army and the south.** The cemetery at Meroe starts to compete with Napata around this time, and the royal court gradually shifts south (date OPEN), a plausible source of tension between a Napata faction and a Meroe faction.
- **Commoners and slaves** are the "communist" 28%: the largest shut-out movement of any country except ATH's.

### Culture and traditions (HIST)

A deliberately Egyptianizing court (hieroglyphic inscriptions, Amun worship, pyramid tombs at Nuri and El-Kurru), with local traits: royal women of high rank (the kandake title is later), cattle wealth, gold from the Nubian desert, iron and ebony. Egypt is Persian-ruled after Cambyses (525).

### Religion and ritual (HIST)

Amun of Napata as chief god, plus Egyptian gods, and local gods such as Apedemak (lion god, later). Temple rites at Gebel Barkal, offerings, oracles, royal tombs with offerings and pyramid chapels.

### Doctrine of war (HIST)

Kush was known as the "Land of the Bow." Mass archery, light infantry, chariots in the earlier phase; defense in depth up the Nile. Herodotus (3.17 to 25) reports Cambyses's failed campaign against the "Ethiopians" (c. 525).

### Equipment (HIST)

Bows (long, with arrows), spears, shields of hide, linen kilts and sashes, leopard skins and feathers for rank, plumed headgear for officers.

### OPEN

- The game makes Kush a Persian client state at the start. History does not clearly say Kush was subject to Persia in 500 BCE.
- No ruler named; no fleet.

---

## SAB: South Arabia

### In the game files (GAME)

- Color 210/170/70. Capital land_09216 ("South Arabia 1"). Culture south_arabian, religion south_arabian_polytheism.
- 6 states, 1,583,519 people, all South Arabian. Classes: farmers 1.17M, slaves 190k, artisans 127k, nobles 47.5k, clergy 47.5k.
- Politics: ruling party neutrality. Popularity neutrality 44, fascist 26, communist 18, democratic 12. Law: restricted_parties.
- No AI profile of its own.
- Army: 2 infantry divisions. Fleet at land_03388 (anachronistic classes).
- **Tributary of ACH** (pays 25% of income, does not join ACH wars, liberty desire 10).
- Generic soldier.

### Rulers (HIST)

The Sabaean state (capital Marib) is led by a **mukarrib** ("unifier", priest-king) who is over the other tribal chiefs. Names and dates of the rulers around 500 are OPEN (Sabaean chronology varies by several centuries between schools). The neighbor kingdoms are Ma'in, Qataban, Hadramawt and Awsan.

### Internal politics and factions (HIST)

- **Mukarrib and tribal chiefs**: tribal confederation under a priest-king. The "neutrality" bloc is the dynasty and the temple.
- **Merchant clans** controlling the incense road. The "fascist" 26% is the warrior tribal faction (raiders and caravan guards).
- **Rival kingdoms** vie for the trade; city-states controlled by tribal assemblies.

### Culture and traditions (HIST)

Incense (frankincense and myrrh) trade north along the caravan road to the Mediterranean. Irrigation: the great dam at Marib and canals made agriculture possible. Monumental inscriptions in the South Arabian script (musnad), large temples and carved stone work. Herodotus (3.97) says the Arabs sent Darius a yearly gift of 1,000 talents of frankincense, though this is about Arabians generally, and the Persians did not conquer South Arabia.

### Religion and ritual (HIST)

Almaqah (chief god of Saba, moon-related), Athtar (Venus star), Haubas and others; temples such as Awam (the "Mahram Bilqis") and Sirwah; pilgrimage and collective hunting rites, ritual hunting festivals recorded in inscriptions, and dedications of statues and inscribed offerings.

### Doctrine of war (HIST)

Tribal levies, defended oases and mountain strongholds, caravan-raiding warfare, walled cities with towers. Archery, spear and camel mounted skirmishing (the Arabian camel is domesticated already).

### Equipment (HIST)

Wrapped kilts or loincloths, daggers on belts, bows and spears, small shields. Ornament in metal.

### OPEN

- The Persian tributary status is a game design choice, not an established historical fact for 500 BCE.
- Capital location named only "South Arabia 1".

---

## OLM: Olmec

### In the game files (GAME)

- Color 80/170/160. Capital land_15261 ("Olmec Gulf Coast"). Culture **maya**, religion mesoamerican_religion.
- 9 states, 2,217,462 people. Cultures: Maya 1.28M, Maize Farming Peoples 383k, Olmec 317k, Zapotec 234k. Religion all mesoamerican_religion.
- Classes: farmers 1.91M, artisans 111k, nobles 83k, slaves 66k, clergy 50k.
- Politics: ruling party neutrality. Popularity neutrality 38, **fascist 34**, communist 16, democratic 12. Law: restricted_parties.
- No AI profile of its own.
- Army: 3 infantry divisions. Fleet at land_12632.
- **Allied with CHV.** Own soldier set (see Equipment).

### Rulers (HIST)

There are no known named rulers for the Olmec in 500 BCE. The great Olmec centers are San Lorenzo (declined c. 900) and La Venta (declined and was abandoned c. 400). The "colossal heads" are portraits of rulers, but the individuals are unknown by name. The successor traditions are Zapotec (Monte Albán, founded c. 500), Isthmian and early Maya.

### Internal politics and factions (HIST, mostly inferred)

- A **chiefly or kingly elite** with priest-rulers: the "neutrality" bloc.
- **Warrior elites**: iconography of captives and rulers with weapons suggests warfare (the "fascist" 34%).
- **Farmers** in river valleys; communal obligations. Details are speculative.

### Culture and traditions (HIST)

Monumental stone sculpture (colossal heads, thrones or altars, stelae), jade and serpentine offerings, rubber and the ball game, a calendar and writing system in early forms (the Cascajal block is disputed), maize agriculture, trade in obsidian and jade across Mesoamerica.

### Religion and ritual (HIST)

A "were-jaguar" motif and other composite creatures, a maize god, a rain or earth deity, bloodletting and offerings buried in ceremonial centers (the La Venta offering caches), cosmological layouts of plazas and pyramids.

### Doctrine of war (HIST/OPEN)

Little is known. Warfare is inferred from art and fortified sites. Do not invent a doctrine without a source. Treat as OPEN.

### Equipment (GAME and HIST/OPEN)

![Olmec soldiers in the game](img/olmec-soldiers-500bce.png)

Sources: Hassig (via a forum summary, not read directly) says organized warfare in Mesoamerica begins with the Olmec, that they may have used bow and spear, had no defensive armor and were defensive more than expansionist (trade routes). Lechtman 1985 (`docs/SOURCES/technology/pre-columbian-mesoamerica/`) covers the **Andes**, not Mesoamerica; it is used here by analogy only (HIST/OPEN). Its points: weapons that work by impact (clubs, slings, spears with fire-hardened wood tips) gained little from metal; soldiers wore quilted cotton tunics, quilted or wooden helmets, round slat-and-cotton shields, and wrapped cloth on the arm; cloth carried rank and magical force. Metal weapons and armor are therefore NOT used for the Olmec, which fits Mesoamerica, where metallurgy comes much later (HIST, not looked up).

Worn in the game (GAME, `gear_*_olm.glb`, built by `tools/assets/models/gear_*_olmec.py`):
- Tunic: sleeveless, hip-length quilted cotton in undyed cream, quilting bands in two tones.
- Breechcloth (maxtlatl): front and back flaps in red and cream, on a red woven waist sash. Bare legs.
- Sandals: leather sole with an ankle strap and instep strap; the foot is bare.
- Cap: padded quilted cotton dome with a red headband, a jade bead on the brow and jade ear spools (jade is the Olmec signature material).
- Spear: hardwood shaft with a cord grip, a lashed point and an obsidian blade. Carried upright, with the two spear attacks (thrust and deep lunge) picked at random in battle.
- Shield: round, hardwood slats bound with cotton cord, cotton-wrapped rim, carried on the left forearm.
- No firearm, no metal.

OPEN: the colors, the quilting, the shield and the obsidian blade are inferred, not Olmec finds. The macuahuitl (wooden sword set with obsidian chips) is a Mesoamerican weapon whose date I did not look up, and is not modeled; the engine's gear slots hold one spear and one shield. The bow is not modeled. **Conflict**: the Hassig summary says the Olmec had no defensive armor, so the quilted tunic and cap come from the Lechtman analogy and the general Mesoamerican trait list (cotton-pad armor), not from Olmec evidence. Kept as a design choice; decide whether to strip them.

### OPEN

- **Culture mismatch**: the country's culture is `maya`, while its capital state is "Olmec Gulf Coast" and `olmec` is a separate culture in the files.
- Chronology: Olmec centers were largely in decline by 500 BCE.

---

## CHV: Chavin

### In the game files (GAME)

- Color 190/120/70. Capital land_11805 ("Chavin Peru 2"). Culture chavin, religion andean_religion.
- 5 states, 1,361,269 people. Cultures: Chavin 619k, Chorrera 572k, Paracas 170k. All andean_religion.
- Classes: farmers 1.01M, slaves 163k, artisans 109k, nobles 41k, clergy 41k.
- Politics: ruling party neutrality. Popularity neutrality 41, **communist 27**, fascist 22, democratic 10. Law: restricted_parties.
- No AI profile of its own (it was removed from the "china" profile in the Greek split, where it was a copy mistake).
- Army: 2 infantry divisions. Fleet at land_12675.
- **Allied with OLM.** Generic soldier.

### Rulers (HIST/OPEN)

No named rulers are known. Chavín de Huántar (Andean highlands) was a religious center whose influence peaked c. 900 to 500 and declined after. Leadership is believed to have been a priestly elite.

### Internal politics and factions (HIST, inferred)

- A **priesthood-led elite** controlling pilgrimage, ritual knowledge and trade: the "neutrality" bloc.
- **Commoner farmers and herders** (llama and alpaca) obliged to labor and tribute: the large "communist" 27%.
- **Regional centers** of the coast (Paracas, Chorrera in Ecuador) with their own traditions.

### Culture and traditions (HIST)

Stone temple complexes with underground galleries, carved stone heads, the Lanzón stela; textiles (Paracas), early gold and metalwork, terraced agriculture, llama caravans, potatoes and maize.

### Religion and ritual (HIST)

Composite feline, bird and serpent beings, the Staff God, ritual use of the San Pedro cactus and other psychoactive plants, rites in dark galleries with acoustic effects, pilgrimage to the oracle.

### Doctrine of war (HIST/OPEN)

Little firm evidence of organized war for the Chavín horizon. Treat as OPEN.

### Equipment (HIST/OPEN)

Likely clubs, slings, spears and spear-throwers. Inference only.

### OPEN

- **Fixed**: the "china" AI profile used to list both ZHO and CHV with rivals and wants pointing at each other. CHV and the self-rivalry were removed.
- The OLM and CHV alliance has no historical basis (no contact between Mesoamerica and the Andes is known in this period); it is a game design choice.

---

## IND: Magadha and Ganges

### In the game files (GAME)

- Color 230/140/40. Capital land_03808 ("Magadha"). Culture indo_aryan, religion vedic_brahmanism.
- 66 states, 17.57M people. Cultures: Indo-Aryan 13.05M, Magadhan 4.52M. Religions: Vedic Brahmanism 13.05M, Shramana movements 4.52M.
- Classes: farmers 13.2M, slaves 1.94M, artisans 1.35M, nobles 541k, clergy 513k.
- Politics: ruling party neutrality. Popularity neutrality 40, democratic 24, fascist 20, communist 16. Law: restricted_parties.
- No AI profile of its own; ACH wants IND (1.5).
- Army: 22 infantry divisions. Fleet at land_03726.
- Generic soldier.

### Rulers (HIST)

**Bimbisara** of Magadha (r. c. 544 to 492 by the long chronology; shorter chronologies place him later) of the Haryanka dynasty, capital Rajagriha; he is succeeded by **Ajatashatru** (c. 492 to 460), who takes the throne after Bimbisara's imprisonment or death. Neighbors: Kosala (Prasenajit), Vatsa (Udayana), Avanti (Pradyota), Anga (conquered by Bimbisara), and the **Vajji** confederation (Licchavis of Vaishali). Religious figures: the Buddha (traditionally died c. 483, modern scholarship often says c. 400) and Mahavira (Jain).

### Internal politics and factions (HIST)

- **Monarchy and gana-sangha.** Magadha is a monarchy, while the Vajji (and Shakya, Malla) are tribal republics governed by assemblies of clan heads. The "democratic" 24% (the second highest among the non-Greek powers) fits the republican tradition.
- **Brahmins vs. Kshatriyas vs. the new teachers.** Vedic Brahmin priests (sacrifice and ritual authority) compete with kshatriya warriors for status, and with the shramana (wandering ascetic) movements: Buddhism, Jainism, Ajivika. The game splits the people into Indo-Aryan (Vedic) and Magadhan (Shramana).
- **Family conflict**: Ajatashatru's rise over his father is a classic dynastic plot.
- **Persian frontier**: Darius's empire took Gandhara and the Indus region (c. 518 to 515), on the northwest edge.

### Culture and traditions (HIST)

Second urbanization in the Ganges plain: cities, coined silver punch-marked money, iron tools and rice farming. Sanskrit and Prakrit languages; Vedic hymns, oral tradition; the four varnas (Brahmin, Kshatriya, Vaishya, Shudra); trade by river.

### Religion and ritual (HIST)

Vedic sacrifices (yajna) with fire, such as the horse sacrifice (ashvamedha) for great kings, offerings to Agni and Indra, Brahmin priests, ascetic practice, early Buddhist and Jain communities with monks, lay supporters and wandering teachers.

### Doctrine of war (HIST)

The **four-limbed army** (chaturanga): infantry, cavalry, chariots, elephants. Elephants for shock and fortress breaking. Fortified cities, rivers as lines, the king as commander. Ajatashatru later uses new engines (a catapult and a war chariot with blades), reported in Jain texts (after 490). Arthashastra-style statecraft is later and cannot be dated here.

### Equipment (HIST)

Cotton clothing (dhoti and turban), long composite and bamboo bows, spears, swords, shields of hide or wicker, little body armor for ordinary infantry; elephants carry archers.

### OPEN

- The country's ruling culture is Indo-Aryan / Vedic, but its core state is Magadha, whose rulers and population are shramana-leaning in the sources. The game split reflects that tension but the capital culture is Vedic.
- Persian influence in the northwest is not shown by the diplomacy file.

---

## ZHO: Zhou

### In the game files (GAME)

- Color 200/50/50. Capital land_01707 (Zhou states, Yellow River). Culture huaxia, religion zhou_ancestor_cult.
- **146 states**, 36.7M people (the largest country). Cultures: Huaxia 28.5M, Chu 4.76M, Wu and Yue 3.46M. Religions: Zhou ancestor cult, Chu shamanism, Baiyue animism.
- Classes: farmers 27.2M, slaves 4.4M, artisans 2.9M, nobles 1.10M, clergy 1.10M.
- Politics: ruling party neutrality. Popularity neutrality 43, fascist 27, communist 18, democratic 12. Law: restricted_parties.
- AI profile "china" (parent: aggressive), no rivals or wants.
- Army: 24 infantry divisions. Fleet at land_02615.
- Generic soldier.

### Rulers (HIST)

The Zhou king (royal house Ji) in 500 is **King Jing of Zhou** (r. 519 to 476), whose court at Luoyi (Chengzhou) has lost real power since the Eastern Zhou began in 770. Real power sits with the great regional states of the Spring and Autumn period: Jin (ruled by six great ministerial families), Qi (house of Jiang, under the Tian clan's rise), Qin (west), Chu (south, claims a royal title), Wu (King Helü, r. 514 to 496, with the general Sunzi of the "Art of War" tradition; Wu defeats Chu in 506), Yue (Yunchang, then Goujian from 496), Lu (Confucius, c. 551 to 479, in office about 500), Song, Zheng, Wei, Yan.

### Internal politics and factions (HIST)

- **The nominal king vs. the hegemons.** The Zhou king is a ritual figurehead; strongest lords act as hegemons (ba). The "neutrality" bloc is the old ritual order; the "fascist" 27% is the militarist states and ministerial usurpers.
- **Ministerial families against dukes**: in Jin, Lu and Qi, great families of ministers seize real power (the Jin partition comes in 453).
- **Philosophers**: Confucius's school (ritual, virtue, hierarchy), later Mohists, Daoists and Legalists (after the period).
- **Commoners and slaves**: peasants and unfree laborers (the "communist" 18%) pressed by war.
- **Peripheral peoples**: Chu, Wu and Yue are treated as semi-barbarian by the central states, and Rong and Di tribes stay in the north and west.

### Culture and traditions (HIST)

Ritual (li) governs rank, funerals and diplomacy; bronze vessels with inscriptions, the Book of Songs and Book of Documents, jade, silk, divination on oracle bones and tortoise shells, written characters, the lunisolar calendar. Walled cities, the well-field idea, iron beginning to be used for tools.

### Religion and ritual (HIST)

**Ancestor veneration**: the king and lords sacrifice to ancestors in clan temples with food and wine in bronze vessels; Heaven (Tian) and the **Mandate of Heaven**; spirits of mountains and rivers; shamans in Chu; cosmic order through ritual music and dance; elaborate burial with objects.

### Doctrine of war (HIST)

- Shift from noble chariot warfare to mass infantry armies levied from the peasantry. Chariots are still the elite arm, with infantry riding around them.
- Honorable-conduct customs of the earlier period (such as not attacking an unformed enemy) are fading; the Art of War tradition values deception, speed, intelligence and avoiding costly sieges.
- Walled cities, big levies, river and lake fleets in Wu, Yue and Chu. Cavalry and crossbow as mass weapons come later (crossbows exist already).

### Equipment (HIST)

Bronze weapons: dagger-axe (ge), halberd (ji), spear, jian sword, composite bow, early crossbow. Armor: lacquered leather or lamellar (leather scales), helmets in bronze or leather, shields of leather or lacquered wood.

### OPEN

- Zhou is a single game country with 146 states, standing for many warring states, while the AI profile gives it a rival within its own profile.
- Wu and Yue are separate cultures inside the Zhou country.

---

## All countries by founding or latest revival (HIST)

Oldest first, as one plain list. Each date is the founding of the national entity, or its most recent revival where one is named. All dates are approximate (BCE), written from memory, and many are tentative or traditional rather than archaeological. Entries with the same date are in no particular order. "In game" means the country exists in `countries.txt`; "proposed" would mean not built yet; all entries are now built (58 countries).

- c. 1200: Olmec (OLM), in game. San Lorenzo, the first Olmec center.
- c. 1046: Qi, in game. Founded as a Zhou fief.
- c. 1033: Jin, in game. Founded as a Zhou fief.
- c. 1000: Libyan and Berber tribes, in game. Tribal peoples of the Maghreb.
- c. 1000: Bantu homeland peoples, in game. Start of the Bantu expansion.
- c. 1000: Nok culture, in game.
- c. 1000: Thracian tribes, in game. The Odrysian kingdom comes later, c. 480.
- c. 1000: Ionian Greek cities, in game (only if they are not already inside ACH). Ionian migration.
- c. 1000: Yaya-Mama peoples of Titicaca, in game.
- c. 900: Chavin (CHV), in game. Chavin de Huantar rises.
- c. 900: Corinth (COR), in game. Dorian city.
- c. 900: Argos (ARG), in game. Dorian city.
- c. 900: Naxos and the Cyclades (NAX), in game. Ionian settlement.
- c. 900: Yayoi and Jomon Japan, in game.
- c. 814: Carthage (CAR), in game. Traditional founding from Tyre.
- c. 800: Sparta (SPA), in game. Dorian settlement and the Lycurgan order.
- c. 800: Etruscan League, in game. Etruscan city-states form from the Villanovan culture.
- c. 800: South Arabia (SAB), in game. Saba and its neighbors.
- c. 800: Crete (CRT), in game. Iron Age city-states.
- c. 800: Hallstatt Gaul and the Celts, in game.
- c. 800: Maya lowlands, in game.
- c. 800: Paracas, in game.
- c. 770: Zhou (ZHO), in game. Eastern Zhou, after the court moved east.
- c. 770: Qin, in game. Recognized as a state.
- c. 753: Rome and Latium, in game. Traditional founding; the Republic dates from 509.
- c. 750: Kush (NUB), in game. Unified Napatan kingdom, the 25th dynasty of Egypt.
- c. 750: Scythians, in game. Settle the Pontic steppe.
- c. 733: Syracuse and Eastern Sicily (SYR), in game. Founded from Corinth.
- c. 710: Croton and Italiote Greeks (CRO), in game. Croton founded by Achaeans.
- c. 704: Chu, in game. The ruler takes the title of king.
- c. 700: Athens (ATH), in game. Attica united; democracy from 508.
- c. 700: Thebes and the Boeotian League (THB), in game.
- c. 700: Macedon (MAC), in game. The Argead kingdom.
- c. 700: Saka and Massagetae, in game.
- c. 700: Kosala and the Ganges plain, in game.
- c. 700: Korea (Gojoseon), in game. Very uncertain.
- c. 700: Ethiopian highlands (D'mt), in game.
- c. 700: Van Lang and the Dong Son culture, in game.
- c. 666: Shu and Ba, in game. The Kaiming dynasty of Shu, traditional date.
- c. 664: Egypt, in game. Saite revival of the 26th dynasty; Persian satrapy since 525.
- c. 650: Pontic Greek Cities (PNT), in game. Milesian colonies such as Istros.
- c. 626: Babylon, in game. Neo-Babylonian revival; Persian since 539.
- c. 600: Thessaly (THS), in game. The Thessalian League.
- c. 600: Iberians, in game.
- c. 600: Celtiberians, in game.
- c. 600: Sarmatians, in game.
- c. 600: Gandhara and the Indus lands, in game. Persian satrapy since about 518.
- c. 600: Deccan (Assaka and neighbors), in game.
- c. 585: Wu, in game. Its ruler takes the title of king.
- c. 550: Achaemenid Empire (ACH), in game. Cyrus; Darius reorganized it in 522.
- c. 544: Magadha and Ganges (IND), in game. The Haryanka dynasty under Bimbisara.
- c. 538: Judah and Yehud, in game. Revived as a Persian province after the Babylonian exile.
- c. 510: Yue, in game. Rises under its new rulers.
- c. 500: Zapotec, in game. Monte Alban founded.
- c. 500: Garamantes, in game. Garama.
- c. 500: Sahel and Mande lands, in game. Very uncertain.
- c. 500: Tamil south, in game. Very uncertain.
- c. 500: Irrawaddy and mainland Southeast Asia, in game. Very uncertain.

## Findings to decide (not changed)

1. **AI profile "china"** was fixed (see CHV).
2. **OLM culture is `maya`**, not `olmec` (a separate culture exists).
3. **Anachronistic fleets and artillery** in every starting file.
4. **Only ACH and OLM have their own gear.** The other countries wear the generic soldier.
5. **NUB (client) and SAB (tributary) of ACH** are design choices; the history for 500 BCE is unclear.
6. **No named leaders exist in the game data.** The `characters` feature is on, but there are no authored characters in this scenario folder.
7. **MAC is a one-state country** and the only one with no slave class.
8. **No country has its own casus belli, events, or traditions.** The only events are election events and a welcome event.

### Added countries (38)

Built by assigning owner and cores on existing states, so each keeps the culture, religion and pop classes already authored for them. Capital, culture and religion come from the capital state. Colors are generated. Armies are about one division per 1.2 million people at the capital. Fleets: none. The Persian-held regions (EGY, BAB, JDH, GAN, ION, THR) start as ACH tributary subjects. ACH, ZHO, IND, OLM and CHV lost states and their starting armies were cut in proportion. OLM culture is now olmec (closes the earlier OPEN culture mismatch). ACH and ZHO capitals moved to a state they still own (land_17558, land_05578). Political parties: ROM democratic; SCY, SAK and SAR fascist-ruled; all others neutrality-ruled with per-country popularity values. OPEN: province granularity, and no historical basis for the OLM and CHV alliance.

| Tag | Country | Pop | Culture | Religion | Subject of |
|---|---|---|---|---|---|
| QIQ | Qi | 6,151,928 | huaxia | zhou_ancestor_cult | - |
| JIN | Jin | 9,736,975 | huaxia | zhou_ancestor_cult | - |
| QIN | Qin | 4,531,948 | huaxia | zhou_ancestor_cult | - |
| CHU | Chu | 4,761,183 | chu | chu_shamanism | - |
| WUU | Wu | 1,751,670 | wu_yue | baiyue_animism | - |
| YUE | Yue | 1,711,693 | wu_yue | baiyue_animism | - |
| SHU | Shu and Ba | 1,814,122 | shu_ba | chu_shamanism | - |
| LIB | Libyan and Berber tribes | 1,919,622 | berber | berber_religion | - |
| BAN | Bantu homeland peoples | 465,276 | bantu | bantu_traditional | - |
| NOK | Nok | 470,400 | nok | west_african_traditional | - |
| THR | Thracian tribes | 475,777 | thracian | thracian_religion | ACH |
| ION | Ionian Greek cities | 44,432 | anatolian | anatolian_polytheism | ACH |
| YAY | Yaya-Mama | 309,270 | yaya_mama | andean_religion | - |
| YAO | Yayoi and Jomon Japan | 386,125 | jomon | jomon_animism | - |
| ETR | Etruscan League | 229,342 | etruscan | etruscan_religion | - |
| CEL | Hallstatt Celts | 1,746,207 | hallstatt_celt | celtic_polytheism | - |
| MAY | Maya lowlands | 1,283,101 | maya | mesoamerican_religion | - |
| PAR | Paracas | 169,901 | paracas | andean_religion | - |
| ROM | Rome and Latium | 84,071 | italic | italic_religion | - |
| SCY | Scythians | 378,778 | scythian | scythian_faith | - |
| SAK | Saka and Massagetae | 567,175 | sakae | scythian_faith | - |
| KOS | Kosala | 1,607,563 | indo_aryan | vedic_brahmanism | - |
| KOR | Gojoseon | 790,592 | korean | korean_shamanism | - |
| DMT | D'mt | 1,722,253 | ethiopian_highland | ethiopian_highland_cult | - |
| VAN | Van Lang | 216,979 | van_lang | austroasiatic_animism | - |
| EGY | Egypt | 3,001,569 | egyptian | egyptian_religion | ACH |
| BAB | Babylon | 4,371,816 | babylonian | mesopotamian_polytheism | ACH |
| IBE | Iberians | 669,770 | iberian | iberian_religion | - |
| CIB | Celtiberians | 679,491 | celtiberian | celtic_polytheism | - |
| SAR | Sarmatians | 361,370 | sarmatian | scythian_faith | - |
| GAN | Gandhara | 1,577,892 | indo_aryan | vedic_brahmanism | ACH |
| DEC | Deccan | 2,196,015 | dravidian | dravidian_folk | - |
| JDH | Judah and Yehud | 1,044,829 | judean | yahwism | ACH |
| ZAP | Zapotec | 234,390 | zapotec | mesoamerican_religion | - |
| GAR | Garamantes | 286,743 | garamantes | saharan_pastoral_faith | - |
| SAH | Sahel and Mande lands | 2,527,983 | west_african | west_african_traditional | - |
| TML | Tamil south | 1,245,693 | dravidian | dravidian_folk | - |
| IRR | Irrawaddy and mainland Southeast Asia | 4,490,485 | austroasiatic | austroasiatic_animism | - |


## EGY equipment (GAME and OPEN)

Worn in the game (`gear_*_egy.glb`, built by `tools/assets/models/gear_spartan_egyptian.py`): linen corselet with shoulder straps and a broad collar in blue, gold and turquoise; pleated linen kilt with a stiff front apron and gold hem; broad belt with a gold plate and a curved khopesh in a sheath on the left hip; striped blue and linen headcloth with gold brow band and short lappets; papyrus sandals; spear with a bronze leaf head; tall arch-topped hide shield with a wooden rim. No firearm.

OPEN: colors, the hide patches and the corselet are design choices, inferred from general Saite-period (Late Period) soldier images and the Herodotus linen corselet tradition; no source was looked up. Greek mercenaries in Egyptian service are not modeled. The sword stays sheathed.

![Egyptian soldiers](img/soldiers-egy.png)
