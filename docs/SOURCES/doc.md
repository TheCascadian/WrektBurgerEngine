TASK: Build the 500 BCE world map for BurgerEngine as a deterministic, programmatic pipeline with complete, auditable provenance for every input, every intermediate file, and every output feature.

READ FIRST (do not skip, do not summarize from memory)
- docs/plans/TIMELINE_DESIGN.md (all sections, especially 0, 9.5, 11.1)
- .ARCHIVE/docs/REBUILD_REPORT.md sections 1.3 and 1.4 (map and folder contracts)
- tools/earth/README.txt and tools/earth/make_earth.py (existing generator, for structure only)
Quote any rule you rely on, with the file and line, in your plan.

RULES THAT CANNOT BE BROKEN
1. Never make assumptions. Every fact about a license, a source, a boundary, a name or a date must come from a primary source you actually opened this session, and be recorded. If you cannot verify it, mark it UNVERIFIED and do not use it. If a decision is the owner's, stop and ask.
2. Banned inputs, in any form (download, clone, cache, tool output, screenshot, tracing, or "just for reference"):
   - aourednik/historical-basemaps and every fork or copy of it
   - idris-maps/world-historical-gis-data and every fork or copy of it
   - any dataset, image or map whose license you have not read from its primary source
   - any dataset under GPL, AGPL, LGPL, CC BY-SA, CC BY-NC, CC BY-ND, or "all rights reserved"
   - any map image from a book, website or atlas, even for tracing
   - any Paradox file, map or data
   If a banned input is found anywhere in the repo, build output, cache or logs, stop, report where, and do not continue.
3. You must never open or read banned datasets, even to compare. The similarity audit against them is done later by the owner outside your access. Your job is to make sure you never touch them.
4. Use only sources whose license you have read in full from the primary source and recorded: public domain, CC0, or attribution-only licenses (for example CC BY) whose terms you can fully satisfy. Copy the license text and the license URL into the ledger. The owner has the final say on any attribution-required source before it is used; list them and wait for approval.
5. Facts are not copyrightable but their expression is. Take facts (who held what, where a settlement stood, what it was called) from sources and draw all geometry yourself as new polygons on the game's own province grid. Do not copy, trace, offset, simplify, resample or smooth any third-party historical boundary line. Terrain, coastline, river and elevation inputs from permissively licensed raw data are allowed under their license terms.
6. American English in all code, comments, docs and messages. No Paradox names or references anywhere. No emojis. No secrets, tokens, personal paths, usernames or emails in any output, log or ledger; use repo-relative paths only.
7. Do not push to any remote. Do not rewrite git history. Do not run builds or long jobs outside ~/bin/wrun (see ~/.claude/CLAUDE.md for usage). Work in a new branch and make local commits only.

DELIVERABLES
A. tools/map500bce/ : a timeline-neutral generator (Python, numpy; no network access during generation). One command regenerates the entire map from the frozen inputs. Same inputs and seed give byte-identical outputs; prove it by running twice and comparing hashes.
B. A new game folder for the scenario with the engine's map contract: map/provinces.png (one unique color per location, alpha 0 = none), map/locations.txt, states, adjacencies, heightmap, terrain, trees, rivers, common/terrain.txt, and the other map files the loader requires (check REBUILD_REPORT.md 1.4 for limits: 255 terrain types, 65,000 locations; unnamed colors must not exist). Include countries, names and starting ownership for every country in the scenario with real historical names and real locations, as the owner decided.
C. provenance/ directory (committed, human readable, machine checkable):
   - sources.csv: one row per source: id, name, publisher, primary URL, retrieval date (ISO), license name, license URL, license text file path, SHA-256 of the exact file or dump used, attribution string required, what it was used for, status (APPROVED / PENDING OWNER / REJECTED), and who approved.
   - licenses/: a verbatim copy of each license text as retrieved, with retrieval date and SHA-256.
   - features.csv: one row per location, state, country, river, coastline segment and name: id, final name, name language and script, source id(s), source locator (page, record id, line, URL fragment), what fact was taken, the transformation applied, confidence (HIGH / MEDIUM / LOW / DISPUTED), and the date range the fact holds for.
   - decisions.md: every judgment call (disputed borders, uncertain locations, name choices) with reasoning and the alternatives rejected.
   - toolchain.md: tool and library versions with licenses (and confirm none is GPL-incompatible with shipping).
   - ai_log.md: for each file you generated, that it was AI-generated, which part was hand-edited by whom (leave owner fields blank for the owner), so the Steam disclosure can be made accurately.
   - NOTICE / attribution file text for every attribution-required source, ready for the credits screen.
D. tools/map500bce/verify.py: a checker that fails the build if any of the following holds:
   - an output feature has no features.csv row, or a row cites a source id that is not APPROVED
   - a source in sources.csv lacks a license file, a hash or a matching hash on disk
   - a banned input name, domain or hash appears anywhere in the repo, inputs, caches or logs (keep the denylist in a file and print it)
   - a Paradox term appears in any new file
   - any absolute path, username, email or token appears in any new file
   - provinces.png has duplicate or unnamed colors, or locations exceed the engine limits
   - any country has no ledger row for its name, ownership and capital
   - two runs do not produce identical hashes
   Wire it into burger validate for this scenario if the validator allows, without hardcoding the scenario.
E. A final report (plain text, in your last message) with: counts per source, every PENDING OWNER item, every UNVERIFIED or DISPUTED item, every place you stopped to ask, and the exact commands to reproduce.

PROCEDURE (follow in order; stop and report at each gate)
1. Plan only. List candidate sources by category: coastlines and rivers, elevation, settlements and place names (ancient and modern), political extents, rulers, peoples and cultures. For each, give the primary URL, the license as read from the primary source (quote it), and whether it needs attribution. Do not download anything yet. Present the plan and wait for owner approval of the source list. GATE 1.
2. Ask the owner for decisions you need, in one batch: the exact start date and calendar display, the province grid size and location count target, the list of countries and peoples in scope, handling of disputed or unknown areas. Do not proceed on guesses. GATE 2.
3. Fetch approved sources into inputs/ (outside any committed path unless the license allows redistribution; add to .gitignore if not). Record SHA-256 and retrieval date immediately. Never edit raw inputs.
4. Build the physical base: coastline and sea level adjustments for 500 BCE only where a sourced, cited basis exists; otherwise keep the modern coast and record it as a LOW-confidence decision. Elevation, terrain, trees and rivers from approved raw data under the engine's formats.
5. Build the province grid ourselves: seed and partition by deterministic algorithm from terrain and settlement density, never from any third-party political polygons.
6. Assign names and ownership from facts in approved sources. Each assignment gets a features.csv row at the time it is made, not afterward.
7. Run verify.py. Fix and rerun until clean. Run burger validate on the scenario through wrun. Run twice and compare hashes.
8. Stop. Commit locally. Report. Do not push.

WHEN IN DOUBT
Stop and ask. Never fill a gap with a plausible guess, a remembered fact, or a model-generated fact. If a fact is not in an approved source, the feature is marked UNVERIFIED and left out or placed as a visible placeholder the owner can see, never as real data.
