"""Tribes and polities of the first start date (500 BCE), for tools/import-history.py.

The data of the project gives a few big blocks (a tag that stands for a whole region: the Hallstatt Celts, the Libyan and Berber tribes, Magadha and the Ganges...).
Each block is split among the polities and tribes that lived there: the nearest seed (key, name, lon, lat) takes each state of the block. The first seed of a block keeps
the tag of the block. Names are those of Herodotus and other writers of the time where they are known, else the name of the archaeological culture or of the people.
"""

BLOCK_SEEDS = {
    # ---- Gaul, the Alps and the Hallstatt lands
    'CEL': [('aedui', 'Aedui', 4.3, 46.9), ('arverni', 'Arverni', 3.2, 45.6), ('sequani', 'Sequani', 5.9, 47.1), ('helvetii', 'Helvetii', 7.9, 47.2), ('allobroges', 'Allobroges', 5.7, 45.5),
            ('belgae', 'Belgae', 3.6, 49.7), ('treveri', 'Treveri', 6.7, 49.8), ('armorici', 'Armoricans', -2.8, 48.0), ('carnutes', 'Carnutes', 1.5, 48.2),
            ('pictones', 'Pictones', -0.3, 46.5), ('aquitani', 'Aquitani', -0.6, 43.7), ('volcae', 'Volcae', 3.6, 43.6), ('salluvii', 'Salluvii and Ligurians', 6.3, 43.9),
            ('vindelici', 'Vindelici', 11.0, 48.0), ('boii', 'Boii', 14.0, 49.2), ('norici', 'Norici', 13.7, 47.2), ('raeti', 'Raeti', 10.7, 46.8), ('lingones', 'Lingones', 5.2, 47.9),
            ('senones', 'Senones', 3.2, 48.0)],
    # ---- Iberia
    'IBE': [('turdetani', 'Turdetani', -5.8, 37.4), ('bastetani', 'Bastetani', -2.6, 37.4), ('contestani', 'Contestani', -0.6, 38.6), ('edetani', 'Edetani', -0.4, 40.0),
            ('ilergetes', 'Ilergetes', 0.8, 41.7), ('cosetani', 'Cosetani and Indigetes', 2.4, 41.8), ('oretani', 'Oretani', -3.5, 38.7), ('tartessians', 'Tartessians', -6.8, 37.6)],
    'CIB': [('arevaci', 'Arevaci', -2.4, 41.6), ('vaccaei', 'Vaccaei', -5.0, 41.6), ('lusitani', 'Lusitani', -8.0, 39.7), ('callaeci', 'Callaeci', -8.0, 42.5), ('cantabri', 'Cantabri', -4.3, 43.0),
            ('vascones', 'Vascones', -1.8, 42.8), ('carpetani', 'Carpetani', -3.8, 40.3), ('lusones', 'Lusones and Belli', -1.8, 41.0), ('astures', 'Astures', -6.0, 42.8),
            ('celtici', 'Celtici', -7.8, 38.2), ('conii', 'Cynetes', -7.9, 37.3)],
    # ---- the Balkans, the Danube and the steppe
    'THR': [('odrysae', 'Odrysae', 26.0, 42.0), ('getae', 'Getae', 27.8, 44.3), ('triballi', 'Triballi', 23.5, 43.7), ('dacians', 'Agathyrsi and Dacians', 24.5, 46.0),
            ('dardani', 'Dardani', 21.3, 42.4), ('thynians', 'Bithynians', 30.5, 41.0), ('paeonians', 'Paeonians', 22.4, 41.4), ('moesi', 'Moesi', 22.0, 44.0)],
    'SCY': [('scythians', 'Royal Scythians', 34.0, 47.2), ('alazones', 'Alazones and Scythian plowmen', 30.5, 49.5), ('budini', 'Budini', 40.0, 51.0), ('geloni', 'Geloni', 35.5, 51.2),
            ('neuri', 'Neuri', 27.5, 52.0), ('callipidae', 'Callipidae', 31.0, 46.3), ('melanchlaeni', 'Melanchlaeni', 37.0, 53.0), ('tauri', 'Tauri', 34.0, 44.8)],
    'SAR': [('sauromatae', 'Sauromatae', 47.0, 50.5), ('aorsi', 'Aorsi', 52.0, 46.5), ('siraces', 'Siraces', 42.5, 45.2), ('iazyges', 'Iazyges', 38.5, 47.0), ('thyssagetae', 'Thyssagetae', 52.0, 53.0),
            ('iyrcae', 'Iyrcae', 58.0, 51.0), ('maeotae', 'Maeotae', 38.0, 46.0)],
    'SAK': [('massagetae', 'Massagetae', 58.0, 46.0), ('dahae', 'Dahae', 55.0, 41.0), ('haumavarga', 'Saka Haumavarga', 72.0, 39.5), ('tigraxauda', 'Saka Tigraxauda', 66.0, 43.0),
            ('issedones', 'Issedones', 77.0, 44.0), ('argippaei', 'Argippaei', 66.0, 50.0), ('sogdians', 'Sogdians', 69.0, 41.0), ('khwarezm', 'Chorasmians', 60.0, 42.0)],
    # ---- North Africa and the Sahel
    'LIB': [('nasamones', 'Nasamones', 19.5, 30.5), ('maxyes', 'Maxyes', 10.5, 34.5), ('mauri', 'Mauri', -5.0, 34.5), ('gaetuli', 'Gaetuli', 1.0, 31.5), ('numidae', 'Numidae', 6.5, 36.0),
            ('lotophagi', 'Lotophagi', 10.5, 33.3), ('adyrmachidae', 'Adyrmachidae', 25.0, 31.2), ('libu', 'Libu', 15.0, 31.5), ('asbystae', 'Asbystae', 21.5, 32.0), ('zaueces', 'Zaueces', 8.0, 33.5),
            ('atarantes', 'Atarantes', 2.0, 28.0), ('sahrawi', 'Sahrawi tribes', -10.0, 26.0), ('maurusii', 'Maurusii', -6.5, 31.0)],
    'SAH': [('mande', 'Mande', -6.0, 12.5), ('soninke', 'Soninke of Tichitt', -8.5, 17.5), ('songhai', 'Songhai', 0.5, 15.5), ('tukulor', 'Takrur', -14.0, 15.5), ('wolof', 'Wolof and Serer', -16.0, 14.0),
            ('akan', 'Akan', -1.5, 7.0), ('gur', 'Gur peoples', -2.0, 11.5), ('mossi', 'Mossi', -1.0, 12.5), ('dogon', 'Dogon', -3.0, 14.5), ('kru', 'Kru', -8.0, 6.0),
            ('temne', 'Mel peoples', -12.5, 9.0), ('hausa', 'Hausa', 8.0, 12.0), ('nupe', 'Nupe', 5.0, 9.0), ('yoruba', 'Yoruba', 4.0, 7.5), ('ewe', 'Ewe and Fon', 1.5, 7.0)],
    'NOK': [('nok', 'Nok', 8.0, 10.0), ('igbo', 'Igbo', 7.0, 6.0), ('chadic', 'Sao', 14.5, 12.5), ('tiv', 'Benue peoples', 9.5, 7.5)],
    'BAN': [('bantu', 'Bantu', 11.0, 5.0), ('bantu_e', 'Interlacustrine Bantu', 28.0, -2.0), ('bantu_s', 'Nguni Bantu', 26.0, -8.0)],
    # ---- India and south-east Asia
    'IND': [('magadha', 'Magadha', 85.3, 25.6), ('anga', 'Anga', 87.2, 25.1), ('vanga', 'Vanga', 89.7, 23.2), ('pundra', 'Pundra', 89.0, 25.6), ('kasi', 'Kasi', 83.0, 25.3), ('vajji', 'Vajji', 85.7, 26.2),
            ('malla', 'Malla', 83.8, 26.8), ('vatsa', 'Vatsa', 81.8, 25.4), ('chedi', 'Chedi', 80.0, 24.5), ('panchala', 'Panchala', 79.5, 27.8), ('kuru', 'Kuru', 77.2, 29.0),
            ('surasena', 'Surasena', 77.7, 27.5), ('matsya', 'Matsya', 76.4, 27.2), ('kamarupa', 'Kamarupa', 92.0, 26.2), ('suhma', 'Suhma and Radha', 87.8, 23.2)],
    'KOS': [('kosala', 'Kosala', 82.2, 27.0), ('sakya', 'Sakya', 83.2, 27.6), ('kosala_s', 'Dakshina Kosala', 82.0, 25.8)],
    'DEC': [('assaka', 'Assaka', 78.0, 18.5), ('avanti', 'Avanti', 75.8, 23.2), ('vidarbha', 'Vidarbha', 79.3, 21.2), ('kalinga', 'Kalinga', 84.5, 19.8), ('mulaka', 'Mulaka', 75.5, 19.7),
            ('andhra', 'Andhra', 80.5, 16.5), ('anarta', 'Anarta and Saurashtra', 71.0, 22.0), ('kuntala', 'Kuntala', 76.0, 15.5), ('mahishmati', 'Narmada tribes', 77.5, 22.5)],
    'TML': [('chola', 'Chola', 79.8, 10.9), ('pandya', 'Pandya', 78.1, 9.9), ('chera', 'Chera', 76.4, 10.4), ('ay', 'Ay and Mushika', 77.0, 8.3), ('tuluva', 'Tuluva', 75.0, 13.0)],
    'GAN': [('gandhara', 'Gandhara', 72.0, 34.0), ('madra', 'Madra', 74.5, 32.5), ('sindhu', 'Sindhu-Sauvira', 68.5, 26.5), ('kamboja', 'Kamboja', 71.5, 36.3), ('kekaya', 'Kekaya', 73.5, 32.8)],
    'IRR': [('pyu', 'Pyu', 95.5, 21.5), ('mon', 'Mon', 97.0, 17.0), ('arakan', 'Arakan', 94.0, 20.0), ('dvaravati', 'Lavo and the Chao Phraya tribes', 100.5, 14.5), ('funan', 'Funan', 105.5, 10.3),
            ('chenla', 'Khmer tribes', 103.5, 13.3), ('champa', 'Cham tribes', 108.5, 15.5), ('lanna', 'Tai tribes', 99.5, 19.0), ('shan', 'Shan', 98.0, 23.0), ('khorat', 'Mun', 103.0, 16.0),
            ('malay', 'Malay tribes', 101.0, 5.0), ('laos', 'Lao tribes', 102.5, 19.5)],
    # ---- east Asia
    'ZHO': [('zhou', 'Zhou', 112.4, 34.7), ('zheng', 'Zheng', 113.7, 34.4), ('wey', 'Wey', 114.6, 35.8), ('lu', 'Lu', 117.0, 35.6), ('song', 'Song', 115.6, 34.4), ('chen', 'Chen', 114.9, 33.7),
            ('cai', 'Cai', 114.9, 33.0), ('cao', 'Cao', 115.5, 35.2), ('xu', 'Xu', 118.0, 34.0), ('wu_c', 'Royal Zhou', 112.5, 36.3)],
    'YAO': [('kyushu', 'Kyushu Yayoi', 131.0, 33.0), ('kinki', 'Kinki', 135.5, 34.6), ('kanto', 'Kanto Jomon', 139.5, 36.0), ('chubu', 'Chubu Jomon', 137.5, 36.2), ('chugoku', 'Chugoku', 132.5, 34.8),
            ('shikoku', 'Shikoku', 133.5, 33.7), ('tohoku', 'Tohoku', 140.5, 39.0)],
    'KOR': [('gojoseon', 'Gojoseon', 126.0, 40.0), ('jin_han', 'Jin', 127.5, 36.5), ('mahan', 'Mahan', 126.8, 35.2), ('byeonhan', 'Byeonhan and Jinhan', 129.0, 35.8)],
    # ---- the Americas
    'OLM': [('olmec', 'Olmec of La Venta', -94.0, 18.1), ('tres_zapotes', 'Tres Zapotes', -95.5, 18.4), ('tlatilco', 'Tlatilco farmers', -99.0, 19.4), ('chupicuaro', 'Chupicuaro', -101.0, 20.2),
            ('west_mexico', 'Shaft tomb peoples', -103.5, 20.7), ('guerrero', 'Guerrero', -99.8, 17.5), ('puebla', 'Puebla and Tlaxcala', -98.0, 19.0), ('huasteca', 'Huastec', -98.5, 22.0)],
    'MAY': [('nakbe', 'Mirador Maya', -89.8, 17.8), ('kaminaljuyu', 'Maya of Kaminaljuyu', -90.5, 14.6), ('izapa', 'Izapa', -92.2, 14.9), ('yucatan', 'Yucatan Maya', -89.0, 20.5),
            ('chiapas', 'Chiapas', -92.5, 16.7), ('ulua', 'Ulua', -88.0, 15.5), ('lenca', 'Lenca', -88.5, 14.0), ('pacific_cr', 'Chorotega', -85.5, 10.8)],
    'ZAP': [('zapotec', 'Zapotec of Monte Alban', -96.7, 17.0), ('mixtec', 'Mixtec', -97.8, 17.4), ('oaxaca_coast', 'Chatino', -97.0, 16.0)],
    'CHV': [('chavin', 'Chavin', -77.2, -9.6), ('cupisnique', 'Cupisnique', -79.0, -7.5), ('kotosh', 'Kotosh', -76.0, -9.9), ('chorrera', 'Chorrera', -79.5, -1.5), ('jaen', 'Jaen', -78.0, -5.5),
            ('machalilla', 'Machalilla', -80.5, -1.0), ('narino', 'Narino', -77.5, 1.0)],
    'PAR': [('paracas', 'Paracas', -75.8, -14.0), ('nazca_c', 'Ica and Nazca', -75.0, -14.8)],
    'YAY': [('yaya_mama', 'Yaya-Mama', -69.5, -16.0), ('chiripa', 'Chiripa', -69.0, -16.5), ('wankarani', 'Wankarani', -67.5, -18.0), ('atacama', 'Atacama oases', -69.0, -22.5)],
}

# unowned land: the cultures of the dataset that were farming or herding peoples become tribes of about this many states; hunter-gatherers stay unowned
TRIBE_SIZE = 8

# seeds for the cultural regions with names worth giving (dataset name -> seeds); the others are cut into clusters named by compass point
CULTURE_SEEDS = {
    'proto-altaic pastoralists': [('donghu', 'Donghu', 118.0, 43.5), ('xiongnu_p', 'Hu and the Ordos nomads', 109.0, 40.0), ('xianyu', 'Xianyu and Wuhuan', 114.0, 41.0), ('dingling', 'Dingling', 100.0, 51.5),
                                  ('yuezhi', 'Yuezhi', 95.0, 40.5), ('wusun', 'Wusun', 82.0, 44.0), ('gobi', 'Gobi nomads', 104.0, 44.5), ('rong', 'Rong and Di', 106.0, 36.5), ('xiqu', 'Qiang', 100.0, 34.0),
                                  ('yanmen', 'Loufan', 112.0, 39.0), ('hulun', 'Hulun tribes', 118.5, 48.5)],
    'illyrians': [('dalmatae', 'Dalmatae', 16.8, 43.7), ('ardiaei', 'Ardiaei', 18.7, 43.0), ('liburni', 'Liburni', 15.3, 44.5), ('taulantii', 'Taulantii', 19.8, 41.2), ('pannonii', 'Pannonians', 18.0, 46.0),
                  ('autariatae', 'Autariatae', 20.0, 44.0), ('iapodes', 'Iapodes', 15.5, 45.2)],
}

# lands that no dataset polygon and no block of the project covers: (box west south east north, seeds); only states nobody owns are given out
MASK_SEEDS = [
    ((-11.0, 49.5, 2.0, 59.5), [('cantiaci', 'Cantiaci and the Kentish tribes', 0.9, 51.3), ('durotriges', 'Durotriges and Dumnonii', -3.0, 50.8), ('belgae_b', 'Belgae of Britain', -1.6, 51.2),
                                ('silures', 'Silures and Ordovices', -3.4, 52.0), ('brigantes', 'Brigantes', -1.8, 54.1), ('iceni', 'Iceni and Trinovantes', 0.8, 52.5), ('coritani', 'Corieltauvi', -0.9, 52.8),
                                ('dobunni', 'Dobunni and Cornovii', -2.3, 52.0), ('caledonii', 'Caledonians', -4.4, 57.0), ('picts_s', 'Verturiones', -3.2, 56.2), ('votadini', 'Votadini', -2.8, 55.5),
                                ('erainn', 'Erainn of Munster', -8.8, 52.2), ('laigin', 'Laigin', -6.9, 52.9), ('ulaid', 'Ulaid', -6.2, 54.5), ('connachta', 'Connachta', -8.8, 53.8), ('midhe', 'Mide', -7.4, 53.5)]),
    ((3.0, 47.0, 30.0, 61.5), [('jutes', 'Jutes', 9.0, 56.4), ('danes', 'Danes', 11.5, 55.3), ('svear', 'Svear', 16.5, 59.6), ('gotar', 'Gautar', 13.5, 57.8), ('norse_w', 'Rygir', 7.0, 60.3),
                                ('norse_n', 'Haalogaland', 14.0, 66.5), ('hedmark', 'Raumar', 10.8, 61.0), ('chauci', 'Chauci', 8.2, 53.3), ('frisii', 'Frisii', 5.8, 53.0), ('saxones', 'Elbe Germanic tribes', 10.0, 53.4),
                                ('semnones', 'Semnones', 13.5, 52.4), ('chatti', 'Chatti', 9.3, 51.2), ('cherusci', 'Cherusci', 9.8, 52.3), ('marcomanni', 'Marcomanni', 12.5, 50.3), ('lugii', 'Lugii', 17.5, 51.2),
                                ('rugii', 'Rugii', 15.5, 54.0), ('gutones', 'Gutones', 19.5, 54.0), ('venedi', 'Venedi', 21.5, 52.8), ('aesti', 'Aesti', 22.5, 55.5), ('curonians', 'Curonians', 21.5, 56.7),
                                ('latgalians', 'Latgalians', 26.5, 56.8), ('estonians', 'Estonians', 25.5, 58.8), ('finns', 'Finns', 24.0, 61.2), ('karelians', 'Karelians', 30.0, 61.5), ('milograd', 'Milograd tribes', 28.0, 52.5),
                                ('zarubintsy', 'Proto-Slavs', 30.5, 51.0), ('batavi', 'Batavi and the Rhine tribes', 5.5, 51.8), ('treveri_e', 'Eburones', 5.5, 50.7)]),
    ((34.0, 12.0, 60.0, 31.0), [('qedar', 'Qedar', 38.5, 30.0), ('lihyan', 'Lihyan', 37.7, 26.6), ('hejaz', 'Hejaz tribes', 40.5, 23.5), ('najd', 'Najd tribes', 44.5, 25.5), ('yamama', 'Yamama', 47.5, 23.0),
                                ('gerrha', 'Gerrha and Dilmun', 50.2, 26.0), ('magan', 'Magan', 57.0, 23.5), ('hadramaut', 'Hadramaut', 49.0, 16.2), ('qataban', 'Qataban and Awsan', 45.5, 14.0), ('rub', 'Empty Quarter tribes', 53.0, 20.0),
                                ('midian', 'Midian', 35.5, 28.0), ('edom', 'Edom', 35.4, 30.2)]),
    ((22.0, 54.0, 60.0, 70.0), [('finnic_n', 'Perm and the Finnic tribes', 50.0, 60.5), ('merya', 'Merya', 40.0, 57.0), ('volga_finns', 'Volga Finns', 46.0, 56.0), ('ananyino', 'Ananyino tribes', 52.0, 57.0),
                                ('dyakovo', 'Dyakovo tribes', 38.0, 55.5), ('onega', 'White Sea tribes', 36.0, 63.0)]),
]

# the lands of the 'Hallstatt culture', 'Celts' and 'Germanic tribes' polygons of the dataset, cut by the peoples that lived there (the keys of the blocks of the first start date, so a tribe keeps its tag)
EXTRA_SEEDS = [('etruscans', 'Etruscans', 11.4, 43.2), ('umbri', 'Umbrians', 12.5, 43.1), ('ligures', 'Ligurians', 8.8, 44.4), ('veneti', 'Veneti', 12.0, 45.6), ('insubres', 'Insubres and Golaseccans', 9.0, 45.6),
               ('lucani', 'Lucanians', 15.8, 40.3), ('apuli', 'Apulians', 16.2, 41.1), ('bruttii', 'Bruttians', 16.3, 39.2), ('piceni', 'Picenes', 13.5, 43.3), ('latini', 'Latins', 12.8, 41.6),
               ('sicani', 'Sicels and Sicani', 14.0, 37.5), ('sardi', 'Sardinians', 9.0, 40.0), ('corsi', 'Corsi', 9.1, 42.1), ('macedon', 'Macedon', 22.2, 40.6), ('epirus', 'Epirotes', 20.8, 39.6),
               ('aetolia', 'Aetolians', 21.6, 38.6), ('achaea', 'Achaeans', 21.9, 38.1), ('elis', 'Elis', 21.6, 37.8), ('phocis', 'Phocians and Locrians', 22.4, 38.6),
               ('ath', 'Athens', 23.7, 38.0), ('thb', 'Thebes', 23.3, 38.4), ('spa', 'Sparta', 22.4, 37.1), ('cor', 'Corinth', 22.9, 37.9), ('arg', 'Argos', 22.7, 37.6), ('ths', 'Thessaly', 22.4, 39.5),
               ('cherusci_h', 'Cherusci', 9.8, 52.3), ('chatti_h', 'Chatti', 9.3, 51.2), ('suebi_h', 'Suebi', 13.0, 52.5), ('marcomanni_h', 'Marcomanni', 12.5, 50.3), ('helvecones', 'Lugii', 17.5, 51.2),
               ('rugii_h', 'Rugii', 15.5, 54.0), ('gutones_h', 'Gutones', 19.5, 54.0), ('chauci_h', 'Chauci', 8.2, 53.3), ('frisii_h', 'Frisii', 5.8, 53.0), ('jutes_h', 'Jutes', 9.0, 56.4)]


def hallstatt_seeds():
    import re
    seeds = {}
    for blk in ('CEL', 'IBE', 'CIB', 'THR'):
        for s in BLOCK_SEEDS[blk]:
            seeds[s[0]] = s
    for s in EXTRA_SEEDS + CULTURE_SEEDS['illyrians']:
        seeds.setdefault(s[0], s)
    return list(seeds.values())


MASK_SEEDS.append(((6.0, 35.0, 29.0, 47.5), EXTRA_SEEDS + CULTURE_SEEDS['illyrians'] + BLOCK_SEEDS['THR']))

# lands the dataset leaves empty in the later dates of antiquity (not used for the first start date, whose blocks cover them)
MASK_LATER = [((-10.0, 36.0, 6.0, 52.0), hallstatt_seeds()),
              ((123.0, 32.0, 132.0, 44.0), BLOCK_SEEDS['KOR']),
              ((28.0, 38.0, 100.0, 56.0), BLOCK_SEEDS['SCY'] + BLOCK_SEEDS['SAR'] + BLOCK_SEEDS['SAK']),
              ((88.0, 36.0, 135.0, 56.0), CULTURE_SEEDS['proto-altaic pastoralists'])]

# umbrellas of the dataset that hide the powers of one date: (from year, to year, normalized name, seeds); the first seed of a polity that has a tag keeps it
YEAR_SEEDS = [
    (-130, -70, r'^seleucid', [('bab', 'Seleucid Kingdom', 36.7, 35.6), ('parthian', 'Parthian Empire', 50.5, 34.5), ('parthian', 'Parthian Empire', 44.0, 34.2), ('persis', 'Persis', 52.5, 29.5),
                               ('elymais', 'Elymais', 48.8, 32.2), ('characene', 'Characene', 47.7, 30.8), ('atropatene', 'Media Atropatene', 47.0, 37.5), ('hyrcania', 'Hyrcania', 54.5, 36.8)]),
    (-130, -70, r'^mauryan', [('ind', 'Shunga Empire', 84.0, 26.0), ('satavahana', 'Satavahana', 77.0, 18.0), ('kalinga_m', 'Kalinga', 85.5, 20.2), ('tamil', 'Pandya', 78.5, 10.0), ('malwa', 'Malwa', 76.5, 23.5)]),
    (-130, -70, r'^kushan', [('kushan', 'Yuezhi', 66.0, 37.5), ('indogreek', 'Indo-Greek Kingdom', 72.0, 33.5), ('indoscythian', 'Indo-Scythians', 67.0, 29.0), ('indoscythian2', 'Sakas of Sindh', 69.5, 25.0), ('kashmir_ig', 'Kashmir', 75.5, 34.5)]),
    (-130, -70, r'^minor states$', [('pontus', 'Kingdom of Pontus', 36.0, 40.8), ('bithynia', 'Bithynia', 30.5, 40.5), ('cappadocia_k', 'Cappadocia', 35.5, 38.3), ('galatia', 'Galatia', 32.5, 39.5),
                                    ('armenia_k', 'Armenia', 43.0, 39.8), ('commagene', 'Commagene', 38.5, 37.8), ('rom', 'Roman Republic', 28.0, 38.0)]),
]

MASK_LATER.insert(0, ((92.0, 5.0, 110.0, 24.0), BLOCK_SEEDS['IRR']))

# unowned lands of antiquity cut into tribes of about TRIBE_SIZE states by position: (box, the family whose names the tribes take)
MASK_CLUSTER = [((95.0, -11.0, 141.0, 6.0), 'Austronesians'), ((117.0, 5.0, 127.0, 27.0), 'Austronesians'), ((-17.0, 14.0, 35.0, 31.0), 'Saharan pastoral nomads')]

YEAR_SEEDS.extend([
    (185, 225, r'^han$|^han empire', [('han', 'Han Empire', 114.5, 34.5), ('han', 'Han Empire', 117.5, 36.3), ('han', 'Han Empire', 113.5, 32.5), ('yuan_shao', 'Yuan Shao', 116.0, 39.0), ('sun', 'Sun Quan', 119.8, 31.5), ('liu_biao', 'Liu Biao', 112.5, 30.8),
                                      ('liu_zhang', 'Liu Zhang', 104.2, 30.7), ('ma_teng', 'Ma Teng', 105.5, 36.0), ('gongsun', 'Gongsun Du', 122.0, 41.5), ('shi_xie', 'Shi Xie', 106.5, 21.5), ('sun', 'Sun Quan', 113.5, 23.5), ('sun', 'Sun Quan', 118.0, 27.0),
                                      ('zhang_lu', 'Zhang Lu', 107.0, 33.0), ('dong_zhuo', 'Dong Zhuo', 109.0, 34.3), ('tao_qian', 'Tao Qian', 118.0, 34.5)]),
    (140, 250, r'^magadha', [('ind', 'Magadha', 85.0, 25.5), ('kushan', 'Kushan Empire', 77.0, 29.0), ('nagas', 'Nagas of Padmavati', 78.5, 26.5)]),
])

# lands left empty by the dataset in certain years: (from year, to year, box, seeds)
MASK_YEAR = [(30, 250, (62.0, 32.0, 80.0, 41.0), [('kushan', 'Kushan Empire', 68.0, 36.0)]),
             (-130, 250, (129.0, 31.0, 146.0, 42.0), [('yamatai', 'Yamatai', 135.5, 34.5), ('tsukushi', 'Tsukushi', 130.5, 33.5), ('kenu', 'Kenu', 139.5, 36.0), ('izumo', 'Izumo', 132.8, 35.4)])]

MASK_YEAR.extend([
    (250, 450, (3.0, 47.0, 30.0, 61.5), [('alamanni', 'Alamanni', 9.0, 48.5), ('franks_t', 'Franks', 7.0, 51.0), ('saxones_t', 'Saxons', 9.0, 53.5), ('burgundi', 'Burgundians', 15.0, 52.5),
                                       ('vandali', 'Vandals', 17.5, 51.0), ('goths_t', 'Tervingi', 27.5, 47.0), ('goths_g', 'Greuthungi', 33.0, 48.5), ('gepidae', 'Gepids', 22.0, 47.5),
                                       ('quadi', 'Quadi and Marcomanni', 17.0, 49.0), ('langobardi', 'Lombards', 11.0, 53.0), ('heruli', 'Heruls', 14.0, 54.5), ('thuringi', 'Thuringians', 11.0, 51.0),
                                       ('anglii', 'Angles', 9.5, 54.8), ('jutes', 'Jutes', 9.0, 56.4), ('danes', 'Danes', 11.5, 55.3), ('svear', 'Svear', 16.5, 59.6), ('gotar', 'Gautar', 13.5, 57.8),
                                       ('aesti_t', 'Aesti', 22.5, 55.5), ('finns_t', 'Finns', 24.0, 61.2), ('slavs_e', 'Slavs', 30.5, 51.0), ('venedi_t', 'Venedi', 21.5, 52.8)]),
    (250, 450, (-11.0, 49.5, 2.0, 59.5), [('picts', 'Picts', -4.0, 57.0), ('scoti', 'Scoti', -7.5, 53.5), ('dalriada', 'Dal Riata', -6.0, 55.0)]),
    (250, 450, (28.0, 38.0, 100.0, 56.0), [('alans', 'Alans', 46.5, 46.0), ('sarmatae', 'Sarmatians', 38.0, 47.5), ('kangju', 'Kangju', 68.0, 43.0), ('chionites', 'Chionites', 58.0, 42.0),
                                          ('wusun', 'Wusun', 80.0, 44.0), ('dingling', 'Dingling', 90.0, 50.0), ('bulgars_t', 'Bulgars', 55.0, 52.0)]),
    (250, 450, (88.0, 36.0, 135.0, 56.0), [('murong', 'Murong', 120.0, 41.5), ('duan', 'Duan', 117.0, 40.0), ('yuwen', 'Yuwen', 122.0, 43.5), ('tuoba', 'Tuoba', 111.0, 41.5), ('tufa', 'Tufa', 103.0, 38.5),
                                          ('qifu', 'Qifu', 105.0, 36.0), ('xianbei_n', 'Xianbei', 100.0, 47.5), ('shiwei', 'Shiwei', 122.0, 50.0)]),
    (250, 450, (92.0, 5.0, 110.0, 24.0), BLOCK_SEEDS['IRR']),
    (250, 450, (123.0, 32.0, 132.0, 44.0), [('baekje', 'Baekje', 126.8, 36.8), ('gaya', 'Gaya', 128.5, 35.5), ('gojoseon', 'Goguryeo', 126.0, 40.0)]),
    (250, 450, (129.0, 31.0, 146.0, 42.0), [('yamatai', 'Yamato', 135.5, 34.5), ('tsukushi', 'Tsukushi', 130.5, 33.5), ('kenu', 'Kenu', 139.5, 36.0), ('izumo', 'Izumo', 132.8, 35.4)]),
])

MASK_YEAR.insert(0, (350, 450, (3.0, 47.0, 30.0, 61.5), [('alamanni', 'Alamanni', 9.0, 48.5), ('franks_t', 'Franks', 6.5, 51.0), ('saxones_t', 'Saxons', 9.0, 53.5), ('burgundi', 'Burgundians', 8.0, 50.5),
                                                       ('vandali', 'Vandals', 17.0, 50.8), ('goths_t', 'Visigoths', 26.0, 45.0), ('goths_g', 'Ostrogoths', 33.0, 48.5), ('gepidae', 'Gepids', 22.0, 47.5),
                                                       ('quadi', 'Suebi', 17.0, 48.8), ('langobardi', 'Lombards', 11.0, 53.0), ('heruli', 'Heruls', 14.0, 54.5), ('thuringi', 'Thuringians', 11.0, 51.0),
                                                       ('anglii', 'Angles', 9.5, 54.8), ('jutes', 'Jutes', 9.0, 56.4), ('danes', 'Danes', 11.5, 55.3), ('svear', 'Svear', 16.5, 59.6), ('gotar', 'Gautar', 13.5, 57.8),
                                                       ('aesti_t', 'Aesti', 22.5, 55.5), ('finns_t', 'Finns', 24.0, 61.2), ('slavs_e', 'Slavs', 30.5, 51.0), ('sciri', 'Sciri', 21.0, 53.0), ('rugii_t', 'Rugii', 16.0, 54.0)]))

MASK_YEAR.extend([
    (451, 700, (3.0, 47.0, 31.0, 61.5), [('alamanni', 'Alamanni', 9.0, 48.5), ('thuringi', 'Thuringians', 11.0, 51.0), ('langobardi', 'Lombards', 16.5, 48.5), ('gepidae', 'Gepids', 21.0, 46.5),
                                        ('heruli', 'Heruls', 14.0, 54.0), ('saxones_t', 'Saxons', 9.0, 53.0), ('frisii_t', 'Frisians', 5.5, 53.2), ('anglii', 'Angles', 9.5, 54.8), ('jutes', 'Jutes', 9.0, 56.4),
                                        ('danes', 'Danes', 11.5, 55.3), ('svear', 'Svear', 16.5, 59.6), ('gotar', 'Gautar', 13.5, 57.8), ('aesti_t', 'Aesti', 22.5, 55.5), ('finns_t', 'Finns', 24.0, 61.2),
                                        ('slavs_e', 'Slavs', 30.5, 51.0), ('slavs_w', 'Wends', 15.0, 50.5), ('bavarii', 'Bavarians', 12.0, 48.6), ('rugii_t', 'Rugii', 16.0, 54.0), ('sciri', 'Sciri', 21.0, 53.0),
                                        ('karelians_t', 'Karelians', 30.0, 61.5)]),
    (451, 700, (-11.0, 49.5, 2.0, 59.5), [('picts', 'Picts', -4.0, 57.5), ('dalriada', 'Dal Riata', -6.0, 56.0), ('gododdin', 'Gododdin', -3.0, 55.8), ('rheged', 'Rheged', -3.0, 54.6), ('gwynedd', 'Gwynedd', -4.0, 53.0),
                                         ('powys', 'Powys', -3.2, 52.5), ('dumnonia', 'Dumnonia', -3.8, 50.7), ('kent', 'Kent', 0.9, 51.3), ('sussex', 'Sussex', -0.5, 50.9), ('wessex', 'Wessex', -1.6, 51.0),
                                         ('essex', 'Essex', 0.5, 51.8), ('eastangles', 'East Angles', 1.0, 52.5), ('mercia', 'Mercia', -1.5, 52.6), ('deira', 'Deira', -1.2, 54.0),
                                         ('ulaid', 'Ulaid', -6.5, 54.6), ('laigin', 'Laigin', -6.8, 53.0), ('mumu', 'Munster', -8.5, 52.3), ('connachta', 'Connachta', -8.7, 53.8), ('midhe', 'Mide', -7.4, 53.5)]),
    (451, 600, (28.0, 38.0, 100.0, 56.0), [('sabir', 'Sabir', 47.0, 44.0), ('utigur', 'Utigurs', 38.0, 47.0), ('kutrigur', 'Kutrigurs', 32.0, 47.5), ('akatziri', 'Akatziri', 50.0, 48.0),
                                           ('hephthalites', 'Hephthalites', 66.0, 38.0), ('hephthalites2', 'Hephthalites', 70.0, 35.0), ('kangju', 'Kangju', 68.0, 43.0), ('tiele', 'Tiele', 95.0, 48.0),
                                           ('bulgars_t', 'Onogurs', 55.0, 52.0), ('chionites', 'Chionites', 58.0, 42.0)]),
    (451, 600, (88.0, 36.0, 135.0, 56.0), [('tiele', 'Tiele', 95.0, 48.0), ('shiwei', 'Shiwei', 122.0, 50.0), ('kurykan', 'Kurykans', 104.0, 52.0), ('xi', 'Xi', 118.0, 43.0), ('khitan', 'Khitan', 120.5, 43.8),
                                           ('wuji', 'Wuji', 128.0, 47.0), ('tuyuhun', 'Tuyuhun', 100.0, 36.5), ('gaoche', 'Gaoche', 90.0, 44.0)]),
    (451, 700, (92.0, 5.0, 110.0, 24.0), BLOCK_SEEDS['IRR']),
    (451, 700, (123.0, 32.0, 132.0, 44.0), [('baekje', 'Baekje', 126.8, 36.8), ('gaya', 'Gaya', 128.5, 35.5), ('gojoseon', 'Goguryeo', 126.0, 40.0), ('silla', 'Silla', 129.0, 36.0)]),
    (451, 700, (129.0, 31.0, 146.0, 42.0), [('yamatai', 'Yamato', 135.5, 34.5), ('tsukushi', 'Tsukushi', 130.5, 33.5), ('kenu', 'Kenu', 139.5, 36.0), ('izumo', 'Izumo', 132.8, 35.4)]),
])

YEAR_SEEDS.append((640, 790, r'^sasanian', [('umayyad', 'Umayyad Caliphate', 52.0, 33.0), ('umayyad', 'Umayyad Caliphate', 60.0, 36.0), ('umayyad', 'Umayyad Caliphate', 46.0, 30.0),
                                           ('tabaristan', 'Dabuyids', 53.5, 36.5), ('khwarazm', 'Khwarazm', 60.0, 41.5), ('sogd', 'Sogdian city-states', 65.5, 39.0)]))

YEAR_SEEDS.append((950, 1070, r'^denmark-norway', [('den', 'Denmark', 10.5, 56.0), ('norway', 'Kingdom of Norway', 9.5, 61.0), ('iceland', 'Iceland', -19.0, 64.8), ('greenland', 'Norse Greenland', -45.0, 61.0),
                                                   ('hebrides', 'Kingdom of the Isles', -6.5, 57.5)]))

YEAR_SEEDS.extend([
    (1130, 1230, r'^song', [('song', 'Southern Song', 114.0, 29.0), ('song', 'Southern Song', 119.0, 27.0), ('song', 'Southern Song', 107.0, 28.0), ('song', 'Southern Song', 111.0, 23.0), ('song', 'Southern Song', 114.0, 32.8), ('song', 'Southern Song', 119.0, 32.5),
                           ('jin', 'Jin Dynasty', 114.0, 36.5), ('jin', 'Jin Dynasty', 110.0, 35.0), ('jin', 'Jin Dynasty', 118.0, 36.5)]),
    (1130, 1230, r'^liao', [('jin', 'Jin Dynasty', 125.0, 44.0)]),
    (1150, 1205, r'^mongol', [('mongol', 'Khamag Mongol', 110.0, 48.5), ('tatars', 'Tatars', 118.0, 47.0), ('keraites', 'Keraites', 104.0, 47.0), ('naiman', 'Naiman', 92.0, 47.0),
                              ('merkit', 'Merkits', 106.0, 52.5), ('oirat', 'Oirats', 95.0, 51.5), ('onggud', 'Ongud', 111.0, 42.0)]),
])

YEAR_SEEDS.append((1230, 1340, r'^ryazan', [('ryazan', 'Ryazan', 40.0, 54.5), ('vladimir', 'Vladimir-Suzdal', 40.4, 56.2), ('smolensk', 'Smolensk', 32.0, 54.8), ('chernigov', 'Chernigov', 31.3, 51.6),
                                           ('galicia', 'Galicia-Volhynia', 25.0, 50.0), ('kiev', 'Kiev', 30.5, 50.4), ('polotsk', 'Polotsk', 28.0, 55.5), ('tver', 'Tver', 35.9, 57.0),
                                           ('murom', 'Murom', 42.0, 55.0), ('turov', 'Turov-Pinsk', 27.5, 52.5)]))
