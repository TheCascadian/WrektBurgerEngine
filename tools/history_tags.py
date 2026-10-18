"""Rules for tools/import-history.py: which names of the dataset are one country, which are not countries, the umbrellas that are split, and colors."""
import re, unicodedata

# names that are peoples, cultures or ways of life rather than states: their land has no owner
IGNORE = re.compile(r'hunter|gatherer|forag|fishing|shellfish|marine mammal|polynesian|aboriginal|^paleo|^arctic|^dorset|^thule|^ainu|^inuit|^tasmanian|^caribbean|^subarctic|^desert|^plain bison|^plateau|'
                    r'^north american|^patagonian|^samoyed|^sami$|^saami|^samis|^guanche|^taino|^arawaks|^boethuk|^beothuk|^tungus|^innu|^athabas|^cree$|^guanahatabeyes|^alaska')
# regions of farming or herding peoples: cut into tribes of regular size
TRIBAL = re.compile(r'^mandes$|^azandes$|^oghuz|^cushites$|^berbers$|^tuaregs$|^bedouins$|^karluks$|^tupis$|^malay$|^anasazi|^mogollon|^athabask|^papuans?$|^dorset|^thule|^ciboney|^bashkirs|^permians|^thule|complex$|^point peninsula|^porter$|^miller$|^couture|^copena|^goodall|^fourche|^mill creek|^thai$|^mon-khmer|^malays$|^arabs$|^tungus$|peoples?$|tribes|farmers|cultures?$|culture$|pastoral|nomads|^khoisan|^bantu|^papuan|^austronesian|^saharan|^proto-altaic|^austro|^iron age|^siberians|^west african|^celts$|^germanic|^slavic|^slavonic|^baltic')
UMBRELLA = re.compile(r'(states|kingdoms|city-states|emirates|khanates|principalities|confederation|chiefdoms|clans|duchies|republics|tribes|minor)\b')
# umbrellas that are one state in fact
NOT_UMBRELLA = re.compile(r'^(oirat confederation|swiss confederation|maratha confederacy|confederation|united states|united provinces)')


def normalize(name):
    n = unicodedata.normalize('NFKD', name)
    n = ''.join(c for c in n if not unicodedata.combining(c)).lower()
    n = re.sub(r'\(.*?\)', '', n).strip()
    n = re.sub(r'^(the )?(grand duchy of|kingdom of|empire of|republic of|sultanate of|emirate of|khanate of|duchy of|principality of|viceroyalty of|united provinces of|caliphate of|state of) ', '', n)
    n = re.sub(r' (empire|kingdom|khanate|sultanate|caliphate|emirate|dynasty|republic|state)$', '', n)
    return n.strip()


# families: (regex on the normalized name, key). The key becomes the tag; keys in EXISTING keep the tags of the 500 BCE data.
FAMILIES = [
    (r'^achaemenid|^persian$', 'ach'), (r'^parthian', 'pth'), (r'^sasanian|^sassanid', 'sasanian'),
    (r'^roman$|^rome$|^roman republic|^roman empire|^rome$', 'rom'), (r'^western roman', 'wrm'), (r'^eastern roman|^byzantine', 'byzantine'),
    (r'^alexander|^macedon', 'mac'), (r'^seleucid', 'bab'), (r'^ptolemaic|^egypt$', 'egy'), (r'^carthaginian|^carthage', 'car'),
    (r'^kush$|^meroe|^makkura|^makuria|^alwa|^sudan$|^anglo-egyptian sudan', 'nub'), (r'^axum|^aksum|^ethiopia|^abyssinia', 'dmt'),
    (r'^magadha$|^mauryan|^gupta|^palas$|^pala$', 'ind'), (r'^india$|^british raj|^british east india', 'india'),
    (r'^gandhara|^indo-greek', 'gan'), (r'^kosala', 'kos'), (r'^kalinga', 'kalinga'), (r'^satavahana|^satavahanihara', 'dec'),
    (r'^zhou$|^zhou states', 'zho'), (r'^qin$', 'qin'), (r'^han$|^han empire', 'han'), (r'^jin$|^jin empire', 'jin'),
    (r'^qing|^manchu', 'qing'), (r'^ming', 'ming'), (r'^china$', 'china'), (r'^great khanate|^yuan', 'yuan'),     (r'^empire of japan|^imperial japan|^japan$|^tokugawa|^imperial japan', 'yao'),
    (r'^korea$|^goryeo|^joseon|^gojoseon', 'kor'), (r'^annam|^dai viet|^dai viet|^dai viet|^dai viet', 'van'),
    (r'^khmer|^cambodia$', 'irr'), (r'^burma$|^pagan$|^ava$|^burmese', 'burma'),
    (r'^united states', 'usa'), (r'^canada$|^quebec$', 'can'), (r'^brazil$|^portuguese brazil|^brazil', 'bra'),
    (r'^russian|^ussr|^russia$|^muscovy|^tsardom|^moscow|^grand duchy of moscow|^far eastern', 'rus'),
    (r'^ottoman', 'ott'), (r'^kyivan rus|^rus$', 'kyivan'), (r'^holy roman', 'hre'),
    (r'^kingdom of france|^france$|^west francia', 'fra'), (r'^frankish|^franks$|^carolingian|^merovingian', 'franks'),
    (r'^england$|^united kingdom|^great britain|^britain', 'eng'), (r'^spain$', 'spain'), (r'^portugal', 'por'),
    (r'^sweden|^swedish', 'swe'), (r'^denmark', 'den'), (r'^poland|^polish', 'pol'),
    (r'^egypt', 'egy'), (r'^persia$|^iran$', 'iran'), (r'^turkey$|^republic of turkey', 'turkey'),
    (r'^abbasid', 'abbasid'), (r'^umayyad', 'umayyad'), (r'^fatimid', 'fatimid'),
    (r'^mongol empire|^mongols', 'mongols'), (r'^golden horde|^khanate of the golden horde|^blue horde', 'goldenhorde'),
    (r'^german(y| empire)?$|^german$|^east germany|^west germany', 'germany'), (r'^austria|^austrian|^austro|^habsburg', 'austria'),
    (r'^italy|^italian', 'italy')
]

# the tags of the 500 BCE data (common/countries)
EXISTING = {'ach': 'ACH', 'rom': 'ROM', 'mac': 'MAC', 'bab': 'BAB', 'egy': 'EGY', 'car': 'CAR', 'nub': 'NUB', 'dmt': 'DMT', 'ind': 'IND', 'gan': 'GAN', 'kos': 'KOS',
            'dec': 'DEC', 'zho': 'ZHO', 'qin': 'QIN', 'jin': 'JIN', 'yao': 'YAO', 'kor': 'KOR', 'van': 'VAN', 'irr': 'IRR', 'ath': 'ATH', 'spa': 'SPA', 'cor': 'COR', 'thb': 'THB',
            'arg': 'ARG', 'ths': 'THS', 'crt': 'CRT', 'nax': 'NAX', 'syr': 'SYR', 'cro': 'CRO', 'pnt': 'PNT', 'ion': 'ION', 'qi': 'QIQ', 'chu': 'CHU', 'yue': 'YUE', 'shu': 'SHU'}

# Greek and Chinese umbrellas follow the states of the 500 BCE data: seeds (key, display name, lon, lat), the nearest seed takes the state
SEEDS = {
    'zhou states': [('qin', 'Qin', 108.9, 34.4), ('zhao', 'Zhao', 113.2, 38.2), ('wei', 'Wei', 113.8, 35.2), ('hanstate', 'Han', 113.2, 33.7), ('qi', 'Qi', 118.6, 36.7),
                    ('chu', 'Chu', 112.5, 31.0), ('yan', 'Yan', 116.5, 40.2), ('yue', 'Yue', 120.2, 29.8), ('shu', 'Shu', 104.0, 30.7), ('song', 'Song', 115.6, 34.4),
                    ('zhou', 'Zhou', 112.3, 34.7), ('lu', 'Lu', 117.0, 35.6)],
    'greek city-states': [('ath', 'Athens', 23.7, 38.0), ('spa', 'Sparta', 22.4, 37.1), ('cor', 'Corinth', 22.9, 37.9), ('thb', 'Thebes', 23.3, 38.4), ('arg', 'Argos', 22.7, 37.6),
                          ('ths', 'Thessaly', 22.4, 39.5), ('crt', 'Crete', 24.8, 35.2), ('nax', 'Naxos and the Cyclades', 25.4, 37.1)],
    'greek colonies': [('syr', 'Syracuse', 15.3, 37.1), ('cro', 'Croton and the Italiote Greeks', 17.1, 39.1), ('pnt', 'Pontic Greek cities', 33.0, 44.5), ('ion', 'Ionian Greek cities', 27.2, 38.5)],
    'holy roman': [('bavaria', 'Duchy of Bavaria', 11.6, 48.4), ('saxony', 'Duchy of Saxony', 10.0, 52.0), ('brandenburg', 'Brandenburg', 13.2, 52.6), ('bohemia', 'Bohemia', 14.5, 49.9),
                   ('austria', 'Austria', 15.5, 48.0), ('swabia', 'Duchy of Swabia', 9.0, 48.5), ('franconia', 'Franconia', 10.2, 49.8), ('lorraine', 'Lorraine', 6.2, 49.2),
                   ('lowcountries', 'Low Countries', 5.0, 52.0), ('burgundy', 'Burgundy', 5.4, 46.4), ('milan', 'Duchy of Milan', 9.2, 45.3), ('tuscany', 'Tuscany', 11.0, 43.6),
                   ('holstein', 'Holstein and Mecklenburg', 10.5, 54.0), ('rhine', 'Rhineland', 7.8, 50.4), ('hesse', 'Hesse and Thuringia', 10.0, 51.0), ('switzerland', 'Swiss lands', 8.2, 46.9),
                   ('hre', 'Holy Roman Empire', 11.0, 50.8)],
}
SEED_NAMES = [(re.compile(k), v) for k, v in SEEDS.items()]

# fitting colors for the well-known polities (by key); others get a color from their region
COLORS = {
    'rom': (160, 32, 48), 'wrm': (190, 70, 90), 'byzantine': (112, 50, 140), 'ach': (116, 62, 150), 'pth': (200, 150, 60), 'sasanian': (210, 170, 60), 'mac': (84, 120, 190),
    'bab': (190, 130, 40), 'egy': (222, 190, 90), 'car': (130, 40, 120), 'nub': (50, 50, 50), 'dmt': (40, 120, 80), 'ind': (230, 130, 40), 'india': (230, 120, 30),
    'qin': (30, 30, 30), 'han': (200, 60, 50), 'jin': (110, 80, 160), 'qing': (225, 190, 40), 'ming': (205, 50, 50), 'china': (200, 40, 40), 'yuan': (60, 150, 190), 'mongols': (60, 150, 190),
    'goldenhorde': (50, 170, 160), 'yao': (220, 220, 225), 'kor': (230, 230, 240), 'van': (150, 170, 50), 'irr': (190, 120, 60), 'usa': (60, 80, 160), 'can': (210, 60, 60),
    'bra': (60, 160, 70), 'rus': (70, 130, 60), 'ott': (60, 110, 70), 'kyivan': (150, 90, 50), 'hre': (232, 200, 70), 'fra': (70, 100, 200), 'franks': (90, 130, 210), 'eng': (200, 50, 70),
    'spain': (230, 170, 40), 'por': (50, 140, 80), 'swe': (60, 120, 200), 'den': (200, 60, 60), 'pol': (220, 80, 90), 'iran': (80, 160, 90), 'turkey': (200, 40, 50), 'abbasid': (40, 40, 44),
    'umayyad': (225, 225, 215), 'fatimid': (50, 140, 70), 'germany': (110, 112, 118), 'austria': (236, 230, 210), 'italy': (70, 160, 110), 'ath': (70, 120, 200), 'spa': (170, 40, 40),
    'cor': (110, 170, 220), 'thb': (140, 90, 160), 'zho': (180, 160, 80), 'qi': (60, 150, 140), 'chu': (80, 150, 70), 'zhao': (200, 120, 60), 'wei': (70, 110, 180), 'hanstate': (190, 90, 120),
    'yan': (100, 100, 170), 'bavaria': (90, 150, 220), 'saxony': (90, 90, 90), 'brandenburg': (60, 60, 80), 'bohemia': (200, 90, 90), 'burgundy': (120, 50, 70), 'milan': (180, 60, 60),
}
