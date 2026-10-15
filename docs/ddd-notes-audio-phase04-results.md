# Phase 4 results

Date: 2026-10-08. Implemented and checked offline.

Accepted new versions use schema 2: stable topic IDs, parent IDs, sibling order and canonical role-bearing blocks with citations, correction relationships and coverage anchors. Mongo stores each section's prose only in blocks. Legacy `Markdown`/`Points` remain read projections; historical schema-1 documents are unchanged. Legacy prose is carried as a whole block; migration does not invent a hierarchy or split meaning. Saved document Markdown remains a versioned export snapshot.

Generation receives a bounded topic outline and proposes routing and content in one call. Domain validates at most 24 create/add/move/reorder/update operations, known identities, depth three, cycles, source citations and protected content. Automatic body rewriting is rejected. Structural moves retain exact bodies, citations and coverage identities. Existing section responses remain supported while callers move to the tree.

The preview renders deterministic numbered topics/subtopics and lettered multi-block lists. Selected outline moves and block edits have explicit version checks; block edits preserve identity and become protected. Full editor no-op round trips retain the tree; restore copies the historical tree into a new protected version. Unsaved/stale drafts retain the existing conflict flow.

Validation: all 21 offline .NET groups passed. New checks cover the three-level TCP fixture, BSON with one canonical body, same-call routing, stable coverage/block identities, literal retention, invalid cycles/depth/source/protection, stale outline/block edits, editor round trip and hierarchical restore. Actual API request delegates exercise the new outline and block-edit endpoints. Frontend production build and the fixture API Edge note-tree check passed, including numbered headings, MathML, citations, conflict draft retention and mobile width. No original database, recording, provider or container was used. These deterministic fixtures validate mechanisms; natural-provider note quality remains a Phase 7 acceptance step.
