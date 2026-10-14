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
