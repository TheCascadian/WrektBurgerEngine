# 500 BCE Map: Source Research Record

Date: 2026-10-14. Status: research only. No dataset has been downloaded, opened or cached. Every license below was read from search summaries or publisher pages, not from the full license text, unless stated. Nothing is approved for use until the owner approves it in `tools/map500bce/provenance/sources.csv`.

## 1. Rules (from the owner)
- Never use aourednik/historical-basemaps (GPL-3.0, confirmed by the owner) or idris-maps/world-historical-gis-data, in any form.
- Facts may be taken from sources; all geometry is drawn by us on our own province grid.
- Real names, real people, real locations. Every feature needs a provenance row.

## 2. Candidate sources and their status
"PENDING OWNER" means license looks usable but no owner approval is recorded. A prior research report called the first three "approved"; it did not say who approved, so they are listed here as pending.

| Id | Source | Terms as found | Use and limits | Status |
|---|---|---|---|---|
| ne_coast | Natural Earth, coastline layer | Public domain, commercial use allowed, no attribution required (naturalearthdata.com/about/terms-of-use) | Coastline only. River and lake layers excluded pending provenance review | PENDING OWNER |
| gebco | GEBCO gridded bathymetry | Public domain, commercial use allowed, source acknowledgement required | Sea depth and relief | PENDING OWNER |
| pleiades | Pleiades | CC BY 3.0 US or later, attribution required | Ancient place and name references only, never borders or geometry | PENDING OWNER |
| dasi | DASI (CSAI records) | CC BY 4.0 for record data, not images | South Arabian inscription references; not territorial evidence | PENDING OWNER |
| ancient_arabia | Gazetteer of Ancient Arabia | CC BY 4.0, attribution | Arabian names and cited references | PENDING OWNER |
| getty_tgn | Getty TGN | ODC-By 1.0, attribution and database notice | Historical names; expressly non-comprehensive | PENDING OWNER |
| met_oa | The Met Open Access | CC0 1.0 for specified collection data | Catalog metadata only; images have separate terms | PENDING OWNER |
| cleveland_oa | Cleveland Museum of Art Open Access | CC0 1.0 datasets | Metadata leads; no records checked | PENDING OWNER |
| artic | Art Institute of Chicago API | CC0 for most data; CC BY 4.0 for the description field | Field-specific attribution | PENDING OWNER |
| smithsonian_oa | Smithsonian Open Access | CC0 only for individually designated items | Item-level CC0 check required | PENDING OWNER |
| syriaca | Syriac Gazetteer | CC BY 4.0 | Limited scope | PENDING OWNER |
| nomisma | Nomisma.org core concepts | CC BY 3.0 | Core concepts only; partner datasets differ | PENDING OWNER |
| periodo | PeriodO | CC0 1.0 | Period definitions only, not evidence of a polity or border | PENDING OWNER |
| chengdu_survey | Chengdu Plain Archaeological Survey (Open Context) | CC BY 4.0, attribution and URI citation | Local project only | PENDING OWNER |
| siddham | SIDDHAM | One report found CC BY 4.0, another could not verify | Unresolved | UNVERIFIED, do not use |
| mesorad | MesoRAD (Mesoamerican radiocarbon dataset) | Conflict: one listing says CC BY 4.0, the journal page says CC BY 3.0 (DOI 10.5334/joad.83). A GitHub issue says coordinates were removed from some versions | Radiocarbon dates from 132 Lowland Maya sites. Not a gazetteer, no place names or polities | UNVERIFIED, resolve license from the DOI record |
| pard | Pacific Archaeology Radiocarbon Database | CC BY-NC 4.0; not downloadable at time of writing | Non-commercial | REJECTED |
| nunn_petchey | Nunn and Petchey 2013, Lapita in Fiji (article) | CC BY 4.0 listed for the article | A published article of 80 dates, not a dataset | UNVERIFIED, lead only |
| hydrorivers | HydroRIVERS (HydroSHEDS) | HydroSHEDS site says free for commercial use under the HydroSHEDS license agreement. License text not read | Global river lines. Required attribution statement must be read from the agreement | UNVERIFIED until the agreement text is read |
| merit_hydro | MERIT Hydro | CC BY-NC 4.0 or ODbL 1.0, depending on choice | ODbL is share-alike for databases | REJECTED for now (share-alike and non-commercial options) |
| geonames | GeoNames | Its export page says CC BY and allows commercial use; attribution (a link) is considered sufficient. Full license page not retrieved | Modern place names for disambiguation only; never evidence of 500 BCE facts | PENDING OWNER |
| openhistoricalmap | OpenHistoricalMap | Appeared in search results only; its copyright page was not examined | Possible lead for historical geometry | UNEXAMINED |
| thanados, austarch | THANADOS, AustArch | THANADOS CC BY 4.0 but burials, not relevant; AustArch dataset not open | Not relevant | REJECTED |

## 3. Region status
- Near East, Levant, Arabia: Pleiades, DASI, Gazetteer of Ancient Arabia. Gaps: Mesopotamian and Levantine rulers and polities.
- North and East Africa: Pleiades, Getty TGN, Met, Cleveland, Art Institute, Smithsonian. None shown complete or 500 BCE specific.
- Europe and Mediterranean: Pleiades, Syriac Gazetteer, Nomisma, Getty TGN. No source for political extents.
- Central Asia and steppe: Pleiades, Nomisma, Met, Smithsonian. No dedicated source.
- South Asia: Pleiades, SIDDHAM (unresolved), Met, Smithsonian. No territorial source.
- East and Southeast Asia: Pleiades, PeriodO, Chengdu survey, Smithsonian. No source for comprehensive rulers or polities.
- North and Central America, Caribbean: MesoRAD (license conflict, Maya lowlands only). Nothing found for the Caribbean or North America.
- South America: no open-licensed Andean settlement dataset found. Smithsonian NMAI Andean entries need item-by-item license checks.
- Oceania and Pacific: the only database found (PARD) is non-commercial and rejected. Nunn and Petchey is a single article.
- Global rivers: HydroRIVERS is the candidate, with its license text unread. Natural Earth rivers are excluded by the earlier report.
- Modern names: GeoNames, for disambiguation only.

## 4. The central gap
No license-verified source supplies comprehensive 500 BCE political extents (who held which land, which rulers). Museum catalogs and gazetteers give leads for places and names, not for territory. Under the owner's rule that unverified facts are not used, the political layer cannot be built from these sources alone.

Options for the owner (not decided):
1. Approve additional source categories that were not searched, for example peer-reviewed publications, with facts extracted and cited per feature (facts are not copyrightable, but every citation is recorded).
2. Build the physical world and settlements now and leave political ownership as clearly marked placeholders until a source set is approved.
3. Commission or write the political layer by hand from cited scholarship, with the citation recorded per country in `features.csv`.

## 5. Not done
- No data downloaded, no license text copied, no hash recorded.
- No record-level check of any source for 500 BCE coverage.
- Rivers, modern-name and four regional searches were run as web searches only (summaries, not primary pages).

## 6. Owner decisions (Gate 2, partial)
- Regional detail: 8,192 x 4,096 pixels for the province image.
- Target: 26,000 total locations.
- Still undecided: exact start date and calendar display, list of countries and peoples, handling of disputed or unknown areas, and approval of the source slate (Gate 1).

## 7. Owner approval and attribution check (2026-10-14)
The owner approved all rows marked PENDING OWNER in section 2, with the instruction to link sources and confirm attribution before integrating anything. Rows marked UNVERIFIED or REJECTED were not approved. Approval is recorded here; a row in `sources.csv` becomes APPROVED only after its license text is saved and hashed, so verify.py still blocks integration.

Primary pages read so far (quotes are from the fetch results, not full license text):

| Source | Page read | What it says | Attribution to use | Open |
|---|---|---|---|---|
| Natural Earth | naturalearthdata.com/about/terms-of-use/ | "All versions of Natural Earth raster + vector map data found on this website are in the public domain." "Crediting the authors is unnecessary." | Optional: "Made with Natural Earth." | None |
| GEBCO | gebco.net/data_and_products/gridded_bathymetry_data/ | "The GEBCO Grid is placed in the public domain and may be used free of charge." The page asks for acknowledgement. | "GEBCO Bathymetric Compilation Group 2026 (2026). The GEBCO_2026 Grid - a continuous terrain model for oceans and land at 15 arc-second intervals. doi:10.5285/4f68d5c7-45eb-f999-e063-7086abc036fa" | The linked conditions-of-use and disclaimer page was not read. The correct year and DOI must match the grid version actually downloaded. |
| Pleiades | pleiades.stoa.org/ | "All content is distributed under the terms of a Creative Commons Attribution license (cc-by)." Credit goes to the individual contributors listed at pleiades.stoa.org/credits/. | Cite Pleiades and the credits page per feature | The CC BY version (earlier found as 3.0 US) was not stated on the page. The terms page returned 404. |
| Getty TGN | getty.edu data-services page | The fetch returned only the page title. No license text was seen. | Unknown | Terms (earlier reported as ODC-By 1.0) and the required notice remain UNVERIFIED. |

Not yet checked at all: DASI, Gazetteer of Ancient Arabia, Met, Cleveland, Art Institute, Smithsonian, Syriaca, Nomisma, PeriodO, Chengdu survey, GeoNames. Each needs its license page read and its attribution text recorded before any data from it is integrated.

## 8. Second license pass (2026-10-14)
| Source | Result | Attribution | Status |
|---|---|---|---|
| Syriaca | Page says "This work is licensed under a Creative Commons Attribution 4.0 International License (CC BY 4.0)." and "Copyright by the contributors. See 'How to Cite' for attribution information." | Follow its "How to Cite" page (not yet read) | Confirmed, citation text open |
| GeoNames | Export page says "cc-by licence". "You should give credit to GeoNames when using data or web services with a link or another reference to GeoNames." | Link to geonames.org | Confirmed, CC BY version not stated |
| PeriodO | Home page calls it a "public domain" gazetteer and links a license page that was not read. CC0 not confirmed | Unknown | UNVERIFIED |
| Nomisma | Documentation URL returned only a directory index. No license seen | Unknown | UNVERIFIED |
| Getty TGN | license URL returned 404 | Unknown | UNVERIFIED |
| Met Open Access | Page was behind a bot-check and showed no license text | Unknown | UNVERIFIED |
| DASI | Rate-limited (HTTP 429), nothing read | Unknown | UNVERIFIED |
| CC BY 3.0 US legal code (for Pleiades) | URL returned 404, not read | n/a | UNVERIFIED |

Remaining unread: Gazetteer of Ancient Arabia, Cleveland, Art Institute, Smithsonian, Chengdu survey. Rule applied: a source stays out of the build until its terms are read from a primary page.

## 9. Required sources by map layer (nothing downloaded)
| Layer | Needed for | Source | Terms status | Gap |
|---|---|---|---|---|
| Coastline | Land and sea mask | Natural Earth 10m coastline | APPROVED, hashed | Modern coast; 500 BCE shoreline changes (deltas, lakes) need cited corrections |
| Elevation and bathymetry | Terrain, mountains, sea depth | GEBCO grid (proposed sole source) | Public domain, conditions page unread | Not downloaded; owner permission pending |
| Rivers | River and lake features | HydroRIVERS | Agreement text unread, not approved | Natural Earth rivers excluded; no approved river source |
| Terrain types | Climate, vegetation, soil | None identified | n/a | Derive from elevation and latitude only, or approve a new source |
| Ancient places and names | Locations, cities | Pleiades; Syriaca; Getty TGN; DASI; Gazetteer of Ancient Arabia | Pleiades and Syriaca read (CC BY); the rest unread | Coverage thin outside the Mediterranean, Near East and Arabia |
| Modern names | Disambiguation only | GeoNames | CC BY read | None |
| Periods and dating | Date labels | PeriodO | UNVERIFIED | License page unread |
| Political ownership | Countries, rulers, peoples | None | n/a | Needs a scholarship-citation source set or an owner choice (section 4) |
| Americas, Oceania, sub-Saharan Africa, Siberia | Peoples and settlements | MesoRAD (not approved) | UNVERIFIED | No approved source |
| Culture, religion, population | Province data | None identified | n/a | Not covered by any source above |

Also required, not yet decided: start date and calendar display, country and peoples list, handling of unknown areas, and a policy for 500 BCE coast corrections.

## 10. Owner-supplied evidence (2026-10-14)
The owner saved license pages and screenshots in docs/SOURCES/. Findings, read from those files:
- **GEBCO:** public domain; free, commercial use allowed; must acknowledge the source and must not suggest GEBCO endorsement. Attribution text is in the grid documentation that ships with the download.
- **Pleiades:** CC BY 3.0 US confirmed (deed saved). Credit with link to the license and note changes. The credits page does not load (owner confirmed); contributors are credited by citing Pleiades.
- **Getty TGN:** ODC-By 1.0. Required credit line: "Contains information from the J. Paul Getty Trust, Getty Research Institute, [tool name], which is made available under the ODC Attribution License". Also cite the contributor and sources where appropriate.
- **Syriaca:** CC BY 4.0, cite as "Syriaca.org: The Syriac Reference Portal. 2014-. http://syriaca.org" with the listed editors.
- **PeriodO:** the contributors dedicated the dataset to the public domain; attribution is requested, not required.
- **The Met:** CC0 for basic data on all accessioned works and images of public-domain works.
- **Nomisma:** CC-BY for concepts on the nomisma.org domain; partner datasets carry their own licenses and must be checked individually.
- **DASI:** CC BY 4.0 for record data, not images. Per-record attribution (scholars, publication date, last revision, DOI) must travel with each record; a general acknowledgement is not enough. DASI objects to bulk scraping and discourages reuse in AI-generated resources, so any use needs the API under an agreement with their contact person.
- **MesoRAD:** the saved screenshot shows only the reuse text, no license. Stays UNVERIFIED and unapproved.
- **HydroRIVERS (HydroSHEDS v1):** the license (technical document, Appendix A) is not a plain open license. It grants rights to integrate the data into a derivative work and distribute it to end users under an end-user agreement at least as protective as the license; it forbids distributing the data as a stand-alone product and forbids reverse engineering; required copyright statement is in Exhibit B; the scientific citation is Lehner, Verdin and Jarvis (2008). Using it means baking rivers into game data and shipping an end-user license and the Exhibit B statement. This conflicts with the owner's goal of open, moddable data unless the owner accepts it. Not approved.
- **Not covered by the saved evidence:** GeoNames (page read earlier: cc-by, link to geonames.org), Gazetteer of Ancient Arabia, Cleveland, Art Institute, Smithsonian, Chengdu survey.

## 11. Third evidence pass (2026-10-14)
- **Cleveland Museum of Art:** its Open Access page says open access content (images, data, software, documentation) is published free of charge and without copyright and licensing restrictions, and that material offered with CC0 can be used in any application. Approved, CC0.
- **Smithsonian:** the FAQ says Open Access items carry a CC0 designation, and "CC0 only applies to copyright so you may still need someone else's permission". Approved, but only per item designated Open Access.
- **GeoNames:** terms page says "cc-by licence", "commercial usage is allowed", credit GeoNames with a link or other reference, data provided "as is". Approved for name disambiguation only.
- **Open Context (Chengdu survey):** the screenshot shows a search map of 81,764 records and no license. UNVERIFIED; the project's own license page must be read.
- **Gazetteer of Ancient Arabia:** the owner reports the domain is for sale. Rejected as unavailable. DASI's terms say DASI supplies data to it, so those inscriptions remain reachable through DASI under DASI's terms.
- **Art Institute of Chicago:** the owner confirms the link is only API documentation. UNVERIFIED; its data terms were not read.
- **Rivers:** the owner requires rivers from another source. HydroRIVERS is rejected. No replacement is identified; options are a new source the owner approves, or rivers derived from GEBCO elevation (flow routing on our own grid, which needs no third-party river data).

## 12. Fourth evidence pass (2026-10-14)
- **Open Context (Chengdu survey):** the filter panel shows "License (Description): Creative Commons Licenses" on all 81,764 records, but not which Creative Commons license. Each record carries its own license. Stays UNVERIFIED until the specific license (BY, BY-SA, BY-NC and so on) is read per record; any share-alike or non-commercial record is excluded.
- **MesoRAD:** the article page shows CC BY 3.0 (2021) for the article. That does not say what license the dataset in the repository carries, and an earlier listing said CC BY 4.0. Stays UNVERIFIED; the dataset record or repository license is still needed.
- **Art Institute of Chicago:** the supplied link is the data-aggregator repository, licensed AGPL-3.0. That is the museum's software, not its data terms, and AGPL code must not be used. Its data terms still need to be read from the museum's own page. UNVERIFIED.

## 13. Fifth evidence pass
- **Open Context (Chengdu survey):** the license filter shows "Attribution 4.0 International (CC BY 4.0)" on all 81,764 records. Approved; credit each record creator and cite the record URI.

## 14. Sixth evidence pass
- **Art Institute of Chicago:** the Open Access Images page (saved) says images are CC0 and that the object caption data (artist, title, date) "is also made available under Creative Commons Zero (CC0)". Additional data is available through its public API, whose terms are not on that page. Approved for artist, title and date only; the museum requests the caption "Artist. Title, Date. The Art Institute of Chicago."
- **Smithsonian:** the saved Terms of Use say CC0-icon content is free for commercial use, but the icon covers copyright only and third-party rights may remain. Users must cite and link to the Website, keep notices, and follow restrictions in the item metadata.

## 15. Seventh evidence pass
- **Art Institute of Chicago:** the saved Terms page says "certain data about the museum's collections, publications, and programs has been made available through AIC's Public API under the CC0 license", and that images or data labeled "CC0 Public Domain Designation", or otherwise stated as CC0, may be used for any purpose including commercial. Scope widened from caption fields to API data that carries that CC0 label. Data without the label stays excluded. The saved Image Licensing page repeats the CC0 image terms. The earlier report of a CC BY 4.0 description field is not confirmed by these pages.
- **MesoRAD:** no MesoRAD page is saved in docs/SOURCES. Only two screenshots were supplied (reuse text and the article license box), now stored in docs/SOURCES/screenshots. They show the article is CC BY 3.0, not the dataset license.

## 16. Gap searches (2026-10-14, web search summaries only, nothing read from primary pages)
- **Rivers:** GRWL (reported CC BY 4.0, river centerlines on Zenodo and Earth Engine catalog) and GRIT (Zenodo, license not shown) are the candidates. MERIT Hydro stays rejected (CC BY-NC or ODbL). Both candidates are modern rivers; neither supplies 500 BCE courses.
- **Terrain and climate:** Beyer, Krapp and Manica (2020) gives 0.5 degree monthly climate, 17 bioclimatic variables and biomes over 120,000 years; its license was not shown. A 21,000-year biome reconstruction (Li et al. 2025) is at 500-year steps; its article is CC BY 4.0, the dataset license is not confirmed.
- **Archaeology by region:** nothing found for the Americas or Siberia. Open-archeOcsean is a catalogue of Pacific and Southeast Asia datasets with the license shown only as "CC". ROAD is share-alike and far too old. Rejected.
- **Political borders:** Seshat data is CC BY-NC-SA (download page), so it is rejected. The Cliopatria polity-borders dataset on Zenodo (v0.0.1, 49.2 MB) is derived from Seshat; its license was not shown and its border geometry is what we cannot copy anyway. Unverified, do not use.
- **MesoRAD:** it is archived on tDAR (project 455305, DOI 10.6067/XCV8455305); mirror records say "License not specified". The tDAR record and its terms must be read.

Pages for the owner to check: https://zenodo.org/records/11219313 (GRIT), the GRWL Zenodo record linked from https://gee-community-catalog.org/projects/grwl/, https://core.tdar.org/project/455305/mesoamerican-radiocarbon-database-mesorad (MesoRAD), and the Zenodo or figshare record for Beyer et al. 2020.

## 17. Eighth evidence pass
- **GRIT:** the Zenodo record shows "Creative Commons Attribution Non Commercial 4.0 International". Non-commercial, so rejected. Remaining river candidate: GRWL (CC BY 4.0 per the Earth Engine catalog; its Zenodo record is not yet read).
- **MesoRAD:** the tDAR project page shows a summary, citation and one dataset (MesoRAD v1.2, 2020), but no license. Its only license statements remain the article's CC BY 3.0. Still unconfirmed; the dataset file's own page on tDAR, or the authors (contact: Julie Hoggarth), would have to state it.
- **Beyer, Krapp and Manica 2020:** the article is CC BY 4.0 and its metadata files are CC0. The dataset is cited as hosted on the Open Science Framework (doi 10.17605/osf.io/p86rt), and its license is not shown. Unconfirmed until the OSF record is read.

## 18. Ninth evidence pass and search prompts
- **GRWL:** the community catalog entry states "This work is licensed under a Creative Commons Attribution 4.0 International License" with the citation Allen and Pavelsky (2018). That is a third-party catalog, not the data's own record, so it stays UNVERIFIED until the Zenodo record is read.
- **Beyer et al. OSF record:** the owner reports the OSF link does not load. Unresolved; search for another host of the dataset.

### Prompts for the remaining searches
Common rules for every prompt: never open or quote aourednik/historical-basemaps, idris-maps/world-historical-gis-data, Seshat, Cliopatria, or any GPL, AGPL, LGPL, CC BY-SA, CC BY-NC or CC BY-ND data. Read each license from the data's own record page and quote it. Report each candidate as: name, URL, license name and version, exact attribution wording, whether commercial use is allowed, what the data covers (region, dates, geometry or facts), and what you could not verify. Do not download anything.

1. **Rivers (replacement for rejected sources).** Find global or regional river-line datasets under CC0, CC BY 4.0 or public domain. Check the GRWL Zenodo record by Allen and Pavelsky and quote its license. Check Natural Earth rivers and Global Lakes and Wetlands licenses. Note which datasets are modern only.
2. **Beyer, Krapp and Manica (2020) dataset.** The OSF link (doi 10.17605/osf.io/p86rt) does not load. Find where else the dataset is hosted (for example Zenodo, Figshare, Dryad, institutional repository) and quote its license.
3. **Paleoclimate and vegetation for about 500 BCE.** Find open-licensed gridded or site-based Holocene reconstructions (pollen, biome, temperature, precipitation) that cover 2500 to 2000 years ago, including PANGAEA, Neotoma and PAGES 2k. Quote the dataset license, not the article license.
4. **Political ownership and rulers around 500 BCE.** Find open-licensed textual or tabular sources (not map geometry) listing polities, rulers, capitals and dated extents: Wikidata, Pleiades polity records, Nomisma, Getty TGN, PeriodO, peer-reviewed open-access chronologies. Report licenses; Wikidata is CC0, confirm from its page.
5. **Americas.** Find open-licensed data on settlements, archaeological cultures and peoples around 500 BCE in North America, Mesoamerica, the Caribbean and the Andes (tDAR, Open Context, Zenodo, Dryad, national heritage portals). Resolve the MesoRAD dataset license from the tDAR dataset page for MesoRAD v1.2 (2020).
6. **Sub-Saharan and North Africa.** Find open-licensed settlement, site and culture data for 500 BCE (for example Nok, Kush/Meroe, Carthage, Garamantes, Aksum precursors), with license quotes.
7. **Central Asia, Siberia and the steppe.** Find open-licensed data on Scythian, Saka, Xiongnu precursor and related cultures, kurgan inventories and sites around 500 BCE.
8. **South, Southeast and East Asia and Oceania.** Find open-licensed data on polities and sites around 500 BCE: Mahajanapadas, Zhou-era states, Yayoi, Dong Son, Lapita and Polynesian settlement. Replace the rejected Pacific database; check Open-archeOcsean's license from its own page.
9. **Europe.** Find open-licensed data on Celtic, Etruscan, Greek, Iberian and Thracian sites and peoples around 500 BCE. Check Pleiades coverage and any national open data portals.
10. **Names and languages.** Find open-licensed multilingual place and people names for ancient places (Wikidata, Pleiades, Getty TGN), including attested ancient-language names, and the license for each.
11. **Synthesis.** Combine the findings of the above into one table: approved, pending owner, rejected, unverified, and what gaps remain per region.
